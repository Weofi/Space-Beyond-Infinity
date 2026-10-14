using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using Voidward.Combat;
using Voidward.Core;
using Voidward.Data;
using Voidward.Inventory;

namespace Voidward.Ship
{
    /// <summary>Effective ship stats (ShipDef + installed modules + engineer power).</summary>
    public struct ShipStats
    {
        public float Thrust, Turn, Roll, Boost, MaxSpeed;
        public float MaxShield, ShieldRegen, MaxHull, MaxFuel, FuelEff, Scanner;
        public int CargoSlots;
        public WeaponKind WeaponA, WeaponB;
        public int TierA, TierB;
        public float DmgMultA, DmgMultB, RateMult;
        public float AimAssist, HitboxMult;
        public int ExtraShots;
    }

    /// <summary>
    /// Networked ship state: hull (HealthComponent), shields, fuel, modules, cargo, power distribution,
    /// weapons. Server-authoritative; the pilot only sends input/fire requests.
    /// </summary>
    [RequireComponent(typeof(HealthComponent))]
    public class ShipCore : NetworkBehaviour, ISurface
    {
        /// <summary>Impact effects: shots spark off the shield bubble while it holds, then scorch the hull.</summary>
        public SurfaceKind Surface => Shield.Value > 0.5f && !ShieldsOffline.Value ? SurfaceKind.Shield : SurfaceKind.Metal;

        public const ulong NoPlayer = ulong.MaxValue;
        public const int ModuleSlotCount = 10;
        /// <summary>Pilot aim assist cone (degrees) without a Targeting module.</summary>
        public const float BaseAimAssist = 4f;
        /// <summary>Thrust and top speed in normal flight: at most this multiple of the hull's base values (#80).</summary>
        public const float NormalFlightCap = 1.3f;
        /// <summary>Fan angle between extra projectiles of a multishot salvo.</summary>
        const float MultishotSpreadDeg = 1.8f;
        /// <summary>Damage of each extra multishot projectile relative to the main one.</summary>
        const float MultishotDamage = 0.6f;

        public ShipClass Class = ShipClass.Dart;
        [Tooltip("Muzzles for weapon slot A / B (pilot weapons).")]
        public Transform[] MuzzlesA = new Transform[0];
        public Transform[] MuzzlesB = new Transform[0];
        [Tooltip("Crafting is possible near this point.")]
        public Transform FabricatorPoint;
        [Tooltip("Where players appear when they respawn at this ship.")]
        public Transform RespawnPoint;
        [Tooltip("Interior gravity volume (large ships).")]
        public GravityVolume Interior;

        public NetworkVariable<float> Fuel = new NetworkVariable<float>(100f);
        public NetworkVariable<float> Shield = new NetworkVariable<float>(50f);
        public NetworkVariable<ulong> OwnerPlayer = new NetworkVariable<ulong>(NoPlayer);
        public NetworkVariable<bool> Derelict = new NetworkVariable<bool>(false);
        public NetworkVariable<bool> ShieldsOffline = new NetworkVariable<bool>(false);
        public NetworkVariable<Vector3> Power = new NetworkVariable<Vector3>(Vector3.one);
        public NetworkVariable<FixedString64Bytes> ShipName = new NetworkVariable<FixedString64Bytes>("Ship");
        public NetworkList<FixedString64Bytes> Modules;

        public ShipDef Def { get; private set; }
        public ShipStats Stats { get; private set; }
        public HealthComponent Hull { get; private set; }
        public NetInventory Cargo { get; private set; }
        public ShipFlight Flight { get; private set; }

        public static readonly List<ShipCore> All = new List<ShipCore>();
        /// <summary>Server: a ship was destroyed (before despawn).</summary>
        public static event Action<ShipCore> ServerDestroyed;
        /// <summary>Every client: stats recomputed.</summary>
        public event Action StatsChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { All.Clear(); ServerDestroyed = null; ShieldBrokenClient = null; }

        float lastHitTime = -100f;
        float throttle;
        float throttleSendTimer;
        readonly float[] nextFire = new float[4];
        int muzzleToggle;

        void Awake()
        {
            Modules = new NetworkList<FixedString64Bytes>();
            Hull = GetComponent<HealthComponent>();
            Cargo = GetComponent<NetInventory>();
            Flight = GetComponent<ShipFlight>();
            Def = ContentCatalog.GetShip(Class);
        }

