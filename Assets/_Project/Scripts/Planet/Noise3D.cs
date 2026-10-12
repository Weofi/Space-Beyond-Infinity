using UnityEngine;

namespace Voidward.Planet
{
    /// <summary>Seeded 3D gradient (improved Perlin) noise in roughly [-1,1]. Deterministic per seed.</summary>
    public sealed class Noise3D
    {
        readonly int[] perm = new int[512];

        public Noise3D(int seed)
        {
            var rng = new System.Random(seed);
            for (int i = 0; i < 256; i++) perm[i] = i;
            for (int i = 255; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (perm[i], perm[j]) = (perm[j], perm[i]);
            }
            for (int i = 0; i < 256; i++) perm[256 + i] = perm[i];
        }

        static int Floor(float v) { int i = (int)v; return v < i ? i - 1 : i; }
        static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

        static float Grad(int hash, float x, float y, float z)
        {
            int h = hash & 15;
            float u = h < 8 ? x : y;
            float v = h < 4 ? y : (h == 12 || h == 14 ? x : z);
            return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
        }

        public float Eval(Vector3 p) => Eval(p.x, p.y, p.z);

        public float Eval(float x, float y, float z)
        {
            int xi = Floor(x), yi = Floor(y), zi = Floor(z);
            x -= xi; y -= yi; z -= zi;
            xi &= 255; yi &= 255; zi &= 255;
            float u = Fade(x), v = Fade(y), w = Fade(z);
            int[] p = perm;
            int a = p[xi] + yi, aa = p[a] + zi, ab = p[a + 1] + zi;
            int b = p[xi + 1] + yi, ba = p[b] + zi, bb = p[b + 1] + zi;
            float x1 = Lerp(Grad(p[aa], x, y, z), Grad(p[ba], x - 1, y, z), u);
            float x2 = Lerp(Grad(p[ab], x, y - 1, z), Grad(p[bb], x - 1, y - 1, z), u);
            float y1 = Lerp(x1, x2, v);
            x1 = Lerp(Grad(p[aa + 1], x, y, z - 1), Grad(p[ba + 1], x - 1, y, z - 1), u);
            x2 = Lerp(Grad(p[ab + 1], x, y - 1, z - 1), Grad(p[bb + 1], x - 1, y - 1, z - 1), u);
            return Lerp(y1, Lerp(x1, x2, v), w);
        }

        static float Lerp(float a, float b, float t) => a + (b - a) * t;
    }
}
