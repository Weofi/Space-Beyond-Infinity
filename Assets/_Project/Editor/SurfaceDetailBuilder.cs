using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Voidward.Core;

namespace Voidward.EditorTools
{
    /// <summary>
    /// Hard-surface detail for VW_ToonFlat's _SURFACEDETAIL_ON path: a tileable panel/trim sheet projected
    /// triplanar in object space. Mask: R = panel seam, G = per-panel random id, B = grime blotches, A = micro noise
    /// (edge-wear threshold + gloss breakup). The normal map carries seams, layered plate heights and rivet rows.
    /// </summary>
    public static partial class ContentBuilder
    {
        public static Material MatHull;

        static void BuildHullMaterial()
        {
            BuildHullDetailTextures(out var mask, out var normal);
            MatHull = Mat("VW_Hull", "VOIDWARD/ToonFlat");
            MatHull.CopyPropertiesFromMaterial(MatToon);
            MatHull.shaderKeywords = new[] { "_USEVERTEXCOLOR_ON", "_SURFACEDETAIL_ON" };
            MatHull.SetFloat("_FlatShading", 0f);
            MatHull.SetFloat("_SurfaceDetail", 1f);
            MatHull.SetTexture("_DetailMask", mask);
            MatHull.SetTexture("_DetailNormal", normal);
            MatHull.SetFloat("_PanelScale", 0.42f);
            MatHull.SetFloat("_PanelNormalStrength", 1f);
            MatHull.SetFloat("_SeamDarken", 0.5f);
            MatHull.SetFloat("_PanelTint", 0.05f);
            MatHull.SetFloat("_GrimeStrength", 0.4f);
            MatHull.SetFloat("_EdgeWear", 0.7f);
            MatHull.SetFloat("_EdgeHighlight", 0.22f);
            MatHull.SetColor("_WearColor", new Color(0.6f, 0.64f, 0.7f));
            MatHull.SetFloat("_Softness", 0.85f);
            MatHull.SetFloat("_SpecStrength", 1f);
            MatHull.SetFloat("_ReflStrength", 1f);
            MatHull.SetFloat("_RimStrength", 0.22f);
            MatHull.enableInstancing = true;
        }

