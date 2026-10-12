using UnityEngine;

namespace Voidward.Core
{
    /// <summary>
    /// Deterministic math helpers: seeded hash noise (value noise 3D + fbm) used by terrain
    /// generation so all clients produce identical geometry from the same seed.
    /// </summary>
    public static class MathUtils
    {
        /// <summary>Deterministic 3D lattice hash -> [0,1).</summary>
        public static float Hash3(int seed, int x, int y, int z)
        {
            unchecked
            {
                uint h = (uint)seed * 0x9E3779B1u;
                h ^= (uint)x * 0x85EBCA6Bu; h = (h << 13) | (h >> 19); h *= 0xC2B2AE35u;
                h ^= (uint)y * 0x27D4EB2Fu; h = (h << 15) | (h >> 17); h *= 0x165667B1u;
                h ^= (uint)z * 0x9E3779B9u; h = (h << 13) | (h >> 19); h *= 0x27D4EB2Fu;
                h ^= h >> 16;
                return (h & 0x00FFFFFF) / 16777216f;
            }
        }

        static float Smooth(float t) => t * t * (3f - 2f * t);

        /// <summary>Seeded 3D value noise in [-1,1].</summary>
        public static float ValueNoise3D(int seed, Vector3 p)
        {
            int x0 = Mathf.FloorToInt(p.x), y0 = Mathf.FloorToInt(p.y), z0 = Mathf.FloorToInt(p.z);
            float fx = Smooth(p.x - x0), fy = Smooth(p.y - y0), fz = Smooth(p.z - z0);

            float c000 = Hash3(seed, x0, y0, z0),     c100 = Hash3(seed, x0 + 1, y0, z0);
            float c010 = Hash3(seed, x0, y0 + 1, z0), c110 = Hash3(seed, x0 + 1, y0 + 1, z0);
            float c001 = Hash3(seed, x0, y0, z0 + 1), c101 = Hash3(seed, x0 + 1, y0, z0 + 1);
            float c011 = Hash3(seed, x0, y0 + 1, z0 + 1), c111 = Hash3(seed, x0 + 1, y0 + 1, z0 + 1);

            float x00 = Mathf.Lerp(c000, c100, fx), x10 = Mathf.Lerp(c010, c110, fx);
            float x01 = Mathf.Lerp(c001, c101, fx), x11 = Mathf.Lerp(c011, c111, fx);
            float y0v = Mathf.Lerp(x00, x10, fy), y1v = Mathf.Lerp(x01, x11, fy);
            return Mathf.Lerp(y0v, y1v, fz) * 2f - 1f;
        }

        /// <summary>Fractal Brownian motion over value noise, roughly [-1,1].</summary>
        public static float Fbm3D(int seed, Vector3 p, int octaves, float frequency, float lacunarity = 2f, float gain = 0.5f)
        {
            float amp = 1f, freq = frequency, sum = 0f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += ValueNoise3D(seed + i * 131, p * freq) * amp;
                norm += amp;
                amp *= gain;
                freq *= lacunarity;
            }
            return norm > 0f ? sum / norm : 0f;
        }

        public static float Remap(float v, float inMin, float inMax, float outMin, float outMax)
        {
            return outMin + Mathf.Clamp01((v - inMin) / Mathf.Max(0.00001f, inMax - inMin)) * (outMax - outMin);
        }

        /// <summary>Deterministic System.Random stream from a seed (use for gameplay scatter, not terrain math).</summary>
        public static System.Random Rng(int seed) => new System.Random(seed);

        public static float Range(System.Random r, float min, float max) => min + (float)r.NextDouble() * (max - min);
        public static int Range(System.Random r, int minInclusive, int maxExclusive) => r.Next(minInclusive, maxExclusive);

        /// <summary>Uniform random point on a sphere, deterministic per rng.</summary>
        public static Vector3 OnSphere(System.Random r)
        {
            float z = Range(r, -1f, 1f);
            float t = Range(r, 0f, Mathf.PI * 2f);
            float s = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
            return new Vector3(s * Mathf.Cos(t), s * Mathf.Sin(t), z);
        }

        public static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c;
        }

        public static Color WithAlpha(this Color c, float a) => new Color(c.r, c.g, c.b, a);
    }
}
