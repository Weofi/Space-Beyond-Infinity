using System.Collections.Generic;
using UnityEngine;

namespace Voidward.Core
{
    /// <summary>
    /// Builds vertex-colored meshes from parametric parts (boxes, prisms, wedges, rocks, lathes, lofts, slabs).
    /// Every triangle gets its own vertices. With <see cref="SmoothAngle"/> = 0 the result is crisp facets
    /// (rocks, decor); with SmoothAngle &gt; 0 Build computes area-weighted normals that stay split across
    /// edges sharper than the angle, so chamfers read as soft highlights on hard surfaces (hero assets).
    /// All Add* calls take a Matrix4x4 local transform (TRS). Primitives are wound outward-facing
    /// relative to their own centre, so any convex part renders correctly with back-face culling.
    /// Per-vertex surface data, read by VW_ToonFlat:
    ///   UV0 = (gloss, glow, metal, edge)  edge = 1 on chamfer faces (edge wear / highlight)
    ///   UV1 = (panel, occlusion)          panel = strength of the procedural panel/trim detail
    /// </summary>
    public class MeshBuilder
    {
        public const float DefaultGloss = 0.3f;

        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<int> tris = new List<int>();
        readonly List<Color> colors = new List<Color>();
        readonly List<Vector4> surface = new List<Vector4>();
        readonly List<Vector2> detail = new List<Vector2>();
        readonly List<int> partner = new List<int>(); // per triangle: the other half of its quad, or -1
        float edgeFlag;

        /// <summary>Chamfer size for AddBox as a fraction of the box's smallest dimension (0 = sharp edges, max ~0.35).</summary>
        public float Bevel;
        /// <summary>Specular strength for subsequently added geometry (0 = matte rubber, 1 = mirror-like visor).</summary>
        public float Gloss = DefaultGloss;
        /// <summary>Emission for subsequently added geometry (vertex color drives the glow tint).</summary>
        public float Glow;
        /// <summary>0 = painted/plastic (white spec), 1 = bare metal (tinted spec, darker diffuse).</summary>
        public float Metal;
        /// <summary>Strength of the procedural panel lines / trim detail on subsequently added geometry (0..1).</summary>
        public float Panel;
        /// <summary>Normals: 0 = flat facets; otherwise faces meeting at less than this angle (degrees) are smoothed.</summary>
        public float SmoothAngle;

        public int VertexCount => verts.Count;

        public static Matrix4x4 TRS(Vector3 pos, Quaternion rot, Vector3 scale) =>
            Matrix4x4.TRS(pos, rot, scale);

        public static Matrix4x4 TRS(Vector3 pos) => Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one);

        /// <summary>Sets gloss/glow/metal for the following Add* calls. Call without args to reset to defaults.</summary>
        public MeshBuilder Surface(float gloss = DefaultGloss, float glow = 0f, float metal = 0f)
        {
            Gloss = gloss; Glow = glow; Metal = metal;
            return this;
        }

        void AddVert(Vector3 v, Color c)
        {
            verts.Add(v); colors.Add(c);
            surface.Add(new Vector4(Gloss, Glow, Metal, edgeFlag));
            detail.Add(new Vector2(Panel, 1f));
        }

        void AddTri(Vector3 a, Vector3 b, Vector3 c, Color col)
        {
            int i = verts.Count;
            AddVert(a, col); AddVert(b, col); AddVert(c, col);
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
            partner.Add(-1);
        }

        public void AddTriangle(Vector3 a, Vector3 b, Vector3 c, Color col) => AddTri(a, b, c, col);

