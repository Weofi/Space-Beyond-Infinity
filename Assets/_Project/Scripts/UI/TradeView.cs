using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using Voidward.Core;
using Voidward.Data;
using Voidward.Player;
using Voidward.Ship;
using Voidward.World;

namespace Voidward.UI
{
    /// <summary>Trader station panel: buy stock, sell items, repair & refuel service.</summary>
    public class TradeView
    {
        readonly RectTransform panel;
        RectTransform body;
        readonly TextMeshProUGUI header;
        TraderStation station;
        float timer;

        public bool IsOpen => panel.gameObject.activeSelf;

        public TradeView(RectTransform root)
        {
            panel = UIKit.Panel(root, "TradePanel");
            UIKit.Place(panel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1400, 860));
            UIKit.VLayout(panel.gameObject, 10, 22);
            var top = UIKit.Row(panel, 56);
            header = UIKit.Text(top, "", 30, TextAlignmentOptions.Left, UIKit.Warn, FontStyles.Bold);
            UIKit.LayoutElement(header.gameObject, -1, 56, 1);
            UIKit.LayoutElement(UIKit.Button(top, "Close", Close, 52, UIKit.Bad, 22).gameObject, 160, 52);
            body = UIKit.Rect(panel, "Body");
            panel.gameObject.SetActive(false);
        }

        public void Open(ulong stationId)
        {
            station = null;
            var nm = NetworkManager.Singleton;
            if (nm != null && nm.SpawnManager.SpawnedObjects.TryGetValue(stationId, out var no)) station = no.GetComponent<TraderStation>();
            if (station == null) return;
            panel.gameObject.SetActive(true);
            timer = 0f;
            Sfx.PlayUI("trade");
        }

        public void Close() { if (IsOpen) panel.gameObject.SetActive(false); }

        public void Update()
        {
            if (!IsOpen) return;
            var pawn = PlayerPawn.Local;
            if (pawn == null || station == null || Vector3.Distance(pawn.transform.position, station.transform.position) > TraderStation.UseRange) { Close(); return; }
            timer -= Time.unscaledDeltaTime;
            if (timer > 0f) return;
            timer = 0.5f;
            Rebuild(pawn);
        }