        public override void OnNetworkSpawn()
        {
            Def = ContentCatalog.GetShip(Class);
            if (IsServer)
            {
                if (Modules.Count == 0)
                {
                    for (int i = 0; i < ModuleSlotCount; i++) Modules.Add(default);
                    if (Def != null)
                        foreach (var id in Def.StartingModuleItemIds) InstallServer(id, false);
                }
                Hull.DamageFilter = FilterDamage;
                Hull.Died += OnDied;
                Hull.GodModeEligible = true;
            }
            Modules.OnListChanged += OnModulesChanged;
            Power.OnValueChanged += OnPowerChanged;
            Shield.OnValueChanged += OnShieldChanged;
            Recompute();
            if (IsServer)
            {
                var s = Stats;
                Hull.SetMaxHealthServer(s.MaxHull, true);
                Shield.Value = s.MaxShield;
                Fuel.Value = s.MaxFuel;
                if (Cargo != null) Cargo.Resize(s.CargoSlots);
            }
            if (FabricatorPoint == null) FabricatorPoint = transform;
            Fabricators.Register(FabricatorPoint);
            All.Add(this);
        }

        public override void OnNetworkDespawn()
        {
            Modules.OnListChanged -= OnModulesChanged;
            Power.OnValueChanged -= OnPowerChanged;
            Shield.OnValueChanged -= OnShieldChanged;
            if (IsServer) Hull.Died -= OnDied;
            Fabricators.Unregister(FabricatorPoint);
            All.Remove(this);
        }

        void OnModulesChanged(NetworkListEvent<FixedString64Bytes> e)
        {
            Recompute();
            if (IsServer)
            {
                var s = Stats;
                Hull.SetMaxHealthServer(s.MaxHull, false);
                Shield.Value = Mathf.Min(Shield.Value, s.MaxShield);
                Fuel.Value = Mathf.Min(Fuel.Value, s.MaxFuel);
                if (Cargo != null) Cargo.Resize(s.CargoSlots);
            }
        }

        void OnPowerChanged(Vector3 a, Vector3 b) => Recompute();

        public ModuleDef ModuleIn(int slot)
        {
            if (slot < 0 || slot >= Modules.Count) return null;
            var id = Modules[slot].ToString();
            if (string.IsNullOrEmpty(id)) return null;
            var item = ContentCatalog.GetItem(id);
            return item != null ? item.Module : null;
        }

        public string ModuleItemIn(int slot) => slot >= 0 && slot < Modules.Count ? Modules[slot].ToString() : "";

        void Recompute()
        {
            var d = Def;
            var s = new ShipStats();
            if (d == null) { Stats = s; return; }
            s.Thrust = d.Thrust; s.Turn = d.TurnSpeedDeg; s.Roll = d.RollSpeedDeg; s.Boost = d.BoostMultiplier;
            s.MaxSpeed = d.MaxSpeed; s.MaxShield = d.MaxShield; s.ShieldRegen = d.ShieldRegen; s.MaxHull = d.MaxHull;
            s.MaxFuel = d.MaxFuel; s.FuelEff = 1f; s.Scanner = 600f; s.CargoSlots = d.CargoWidth * d.CargoHeight;
            s.DmgMultA = 1f; s.DmgMultB = 1f; s.RateMult = 1f;
            s.AimAssist = BaseAimAssist; s.HitboxMult = 1f; s.ExtraShots = 0;

            for (int i = 0; i < Modules.Count; i++)
            {
                var m = ModuleIn(i);
                if (m == null) continue;
                s.Thrust *= m.ThrustMult; s.Turn *= m.TurnMult; s.Roll *= m.TurnMult; s.Boost *= m.BoostMult;
                s.MaxSpeed *= Mathf.Sqrt(m.ThrustMult);
                s.MaxShield += m.ShieldMaxAdd; s.ShieldRegen += m.ShieldRegenAdd; s.MaxHull += m.HullMaxAdd;
                s.CargoSlots += m.CargoSlotsAdd; s.Scanner += m.ScannerRangeAdd; s.FuelEff *= m.FuelEfficiencyMult;
                s.MaxFuel += m.FuelTankAdd;
                s.RateMult *= m.FireRateMult; s.AimAssist += m.AimAssistAdd; s.HitboxMult *= m.HitboxMult; s.ExtraShots += m.ExtraProjectiles;
                if (i == (int)ModuleSlot.WeaponA) { s.WeaponA = m.Weapon; s.TierA = m.Tier; s.DmgMultA = m.WeaponDamageMult; }
                if (i == (int)ModuleSlot.WeaponB) { s.WeaponB = m.Weapon; s.TierB = m.Tier; s.DmgMultB = m.WeaponDamageMult; }
            }
            // Normal flight stays close to the hull's own feel (#80): engine parts add at most +30 %, travel speed comes from cruise and warp.
            s.Thrust = Mathf.Min(s.Thrust, d.Thrust * NormalFlightCap);
            s.MaxSpeed = Mathf.Min(s.MaxSpeed, d.MaxSpeed * NormalFlightCap);
            var p = Power.Value;
            s.Thrust *= p.x;
            s.ShieldRegen *= p.y;
            s.RateMult *= p.z;
            Stats = s;
            StatsChanged?.Invoke();
        }

