using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using Voidward.Combat;
using Voidward.Core;
using Voidward.Data;

namespace Voidward.Inventory
{
    /// <summary>
    /// Networked item drop. Static (no physics): the server places it on the ground (near a planet) or floating
    /// in space. Juice (#43), all client-side on the Visual child: it pops out of its source in a short arc and
    /// lands with a bounce, glows in its rarity colour with a light beam (Uncommon+), and is pulled into the
    /// local player by the magnet (PlayerPawn calls Attract) before the pickup request is sent.
    /// </summary>
    public class WorldPickup : NetworkBehaviour, IInteractable
    {
        public NetworkVariable<FixedString64Bytes> ItemId = new NetworkVariable<FixedString64Bytes>();
        public NetworkVariable<int> Amount = new NetworkVariable<int>(1);
        /// <summary>Where the drop popped out (enemy, crate); equal to the rest position for plain drops.</summary>
        public NetworkVariable<Vector3> DropFrom = new NetworkVariable<Vector3>();
        /// <summary>Server time of the drop: late joiners skip the arc.</summary>
        public NetworkVariable<double> DropTime = new NetworkVariable<double>();

        [SerializeField] Transform visual;

        public static readonly List<WorldPickup> All = new List<WorldPickup>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => All.Clear();

        bool requested;
        float requestTime;
        Vector3 basePos;

        // Client-side presentation state.
        Rarity rarity;
        Color rarityColor = Color.white;
        float arcStart = -1f, arcDur, landedAt = -99f;
        Vector3 arcFrom;
        Transform magnetTarget;
        float magnetUntil, magnetT, magnetCooldown, collectedAt = -1f;
        Vector3 magnetPos;
        Transform beam, glow;
        MeshRenderer beamRenderer, glowRenderer;
        Light beamLight;
        MaterialPropertyBlock beamMpb;

        /// <summary>Magnet radius for the local pawn (rare loot reaches further).</summary>
        public float MagnetRadius => rarity >= Rarity.Rare ? 6f : 4.5f;
        public Rarity Rarity => rarity;
        /// <summary>Still in its pop-out arc (cannot be collected yet).</summary>
        public bool InFlight => arcStart >= 0f && Time.time - arcStart < arcDur;
        /// <summary>Pulled in by the magnet or already collected locally (dev tests).</summary>
        public bool Magnetized => magnetT > 0f || collectedAt >= 0f;

        public ItemDef Def => ContentCatalog.GetItem(ItemId.Value.ToString());

        public string Prompt
        {
            get
            {
                var d = Def;
                return $"Pick up {(d != null ? d.DisplayName : "item")} x{Amount.Value}";
            }
        }

        public bool CanInteract(IInteractAgent agent) => IsSpawned;
        public void Interact(IInteractAgent agent) => RequestPickup();

        /// <summary>Local: ask the server to collect (throttled).</summary>
        public void RequestPickup()
        {
            if (requested && Time.time - requestTime < 1f) return;
            requested = true;
            requestTime = Time.time;
            PickupServerRpc();
        }

        /// <summary>Server: drop an item. pos = where it comes from (enemy, crate); it pops out and lands nearby.</summary>
        public static WorldPickup SpawnServer(string id, int count, Vector3 pos)
        {
            var cat = ContentCatalog.Instance;
            var nm = NetworkManager.Singleton;
            if (cat == null || cat.WorldPickup == null || nm == null || !nm.IsServer || count <= 0) return null;

            Vector3 from = pos;
            // Snap to ground if a planet is close below. Drops scatter around their source, so they visibly
            // jump out of it instead of stacking up in one spot.
            var src = GravitySource.Dominant(pos);
            Quaternion rot = Quaternion.identity;
            if (src != null)
            {
                Vector3 up0 = (pos - src.Center).normalized;
                Vector3 side = Vector3.ProjectOnPlane(Random.onUnitSphere, up0);
                if (side.sqrMagnitude > 1e-4f) pos += side.normalized * Random.Range(1f, 2.4f);
                Vector3 down = (src.Center - pos).normalized;
                if (Physics.Raycast(pos - down * 3f, down, out var hit, 30f, ~0, QueryTriggerInteraction.Ignore))
                    pos = hit.point - down * 0.5f;
                else if (src.SurfaceDistance(pos) < 30f)
                    pos = src.GroundPoint(pos) - down * 0.5f;
                rot = Quaternion.FromToRotation(Vector3.up, -down);
            }
            else pos += Random.onUnitSphere * Random.Range(1.5f, 3.2f);
            pos += Random.insideUnitSphere * 0.3f;

            var go = Instantiate(cat.WorldPickup, pos, rot);
            var p = go.GetComponent<WorldPickup>();
            p.pendingId = id;
            p.pendingAmount = count;
            p.pendingFrom = from;
            p.NetworkObject.Spawn(true);
            return p;
        }

