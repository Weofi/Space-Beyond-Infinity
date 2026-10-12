using TMPro;
using UnityEngine;
using Voidward.Core;

namespace Voidward.Ship
{
    /// <summary>Diegetic cockpit instruments (world-space TMP), refreshed only when the local player is close.</summary>
    public class ShipDashboard : MonoBehaviour
    {
        public TextMeshPro Left;
        public TextMeshPro Right;
        public TextMeshPro Center;

        ShipCore ship;
        float timer;

        void Awake() => ship = GetComponentInParent<ShipCore>();

        void Update()
        {
            if (ship == null || !ship.IsSpawned) return;
            timer -= Time.deltaTime;
            if (timer > 0f) return;
            timer = 0.2f;
            var cam = Camera.main;
            bool near = cam != null && (cam.transform.position - transform.position).sqrMagnitude < 40f * 40f;
            if (Left) Left.enabled = near;
            if (Right) Right.enabled = near;
            if (Center) Center.enabled = near;
            if (!near) return;

            var s = ship.Stats;
            float spd = ship.Flight != null ? ship.Flight.Speed : 0f;
            float alt = ship.Flight != null ? ship.Flight.Altitude : 9999f;
            if (Left)
                Left.text = $"<b>HULL</b> {Mathf.CeilToInt(ship.Hull.Hp.Value)}/{Mathf.CeilToInt(ship.Hull.MaxHp.Value)}\n" +
                            $"<b>SHLD</b> {(ship.ShieldsOffline.Value ? "<color=#ff6080>OFFLINE</color>" : Mathf.CeilToInt(ship.Shield.Value) + "/" + Mathf.CeilToInt(s.MaxShield))}\n" +
                            $"<b>FUEL</b> {Mathf.CeilToInt(ship.Fuel.Value)}/{Mathf.CeilToInt(s.MaxFuel)}";
            if (Center)
                Center.text = $"<size=140%>{spd:0}</size> m/s\n{(alt < 2000f ? $"ALT {alt:0} m" : "DEEP SPACE")}";
            if (Right)
            {
                float collapse = Mathf.Max(0f, ship.transform.position.x - GameState.CollapseX);
                string gate = GameState.HasGate ? $"GATE {Vector3.Distance(ship.transform.position, GameState.GatePosition):0} m" : "GATE --";
                string col = collapse < 400f ? "#ff5070" : collapse < 1000f ? "#ffc060" : "#80ffd0";
                Right.text = $"<b>COLLAPSE</b>\n<color={col}>{(collapse > 50000f ? "--" : collapse.ToString("0"))} m</color>\n{gate}";
            }
        }
    }
}