        static void BuildHullDetailTextures(out Texture2D mask, out Texture2D normal)
        {
            const int size = 1024;
            var rng = new System.Random(2601);

            // 1) Panel layout: recursive kd-split of the tile. Splits on the tile border keep it seamless.
            var rects = new List<RectInt>();
            void Split(RectInt r, int depth)
            {
                bool canX = r.width > 150, canY = r.height > 150;
                bool stop = depth > 1 && rng.NextDouble() < 0.12 + depth * 0.08;
                if ((!canX && !canY) || stop) { rects.Add(r); return; }
                bool splitX = canX && (!canY || (r.width > r.height ? rng.NextDouble() < 0.8 : rng.NextDouble() < 0.2));
                if (splitX)
                {
                    int s = Mathf.RoundToInt(r.width * (0.3f + (float)rng.NextDouble() * 0.4f));
                    Split(new RectInt(r.x, r.y, s, r.height), depth + 1);
                    Split(new RectInt(r.x + s, r.y, r.width - s, r.height), depth + 1);
                }
                else
                {
                    int s = Mathf.RoundToInt(r.height * (0.3f + (float)rng.NextDouble() * 0.4f));
                    Split(new RectInt(r.x, r.y, r.width, s), depth + 1);
                    Split(new RectInt(r.x, r.y + s, r.width, r.height - s), depth + 1);
                }
            }
            Split(new RectInt(0, 0, size, size), 0);

            var height = new float[size * size];
            var seam = new float[size * size];
            var id = new float[size * size];
            const float seamHalf = 2.2f;
            foreach (var r in rects)
            {
                float pid = (float)rng.NextDouble();
                float plate = (float)rng.NextDouble() < 0.35 ? 0.35f : 0f;                   // some plates sit proud of their neighbours
                bool rivets = rng.NextDouble() < 0.45 && r.width > 120 && r.height > 120;
                bool hatch = rng.NextDouble() < 0.25 && r.width > 220 && r.height > 180;      // inset service hatch
                var inner = new RectInt(r.x + r.width / 4, r.y + r.height / 4, r.width / 2, r.height / 2);
                for (int y = r.yMin; y < r.yMax; y++)
                    for (int x = r.xMin; x < r.xMax; x++)
                    {
                        float d = Mathf.Min(Mathf.Min(x - r.xMin + 0.5f, r.xMax - 0.5f - x), Mathf.Min(y - r.yMin + 0.5f, r.yMax - 0.5f - y));
                        float s = 1f - Mathf.SmoothStep(seamHalf - 1f, seamHalf + 1f, d);
                        float h = plate - s * 1.2f;
                        // Bevelled plate rim: the plate slopes down over a few pixels into the seam.
                        h -= (1f - Mathf.SmoothStep(seamHalf, seamHalf + 7f, d)) * 0.35f;
                        if (rivets)
                        {
                            const float inset = 11f, spacing = 26f;
                            float ex = Mathf.Abs(d - inset);
                            if (ex < 4f)
                            {
                                // distance along the border, so rivets line up in rows
                                float along = (d == x - r.xMin + 0.5f || d == r.xMax - 0.5f - x) ? y : x;
                                float t = Mathf.Repeat(along, spacing) - spacing * 0.5f;
                                float rr = Mathf.Sqrt(ex * ex + t * t);
                                h += Mathf.Clamp01(1f - rr / 3.6f) * 0.6f;
                            }
                        }
                        if (hatch && inner.Contains(new Vector2Int(x, y)))
                        {
                            float di = Mathf.Min(Mathf.Min(x - inner.xMin + 0.5f, inner.xMax - 0.5f - x), Mathf.Min(y - inner.yMin + 0.5f, inner.yMax - 0.5f - y));
                            float si = 1f - Mathf.SmoothStep(0.6f, 2.4f, di);
                            h -= si * 0.9f + 0.12f;
                            s = Mathf.Max(s, si * 0.8f);
                        }
                        int i = y * size + x;
                        height[i] = h;
                        seam[i] = s;
                        id[i] = pid;
                    }
            }

            var maskPx = new Color[size * size];
            var normalPx = new Color[size * size];
            float Hs(int x, int y) => height[((y + size) % size) * size + (x + size) % size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;
                    float grime = TileNoise(u * 3f, v * 3f, 3, 811) * 0.6f + TileNoise(u * 9f, v * 9f, 9, 812) * 0.3f + TileNoise(u * 27f, v * 27f, 27, 813) * 0.1f;
                    float micro = TileNoise(u * 48f, v * 48f, 48, 821) * 0.55f + TileNoise(u * 128f, v * 128f, 128, 822) * 0.45f;
                    int i = y * size + x;
                    maskPx[i] = new Color(seam[i], id[i], grime, micro);
                    // Sobel gradient; micro noise adds a faint brushed/cast texture.
                    float dx = (Hs(x + 1, y - 1) + 2f * Hs(x + 1, y) + Hs(x + 1, y + 1)) - (Hs(x - 1, y - 1) + 2f * Hs(x - 1, y) + Hs(x - 1, y + 1));
                    float dy = (Hs(x - 1, y + 1) + 2f * Hs(x, y + 1) + Hs(x + 1, y + 1)) - (Hs(x - 1, y - 1) + 2f * Hs(x, y - 1) + Hs(x + 1, y - 1));
                    dx += (micro - 0.5f) * 0.05f; dy += (grime - 0.5f) * 0.03f;
                    var n = new Vector3(-dx * 0.35f, -dy * 0.35f, 1f).normalized;
                    normalPx[i] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
                }
            mask = SaveTexture("HullDetailMask", size, maskPx);
            normal = SaveTexture("HullDetailNormal", size, normalPx);
        }
    }
}