        public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col)
        {
            int t = partner.Count;
            AddTri(a, b, c, col);
            AddTri(a, c, d, col);
            partner[t] = t + 1; partner[t + 1] = t;
        }

        // Unity's front face normal is cross(b - a, c - a); flip whenever it points towards the part's centre.
        void TriOut(Vector3 a, Vector3 b, Vector3 c, Vector3 center, Color col)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), (a + b + c) / 3f - center) < 0f) AddTri(a, c, b, col);
            else AddTri(a, b, c, col);
        }

        void QuadOut(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 center, Color col)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), (a + b + c + d) * 0.25f - center) < 0f) AddQuad(a, d, c, b, col);
            else AddQuad(a, b, c, d, col);
        }

        // Winding chosen so the face normal agrees with an explicit facing direction (concave parts, inner surfaces).
        void TriFacing(Vector3 a, Vector3 b, Vector3 c, Vector3 n, Color col)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), n) < 0f) AddTri(a, c, b, col);
            else AddTri(a, b, c, col);
        }

        void QuadFacing(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Color col)
        {
            if (Vector3.Dot(Vector3.Cross(c - a, d - b), n) < 0f) AddQuad(a, d, c, b, col);
            else AddQuad(a, b, c, d, col);
        }

        /// <summary>Box of given size, transformed by m. Edges are chamfered when <see cref="Bevel"/> &gt; 0.</summary>
        public void AddBox(Matrix4x4 m, Vector3 size, Color col)
        {
            Vector3 h = size * 0.5f;
            float b = Mathf.Clamp(Bevel, 0f, 0.35f) * Mathf.Min(size.x, Mathf.Min(size.y, size.z));
            Vector3 center = m.MultiplyPoint3x4(Vector3.zero);
            if (b > 0.004f) { AddChamferBox(m, h, b, center, col); return; }
            Vector3[] c =
            {
                new Vector3(-h.x,-h.y,-h.z), new Vector3( h.x,-h.y,-h.z),
                new Vector3( h.x, h.y,-h.z), new Vector3(-h.x, h.y,-h.z),
                new Vector3(-h.x,-h.y, h.z), new Vector3( h.x,-h.y, h.z),
                new Vector3( h.x, h.y, h.z), new Vector3(-h.x, h.y, h.z),
            };
            for (int i = 0; i < 8; i++) c[i] = m.MultiplyPoint3x4(c[i]);
            QuadOut(c[0], c[1], c[2], c[3], center, col); // back
            QuadOut(c[5], c[4], c[7], c[6], center, col); // front
            QuadOut(c[4], c[0], c[3], c[7], center, col); // left
            QuadOut(c[1], c[5], c[6], c[2], center, col); // right
            QuadOut(c[3], c[2], c[6], c[7], center, col); // top
            QuadOut(c[4], c[5], c[1], c[0], center, col); // bottom
        }

        void AddChamferBox(Matrix4x4 m, Vector3 h, float b, Vector3 center, Color col)
        {
            // P = corner vertex lying on the face perpendicular to `axis` (0=x, 1=y, 2=z).
            Vector3 P(int sx, int sy, int sz, int axis)
            {
                var p = new Vector3(sx * (h.x - b), sy * (h.y - b), sz * (h.z - b));
                if (axis == 0) p.x = sx * h.x; else if (axis == 1) p.y = sy * h.y; else p.z = sz * h.z;
                return m.MultiplyPoint3x4(p);
            }
            for (int s = -1; s <= 1; s += 2)
            {
                QuadOut(P(s, -1, -1, 0), P(s, 1, -1, 0), P(s, 1, 1, 0), P(s, -1, 1, 0), center, col);
                QuadOut(P(-1, s, -1, 1), P(1, s, -1, 1), P(1, s, 1, 1), P(-1, s, 1, 1), center, col);
                QuadOut(P(-1, -1, s, 2), P(1, -1, s, 2), P(1, 1, s, 2), P(-1, 1, s, 2), center, col);
                edgeFlag = 1f;
                for (int t = -1; t <= 1; t += 2)
                {
                    QuadOut(P(s, t, -1, 0), P(s, t, 1, 0), P(s, t, 1, 1), P(s, t, -1, 1), center, col);
                    QuadOut(P(s, -1, t, 0), P(s, 1, t, 0), P(s, 1, t, 2), P(s, -1, t, 2), center, col);
                    QuadOut(P(-1, s, t, 1), P(1, s, t, 1), P(1, s, t, 2), P(-1, s, t, 2), center, col);
                    for (int u = -1; u <= 1; u += 2)
                        TriOut(P(s, t, u, 0), P(s, t, u, 1), P(s, t, u, 2), center, col);
                }
                edgeFlag = 0f;
            }
        }

        /// <summary>Wedge (triangular prism): a box cut diagonally. +Z stays full height, -Z slopes to the floor.</summary>
        public void AddWedge(Matrix4x4 m, Vector3 size, Color col)
        {
            Vector3 h = size * 0.5f;
            Vector3 b0 = m.MultiplyPoint3x4(new Vector3(-h.x, -h.y, -h.z));
            Vector3 b1 = m.MultiplyPoint3x4(new Vector3(h.x, -h.y, -h.z));
            Vector3 b2 = m.MultiplyPoint3x4(new Vector3(h.x, -h.y, h.z));
            Vector3 b3 = m.MultiplyPoint3x4(new Vector3(-h.x, -h.y, h.z));
            Vector3 t2 = m.MultiplyPoint3x4(new Vector3(h.x, h.y, h.z));
            Vector3 t3 = m.MultiplyPoint3x4(new Vector3(-h.x, h.y, h.z));
            Vector3 center = (b0 + b1 + b2 + b3 + t2 + t3) / 6f;
            QuadOut(b3, b2, t2, t3, center, col);       // tall front face
            QuadOut(b0, b1, b2, b3, center, col);       // bottom
            TriOut(b0, b3, t3, center, col);            // left triangle
            TriOut(b2, b1, t2, center, col);            // right triangle
            QuadOut(b1, b0, t3, t2, center, col);       // sloped top
        }

        /// <summary>Cylinder / prism / cone between two radii along +Y.</summary>
        public void AddPrism(Matrix4x4 m, float rBottom, float rTop, float height, int sides, Color col, bool capBottom = true, bool capTop = true)
        {
            sides = Mathf.Max(3, sides);
            float hy = height * 0.5f;
            Vector3 center = m.MultiplyPoint3x4(Vector3.zero);
            Vector3[] ringB = new Vector3[sides];
            Vector3[] ringT = new Vector3[sides];
            for (int i = 0; i < sides; i++)
            {
                float a = (i / (float)sides) * Mathf.PI * 2f;
                float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                ringB[i] = m.MultiplyPoint3x4(new Vector3(ca * rBottom, -hy, sa * rBottom));
                ringT[i] = m.MultiplyPoint3x4(new Vector3(ca * rTop, hy, sa * rTop));
            }
            for (int i = 0; i < sides; i++)
            {
                int n = (i + 1) % sides;
                if (rBottom < 1e-4f) TriOut(ringB[i], ringT[n], ringT[i], center, col);
                else if (rTop < 1e-4f) TriOut(ringB[i], ringB[n], ringT[i], center, col);
                else QuadOut(ringB[i], ringB[n], ringT[n], ringT[i], center, col);
                if (capBottom && rBottom > 1e-4f) TriOut(m.MultiplyPoint3x4(new Vector3(0, -hy, 0)), ringB[n], ringB[i], center, col);
                if (capTop && rTop > 1e-4f) TriOut(m.MultiplyPoint3x4(new Vector3(0, hy, 0)), ringT[i], ringT[n], center, col);
            }
        }

        /// <summary>Cylinder (or cone) between two points, e.g. struts, cables, antennas, barrels.</summary>
        public void AddTube(Vector3 a, Vector3 b, float rA, float rB, int sides, Color col, bool caps = true)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 1e-5f) return;
            AddPrism(Matrix4x4.TRS((a + b) * 0.5f, Quaternion.FromToRotation(Vector3.up, d / len), Vector3.one), rA, rB, len, sides, col, caps, caps);
        }

        /// <summary>
        /// Surface of revolution around local +Y. profile = (radius, y) points. Faces point to the right of the
        /// travel direction in the (r, y) plane: walk up the outside (normals face out), over a lip and back down
        /// the inside (normals face in) - so nozzles, bells, rings and helmets work without extra flags.
        /// Closed profiles (first == last) make tori and rings.
        /// </summary>
        public void AddLathe(Matrix4x4 m, IList<Vector2> profile, int sides, Color col, float startDeg = 0f, float sweepDeg = 360f)
        {
            sides = Mathf.Max(3, sides);
            bool full = sweepDeg >= 359.9f;
            int cols = full ? sides : sides + 1;
            var rings = new Vector3[profile.Count, cols];
            for (int j = 0; j < profile.Count; j++)
                for (int i = 0; i < cols; i++)
                {
                    float a = (startDeg + sweepDeg * i / sides) * Mathf.Deg2Rad;
                    rings[j, i] = m.MultiplyPoint3x4(new Vector3(Mathf.Cos(a) * profile[j].x, profile[j].y, Mathf.Sin(a) * profile[j].x));
                }
            Vector3 axis = m.MultiplyVector(Vector3.up);
            Vector3 origin = m.MultiplyPoint3x4(Vector3.zero);
            for (int j = 0; j + 1 < profile.Count; j++)
            {
                Vector2 p0 = profile[j], p1 = profile[j + 1];
                Vector2 dir = p1 - p0;
                if (dir.sqrMagnitude < 1e-10f) continue;
                Vector2 n2 = new Vector2(dir.y, -dir.x); // (radial, axial) component of the facing direction
                for (int i = 0; i < sides; i++)
                {
                    int k = full ? (i + 1) % sides : i + 1;
                    Vector3 mid = (rings[j, i] + rings[j, k] + rings[j + 1, i] + rings[j + 1, k]) * 0.25f;
                    Vector3 radial = Vector3.ProjectOnPlane(mid - origin, axis).normalized;
                    Vector3 n = radial * n2.x + axis * n2.y;
                    if (p0.x < 1e-5f) TriFacing(rings[j, i], rings[j + 1, i], rings[j + 1, k], n, col);
                    else if (p1.x < 1e-5f) TriFacing(rings[j, i], rings[j, k], rings[j + 1, i], n, col);
                    else QuadFacing(rings[j, i], rings[j, k], rings[j + 1, k], rings[j + 1, i], n, col);
                }
            }
        }

        /// <summary>Superellipse cross-section for <see cref="AddLoft"/>: centre (x, y) at depth z, half extents, roundness (2 = ellipse, 4+ = rounded box).</summary>
        public struct Section
        {
            public float Z, X, Y, HalfW, HalfH, Power;
            public Section(float z, float halfW, float halfH, float power = 2.6f, float y = 0f, float x = 0f)
            { Z = z; HalfW = halfW; HalfH = halfH; Power = power; Y = y; X = x; }
        }

        /// <summary>
        /// Lofted hull along local +Z through superellipse sections (fuselages, pods, limbs, tanks). Ends are capped.
        /// smoothing &gt; 1 inserts Catmull-Rom interpolated sections between the given ones for curved profiles.
        /// </summary>
        public void AddLoft(Matrix4x4 m, IList<Section> sections, int sides, Color col, bool capStart = true, bool capEnd = true, int smoothing = 1)
        {
            if (smoothing > 1 && sections.Count > 2) sections = SmoothSections(sections, smoothing);
            sides = Mathf.Max(4, sides);
            int n = sections.Count;
            var rings = new Vector3[n, sides];
            var centers = new Vector3[n];
            for (int j = 0; j < n; j++)
            {
                var s = sections[j];
                float e = 2f / Mathf.Max(0.5f, s.Power);
                for (int i = 0; i < sides; i++)
                {
                    float a = (i / (float)sides) * Mathf.PI * 2f;
                    float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                    float x = Mathf.Sign(ca) * Mathf.Pow(Mathf.Abs(ca), e) * s.HalfW;
                    float y = Mathf.Sign(sa) * Mathf.Pow(Mathf.Abs(sa), e) * s.HalfH;
                    rings[j, i] = m.MultiplyPoint3x4(new Vector3(s.X + x, s.Y + y, s.Z));
                }
                centers[j] = m.MultiplyPoint3x4(new Vector3(s.X, s.Y, s.Z));
            }
            for (int j = 0; j + 1 < n; j++)
                for (int i = 0; i < sides; i++)
                {
                    int k = (i + 1) % sides;
                    Vector3 mid = (rings[j, i] + rings[j, k] + rings[j + 1, i] + rings[j + 1, k]) * 0.25f;
                    Vector3 axisMid = (centers[j] + centers[j + 1]) * 0.5f;
                    bool tipA = sections[j].HalfW < 1e-4f && sections[j].HalfH < 1e-4f;
                    bool tipB = sections[j + 1].HalfW < 1e-4f && sections[j + 1].HalfH < 1e-4f;
                    if (tipA) TriFacing(rings[j, i], rings[j + 1, k], rings[j + 1, i], mid - axisMid, col);
                    else if (tipB) TriFacing(rings[j, i], rings[j, k], rings[j + 1, i], mid - axisMid, col);
                    else QuadFacing(rings[j, i], rings[j, k], rings[j + 1, k], rings[j + 1, i], mid - axisMid, col);
                }
            Vector3 fwd = m.MultiplyVector(Vector3.forward);
            if (capStart && (sections[0].HalfW > 1e-4f || sections[0].HalfH > 1e-4f))
                for (int i = 0; i < sides; i++) TriFacing(centers[0], rings[0, i], rings[0, (i + 1) % sides], -fwd, col);
            if (capEnd && (sections[n - 1].HalfW > 1e-4f || sections[n - 1].HalfH > 1e-4f))
                for (int i = 0; i < sides; i++) TriFacing(centers[n - 1], rings[n - 1, i], rings[n - 1, (i + 1) % sides], fwd, col);
        }

        static List<Section> SmoothSections(IList<Section> s, int steps)
        {
            float CR(float p0, float p1, float p2, float p3, float t) =>
                0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t * t + (-p0 + 3f * p1 - 3f * p2 + p3) * t * t * t);
            var outList = new List<Section>();
            for (int i = 0; i + 1 < s.Count; i++)
            {
                Section a = s[Mathf.Max(0, i - 1)], b = s[i], c = s[i + 1], d = s[Mathf.Min(s.Count - 1, i + 2)];
                for (int k = 0; k < steps; k++)
                {
                    float t = k / (float)steps;
                    // Tips (zero size) and the z spacing interpolate linearly so the profile never overshoots past them.
                    outList.Add(new Section(Mathf.Lerp(b.Z, c.Z, t),
                        Mathf.Max(0f, CR(a.HalfW, b.HalfW, c.HalfW, d.HalfW, t)),
                        Mathf.Max(0f, CR(a.HalfH, b.HalfH, c.HalfH, d.HalfH, t)),
                        Mathf.Lerp(b.Power, c.Power, t),
                        CR(a.Y, b.Y, c.Y, d.Y, t),
                        CR(a.X, b.X, c.X, d.X, t)));
                }
            }
            outList.Add(s[s.Count - 1]);
            return outList;
        }

        /// <summary>
        /// Repaints every triangle added since vertex index `from` whose centroid satisfies `where`
        /// (stripes, two-tone liveries, decal bands). Optionally overrides gloss/metal of the painted faces.
        /// </summary>
        public void Paint(int from, System.Func<Vector3, bool> where, Color col, float gloss = -1f, float metal = -1f)
        {
            // Quads are judged by their centre, so stripe edges follow the quad grid instead of zig-zagging.
            Vector3 Centroid(int t) => (verts[t * 3] + verts[t * 3 + 1] + verts[t * 3 + 2]) / 3f;
            for (int t = from / 3; t < partner.Count; t++)
            {
                int p = partner[t];
                if (p >= 0 && p < t) continue; // decided together with its partner
                Vector3 c = p >= 0 ? (Centroid(t) + Centroid(p)) * 0.5f : Centroid(t);
                if (!where(c)) continue;
                PaintTri(t, col, gloss, metal);
                if (p >= 0) PaintTri(p, col, gloss, metal);
            }
        }

        /// <summary>
        /// Soft per-vertex weathering for everything added since `from`: blends vertex colours towards `tint` by
        /// amount(position) in 0..1 and dulls the gloss accordingly. Shared positions get identical values, so the
        /// result is a smooth gradient across faces (soot, burns, dust) instead of hard painted patches.
        /// </summary>
        public void Weather(int from, System.Func<Vector3, float> amount, Color tint, float dull = 0.8f)
        {
            for (int i = from; i < verts.Count; i++)
            {
                float k = Mathf.Clamp01(amount(verts[i]));
                if (k <= 0f) continue;
                var c = colors[i];
                colors[i] = new Color(Mathf.Lerp(c.r, tint.r, k), Mathf.Lerp(c.g, tint.g, k), Mathf.Lerp(c.b, tint.b, k), c.a);
                var s = surface[i];
                s.x *= 1f - k * dull;
                s.y *= 1f - k;
                surface[i] = s;
            }
        }

        void PaintTri(int t, Color col, float gloss, float metal)
        {
            for (int k = t * 3; k < t * 3 + 3; k++)
            {
                colors[k] = col;
                var s = surface[k];
                if (gloss >= 0f) s.x = gloss;
                if (metal >= 0f) s.z = metal;
                surface[k] = s;
            }
        }

        /// <summary>
        /// Extruded convex outline in the local XZ plane (wings, fins, armour plates), centred on y = 0.
        /// thickness may vary per outline point (tapered wings); bevel chamfers the top and bottom rims.
        /// </summary>
        public void AddSlab(Matrix4x4 m, IList<Vector2> outline, float thickness, float bevel, Color col, IList<float> thicknessPerPoint = null)
        {
            int n = outline.Count;
            if (n < 3) return;
            float area = 0f;
            for (int i = 0; i < n; i++) { var p = outline[i]; var q = outline[(i + 1) % n]; area += p.x * q.y - q.x * p.y; }
            float wind = Mathf.Sign(area); // +1 = counter-clockwise in (x, z)
            var inset = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                Vector2 prev = outline[(i + n - 1) % n], cur = outline[i], next = outline[(i + 1) % n];
                Vector2 e0 = (cur - prev).normalized, e1 = (next - cur).normalized;
                Vector2 n0 = new Vector2(-e0.y, e0.x) * wind, n1 = new Vector2(-e1.y, e1.x) * wind; // inward normals
                Vector2 bis = (n0 + n1);
                float d = Vector2.Dot(bis.normalized, n0);
                inset[i] = cur + (d > 0.2f ? bis.normalized * (bevel / d) : n0 * bevel);
            }
            Vector3 P(Vector2 v, float y) => m.MultiplyPoint3x4(new Vector3(v.x, y, v.y));
            float H(int i) => (thicknessPerPoint != null ? thicknessPerPoint[i] : thickness) * 0.5f;
            Vector3 up = m.MultiplyVector(Vector3.up);
            Vector2 c2 = Vector2.zero;
            foreach (var v in outline) c2 += v;
            c2 /= n;
            Vector3 centerTop = P(c2, 0f), centerBot = centerTop;
            {
                float avg = 0f;
                for (int i = 0; i < n; i++) avg += H(i);
                avg /= n;
                centerTop = P(c2, avg); centerBot = P(c2, -avg);
            }
            for (int i = 0; i < n; i++)
            {
                int k = (i + 1) % n;
                float hi = H(i), hk = H(k);
                float bi = Mathf.Min(bevel, hi * 0.9f), bk = Mathf.Min(bevel, hk * 0.9f);
                // top / bottom caps (fan from centre of the inset outline)
                TriFacing(centerTop, P(inset[i], hi), P(inset[k], hk), up, col);
                TriFacing(centerBot, P(inset[k], -hk), P(inset[i], -hi), -up, col);
                Vector2 edge = outline[k] - outline[i];
                Vector2 outward2 = new Vector2(edge.y, -edge.x) * wind;
                Vector3 outward = m.MultiplyVector(new Vector3(outward2.x, 0f, outward2.y));
                QuadFacing(P(outline[i], -hi + bi), P(outline[k], -hk + bk), P(outline[k], hk - bk), P(outline[i], hi - bi), outward, col);
                edgeFlag = 1f;
                QuadFacing(P(outline[i], hi - bi), P(outline[k], hk - bk), P(inset[k], hk), P(inset[i], hi), outward + up, col);
                QuadFacing(P(inset[i], -hi), P(inset[k], -hk), P(outline[k], -hk + bk), P(outline[i], -hi + bi), outward - up, col);
                edgeFlag = 0f;
            }
        }

        /// <summary>Low-poly rock: jittered icosahedron (1 subdivision), deterministic per seed.</summary>
        public void AddRock(Matrix4x4 m, float radius, int seed, Color col) => AddIco(m, radius, 1, seed, 0.45f, col);

        /// <summary>Icosphere (subdiv 0..3). Faceted unless SmoothAngle is set. Scale m for ellipsoids, domes and helmets.</summary>
        public void AddSphere(Matrix4x4 m, float radius, int subdivisions, Color col) => AddIco(m, radius, subdivisions, 0, 0f, col);

        void AddIco(Matrix4x4 m, float radius, int subdivisions, int seed, float jitter, Color col)
        {
            var srcTris = IcoSphere.GetBaseTris();
            var mid = new Dictionary<long, int>();
            var v = new List<Vector3>(IcoSphere.GetBaseVerts());
            var t = new List<int>(srcTris);
            for (int s = 0; s < Mathf.Clamp(subdivisions, 0, 3); s++)
            {
                var nt = new List<int>(t.Count * 4);
                for (int i = 0; i < t.Count; i += 3)
                {
                    int a = t[i], b = t[i + 1], c = t[i + 2];
                    int ab = Midpoint(v, a, b, mid), bc = Midpoint(v, b, c, mid), ca = Midpoint(v, c, a, mid);
                    nt.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                t = nt;
            }
            var pts = new Vector3[v.Count];
            for (int i = 0; i < v.Count; i++)
            {
                var dir = v[i].normalized;
                float j = jitter > 0f ? 1f + MathUtils.ValueNoise3D(seed, dir * 2.3f) * jitter : 1f;
                pts[i] = m.MultiplyPoint3x4(dir * (radius * j));
            }
            Vector3 center = m.MultiplyPoint3x4(Vector3.zero);
            for (int i = 0; i < t.Count; i += 3)
                TriOut(pts[t[i]], pts[t[i + 1]], pts[t[i + 2]], center, col);
        }

        static int Midpoint(List<Vector3> v, int a, int b, Dictionary<long, int> cache)
        {
            long key = a < b ? ((long)a << 32) + b : ((long)b << 32) + a;
            if (cache.TryGetValue(key, out int idx)) return idx;
            idx = v.Count;
            v.Add(((v[a] + v[b]) * 0.5f).normalized);
            cache[key] = idx;
            return idx;
        }

        /// <summary>Overwrites the occlusion channel (UV1.y) of every vertex, e.g. with values from an editor AO bake.</summary>
        public void SetOcclusion(IList<float> ao)
        {
            for (int i = 0; i < detail.Count && i < ao.Count; i++) detail[i] = new Vector2(detail[i].x, ao[i]);
        }

        public Mesh Build(string name)
        {
            var mesh = new Mesh { name = name };
            if (verts.Count > 65000)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.SetColors(colors);
            mesh.SetUVs(0, surface);
            mesh.SetUVs(1, detail);
            if (SmoothAngle > 0f) mesh.SetNormals(WeightedNormals(SmoothAngle));
            else mesh.RecalculateNormals(); // duplicated verts => flat facets
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Area-weighted vertex normals; faces meeting at more than maxAngle keep their own normal (hard edge).</summary>
        Vector3[] WeightedNormals(float maxAngle)
        {
            int triCount = tris.Count / 3;
            var faceN = new Vector3[triCount];
            for (int t = 0; t < triCount; t++)
            {
                Vector3 c = Vector3.Cross(verts[tris[t * 3 + 1]] - verts[tris[t * 3]], verts[tris[t * 3 + 2]] - verts[tris[t * 3]]);
                faceN[t] = c * 0.5f; // magnitude = area
            }
            const float q = 1e4f; // weld tolerance 0.1 mm
            var groups = new Dictionary<Vector3Int, List<int>>();
            var keyOf = new Vector3Int[verts.Count];
            for (int i = 0; i < verts.Count; i++)
            {
                var v = verts[i];
                var key = new Vector3Int(Mathf.RoundToInt(v.x * q), Mathf.RoundToInt(v.y * q), Mathf.RoundToInt(v.z * q));
                keyOf[i] = key;
                if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<int>(6);
                list.Add(i);
            }
            var triOf = new int[verts.Count];
            for (int t = 0; t < triCount; t++) { triOf[tris[t * 3]] = t; triOf[tris[t * 3 + 1]] = t; triOf[tris[t * 3 + 2]] = t; }
            float cosMax = Mathf.Cos(maxAngle * Mathf.Deg2Rad);
            var normals = new Vector3[verts.Count];
            for (int i = 0; i < verts.Count; i++)
            {
                Vector3 own = faceN[triOf[i]];
                Vector3 ownDir = own.normalized;
                Vector3 sum = Vector3.zero;
                foreach (int j in groups[keyOf[i]])
                {
                    Vector3 fn = faceN[triOf[j]];
                    if (Vector3.Dot(ownDir, fn.normalized) >= cosMax) sum += fn;
                }
                normals[i] = sum.sqrMagnitude > 1e-20f ? sum.normalized : ownDir;
            }
            return normals;
        }
    }
}
