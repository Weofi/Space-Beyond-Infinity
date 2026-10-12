using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using Voidward.Core;
using Voidward.Data;
using Voidward.Inventory;
using Voidward.Player;
using Voidward.Ship;

namespace Voidward.UI
{
    /// <summary>Tab panel: personal inventory + ship cargo, ship modules & services, crafting.</summary>
    public class InventoryView
    {
        public enum Tab { Inventory, Ship, Crafting }

        readonly RectTransform panel;
        RectTransform body;
        readonly TextMeshProUGUI header, details;
        readonly Button[] tabs = new Button[3];
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
            string[] names = { "Inventory", "Ship", "Crafting" };
            for (int i = 0; i < 3; i++)
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
            for (int i = 0; i < 3; i++) UIKit.SetButtonColor(tabs[i], (int)tab == i ? UIKit.Primary : UIKit.Secondary);
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
                    var swatch = UIKit.Image(cell.transform, "Tint", def.Tint);
                    UIKit.Place(swatch.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -10), new Vector2(40, 30), new Vector2(0.5f, 1f));
                    swatch.raycastTarget = false;
                    var t = UIKit.Text(cell.transform, ShortName(def) + $"\n<b>x{st.Count}</b>", 17, TextAlignmentOptions.Bottom);
                    UIKit.Stretch(t.rectTransform, 4, 4, 40, 6);
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
                $"Thrust {s.Thrust:0}   Cargo {s.CargoSlots} slots   Scanner {s.Scanner:0} m", 22);
            UIKit.LayoutElement(stats.gameObject, -1, 40);

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
                UIKit.LayoutElement(row.gameObject, -1, 54);
                var m = ship.ModuleIn(i);
                string slotName = ((ModuleSlot)i).ToString().Replace("WeaponA", "Weapon 1").Replace("WeaponB", "Weapon 2").Replace("FuelTank", "Fuel Tank");
                var t = UIKit.Text(row, $"<color=#a0a0c0>{slotName}</color>   {(m != null ? $"<b>{m.DisplayName}</b>" : "<color=#707090>empty</color>")}", 22);
                UIKit.LayoutElement(t.gameObject, -1, 44, 1);
                if (m != null)
                    UIKit.LayoutElement(UIKit.Button(row, "Remove", () => ship.UninstallServerRpc(slot), 42, UIKit.Accent2, 20).gameObject, 130, 42);
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
            details.text = "Modules change your ship's stats immediately. Weapons: Laser (fast), Missiles (homing), Flak (area). Big ships like the Ark have more slots for crew stations.";
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
                var sw = UIKit.Image(row, "Tint", r.Output.Tint);
                UIKit.LayoutElement(sw.gameObject, 36, 36);
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
            details.text = "Energy Cells power Jump Gates. Medkits heal (hotbar 4). Gear upgrades are used from the backpack.";
        }
    }
}
