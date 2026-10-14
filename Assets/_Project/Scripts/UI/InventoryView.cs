using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using Voidward.Core;
using Voidward.Data;
using Voidward.Inventory;
using Voidward.Player;
using Voidward.Ship;
using Voidward.World;

namespace Voidward.UI
{
    /// <summary>Tab panel: personal inventory + ship cargo, ship modules & services, crafting, the crew's warp drive (#78).</summary>
    public class InventoryView
    {
        public enum Tab { Inventory, Ship, Crafting, Drive }
        const int TabCount = 4;

        readonly RectTransform panel;
        RectTransform body;
        readonly TextMeshProUGUI header, details;
        readonly Button[] tabs = new Button[TabCount];
        Tab tab;
        int selected = -1;
        bool selectedInCargo;
        float refreshTimer;
        bool dirty = true;
        NetInventory trackedInv, trackedCargo;

        public bool IsOpen => panel.gameObject.activeSelf;

        public InventoryView(RectTransform root)
        {
            panel = UIKit.Panel(root, "InventoryPanel");
            UIKit.Place(panel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1500, 900));
            UIKit.VLayout(panel.gameObject, 10, 22);

            var top = UIKit.Row(panel, 56);
            header = UIKit.Text(top, "", 30, TextAlignmentOptions.Left, UIKit.Accent, FontStyles.Bold);
            UIKit.LayoutElement(header.gameObject, -1, 56, 1);
            string[] names = { "Inventory", "Ship", "Crafting", "Warp Drive" };
            for (int i = 0; i < TabCount; i++)
            {
                var t = (Tab)i;
                tabs[i] = UIKit.Button(top, names[i], () => { tab = t; selected = -1; dirty = true; }, 52, UIKit.PanelLight * 2.5f, 22);
                UIKit.LayoutElement(tabs[i].gameObject, 170, 52);
            }
            UIKit.LayoutElement(UIKit.Button(top, "Close [Tab]", Close, 52, UIKit.Bad, 22).gameObject, 170, 52);

            body = UIKit.Rect(panel, "Body");
            UIKit.LayoutElement(body.gameObject, -1, 640, 1, 1);
            details = UIKit.Text(panel, "", 22, TextAlignmentOptions.Left, UIKit.TextDim);
            UIKit.LayoutElement(details.gameObject, -1, 90);
            panel.gameObject.SetActive(false);
        }

        public void Open(Tab t)
        {
            tab = t;
            selected = -1;
            dirty = true;
            panel.gameObject.SetActive(true);
            Sfx.PlayUI("ui_click");
        }

        public void Close()
        {
            if (!IsOpen) return;
            panel.gameObject.SetActive(false);
        }

        static ShipCore AccessibleShip(PlayerPawn pawn)
        {
            if (pawn == null) return null;
            if (pawn.CurrentShipSeat != null) return pawn.CurrentShipSeat.Ship;
            var own = ShipCore.OwnedBy(pawn.OwnerClientId);
            if (own != null && Vector3.Distance(own.transform.position, pawn.transform.position) < 30f) return own;
            var near = ShipCore.Nearest(pawn.transform.position, 30f);
            return near;
        }

        public void Update()
        {
            if (!IsOpen) return;
            var pawn = PlayerPawn.Local;
            if (pawn == null) { Close(); return; }
            var ship = AccessibleShip(pawn);
            Track(pawn.Inventory, ship != null ? ship.Cargo : null);
            refreshTimer -= Time.unscaledDeltaTime;
            if (refreshTimer <= 0f) { refreshTimer = 1f; dirty = true; }
            if (!dirty) return;
            dirty = false;
            for (int i = 0; i < TabCount; i++) UIKit.SetButtonColor(tabs[i], (int)tab == i ? UIKit.Primary : UIKit.Secondary);
            header.text = $"{pawn.Inventory.Credits.Value} credits   <size=70%><color=#b0b0d0>{(ship != null ? "Ship in range: " + ship.ShipName.Value : "No ship in range")}</color></size>";
            Object.Destroy(body.gameObject);
            body = UIKit.Rect(panel, "Body");
            body.SetSiblingIndex(1);
            UIKit.LayoutElement(body.gameObject, -1, 640, 1, 1);
            switch (tab)
            {
                case Tab.Inventory: BuildInventory(pawn, ship); break;
                case Tab.Ship: BuildShip(pawn, ship); break;
                case Tab.Crafting: BuildCrafting(pawn); break;
                case Tab.Drive: BuildDrive(pawn, ship); break;
            }
        }