        string pendingId;
        int pendingAmount;
        Vector3 pendingFrom;

        public override void OnNetworkSpawn()
        {
            if (IsServer && pendingId != null)
            {
                ItemId.Value = pendingId;
                Amount.Value = pendingAmount;
                DropFrom.Value = pendingFrom;
                DropTime.Value = NetworkManager.ServerTime.Time;
            }
            All.Add(this);
            basePos = visual != null ? visual.localPosition : Vector3.zero;
            ApplyTint();
            ItemId.OnValueChanged += (a, b) => ApplyTint();

            // Pop-out arc, unless this drop is old (late join) or did not come from anywhere.
            double age = NetworkManager.ServerTime.Time - DropTime.Value;
            arcFrom = DropFrom.Value;
            if (visual != null && DropTime.Value > 0 && age < 0.6 && (arcFrom - visual.position).sqrMagnitude > 0.25f)
            {
                arcStart = Time.time;
                arcDur = 0.55f + (NetworkObjectId % 7) * 0.035f;
                FeedbackFx.LootPopped(arcFrom, rarity);
            }
            Update();
        }

        public override void OnNetworkDespawn() => All.Remove(this);

        /// <summary>
        /// Local pawn, every frame while inside MagnetRadius: start (or keep) pulling the drop in. Once it has
        /// picked up speed the pull commits, so walking past still collects it.
        /// </summary>
        public void Attract(Transform target)
        {
            if (target == null || visual == null || InFlight || collectedAt >= 0f || Time.time < magnetCooldown) return;
            if (magnetT <= 0f) { magnetPos = visual.position; magnetT = 0.0001f; }
            magnetTarget = target;
            magnetUntil = Time.time + 0.3f;
        }

        /// <summary>Shows the item's own model (fallback: the generic capsule tinted in the item colour).</summary>
        void ApplyTint()
        {
            var d = Def;
            if (d == null || visual == null) return;
            var mf = visual.GetComponentInChildren<MeshFilter>();
            if (mf != null && genericMesh == null) genericMesh = mf.sharedMesh;
            bool model = d.Model != null && mf != null;
            if (mf != null) mf.sharedMesh = model ? d.Model : genericMesh;
            if (mf != null) mf.transform.localScale = Vector3.one * (model ? 1.25f : 1f);
            if (mf != null) mf.transform.localPosition = model ? new Vector3(0f, -0.25f, 0f) : Vector3.zero;
            rarity = LootRarity.Of(d);
            rarityColor = LootRarity.ColorOf(rarity);
            // Rarer loot glows in its rarity colour, so it reads at a glance even without the beam.
            float rim = rarity == Rarity.Common ? 0f : rarity == Rarity.Uncommon ? 0.12f : rarity == Rarity.Rare ? 0.22f : 0.32f;
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", model ? Color.white : d.Tint);
            mpb.SetColor("_EmissionColor", (model ? Color.black : d.Tint * 0.35f) + rarityColor.linear * rim);
            foreach (var r in visual.GetComponentsInChildren<Renderer>()) r.SetPropertyBlock(mpb);
            BuildBeam();
        }

        Mesh genericMesh;

