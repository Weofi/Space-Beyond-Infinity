Shader "VOIDWARD/GlowSprite"
{
    // Camera-facing additive glow (#96): projectile heads, engine nozzle glow. A unit quad (-0.5..0.5) expands around
    // the object's pivot towards the camera; its size is the length of the object's X axis (so a parent stretched
    // along Z, like a bolt or a throttled plume, does not distort it). Kenney sprite texture, HDR tint (+ per-renderer
    // _BaseColor via property block), soft against geometry, fades when the camera is right on it.
    Properties
    {
        [NoScaleOffset] _MainTex ("Texture", 2D) = "white" {}
        [HDR] _BaseColor ("Tint", Color) = (1,1,1,1)
        _SoftFade ("Soft Fade Distance (m)", Range(0,4)) = 0.3
        _NearFade ("Camera Near Fade (m)", Range(0,6)) = 1.5
        _Twinkle ("Twinkle", Range(0,1)) = 0.15
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" "DisableBatching"="True" }
        Pass
        {
            Name "Glow"
            Tags { "LightMode"="UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float4 screen : TEXCOORD1; float flick : TEXCOORD2; };
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _SoftFade;
                float _NearFade;
                float _Twinkle;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 center = TransformObjectToWorld(float3(0, 0, 0));
                float size = length(float3(UNITY_MATRIX_M[0].x, UNITY_MATRIX_M[1].x, UNITY_MATRIX_M[2].x));
                float3 right = UNITY_MATRIX_V[0].xyz;
                float3 up = UNITY_MATRIX_V[1].xyz;
                float3 posWS = center + (right * IN.positionOS.x + up * IN.positionOS.y) * size;
                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.uv = IN.uv;
                OUT.screen = ComputeScreenPos(OUT.positionCS);
                float seed = frac(dot(center, float3(0.137, 0.271, 0.519)));
                OUT.flick = 1.0 - _Twinkle + _Twinkle * (0.5 + 0.5 * sin(_Time.y * 37.0 + seed * 40.0));
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                float2 uv = IN.screen.xy / IN.screen.w;
                float scene = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                float self = IN.screen.w;
                float fade = _SoftFade > 0.0 ? saturate((scene - self) / _SoftFade) : 1.0;
                fade *= _NearFade > 0.0 ? saturate((self - _ProjectionParams.y) / _NearFade) : 1.0;
                float a = t.a * fade * IN.flick;
                return half4(t.rgb * _BaseColor.rgb * a, 0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