        void Rebuild(PlayerPawn pawn)
        {
            header.text = $"TRADER STATION   <size=75%><color=#ffe0a0>{pawn.Inventory.Salvage.Value} salvage</color></size>";
            Object.Destroy(body.gameObject);
            body = UIKit.Rect(panel, "Body");
            UIKit.LayoutElement(body.gameObject, -1, 740, 1, 1);
            UIKit.HLayout(body.gameObject, 26, 0, TextAnchor.UpperLeft);

            var buy = UIKit.Rect(body, "Buy");
            UIKit.VLayout(buy.gameObject, 8, 0, TextAnchor.UpperLeft);
            UIKit.LayoutElement(buy.gameObject, 660, 740);
            UIKit.LayoutElement(UIKit.Text(buy, "BUY", 24, TextAlignmentOptions.Left, UIKit.TextDim, FontStyles.Bold).gameObject, -1, 30);
            var bl = UIKit.Scroll(buy, 560);
            var stock = station.Stock;
            for (int i = 0; i < stock.Count; i++)
            {
                var d = stock[i];
                int idx = i;
                int price = TraderStation.BuyPrice(d);
                var row = UIKit.Panel(bl, "Item", UIKit.PanelLight);
                UIKit.HLayout(row.gameObject, 10, 8);
                UIKit.LayoutElement(row.gameObject, -1, 62);
                UIKit.ItemIcon(row, d, 52);
                string desc = d.Module != null ? d.Module.StatsSummary().Replace("\n", ", ") : d.Description;
                var t = UIKit.Text(row, $"<b>{d.DisplayName}</b>\n<size=75%><color=#b0b0d0>{desc}</color></size>", 21);
                UIKit.LayoutElement(t.gameObject, -1, 58, 1);
                var b = UIKit.Button(row, $"{price}", () => station.BuyServerRpc(idx), 48, UIKit.Warn, 22, Icons.Salvage);
                b.interactable = pawn.Inventory.Salvage.Value >= price;
                UIKit.LayoutElement(b.gameObject, 130, 48).minWidth = 130; // long descriptions must not squeeze the price
            }
            var svc = UIKit.Button(buy, $"Repair, refuel & recharge my ship ({TraderStation.ServiceCost} salvage)", () => station.ServiceServerRpc(), 56, UIKit.Good, 22);
            svc.interactable = pawn.Inventory.Salvage.Value >= TraderStation.ServiceCost;

            var sell = UIKit.Rect(body, "Sell");
            UIKit.VLayout(sell.gameObject, 8, 0, TextAnchor.UpperLeft);
            UIKit.LayoutElement(sell.gameObject, 660, 740);
            UIKit.LayoutElement(UIKit.Text(sell, "SELL (from backpack)", 24, TextAlignmentOptions.Left, UIKit.TextDim, FontStyles.Bold).gameObject, -1, 30);
            var sl = UIKit.Scroll(sell, 620);
            int n = 0;
            for (int i = 0; i < pawn.Inventory.Slots.Count; i++)
            {
                var st = pawn.Inventory.Slots[i];
                var d = st.Def;
                if (d == null) continue;
                int slot = i;
                var row = UIKit.Panel(sl, "Item", UIKit.PanelLight);
                UIKit.HLayout(row.gameObject, 10, 8);
                UIKit.LayoutElement(row.gameObject, -1, 56);
                UIKit.ItemIcon(row, d, 46);
                var t = UIKit.Text(row, $"<b>{d.DisplayName}</b> x{st.Count}  <size=75%><color=#ffe0a0>{TraderStation.SellPrice(d)} salvage each</color></size>", 21);
                UIKit.LayoutElement(t.gameObject, -1, 50, 1);
                UIKit.LayoutElement(UIKit.Button(row, "Sell 1", () => station.SellServerRpc(slot, false), 44, UIKit.Accent, 20).gameObject, 100, 44);
                UIKit.LayoutElement(UIKit.Button(row, "All", () => station.SellServerRpc(slot, true), 44, UIKit.Accent2, 20).gameObject, 80, 44);
                n++;
            }
            if (n == 0) UIKit.Text(sl, "Nothing to sell.", 22, TextAlignmentOptions.Left, UIKit.TextDim);
        }
    }

    /// <summary>Engineer station: power distribution (engines/shields/weapons), repair and refuel.</summary>
    public class EngineerView
    {
        readonly RectTransform panel;
        readonly TextMeshProUGUI info;
        readonly Slider engines, shields, weapons;
        ShipCore ship;
        bool suppress;

        public bool IsOpen => panel.gameObject.activeSelf;

        public EngineerView(RectTransform root)
        {
            panel = UIKit.Panel(root, "EngineerPanel");
            UIKit.Place(panel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900, 700));
            UIKit.VLayout(panel.gameObject, 12, 26);
            var t = UIKit.Text(panel, "ENGINEERING", 40, TextAlignmentOptions.Center, UIKit.Accent, FontStyles.Bold);
            UIKit.LayoutElement(t.gameObject, -1, 56);
            info = UIKit.Text(panel, "", 22, TextAlignmentOptions.Center, UIKit.TextDim);
            UIKit.LayoutElement(info.gameObject, -1, 90);
            engines = Row("Engines", UIKit.Warn);
            shields = Row("Shields", new Color(0.5f, 0.75f, 1f));
            weapons = Row("Weapons", UIKit.Bad);
            var r = UIKit.Row(panel, 60);
            r.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
            UIKit.Button(r, "Balanced", () => SetPower(Vector3.one), 56, UIKit.PanelLight * 2.5f, 22);
            UIKit.Button(r, "Repair (Scrap)", () => ship?.RepairServerRpc(), 56, UIKit.Good, 22);
            UIKit.Button(r, "Refuel (Fuel)", () => ship?.RefuelServerRpc(), 56, UIKit.Warn, 22);
            var hint = UIKit.Text(panel, "Close the panel to look around; LMB re-opens it. [F] leaves the station.", 20, TextAlignmentOptions.Center, UIKit.TextDim);
            UIKit.LayoutElement(hint.gameObject, -1, 40);
            UIKit.Button(panel, "Close", Close, 56, UIKit.Bad);
            panel.gameObject.SetActive(false);
        }

        Slider Row(string label, Color c)
        {
            var row = UIKit.Row(panel, 56);
            var t = UIKit.Text(row, label, 26, TextAlignmentOptions.Left, c, FontStyles.Bold);
            UIKit.LayoutElement(t.gameObject, 200, 50);
            var s = UIKit.Slider(row, 0.4f, 1.8f, 1f, v => { if (!suppress) Push(); });
            return s;
        }

        void Push()
        {
            if (ship == null) return;
            ship.SetPowerServerRpc(new Vector3(engines.value, shields.value, weapons.value));
        }

        void SetPower(Vector3 p)
        {
            suppress = true;
            engines.value = p.x; shields.value = p.y; weapons.value = p.z;
            suppress = false;
            Push();
        }

        public void Open(ulong shipId)
        {
            ship = null;
            var nm = NetworkManager.Singleton;
            if (nm != null && nm.SpawnManager.SpawnedObjects.TryGetValue(shipId, out var no)) ship = no.GetComponent<ShipCore>();
            if (ship == null) return;
            suppress = true;
            var p = ship.Power.Value;
            engines.value = p.x; shields.value = p.y; weapons.value = p.z;
            suppress = false;
            panel.gameObject.SetActive(true);
        }

        public void Close() { if (IsOpen) panel.gameObject.SetActive(false); }

        public void Update()
        {
            if (!IsOpen) return;
            var pawn = PlayerPawn.Local;
            if (ship == null || pawn == null || pawn.CurrentShipSeat == null || pawn.CurrentShipSeat.Ship != ship) { Close(); return; }
            var s = ship.Stats;
            float sum = engines.value + shields.value + weapons.value;
            info.text = $"{ship.ShipName.Value}\nHull {ship.Hull.Hp.Value:0}/{ship.Hull.MaxHp.Value:0}   Shield {ship.Shield.Value:0}/{s.MaxShield:0}   Fuel {ship.Fuel.Value:0}/{s.MaxFuel:0}\n" +
                       $"<color={(sum > 3.6f ? "#ff8090" : "#a0f0b0")}>Power budget {sum:0.0} / 3.6</color>";
        }
    }
}