        void Track(NetInventory inv, NetInventory cargo)
        {
            if (trackedInv != inv)
            {
                if (trackedInv != null) trackedInv.Changed -= MarkDirty;
                trackedInv = inv;
                if (inv != null) inv.Changed += MarkDirty;
            }
            if (trackedCargo != cargo)
            {
                if (trackedCargo != null) trackedCargo.Changed -= MarkDirty;
                trackedCargo = cargo;
                if (cargo != null) cargo.Changed += MarkDirty;
                dirty = true;
            }
        }

        void MarkDirty() => dirty = true;

        // ------------------------------------------------------------ inventory tab

        void BuildInventory(PlayerPawn pawn, ShipCore ship)
        {
            UIKit.HLayout(body.gameObject, 30, 0, TextAnchor.UpperLeft);
            var left = UIKit.Rect(body, "Player");
            UIKit.VLayout(left.gameObject, 8, 0, TextAnchor.UpperLeft);
            UIKit.LayoutElement(left.gameObject, 680, 640);
            UIKit.LayoutElement(UIKit.Text(left, "BACKPACK", 24, TextAlignmentOptions.Left, UIKit.TextDim, FontStyles.Bold).gameObject, -1, 30);
            GridFor(left, pawn.Inventory, false, pawn.Inventory.Width);

            var actions = UIKit.Row(left, 52);
            var selStack = SelectedStack(pawn.Inventory, false);
            var def = selStack.Def;
            if (def != null && !selectedInCargo)
            {
                int slot = selected;
                if (def.Kind != ItemKind.Resource)
                    UIKit.LayoutElement(UIKit.Button(actions, def.Kind == ItemKind.Module ? "Install" : "Use", () => pawn.Inventory.UseSlotServerRpc(slot), 50, UIKit.Good, 22).gameObject, 150, 50);
                UIKit.LayoutElement(UIKit.Button(actions, "Drop", () => { pawn.Inventory.DropSlotServerRpc(slot, pawn.transform.position + pawn.transform.forward * 1.5f + pawn.transform.up); selected = -1; }, 50, UIKit.Bad, 22).gameObject, 130, 50);
                if (ship != null && ship.Cargo != null)
                    UIKit.LayoutElement(UIKit.Button(actions, "To cargo \u2192", () => { pawn.Inventory.TransferServerRpc(slot, ship.Cargo.NetworkObject); selected = -1; }, 50, UIKit.Accent, 22).gameObject, 190, 50);
            }

            var right = UIKit.Rect(body, "Cargo");
            UIKit.VLayout(right.gameObject, 8, 0, TextAnchor.UpperLeft);
            UIKit.LayoutElement(right.gameObject, 700, 640);
            if (ship == null || ship.Cargo == null)
            {
                UIKit.LayoutElement(UIKit.Text(right, "Get close to a ship (or sit in one) to access its cargo hold, modules and fabricator.", 24, TextAlignmentOptions.Left, UIKit.TextDim).gameObject, -1, 120);
            }
            else
            {
                UIKit.LayoutElement(UIKit.Text(right, $"CARGO - {ship.ShipName.Value}", 24, TextAlignmentOptions.Left, UIKit.TextDim, FontStyles.Bold).gameObject, -1, 30);
                var scroll = UIKit.Scroll(right, 470);
                GridFor(scroll, ship.Cargo, true, 6);
                var row = UIKit.Row(right, 52);
                var cs = SelectedStack(ship.Cargo, true);
                if (!cs.IsEmpty && selectedInCargo)
                {
                    int slot = selected;
                    UIKit.LayoutElement(UIKit.Button(row, "\u2190 To backpack", () => { ship.Cargo.TransferServerRpc(slot, pawn.Inventory.NetworkObject); selected = -1; }, 50, UIKit.Accent, 22).gameObject, 220, 50);
                }
            }
            ShowDetails(selectedInCargo && ship != null && ship.Cargo != null ? SelectedStack(ship.Cargo, true) : selStack);
        }

