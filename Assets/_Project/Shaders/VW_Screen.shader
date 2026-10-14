// Cockpit display elements (#94): crisp SDF shapes on a unit quad, unlit HDR (bloom-friendly), alpha blended.
// Per-element values come from a MaterialPropertyBlock (CockpitScreen). _Size is the quad size in metres, so
// corner radius, line width and segment gaps stay round and even on any aspect.
//   _Mode 0 = panel  : rounded glass plate (_BgColor, vertical sheen + fine scanlines) with a _Color rim of _Line
//   _Mode 1 = bar    : rounded track (_BgColor) filled to _Fill (0..1) with _Color, split into _Segments cells
//   _Mode 2 = radar  : disc (_BgColor) with range rings, cross, forward tick and a rotating sweep in _Color
//   _Mode 3 = dot    : soft glowing dot (blips, LEDs)
//   _Mode 4 = frame  : corner brackets only (_Color, _Line thick, _Radius long)
Shader "VOIDWARD/Screen"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (0.4, 0.9, 1, 1)
        [HDR] _BgColor ("Background", Color) = (0.02, 0.05, 0.08, 0.8)
        _Size ("Size (m)", Vector) = (0.2, 0.1, 0, 0)
        _Radius ("Corner radius (m)", Float) = 0.008
        _Line ("Line width (m)", Float) = 0.0015
        _Fill ("Fill", Range(0, 1)) = 1
        _Segments ("Segments", Float) = 0
        _Mode ("Mode", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Name "Screen"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            CBUFFER_START(UnityPerMaterial)
                float4 _Color, _BgColor, _Size;
                float _Radius, _Line, _Fill, _Segments, _Mode;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            // signed distance of a rounded box (half size b, radius r) at p
            float RoundBox(float2 p, float2 b, float r)
            {
                float2 q = abs(p) - b + r;
                return length(max(q, 0)) + min(max(q.x, q.y), 0) - r;
            }

            // anti-aliased coverage of "d < 0"
            float Cover(float d) { float w = max(fwidth(d), 1e-5); return saturate(0.5 - d / w); }

            float4 Over(float4 dst, float4 src) { return float4(lerp(dst.rgb, src.rgb, src.a), src.a + dst.a * (1 - src.a)); }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 size = max(_Size.xy, 1e-4);
                float2 p = (IN.uv - 0.5) * size;      // metres from the centre
                float2 hb = size * 0.5;
                float4 col = 0;
                int mode = (int)round(_Mode);

                if (mode == 0)
                {
                    float d = RoundBox(p, hb, _Radius);
                    float inside = Cover(d);
                    float4 bg = _BgColor;
                    bg.rgb *= 1.0 + 0.35 * IN.uv.y;                                    // sheen towards the top
                    bg.rgb *= 0.92 + 0.08 * step(0.5, frac(IN.uv.y * size.y * 900.0)); // fine scanlines
                    col = float4(bg.rgb, bg.a * inside);
                    float rim = Cover(abs(d + _Line * 0.5) - _Line * 0.5);
                    col = Over(col, float4(_Color.rgb, _Color.a * rim));
                }
                else if (mode == 1)
                {
                    float d = RoundBox(p, hb, min(_Radius, hb.y));
                    float inside = Cover(d);
                    float x = IN.uv.x;
                    float filled = Cover((x - _Fill) * size.x);
                    float cell = 1;
                    if (_Segments > 0.5)
                    {
                        float gap = 0.0012 / size.x * _Segments;      // ~1.2 mm gaps
                        float f = frac(x * _Segments);
                        cell = Cover((abs(f - 0.5) - (0.5 - gap * 0.5)) * size.x / _Segments);
                    }
                    col = float4(_BgColor.rgb, _BgColor.a * inside);
                    col = Over(col, float4(_Color.rgb, _Color.a * inside * filled * cell));
                }
                else if (mode == 2)
                {
                    float r = hb.x;
                    float len = length(p);
                    float disc = Cover(len - r);
                    col = float4(_BgColor.rgb, _BgColor.a * disc);
                    float lw = _Line;
                    float rings = Cover(abs(len - r + lw) - lw * 0.6) * 0.9
                                + Cover(abs(len - r * 0.66) - lw * 0.35) * 0.35
                                + Cover(abs(len - r * 0.33) - lw * 0.35) * 0.35;
                    float cross = (Cover(abs(p.x) - lw * 0.3) + Cover(abs(p.y) - lw * 0.3)) * 0.18 * disc;
                    float tick = Cover(abs(p.x) - lw * 0.8) * Cover(abs(p.y - r * 0.9) - r * 0.08) * 0.9;
                    float ang = atan2(p.x, p.y);                                       // 0 = forward (up)
                    float sweepA = frac(_Time.y * 0.35) * 6.2831853 - 3.14159265;
                    float behind = frac((sweepA - ang) / 6.2831853);                    // 0 right at the beam
                    float sweep = pow(saturate(1 - behind * 4), 3) * 0.35 * disc;
                    float a = saturate(rings + cross + tick + sweep);
                    col = Over(col, float4(_Color.rgb, _Color.a * a));
                }
                else if (mode == 3)
                {
                    float len = length(p) / hb.x;
                    float core = Cover((len - 0.45) * hb.x);
                    float glow = saturate(1 - len) * 0.45;
                    col = float4(_Color.rgb, _Color.a * saturate(core + glow));
                }
                else
                {
                    // corner brackets: inside the border band and within _Radius of a corner along the edge
                    float d = RoundBox(p, hb, 0);
                    float band = Cover(abs(d + _Line * 0.5) - _Line * 0.5);
                    float2 fromCorner = hb - abs(p);
                    float nearCorner = step(min(fromCorner.x, fromCorner.y), _Line * 1.5) * step(max(fromCorner.x, fromCorner.y), _Radius);
                    col = float4(_Color.rgb, _Color.a * band * nearCorner);
                }
                return half4(col.rgb, col.a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
