using UnityEngine;
using Voidward.Core;

namespace Voidward.Data
{
    /// <summary>Effective numbers of a warp drive build (#78), see WarpDriveDef for the model.</summary>
    public struct DriveStats
    {
        public int Core, Nav, Cooling, Tank;
        /// <summary>Core speed relative to Mk I.</summary>
        public float Speed;
        /// <summary>Fraction of the core speed safely flyable in fields.</summary>
        public float NavFraction;
        /// <summary>Sustainable throttle in free space (1 = full).</summary>
        public float Sustain;
        /// <summary>Exotic Matter tank capacity.</summary>
        public int Capacity;
        /// <summary>Travel time multiplier vs. a balanced build (1 = balanced, higher = slower).</summary>
        public float Factor;

        public int Tier(DriveSlot slot) =>
            slot == DriveSlot.Core ? Core : slot == DriveSlot.Navigation ? Nav : slot == DriveSlot.Cooling ? Cooling : Tank;

        public DriveStats With(DriveSlot slot, int tier, WarpDriveDef d) =>
            DriveMath.Compute(d, slot == DriveSlot.Core ? tier : Core, slot == DriveSlot.Navigation ? tier : Nav,
                slot == DriveSlot.Cooling ? tier : Cooling, slot == DriveSlot.Tank ? tier : Tank);
    }

    /// <summary>Home ETA, leg times and fuel need of the journey home (#78). Pure math, same on every peer.</summary>
    public static class DriveMath
    {
        /// <summary>Route stop after the last galaxy: home (Milky Way / Sol).</summary>
        public const int Home = GalaxyDef.Count + 1;

        public static DriveStats Compute(WarpDriveDef d, int core, int nav, int cooling, int tank)
        {
            var s = new DriveStats { Core = core, Nav = nav, Cooling = cooling, Tank = tank };
            if (d == null || core < 1) { s.Speed = 0f; s.Factor = 99f; return s; }
            s.Speed = d.CoreSpeed[Mathf.Clamp(core, 1, d.CoreSpeed.Length) - 1];
            s.NavFraction = Mathf.Clamp(d.NavMatched + d.NavStep * (nav - core), d.NavMin, 1f);
            s.Sustain = Mathf.Clamp(1f + d.CoolingStep * (cooling - core), d.CoolingMin, 1f);
            s.Capacity = tank < 1 ? 0 : d.TankCapacity[Mathf.Clamp(tank, 1, d.TankCapacity.Length) - 1];
            s.Factor = (d.FieldShare / s.NavFraction + (1f - d.FieldShare) / s.Sustain) / d.BalancedFactor;
            return s;
        }

        /// <summary>Flight minutes from galaxy `from` to stop `to` (to = Home for the flight home). Infinity without a core.</summary>
        public static float Minutes(DriveStats s, int from, int to)
        {
            if (s.Speed <= 0f) return float.PositiveInfinity;
            float len = 0f;
            for (int g = Mathf.Max(1, from); g < Mathf.Min(to, Home); g++)
            {
                var def = ContentCatalog.GetGalaxy(g);
                if (def != null) len += def.LegLength;
            }
            return len * s.Factor / s.Speed;
        }

        /// <summary>Flight minutes of a warp between two systems of a galaxy (#80). Infinity without a core.</summary>
        public static float HopMinutes(WarpDriveDef d, DriveStats s)
        {
            if (d == null || s.Speed <= 0f) return float.PositiveInfinity;
            return d.HopMinutes * s.Factor / Mathf.Pow(s.Speed, d.HopSpeedExponent);
        }

        /// <summary>System cruise speed (m/s) of a core tier (#80).</summary>
        public static float CruiseSpeed(WarpDriveDef d, int coreTier)
        {
            if (d == null || d.CruiseSpeed == null || d.CruiseSpeed.Length == 0) return 1000f;
            return d.CruiseSpeed[Mathf.Clamp(coreTier, 1, d.CruiseSpeed.Length) - 1];
        }

        /// <summary>Exotic Matter needed from galaxy `from` to stop `to`; every leg beyond the first costs a surcharge.</summary>
        public static int Fuel(WarpDriveDef d, int from, int to)
        {
            float sum = 0f;
            int legs = 0;
            for (int g = Mathf.Max(1, from); g < Mathf.Min(to, Home); g++)
            {
                var def = ContentCatalog.GetGalaxy(g);
                if (def != null) sum += def.LegFuel;
                legs++;
            }
            float surcharge = d != null ? d.SkipFuelSurcharge : 0.25f;
            return Mathf.CeilToInt(sum * (1f + surcharge * Mathf.Max(0, legs - 1)));
        }

        /// <summary>ETA text: "10:30" below an hour, "9 h 50 min" above, "--" when unreachable.</summary>
        public static string Eta(float minutes)
        {
            if (float.IsInfinity(minutes) || float.IsNaN(minutes)) return "--";
            int sec = Mathf.Max(0, Mathf.RoundToInt(minutes * 60f));
            if (sec < 3600) return $"{sec / 60}:{sec % 60:00}";
            int min = Mathf.RoundToInt(minutes);
            return $"{min / 60} h {min % 60:00} min";
        }

        /// <summary>Signed ETA change for part tooltips, e.g. "-2 h 10 min", "+4:30", "+-0".</summary>
        public static string Delta(float minutes)
        {
            if (float.IsInfinity(minutes) || float.IsNaN(minutes)) return "--";
            if (Mathf.Abs(minutes) < 1f / 60f) return "+-0";
            return (minutes < 0f ? "-" : "+") + Eta(Mathf.Abs(minutes));
        }
    }
}