        void Update()
        {
            if (visual == null) return;
            float now = Time.time;
            Vector3 up = transform.up;
            Vector3 rest = transform.TransformPoint(basePos + Vector3.up * (Mathf.Sin(now * 2.5f + NetworkObjectId) * 0.12f));

            if (collectedAt >= 0f)
            {
                // Collected locally, waiting for the server despawn. Refused (inventory full): drop back.
                if (now - collectedAt < 1.2f) return;
                collectedAt = -1f; magnetT = 0f; magnetCooldown = now + 3f;
                visual.gameObject.SetActive(true);
            }

            if (InFlight)
            {
                float s = Mathf.Clamp01((now - arcStart) / arcDur);
                float h = 1.1f + Vector3.Distance(arcFrom, rest) * 0.35f;
                visual.position = Vector3.Lerp(arcFrom, rest, s) + up * (h * 4f * s * (1f - s));
                visual.localRotation = Quaternion.Euler(0f, now * (90f + 720f * (1f - s)), 0f);
                visual.localScale = Vector3.one * Mathf.Lerp(0.35f, 1f, Mathf.Clamp01(s * 2.5f));
                UpdateBeam(0f);
                return;
            }
            if (arcStart >= 0f)
            {
                arcStart = -1f;
                landedAt = now;
                FeedbackFx.LootLanded(transform.TransformPoint(basePos) - up * 0.5f, up, rarity, rarityColor);
            }

            if (magnetT > 0f)
            {
                if (magnetTarget == null || (now > magnetUntil && magnetT < 0.15f)) { magnetT = 0f; visual.localScale = Vector3.one; }
                else
                {
                    magnetT += Time.deltaTime;
                    Vector3 goal = magnetTarget.position + magnetTarget.up * 0.6f;
                    magnetPos = Vector3.MoveTowards(magnetPos, goal, (3f + 26f * magnetT * magnetT) * Time.deltaTime);
                    // Slight swirl on the way in, fading out as it closes.
                    Vector3 side = Vector3.Cross(goal - magnetPos, up);
                    float swirl = Mathf.Sin(magnetT * 9f) * 0.25f * Mathf.Clamp01(1f - magnetT);
                    visual.position = magnetPos + (side.sqrMagnitude > 1e-4f ? side.normalized * swirl : Vector3.zero);
                    visual.localRotation = Quaternion.Euler(0f, now * 540f, 0f);
                    visual.localScale = Vector3.one * Mathf.Lerp(1f, 0.45f, Mathf.Clamp01(magnetT * 1.5f));
                    UpdateBeam(0f);
                    if ((goal - magnetPos).sqrMagnitude < 0.5f * 0.5f || magnetT > 1.5f)
                    {
                        collectedAt = now;
                        visual.gameObject.SetActive(false);
                        RequestPickup();
                    }
                    return;
                }
            }

            visual.position = rest;
            visual.localRotation = Quaternion.Euler(0f, now * 90f, 0f);
            // Landing squash: a short damped bounce, the beam grows in meanwhile.
            float since = now - landedAt;
            if (since < 0.45f)
            {
                float b = Mathf.Sin(since * 28f) * Mathf.Exp(-since * 9f);
                visual.localScale = new Vector3(1f + b * 0.25f, 1f - b * 0.35f, 1f + b * 0.25f);
                UpdateBeam(Mathf.Clamp01(since / 0.45f));
            }
            else
            {
                visual.localScale = Vector3.one;
                UpdateBeam(1f);
            }
        }

        // ------------------------------------------------------------------ rarity beam

        static Mesh beamMesh, glowMesh;

        /// <summary>Soft vertical light column and ground glow (Uncommon+), plus a point light for Rare+.</summary>
        void BuildBeam()
        {
            if (beam != null) Destroy(beam.gameObject);
            if (glow != null) Destroy(glow.gameObject);
            if (beamLight != null) Destroy(beamLight.gameObject);
            beam = glow = null; beamLight = null;
            var cat = ContentCatalog.Instance;
            if (rarity == Rarity.Common || cat == null || cat.ParticleMaterial == null) return;
            if (beamMesh == null) beamMesh = BuildBeamMesh();
            if (glowMesh == null) glowMesh = BuildGlowMesh();
            if (beamMpb == null) beamMpb = new MaterialPropertyBlock();

            beam = MakeQuad("LootBeam", beamMesh, cat.ParticleMaterial, out beamRenderer);
            glow = MakeQuad("LootGlow", glowMesh, cat.ParticleMaterial, out glowRenderer);
            if (rarity >= Rarity.Rare)
            {
                var lgo = new GameObject("LootLight");
                lgo.transform.SetParent(transform, false);
                lgo.transform.localPosition = basePos + Vector3.up * 0.4f;
                beamLight = lgo.AddComponent<Light>();
                beamLight.type = LightType.Point;
                beamLight.color = rarityColor;
                beamLight.range = rarity == Rarity.Epic ? 6f : 4f;
                beamLight.shadows = LightShadows.None;
            }
            UpdateBeam(InFlight ? 0f : 1f);
        }

        Transform MakeQuad(string name, Mesh mesh, Material mat, out MeshRenderer mr)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = basePos + Vector3.down * 0.5f;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go.transform;
        }

