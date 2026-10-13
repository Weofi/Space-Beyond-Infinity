using UnityEngine;

namespace Voidward.Core
{
    /// <summary>
    /// Per-player crew colours (#60): one fixed colour per session slot, used for the suit light strips,
    /// HUD markers, compass, collapse-meter pips, crew panel and lobby list. Chosen to stay apart from the
    /// HUD's semantic colours (gold gate, red danger, cyan own ship), the blue suit and each other.
    /// </summary>
    public static class CrewPalette
    {
        public const int Slots = 4;

        static readonly Color[] colors =
        {
            new Color(0.976f, 0.451f, 0.086f), // 0 orange #F97316
            new Color(0.639f, 0.902f, 0.208f), // 1 lime   #A3E635
            new Color(0.925f, 0.282f, 0.600f), // 2 pink   #EC4899
            new Color(0.176f, 0.831f, 0.749f), // 3 teal   #2DD4BF
        };

        public static Color Get(int slot) => colors[Mathf.Abs(slot) % colors.Length];
    }
}