        ItemStack SelectedStack(NetInventory inv, bool cargo)
        {
            if (inv == null || selected < 0 || selectedInCargo != cargo || selected >= inv.Slots.Count) return default;
            return inv.Slots[selected];
        }

        void GridFor(Transform parent, NetInventory inv, bool cargo, int columns)
        {
            var gridRt = UIKit.Rect(parent, "Grid");
            var g = UIKit.Grid(gridRt.gameObject, new Vector2(104, 96), new Vector2(8, 8), columns);
            int rows = Mathf.CeilToInt(inv.Slots.Count / (float)columns);
            UIKit.LayoutElement(gridRt.gameObject, columns * 112, rows * 104);
            for (int i = 0; i < inv.Slots.Count; i++)
            {
                int idx = i;
                var st = inv.Slots[i];
                var def = st.Def;
                bool sel = selected == i && selectedInCargo == cargo;
                var cell = UIKit.Image(gridRt, "Cell", sel ? UIKit.Accent * new Color(1, 1, 1, 0.8f) : UIKit.PanelLight);
                var b = cell.gameObject.AddComponent<Button>();
                b.onClick.AddListener(() =>
                {
                    Sfx.PlayUI("ui_click", 0.5f);
                    if (selected == idx && selectedInCargo == cargo) selected = -1;
                    else if (selected >= 0 && selectedInCargo == cargo && !cargo) { inv.MoveSlotServerRpc(selected, idx); selected = -1; }
                    else { selected = idx; selectedInCargo = cargo; }
                    dirty = true;
                });
                if (def != null)
                {
                    int tier = UIKit.TierOf(def);
                    if (tier > 0)
                    {
                        var edge = UIKit.Image(cell.transform, "Tier", UIKit.TierColor(tier), false);
                        UIKit.Place(edge.rectTransform, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0, 4), new Vector2(0.5f, 0f));
                        edge.rectTransform.anchorMin = new Vector2(0f, 0f); edge.rectTransform.anchorMax = new Vector2(1f, 0f);
                        edge.raycastTarget = false;
                        var tl = UIKit.Text(cell.transform, UIKit.TierLabel(tier), 15, TextAlignmentOptions.TopRight, UIKit.TierColor(tier), FontStyles.Bold);
                        UIKit.Stretch(tl.rectTransform, 4, 6, 3, 4);
                        tl.raycastTarget = false;
                    }
                    var icon = UIKit.ItemIcon(cell.transform, def, 58);
                    UIKit.Place(icon.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -4), new Vector2(58, 58), new Vector2(0.5f, 1f));
                    var t = UIKit.Text(cell.transform, ShortName(def), 15, TextAlignmentOptions.Bottom, UIKit.TextDim);
                    UIKit.Stretch(t.rectTransform, 4, 4, 60, 5);
                    t.raycastTarget = false;
                    if (st.Count > 1)
                    {
                        var q = UIKit.Text(cell.transform, "x" + st.Count, 18, TextAlignmentOptions.TopLeft, UIKit.TextColor, FontStyles.Bold);
                        UIKit.Stretch(q.rectTransform, 6, 4, 3, 4);
                        q.raycastTarget = false;
                    }
                }
            }
        }

        static string ShortName(ItemDef d)
        {
            string n = d.DisplayName;
            return n.Length > 14 ? n.Substring(0, 13) + "." : n;
        }

        void ShowDetails(ItemStack st)
        {
            var d = st.Def;
            if (d == null) { details.text = "Click an item to select it. Click another backpack slot to move/stack it. Tab closes."; return; }
            string extra = d.Module != null ? "\n" + d.Module.StatsSummary().Replace("\n", "  |  ") : "";
            details.text = $"<b><color={UIKit.Hex(d.Tint)}>{d.DisplayName}</color></b>  x{st.Count}   <size=80%>value {d.CreditValue} cr</size>\n{d.Description}{extra}";
        }

        // ------------------------------------------------------------ ship tab

        void BuildShip(PlayerPawn pawn, ShipCore ship)
        {
            UIKit.VLayout(body.gameObject, 8, 0, TextAnchor.UpperLeft);
            if (ship == null)
            {
                UIKit.LayoutElement(UIKit.Text(body, "No ship in range. Walk up to (or sit in) a ship.", 26, TextAlignmentOptions.Left, UIKit.TextDim).gameObject, -1, 60);
                details.text = "";
                return;
            }
            var s = ship.Stats;
            var stats = UIKit.Text(body,
                $"<b>{ship.ShipName.Value}</b>   Hull {ship.Hull.Hp.Value:0}/{ship.Hull.MaxHp.Value:0}   Shield {ship.Shield.Value:0}/{s.MaxShield:0}   Fuel {ship.Fuel.Value:0}/{s.MaxFuel:0}   " +
                $"Thrust {s.Thrust:0}   Cargo {s.CargoSlots} slots   Scanner {s.Scanner:0} m\n" +
                $"<size=85%><color=#b0b0d0>Aim assist {s.AimAssist:0} deg   Shots x{1 + s.ExtraShots}   Hit radius x{s.HitboxMult:0.#}   Fire rate x{s.RateMult:0.##}</color></size>", 22);
            UIKit.LayoutElement(stats.gameObject, -1, 60);

            var cols = UIKit.Rect(body, "Cols");
            UIKit.HLayout(cols.gameObject, 30, 0, TextAnchor.UpperLeft);
            UIKit.LayoutElement(cols.gameObject, -1, 560);

            var mods = UIKit.Rect(cols, "Modules");
            UIKit.VLayout(mods.gameObject, 6, 0, TextAnchor.UpperLeft);
            UIKit.LayoutElement(mods.gameObject, 720, 560);
            UIKit.LayoutElement(UIKit.Text(mods, "INSTALLED MODULES", 22, TextAlignmentOptions.Left, UIKit.TextDim, FontStyles.Bold).gameObject, -1, 28);
            for (int i = 0; i < ShipCore.ModuleSlotCount; i++)
            {
                int slot = i;
                var row = UIKit.Panel(mods, "Slot", UIKit.PanelLight);
                UIKit.HLayout(row.gameObject, 10, 8);
                UIKit.LayoutElement(row.gameObject, -1, 46);
                var m = ship.ModuleIn(i);
                string slotName = ((ModuleSlot)i).ToString().Replace("WeaponA", "Weapon 1").Replace("WeaponB", "Weapon 2").Replace("FuelTank", "Fuel Tank").Replace("WeaponMod", "Weapon Mod");
                var t = UIKit.Text(row, $"<color=#a0a0c0>{slotName}</color>   {(m != null ? $"<b>{m.DisplayName}</b>" : "<color=#707090>empty</color>")}", 21);
                UIKit.LayoutElement(t.gameObject, -1, 38, 1);
                if (m != null)
                    UIKit.LayoutElement(UIKit.Button(row, "Remove", () => ship.UninstallServerRpc(slot), 36, UIKit.Accent2, 20).gameObject, 130, 36);
            }

            var side = UIKit.Rect(cols, "Side");
            UIKit.VLayout(side.gameObject, 8, 0, TextAnchor.UpperLeft);
            UIKit.LayoutElement(side.gameObject, 660, 560);
            UIKit.LayoutElement(UIKit.Text(side, "SERVICES", 22, TextAlignmentOptions.Left, UIKit.TextDim, FontStyles.Bold).gameObject, -1, 28);
            var sr = UIKit.Row(side, 54);
            UIKit.LayoutElement(UIKit.Button(sr, $"Repair (Scrap: {pawn.Inventory.Count("scrap")})", () => ship.RepairServerRpc(), 52, UIKit.Good, 22).gameObject, 320, 52);
            UIKit.LayoutElement(UIKit.Button(sr, $"Refuel (Fuel: {pawn.Inventory.Count("fuel")})", () => ship.RefuelServerRpc(), 52, UIKit.Warn, 22).gameObject, 320, 52);
            UIKit.LayoutElement(UIKit.Text(side, "MODULES IN BACKPACK", 22, TextAlignmentOptions.Left, UIKit.TextDim, FontStyles.Bold).gameObject, -1, 28);
            var list = UIKit.Scroll(side, 400);
            int count = 0;
            for (int i = 0; i < pawn.Inventory.Slots.Count; i++)
            {
                var st = pawn.Inventory.Slots[i];
                var d = st.Def;
                if (d == null || d.Kind != ItemKind.Module || d.Module == null) continue;
                int slot = i;
                var row = UIKit.Row(list, 50);
                var t = UIKit.Text(row, $"<b>{d.DisplayName}</b> <size=75%><color=#b0b0d0>{d.Module.StatsSummary().Replace("\n", ", ")}</color></size>", 20);
                UIKit.LayoutElement(t.gameObject, -1, 48, 1);
                UIKit.LayoutElement(UIKit.Button(row, "Install", () => ship.InstallFromInventoryServerRpc(slot, false), 44, UIKit.Good, 20).gameObject, 120, 44);
                if (d.Module.Weapon != WeaponKind.None)
                    UIKit.LayoutElement(UIKit.Button(row, "Slot 2", () => ship.InstallFromInventoryServerRpc(slot, true), 44, UIKit.Accent, 20).gameObject, 100, 44);
                count++;
            }
            if (count == 0) UIKit.Text(list, "No modules - find them in crates, buy them at stations or craft them.", 20, TextAlignmentOptions.Left, UIKit.TextDim);
            details.text = "Modules change your ship's stats immediately. Weapons: Laser (fast), Missiles (homing), Flak (area). Targeting CPUs widen aim assist; Weapon Mods add shots, hit radius or fire rate. Big ships like the Ark have more slots for crew stations.";
        }

        // ------------------------------------------------------------ warp drive tab (#78)

        static string Tier(DriveSlot slot, int tier) => DriveComponentDef.FullName(slot, tier);

        static string Effect(DriveSlot slot, DriveStats s)
        {
            switch (slot)
            {
                case DriveSlot.Core: return $"Warp speed x{s.Speed:0.#}";
                case DriveSlot.Navigation: return $"Safe in fields: {Mathf.RoundToInt(s.NavFraction * 100f)}% of core speed";
                case DriveSlot.Cooling: return $"Sustained throttle: {Mathf.RoundToInt(s.Sustain * 100f)}%";
                default: return $"Holds {s.Capacity} Exotic Matter";
            }
        }

        void BuildDrive(PlayerPawn pawn, ShipCore ship)
        {
            UIKit.VLayout(body.gameObject, 8, 0, TextAnchor.UpperLeft);
            var drive = WarpDrive.Instance;
            var rm = RunManager.Instance;
            if (drive == null || rm == null)
            {
                UIKit.LayoutElement(UIKit.Text(body, "No warp drive.", 26, TextAlignmentOptions.Left, UIKit.TextDim).gameObject, -1, 60);
                return;
            }
            var s = drive.Stats;
            int g = rm.GalaxyIndex.Value;
            bool last = g >= GalaxyDef.Count;
            string next = DriveMath.Eta(drive.EtaTo(g + 1));
            string skip = g + 2 <= GalaxyDef.Count ? DriveMath.Eta(drive.EtaTo(g + 2)) : "-";
            var head = UIKit.Text(body,
                $"<b>HOME ETA  <color=#F59E0B>{DriveMath.Eta(drive.EtaTo(DriveMath.Home))}</color></b>   " +
                $"<size=85%><color=#b0b0d0>{(last ? "Home" : "Next galaxy")} {next}   Skip {skip}   Build efficiency {Mathf.RoundToInt(100f / Mathf.Max(0.01f, s.Factor))}%</color></size>\n" +
                $"<size=85%>Exotic Matter <b>{Mathf.FloorToInt(drive.Exotic.Value)}/{drive.Capacity}</b>   <color=#b0b0d0>next warp needs {WarpDrive.FuelTo(g + 1)}" +
                $"{(g + 2 <= GalaxyDef.Count ? $", skipping needs {WarpDrive.FuelTo(g + 2)}" : "")}</color></size>", 24);
            UIKit.LayoutElement(head.gameObject, -1, 66);

            var cols = UIKit.Rect(body, "Cols");
            UIKit.HLayout(cols.gameObject, 30, 0, TextAnchor.UpperLeft);
            UIKit.LayoutElement(cols.gameObject, -1, 540);

            var parts = UIKit.Rect(cols, "Parts");
            UIKit.VLayout(parts.gameObject, 6, 0, TextAnchor.UpperLeft);
            UIKit.LayoutElement(parts.gameObject, 720, 540);
            UIKit.LayoutElement(UIKit.Text(parts, "INSTALLED COMPONENTS", 22, TextAlignmentOptions.Left, UIKit.TextDim, FontStyles.Bold).gameObject, -1, 28);
            for (int i = 0; i < 4; i++)
            {
                var slot = (DriveSlot)i;
                var row = UIKit.Panel(parts, "Part", UIKit.PanelLight);
                UIKit.HLayout(row.gameObject, 10, 8);
                UIKit.LayoutElement(row.gameObject, -1, 64);
                var item = ContentCatalog.GetItem(DriveComponentDef.ItemId(slot, drive.TierOf(slot)));
                if (item != null) UIKit.ItemIcon(row, item, 48);
                var t = UIKit.Text(row, $"<color=#a0a0c0>{DriveComponentDef.SlotName(slot)}</color>   <b>{Tier(slot, drive.TierOf(slot))}</b>\n<size=80%><color=#b0b0d0>{Effect(slot, s)}</color></size>", 21);
                UIKit.LayoutElement(t.gameObject, -1, 58, 1);
            }
            int have = pawn.Inventory.Count(WarpDrive.ExoticId);
            var load = UIKit.Button(parts, $"Load Exotic Matter (backpack: {have})", () => drive.LoadExoticServerRpc(), 56, UIKit.Accent, 22);
            load.interactable = ship != null && have > 0 && drive.Exotic.Value + 1f <= drive.Capacity;
            UIKit.LayoutElement(load.gameObject, -1, 56);

            var side = UIKit.Rect(cols, "Side");
            UIKit.VLayout(side.gameObject, 8, 0, TextAnchor.UpperLeft);
            UIKit.LayoutElement(side.gameObject, 660, 540);
            UIKit.LayoutElement(UIKit.Text(side, "DRIVE PARTS IN BACKPACK", 22, TextAlignmentOptions.Left, UIKit.TextDim, FontStyles.Bold).gameObject, -1, 28);
            var list = UIKit.Scroll(side, 480);
            float home = drive.EtaTo(DriveMath.Home);
            int count = 0;
            for (int i = 0; i < pawn.Inventory.Slots.Count; i++)
            {
                var d = pawn.Inventory.Slots[i].Def;
                if (d == null || d.Kind != ItemKind.DriveComponent || d.Drive == null) continue;
                int invSlot = i;
                var with = s.With(d.Drive.Slot, d.Drive.Tier, WarpDrive.Def);
                float delta = WarpDrive.EtaTo(with, DriveMath.Home) - home;
                string col = delta < -0.01f ? "#90f0a0" : delta > 0.01f ? "#ff90a0" : "#b0b0d0";
                var row = UIKit.Row(list, 56);
                UIKit.ItemIcon(row, d, 48);
                var t = UIKit.Text(row, $"<b>{d.DisplayName}</b>\n<size=80%><color={col}>Home {DriveMath.Delta(delta)}</color> <color=#b0b0d0>{Effect(d.Drive.Slot, with)}</color></size>", 20);
                UIKit.LayoutElement(t.gameObject, -1, 54, 1);
                var b = UIKit.Button(row, "Install", () => drive.InstallFromInventoryServerRpc(invSlot), 44, UIKit.Good, 20);
                b.interactable = ship != null;
                UIKit.LayoutElement(b.gameObject, 120, 44);
                count++;
            }
            if (count == 0) UIKit.Text(list, "No drive parts - Warden enclaves guard the best warp cores; crates, wrecks and Nomad traders have nav computers, cooling and tanks.", 20, TextAlignmentOptions.Left, UIKit.TextDim);
            details.text = ship != null
                ? "The whole crew shares one warp drive. The core sets the top speed; nav computer and cooling decide how much of it you can really fly - match them to the core. Swapping returns the old part."
                : "Get to a ship (or sit in one) to install parts or load Exotic Matter.";
        }

        // ------------------------------------------------------------ crafting tab

        void BuildCrafting(PlayerPawn pawn)
        {
            UIKit.VLayout(body.gameObject, 8, 0, TextAnchor.UpperLeft);
            bool fab = Fabricators.AnyNear(pawn.transform.position, Fabricators.UseRange);
            UIKit.LayoutElement(UIKit.Text(body, fab ? "<color=#90f0a0>Fabricator online</color>" : "<color=#ff8090>No fabricator nearby - stand next to (or sit in) a ship</color>", 24).gameObject, -1, 34);
            var list = UIKit.Scroll(body, 590);
            var cat = ContentCatalog.Instance;
            if (cat == null) return;
            for (int i = 0; i < cat.Recipes.Length; i++)
            {
                var r = cat.Recipes[i];
                if (r == null || r.Output == null) continue;
                int idx = i;
                bool can = PlayerGear.CanAfford(pawn.Inventory, r);
                var row = UIKit.Panel(list, "Recipe", UIKit.PanelLight);
                UIKit.HLayout(row.gameObject, 12, 8);
                UIKit.LayoutElement(row.gameObject, -1, 64);
                UIKit.ItemIcon(row, r.Output, 52);
                var sb = new System.Text.StringBuilder();
                foreach (var ing in r.Inputs)
                {
                    if (ing.Item == null) continue;
                    int have = pawn.Inventory.Count(ing.Item.Id);
                    sb.Append($"<color={(have >= ing.Count ? "#a0f0b0" : "#ff90a0")}>{ing.Count}x {ing.Item.DisplayName} ({have})</color>   ");
                }
                var t = UIKit.Text(row, $"<b>{r.Output.DisplayName}</b>{(r.OutputCount > 1 ? " x" + r.OutputCount : "")}\n<size=80%>{sb}</size>", 22);
                UIKit.LayoutElement(t.gameObject, -1, 60, 1);
                var b = UIKit.Button(row, "Craft", () => pawn.Gear.CraftServerRpc(idx), 50, UIKit.Good, 22);
                b.interactable = can && fab;
                UIKit.LayoutElement(b.gameObject, 140, 50);
            }
            details.text = "Exotic Matter fuels the warp drive. Medkits heal (hotbar 4). Gear upgrades are used from the backpack.";
        }
    }
}
