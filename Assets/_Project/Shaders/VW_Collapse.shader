Shader "VOIDWARD/Collapse"
{
    Properties
    {
        _CoreColor ("Core Color", Color) = (0.02,0.0,0.05,1)
        _SwirlColorA ("Swirl A", Color) = (0.55,0.1,0.9,1)
        _SwirlColorB ("Swirl B", Color) = (1.0,0.3,0.55,1)
        _RimColor ("Rim Glow", Color) = (2.0,0.6,1.6,1)
        _Speed ("Swirl Speed", Float) = 0.04
        _Scale ("Swirl Scale", Float) = 6
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+10" }
        Pass
        {
            Name "Unlit"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 posOS : TEXCOORD0; float3 normalWS : TEXCOORD1; float3 posWS : TEXCOORD2; };

            CBUFFER_START(UnityPerMaterial)
                float4 _CoreColor;
                float4 _SwirlColorA;
                float4 _SwirlColorB;
                float4 _RimColor;
                float _Speed;
                float _Scale;
            CBUFFER_END

            float hash(float3 p) { p = frac(p * 0.3183099 + 0.1); p *= 17.0; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }
            float noise(float3 x)
            {
                float3 i = floor(x); float3 f = frac(x); f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(hash(i + float3(0,0,0)), hash(i + float3(1,0,0)), f.x),
                                 lerp(hash(i + float3(0,1,0)), hash(i + float3(1,1,0)), f.x), f.y),
                            lerp(lerp(hash(i + float3(0,0,1)), hash(i + float3(1,0,1)), f.x),
                                 lerp(hash(i + float3(0,1,1)), hash(i + float3(1,1,1)), f.x), f.y), f.z);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.posWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.posWS);
                OUT.posOS = IN.positionOS.xyz;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float t = _Time.y * _Speed;
                float3 p = normalize(IN.posOS) * _Scale;
                float a = atan2(p.z, p.y) + t * 3.0 + length(p.yz) * 0.3;
                float3 q = float3(p.x, cos(a) * length(p.yz), sin(a) * length(p.yz));
                float n = noise(q + t) * 0.6 + noise(q * 2.3 - t * 1.7) * 0.3 + noise(q * 5.1) * 0.1;
                float bands = smoothstep(0.45, 0.75, n);
                float3 col = lerp(_CoreColor.rgb, lerp(_SwirlColorA.rgb, _SwirlColorB.rgb, noise(q * 0.7)), bands);
                float3 v = normalize(GetWorldSpaceViewDir(IN.posWS));
                float rim = pow(1.0 - saturate(abs(dot(normalize(IN.normalWS), v))), 3.0);
                col += _RimColor.rgb * rim + _SwirlColorA.rgb * bands * 0.6;
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