        // ------------------------------------------------------------ Server loop

        void Update()
        {
            if (!IsSpawned) return;
            if (IsServer)
            {
                var s = Stats;
                if (!ShieldsOffline.Value && Def != null && Time.time - lastHitTime > Def.ShieldRegenDelay && Shield.Value < s.MaxShield)
                    Shield.Value = Mathf.Min(s.MaxShield, Shield.Value + s.ShieldRegen * Time.deltaTime);
                if (ShieldsOffline.Value && Shield.Value > 0f) Shield.Value = 0f;

                if (throttle > 0.01f && Def != null && Fuel.Value > 0f && !CheatManager.GodMode)
                    Fuel.Value = Mathf.Max(0f, Fuel.Value - Def.FuelBurnPerSecond * throttle * s.FuelEff * Time.deltaTime);
            }
        }

        float FilterDamage(DamageInfo info)
        {
            lastHitTime = Time.time;
            if (info.Type == DamageType.Collapse) return info.Amount;
            float absorbed = Mathf.Min(Shield.Value, info.Amount);
            if (absorbed > 0f)
            {
                Shield.Value -= absorbed;
                ShieldHitClientRpc(info.Point);
            }
            return info.Amount - absorbed;
        }

        /// <summary>Every client: this ship's shield just collapsed under fire (not when the engineer powers it down).</summary>
        public static event Action<ShipCore> ShieldBrokenClient;
        float shieldRadius;

        void OnShieldChanged(float before, float now)
        {
            if (before < 1f || now > 0.5f || ShieldsOffline.Value || Derelict.Value) return;
            if (shieldRadius <= 0f)
            {
                var b = new Bounds(transform.position, Vector3.zero);
                foreach (var r in GetComponentsInChildren<Renderer>()) if (!(r is ParticleSystemRenderer)) b.Encapsulate(r.bounds);
                shieldRadius = Mathf.Clamp(b.extents.magnitude * 0.6f, 2f, 40f);
            }
            FeedbackFx.ShieldBroken(transform.position, shieldRadius);
            ShieldBrokenClient?.Invoke(this);
        }

        [ClientRpc]
        void ShieldHitClientRpc(Vector3 p)
        {
            Vfx.ShieldHit(p);
            Sfx.Play("shield_hit", p, 0.7f);
        }

        void OnDied(DamageInfo info)
        {
            ServerDestroyed?.Invoke(this);
            ShipSeat.VacateAllOn(this);
            // Spill part of the cargo.
            if (Cargo != null)
            {
                for (int i = 0; i < Cargo.Slots.Count; i++)
                {
                    var st = Cargo.Slots[i];
                    if (!st.IsEmpty && UnityEngine.Random.value < 0.6f)
                        WorldPickup.SpawnServer(st.Id.ToString(), Mathf.Max(1, st.Count / 2), transform.position + UnityEngine.Random.insideUnitSphere * 6f);
                }
            }
            ExplodeClientRpc(transform.position, Class == ShipClass.Ark ? 25f : 10f);
            NetworkObject.Despawn(true);
        }

        [ClientRpc]
        void ExplodeClientRpc(Vector3 pos, float r)
        {
            Vfx.Explosion(pos, r, new Color(1f, 0.6f, 0.3f));
            Sfx.Play("explosion_big", pos, 1f, 0.8f);
            Vfx.Shake(0.6f);
        }

        // ------------------------------------------------------------ Pilot API (local)

