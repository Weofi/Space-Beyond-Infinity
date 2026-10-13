using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Voidward.Core;
using Voidward.Networking;
using Voidward.Player;

namespace Voidward.UI
{
    /// <summary>
    /// Crew awareness for multiplayer (#60): edge-clamped name markers over every teammate in their crew colour
    /// (red pulse while down), collapse-meter pips, a crew panel above the vitals (colour, name, health, status)
    /// and a toast when a teammate goes down. Compass marks come from <see cref="Teammates"/>. Shows nothing solo.
    /// </summary>
    public class CrewHud
    {
        class Mate
        {
            public HudMarker Marker;
            public Image Pip;
            public bool WasDown;
        }

        class Row
        {
            public RectTransform Rt;
            public Image Swatch, HpFill;
            public TextMeshProUGUI Name, Status;
        }

        public struct Teammate
        {
            public ulong Id;
            public string Name;
            public Vector3 Pos;
            public Color Color;
            public bool Down;
        }

        const float RowHeight = 30f, PanelWidth = 360f;

        readonly RectTransform markerLayer, meterTrack, panel;
        readonly Dictionary<ulong, Mate> mates = new Dictionary<ulong, Mate>();
        readonly List<Row> rows = new List<Row>();
        readonly List<Teammate> teammates = new List<Teammate>();
        readonly List<NetworkPlayer> crew = new List<NetworkPlayer>();
        readonly HashSet<ulong> seen = new HashSet<ulong>();

        /// <summary>Living, active teammates this frame (excludes the local player).</summary>
        public IReadOnlyList<Teammate> Teammates => teammates;

        public CrewHud(RectTransform root, RectTransform markerLayer, RectTransform meterTrack, Vector2 panelPos)
        {
            this.markerLayer = markerLayer;
            this.meterTrack = meterTrack;
            panel = HudKit.Plate(root, "Crew").rectTransform;
            UIKit.Place(panel, Vector2.zero, panelPos, new Vector2(PanelWidth, 0f), Vector2.zero);
            var v = UIKit.VLayout(panel.gameObject, 4, 0, TextAnchor.LowerLeft);
            v.padding = new RectOffset(14, 14, 10, 10);
            v.childControlHeight = true;
            v.childForceExpandHeight = false;
            var fit = panel.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            UIKit.LayoutElement(HudKit.Micro(panel, "CREW", 13f, HudKit.TextDim).gameObject, PanelWidth - 28f, 18f);
            panel.gameObject.SetActive(false);
        }

        static PlayerPawn PawnOf(NetworkPlayer p) => p != null ? p.GetComponent<PlayerPawn>() : null;

        public void Update(Camera cam, PlayerPawn local, float sectorStartX, float sectorLen)
        {
            teammates.Clear();
            crew.Clear();
            foreach (var p in NetworkPlayer.All) if (p != null && p.IsSpawned) crew.Add(p);
            crew.Sort((a, b) => a.CrewSlot.Value.CompareTo(b.CrewSlot.Value));
            bool multi = crew.Count > 1;

            seen.Clear();
            foreach (var np in crew)
            {
                var pawn = PawnOf(np);
                if (pawn == null || np.IsOwner) continue;
                seen.Add(np.OwnerClientId);
                if (!mates.TryGetValue(np.OwnerClientId, out var m))
                {
                    m = new Mate { Marker = new HudMarker(markerLayer) };
                    m.Pip = UIKit.Image(meterTrack, "CrewPip", Color.white, false);
                    m.Pip.sprite = HudKit.Chevron;
                    m.Pip.raycastTarget = false;
                    var rt = m.Pip.rectTransform;
                    rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
                    rt.sizeDelta = new Vector2(11f, 11f);
                    mates[np.OwnerClientId] = m;
                }

                bool active = pawn.Active.Value && !pawn.IsLost.Value;
                bool down = pawn.IsDown.Value;
                string name = np.PlayerName.Value.ToString();
                Color c = np.CrewColor;

                if (active && down && !m.WasDown)
                {
                    EventBus.RaiseNotification(name + " is down!");
                    Sfx.PlayUI("alarm", 0.5f);
                }
                m.WasDown = active && down;

                if (!active) { m.Marker.Hide(); m.Pip.enabled = false; continue; }

                Vector3 head = pawn.transform.position + pawn.transform.up * 2.25f;
                teammates.Add(new Teammate { Id = np.OwnerClientId, Name = name, Pos = head, Color = c, Down = down });

                if (cam != null)
                {
                    float d = Vector3.Distance(cam.transform.position, head);
                    if (down)
                    {
                        Color pulse = Color.Lerp(HudKit.Danger, c, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 8f));
                        m.Marker.Place(cam, markerLayer, head, Icons.Danger, name + "  -  DOWN", pulse, true);
                    }
                    else
                    {
                        // Up close the tag fades so it doesn't cover the teammate; still readable.
                        float alpha = Mathf.Lerp(0.45f, 1f, Mathf.InverseLerp(4f, 18f, d));
                        string label = pawn.CurrentShipSeat != null ? name + "  -  " + pawn.CurrentShipSeat.Role.ToString().ToUpperInvariant() : name;
                        m.Marker.Place(cam, markerLayer, head, Icons.Person, label, c, true, alpha);
                    }
                }

                float pf = Mathf.Clamp01((pawn.transform.position.x - sectorStartX) / Mathf.Max(1f, sectorLen));
                m.Pip.enabled = true;
                m.Pip.color = down ? HudKit.Danger : c;
                m.Pip.rectTransform.anchoredPosition = new Vector2(pf * meterTrack.rect.width, 12f);
            }

