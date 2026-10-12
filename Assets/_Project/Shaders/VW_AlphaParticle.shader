Shader "VOIDWARD/AlphaParticle"
{
    // Alpha-blended soft particle (smoke, dust): radial falloff with a little noise break-up, fogged like the scene.
    Properties
    {
        [HDR] _BaseColor ("Tint", Color) = (1,1,1,1)
        _Softness ("Softness", Range(0.05,1)) = 0.8
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "AlphaBlend"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; float fog : TEXCOORD1; };
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _Softness;
            CBUFFER_END
            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.color = IN.color * _BaseColor;
                OUT.uv = IN.uv;
                OUT.fog = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }
            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            half4 frag(Varyings IN) : SV_Target
            {
                float2 c = IN.uv - 0.5;
                float d = length(c) * 2.0;
                // cheap lumpy edge: modulate the radius by angle
                float ang = atan2(c.y, c.x);
                float lump = 0.12 * sin(ang * 5.0 + IN.color.a * 7.0) + 0.06 * sin(ang * 11.0 - 1.3);
                float a = saturate((1.0 - d + lump) / _Softness);
                float3 col = MixFog(IN.color.rgb, IN.fog);
                return half4(col, IN.color.a * a * a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
