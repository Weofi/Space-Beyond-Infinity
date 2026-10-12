Shader "VOIDWARD/AdditiveParticle"
{
    Properties
    {
        [HDR] _BaseColor ("Tint", Color) = (1,1,1,1)
        _Softness ("Softness", Range(0.05,1)) = 0.6
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Additive"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
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
                return OUT;
            }
            half4 frag(Varyings IN) : SV_Target
            {
                float d = length(IN.uv - 0.5) * 2.0;
                float a = saturate((1.0 - d) / _Softness);
                return half4(IN.color.rgb, IN.color.a * a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