            // Players that left: drop their marker and pip.
            var gone = new List<ulong>();
            foreach (var kv in mates) if (!seen.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (var id in gone)
            {
                Object.Destroy(mates[id].Marker.Root.gameObject);
                Object.Destroy(mates[id].Pip.gameObject);
                mates.Remove(id);
            }

            UpdatePanel(multi, local);
        }

        void UpdatePanel(bool multi, PlayerPawn local)
        {
            if (panel.gameObject.activeSelf != multi) panel.gameObject.SetActive(multi);
            if (!multi) return;
            while (rows.Count < crew.Count) rows.Add(MakeRow());
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                bool on = i < crew.Count;
                if (r.Rt.gameObject.activeSelf != on) r.Rt.gameObject.SetActive(on);
                if (!on) continue;
                var np = crew[i];
                var pawn = PawnOf(np);
                Color c = np.CrewColor;
                r.Swatch.color = c;
                r.Name.text = np.PlayerName.Value.ToString() + (np.IsOwner ? "  <size=75%><color=#94A3B8>YOU</color></size>" : "");
                r.Name.color = c;
                float hp = pawn != null && pawn.Health != null ? pawn.Health.Fraction : 0f;
                r.HpFill.fillAmount = hp;
                r.HpFill.color = hp < 0.25f ? HudKit.Danger : HudKit.Health;
                r.Status.text = Status(pawn, local, np.IsOwner, out Color sc);
                r.Status.color = sc;
            }
        }

        static string Status(PlayerPawn pawn, PlayerPawn local, bool self, out Color c)
        {
            c = HudKit.TextDim;
            if (pawn == null || !pawn.Active.Value) return "LOBBY";
            if (pawn.IsLost.Value) { c = HudKit.TextDim; return "LOST"; }
            if (pawn.IsDown.Value)
            {
                c = HudKit.Danger;
                float t = pawn.RespawnAt.Value - Time.time;
                return t > 0f ? "DOWN " + HudKit.Num(Mathf.CeilToInt(t) + "s") : "DOWN";
            }
            if (pawn.CurrentShipSeat != null) { c = HudKit.Shield; return pawn.CurrentShipSeat.Role.ToString().ToUpperInvariant(); }
            if (self || local == null) return "";
            return HudKit.Dist(Vector3.Distance(local.transform.position, pawn.transform.position));
        }

        Row MakeRow()
        {
            var r = new Row();
            var row = UIKit.Row(panel, RowHeight, 8);
            r.Rt = row;
            UIKit.LayoutElement(row.gameObject, PanelWidth - 28f, RowHeight);
            r.Swatch = UIKit.Image(row, "Swatch", Color.white, false);
            r.Swatch.sprite = HudKit.Diamond;
            r.Swatch.raycastTarget = false;
            UIKit.LayoutElement(r.Swatch.gameObject, 14, 14);
            r.Name = UIKit.Text(row, "", 17, TextAlignmentOptions.Left, HudKit.TextHi, FontStyles.Bold);
            r.Name.textWrappingMode = TextWrappingModes.NoWrap;
            r.Name.overflowMode = TextOverflowModes.Ellipsis;
            UIKit.LayoutElement(r.Name.gameObject, 150, RowHeight);
            var track = UIKit.Image(row, "Hp", HudKit.Inset, false);
            track.sprite = HudKit.Soft; track.type = Image.Type.Sliced;
            UIKit.LayoutElement(track.gameObject, 70, 6);
            r.HpFill = UIKit.Image(track.transform, "Fill", HudKit.Health, false);
            UIKit.Stretch(r.HpFill.rectTransform, 1, 1, 1, 1);
            r.HpFill.sprite = UIKit.White; r.HpFill.type = Image.Type.Filled; r.HpFill.fillMethod = Image.FillMethod.Horizontal;
            r.Status = HudKit.Micro(row, "", 13f, HudKit.TextDim, TextAlignmentOptions.Right);
            UIKit.LayoutElement(r.Status.gameObject, 76, RowHeight);
            return r;
        }
    }
}