        /// <summary>Pilot client: report throttle for fuel burn (rate-limited).</summary>
        public void ReportThrottle(float t)
        {
            if (IsServer) { throttle = t; return; }
            throttleSendTimer -= Time.deltaTime;
            if (throttleSendTimer > 0f) return;
            throttleSendTimer = 0.25f;
            ThrottleServerRpc(t);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void ThrottleServerRpc(float t) => throttle = Mathf.Clamp01(t);

        /// <summary>Pilot client: fire weapon slot 0 (A) or 1 (B), rate-limited locally to avoid RPC spam.</summary>
        public void TryFireThrottled(int slot, Vector3 aimDir)
        {
            var s = Stats;
            var kind = slot == 0 ? s.WeaponA : s.WeaponB;
            if (kind == WeaponKind.None || Time.time < localNextFire[slot]) return;
            int tier = slot == 0 ? s.TierA : s.TierB;
            localNextFire[slot] = Time.time + Cooldown(kind, tier) / Mathf.Max(0.3f, s.RateMult) * 0.9f;
            FireServerRpc(slot, aimDir);
            FeedbackFx.LocalRecoil(Recoil(kind));
        }

        /// <summary>Local camera kick per shot (#43), scaled by the Screen shake setting in FeedbackFx.</summary>
        public static float Recoil(WeaponKind k)
        {
            switch (k)
            {
                case WeaponKind.Laser: return 0.16f;
                case WeaponKind.Flak: return 0.35f;
                case WeaponKind.Missile: return 0.6f;
                default: return 0f;
            }
        }

        readonly float[] localNextFire = new float[2];

        public static float Cooldown(WeaponKind k, int tier)
        {
            switch (k)
            {
                case WeaponKind.Laser: return 0.17f / (1f + 0.15f * (tier - 1));
                case WeaponKind.Missile: return 1.6f / (1f + 0.2f * (tier - 1));
                case WeaponKind.Flak: return 0.38f / (1f + 0.15f * (tier - 1));
                default: return 1f;
            }
        }

        public static float Damage(WeaponKind k, int tier)
        {
            switch (k)
            {
                case WeaponKind.Laser: return 9f + 4f * (tier - 1);
                case WeaponKind.Missile: return 45f + 22f * (tier - 1);
                case WeaponKind.Flak: return 15f + 7f * (tier - 1);
                default: return 0f;
            }
        }

        public static ProjectileKind ProjectileFor(WeaponKind k)
        {
            switch (k)
            {
                case WeaponKind.Missile: return ProjectileKind.Missile;
                case WeaponKind.Flak: return ProjectileKind.Flak;
                default: return ProjectileKind.LaserBolt;
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void FireServerRpc(int slot, Vector3 aimDir, RpcParams rp = default)
        {
            var seat = ShipSeat.SeatOf(rp.Receive.SenderClientId);
            if (seat == null || seat.Ship != this || seat.Role != StationRole.Pilot) return;
            var s = Stats;
            var kind = slot == 0 ? s.WeaponA : s.WeaponB;
            int tier = slot == 0 ? s.TierA : s.TierB;
            if (kind == WeaponKind.None) return;
            if (Time.time < nextFire[slot]) return;
            nextFire[slot] = Time.time + Cooldown(kind, tier) / Mathf.Max(0.3f, s.RateMult);

            var muzzles = slot == 0 ? MuzzlesA : MuzzlesB;
            if (muzzles == null || muzzles.Length == 0) muzzles = MuzzlesA;
            Transform m = muzzles.Length > 0 ? muzzles[muzzleToggle++ % muzzles.Length] : transform;
            float dmg = Damage(kind, tier) * (slot == 0 ? s.DmgMultA : s.DmgMultB);
            NetworkObject target = kind == WeaponKind.Missile ? FindMissileTarget(m.position, aimDir) : null;
            Vector3 dir = aimDir.sqrMagnitude > 0.01f ? aimDir.normalized : transform.forward;
            var proj = ProjectileFor(kind);
            ulong src = rp.Receive.SenderClientId;
            CombatNet.ServerFire(proj, m.position + dir * 1.5f, dir, dmg, Team.Players, src, true, target, s.HitboxMult);

            // Multishot: extra projectiles fanned around the aim, alternating sides.
            int extra = Mathf.Clamp(s.ExtraShots, 0, 4);
            Vector3 right = Vector3.Cross(transform.up, dir).normalized;
            if (right.sqrMagnitude < 0.01f) right = transform.right;
            for (int i = 0; i < extra; i++)
            {
                float side = (i % 2 == 0 ? 1f : -1f) * (i / 2 + 1) * MultishotSpreadDeg;
                Vector3 d = Quaternion.AngleAxis(side, transform.up) * dir;
                Vector3 offset = right * (i % 2 == 0 ? 1f : -1f) * (0.8f + 0.6f * (i / 2));
                CombatNet.ServerFire(proj, m.position + offset + d * 1.5f, d, dmg * MultishotDamage, Team.Players, src, true, target, s.HitboxMult);
            }
        }

        /// <summary>Server: fire from a turret seat.</summary>
        public void FireTurret(ShipSeat seat, Vector3 dir, ulong client)
        {
            if (!IsServer) return;
            int slot = 2 + Mathf.Clamp(seat.TurretIndex, 0, 1);
            var kind = seat.TurretWeapon;
            if (Time.time < nextFire[slot]) return;
            nextFire[slot] = Time.time + Cooldown(kind, 2) / Mathf.Max(0.3f, Stats.RateMult);
            var muzzle = seat.Muzzle != null ? seat.Muzzle : seat.transform;
            CombatNet.ServerFire(ProjectileFor(kind), muzzle.position + dir * 1.5f, dir, Damage(kind, 2), Team.Players, client, true,
                kind == WeaponKind.Missile ? FindMissileTarget(muzzle.position, dir) : null);
        }

        NetworkObject FindMissileTarget(Vector3 from, Vector3 dir)
        {
            NetworkObject best = null;
            float bestScore = float.MaxValue;
            foreach (var h in HealthComponent.All)
            {
                if (h == null || !h.IsTargetable || h.Team == Team.Players || h.Team == Team.Neutral) continue;
                Vector3 to = h.transform.position - from;
                float dist = to.magnitude;
                if (dist > 700f) continue;
                float ang = Vector3.Angle(dir, to);
                if (ang > 35f) continue;
                float score = dist * (1f + ang / 10f);
                if (score < bestScore) { bestScore = score; best = h.NetworkObject; }
            }
            return best;
        }

        // ------------------------------------------------------------ Modules / services

        /// <summary>Server: install a module item into its slot (no inventory involved). Returns the replaced item id.</summary>
        public string InstallServer(string itemId, bool preferB)
        {
            var item = ContentCatalog.GetItem(itemId);
            if (item == null || item.Module == null) return null;
            int slot = (int)item.Module.Slot;
            if (item.Module.Weapon != WeaponKind.None)
            {
                bool aEmpty = string.IsNullOrEmpty(ModuleItemIn((int)ModuleSlot.WeaponA));
                bool bEmpty = string.IsNullOrEmpty(ModuleItemIn((int)ModuleSlot.WeaponB));
                slot = preferB || (!aEmpty && bEmpty) ? (int)ModuleSlot.WeaponB : (int)ModuleSlot.WeaponA;
            }
            string old = ModuleItemIn(slot);
            Modules[slot] = itemId;
            return old;
        }

        bool SenderNear(ulong client, float range)
        {
            var inv = NetInventory.ForClient(client);
            if (inv == null) return false;
            if (ShipSeat.SeatOf(client)?.Ship == this) return true;
            return Vector3.Distance(inv.transform.position, transform.position) < range;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void InstallFromInventoryServerRpc(int invSlot, bool preferB, RpcParams rp = default)
        {
            ulong c = rp.Receive.SenderClientId;
            if (!SenderNear(c, 30f + (Class == ShipClass.Ark ? 25f : 0f))) return;
            var inv = NetInventory.ForClient(c);
            if (inv == null || invSlot < 0 || invSlot >= inv.Slots.Count) return;
            var st = inv.Slots[invSlot];
            var item = st.Def;
            if (item == null || item.Kind != ItemKind.Module || item.Module == null) return;
            if (!inv.Remove(item.Id, 1)) return;
            string old = InstallServer(item.Id, preferB);
            if (!string.IsNullOrEmpty(old)) NetInventory.GiveOrDrop(c, old, 1, transform.position);
            ServiceClientRpc("craft", "Installed " + item.DisplayName, c);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void UninstallServerRpc(int moduleSlot, RpcParams rp = default)
        {
            ulong c = rp.Receive.SenderClientId;
            if (!SenderNear(c, 55f) || moduleSlot < 0 || moduleSlot >= Modules.Count) return;
            string id = ModuleItemIn(moduleSlot);
            if (string.IsNullOrEmpty(id)) return;
            Modules[moduleSlot] = default;
            NetInventory.GiveOrDrop(c, id, 1, transform.position);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RepairServerRpc(RpcParams rp = default)
        {
            ulong c = rp.Receive.SenderClientId;
            if (!SenderNear(c, 55f)) return;
            var inv = NetInventory.ForClient(c);
            int used = 0;
            while (Hull.Hp.Value < Hull.MaxHp.Value - 1f && inv.Remove("scrap", 1))
            {
                Hull.Heal(25f);
                used++;
            }
            if (used == 0 && inv.Remove("repair_kit", 1)) { Hull.Heal(80f); used = 1; }
            ServiceClientRpc("heal", used > 0 ? "Hull repaired" : "Need Scrap or a Repair Kit", c);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RefuelServerRpc(RpcParams rp = default)
        {
            ulong c = rp.Receive.SenderClientId;
            if (!SenderNear(c, 55f)) return;
            var inv = NetInventory.ForClient(c);
            int used = 0;
            while (Fuel.Value < Stats.MaxFuel - 5f && inv.Remove("fuel", 1))
            {
                Fuel.Value = Mathf.Min(Stats.MaxFuel, Fuel.Value + 25f);
                used++;
            }
            ServiceClientRpc("pickup", used > 0 ? $"Refueled (+{used * 25})" : "Need Fuel", c);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void SetPowerServerRpc(Vector3 p, RpcParams rp = default)
        {
            var seat = ShipSeat.SeatOf(rp.Receive.SenderClientId);
            if (seat == null || seat.Ship != this || seat.Role != StationRole.Engineer) return;
            p = new Vector3(Mathf.Clamp(p.x, 0.4f, 1.8f), Mathf.Clamp(p.y, 0.4f, 1.8f), Mathf.Clamp(p.z, 0.4f, 1.8f));
            float sum = p.x + p.y + p.z;
            if (sum > 3.6f) p *= 3.6f / sum; // an engineer can overclock up to +20% total
            Power.Value = p;
        }

        /// <summary>Restore a derelict ship (costs scrap).</summary>
        public const int DerelictRepairCost = 8;

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void ClaimServerRpc(RpcParams rp = default)
        {
            ulong c = rp.Receive.SenderClientId;
            if (!Derelict.Value || !SenderNear(c, 60f)) return;
            var inv = NetInventory.ForClient(c);
            if (inv == null || !inv.Remove("scrap", DerelictRepairCost))
            {
                ServiceClientRpc("ui_back", $"Need {DerelictRepairCost} Scrap to restore power", c);
                return;
            }
            Derelict.Value = false;
            Hull.Heal(Hull.MaxHp.Value);
            Fuel.Value = Stats.MaxFuel;
            ShipName.Value = "Ark (crew)";
            AnnounceClientRpc("The Ark is online! Crew it together: Pilot, Turrets, Engineer.");
        }

        [ClientRpc]
        void AnnounceClientRpc(string msg)
        {
            EventBus.RaiseNotification(msg);
            Sfx.PlayUI("jump_charge");
        }

        [ClientRpc]
        void ServiceClientRpc(string sfx, string msg, ulong who)
        {
            if (NetworkManager.LocalClientId != who) return;
            Sfx.PlayUI(sfx);
            EventBus.RaiseNotification(msg);
        }

        /// <summary>Server: refill everything (trader services, cheats, jumps).</summary>
        public void RestoreServer(bool hull, bool fuel)
        {
            if (!IsServer) return;
            if (hull) Hull.Heal(Hull.MaxHp.Value);
            if (fuel) Fuel.Value = Stats.MaxFuel;
            Shield.Value = Stats.MaxShield;
        }

        /// <summary>The ship a client owns (server and clients).</summary>
        public static ShipCore OwnedBy(ulong client)
        {
            foreach (var s in All) if (s != null && s.OwnerPlayer.Value == client) return s;
            return null;
        }

        public static ShipCore Nearest(Vector3 pos, float range, bool includeDerelict = false)
        {
            ShipCore best = null;
            float bestSqr = range * range;
            foreach (var s in All)
            {
                if (s == null || (!includeDerelict && s.Derelict.Value)) continue;
                float d = (s.transform.position - pos).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = s; }
            }
            return best;
        }
    }
}