        /// <summary>k = 0..1 fade (hidden during the arc and the magnet pull), scaled by the Reward effects setting.</summary>
        void UpdateBeam(float k)
        {
            if (beam == null) return;
            float fx = Mathf.Clamp01(FeedbackFx.RewardFx);
            k *= fx;
            bool on = k > 0.01f;
            if (beam.gameObject.activeSelf != on) { beam.gameObject.SetActive(on); glow.gameObject.SetActive(on); }
            if (beamLight != null) beamLight.enabled = on && fx > 0.3f;
            if (!on) return;
            float height = rarity == Rarity.Epic ? 9f : rarity == Rarity.Rare ? 5.5f : 2.6f;
            float width = rarity == Rarity.Epic ? 0.7f : rarity == Rarity.Rare ? 0.5f : 0.36f;
            float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * 3.1f + NetworkObjectId);
            // Billboard around the local up axis.
            var cam = Camera.main;
            Vector3 up = transform.up;
            if (cam != null)
            {
                Vector3 toCam = Vector3.ProjectOnPlane(cam.transform.position - beam.position, up);
                if (toCam.sqrMagnitude > 1e-4f) beam.rotation = Quaternion.LookRotation(toCam, up);
            }
            beam.localScale = new Vector3(width, height * Mathf.Lerp(0.3f, 1f, k), 1f);
            glow.localScale = Vector3.one * (width * 4f);
            Color c = rarityColor.linear * (rarity == Rarity.Epic ? 2.4f : 1.8f);
            c.a = 0.55f * k * pulse;
            beamMpb.SetColor("_BaseColor", c);
            beamRenderer.SetPropertyBlock(beamMpb);
            c.a = 0.5f * k * pulse;
            beamMpb.SetColor("_BaseColor", c);
            glowRenderer.SetPropertyBlock(beamMpb);
            if (beamLight != null) beamLight.intensity = (rarity == Rarity.Epic ? 2.2f : 1.4f) * k * pulse;
        }

        /// <summary>Unit column, base at 0. uv.y stays 0.5 so VW_AdditiveParticle only fades across; vertex alpha fades upwards.</summary>
        static Mesh BuildBeamMesh()
        {
            float[] ys = { 0f, 0.12f, 0.45f, 1f };
            float[] al = { 1f, 0.9f, 0.4f, 0f };
            var v = new Vector3[8]; var uv = new Vector2[8]; var col = new Color[8];
            for (int i = 0; i < 4; i++)
            {
                v[i * 2] = new Vector3(-0.5f, ys[i], 0f); v[i * 2 + 1] = new Vector3(0.5f, ys[i], 0f);
                uv[i * 2] = new Vector2(0f, 0.5f); uv[i * 2 + 1] = new Vector2(1f, 0.5f);
                col[i * 2] = col[i * 2 + 1] = new Color(1f, 1f, 1f, al[i]);
            }
            var tris = new int[18];
            for (int i = 0; i < 3; i++)
            {
                int b = i * 2, t = i * 6;
                tris[t] = b; tris[t + 1] = b + 2; tris[t + 2] = b + 1;
                tris[t + 3] = b + 1; tris[t + 4] = b + 2; tris[t + 5] = b + 3;
            }
            var m = new Mesh { name = "LootBeam", vertices = v, uv = uv, colors = col, triangles = tris };
            m.bounds = new Bounds(new Vector3(0f, 0.5f, 0f), Vector3.one);
            return m;
        }

        /// <summary>Flat unit quad on the ground (radial soft disc).</summary>
        static Mesh BuildGlowMesh()
        {
            return new Mesh
            {
                name = "LootGlow",
                vertices = new[] { new Vector3(-0.5f, 0.02f, -0.5f), new Vector3(0.5f, 0.02f, -0.5f), new Vector3(-0.5f, 0.02f, 0.5f), new Vector3(0.5f, 0.02f, 0.5f) },
                uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) },
                colors = new[] { Color.white, Color.white, Color.white, Color.white },
                triangles = new[] { 0, 2, 1, 1, 2, 3 },
            };
        }

        // ------------------------------------------------------------------ network

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void PickupServerRpc(RpcParams rp = default)
        {
            if (!IsSpawned) return;
            var inv = NetInventory.ForClient(rp.Receive.SenderClientId);
            if (inv == null) return;
            if (Vector3.Distance(inv.transform.position, transform.position) > 8f) return;
            string id = ItemId.Value.ToString();
            int left = inv.Add(id, Amount.Value);
            int taken = Amount.Value - left;
            if (taken > 0)
            {
                PickedUpClientRpc(transform.position, rp.Receive.SenderClientId, id, taken);
                LootStats.Collected?.Invoke(rp.Receive.SenderClientId, taken);
            }
            if (left <= 0) NetworkObject.Despawn(true);
            else
            {
                Amount.Value = left;
                NetInventory.EventBusNotify(rp.Receive.SenderClientId, "Inventory full");
            }
        }

        [ClientRpc]
        void PickedUpClientRpc(Vector3 pos, ulong who, FixedString64Bytes id, int count)
        {
            var d = ContentCatalog.GetItem(id.ToString());
            bool local = NetworkManager.LocalClientId == who;
            // The collector sees the burst where the item reached them, everyone else at the drop.
            Vector3 at = local && visual != null && collectedAt >= 0f ? visual.position : pos + transform.up * 0.6f;
            FeedbackFx.LootCollected(at, LootRarity.Of(d), d != null ? d.Tint : Color.white, local);
            if (local) EventBus.RaiseNotification($"+{count} {(d != null ? d.DisplayName : id.ToString())}");
        }
    }

    /// <summary>Server-side loot bookkeeping hooks (run stats).</summary>
    public static class LootStats
    {
        public static System.Action<ulong, int> Collected;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Collected = null;
    }
}
