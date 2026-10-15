Shader "VOIDWARD/FlipbookParticle"
{
    // Textured particles (#96): Unity Labs flipbooks (explosions, smoke) and Kenney sprites (flares, sparks, muzzle flashes).
    // Output is premultiplied: _Additive = 1 -> Blend One One (glow, ignores alpha coverage), 0 -> Blend One OneMinusSrcAlpha
    // (fire/smoke that occludes). Bright texels of fire get an HDR heat boost for bloom while smoke stays dark.
    // Flipbook frame blending: the renderer sends UV2 + AnimBlend (custom vertex streams), soft edges against the depth buffer.
    Properties
    {
        [NoScaleOffset] _MainTex ("Texture", 2D) = "white" {}
        [HDR] _BaseColor ("Tint", Color) = (1,1,1,1)
        _Heat ("Heat Boost (bright texels)", Range(0,8)) = 0
        _HeatStart ("Heat Start Luma", Range(0,1)) = 0.45
        _Additive ("Additive", Float) = 1
        _SoftFade ("Soft Fade Distance (m)", Range(0,4)) = 0.6
        _NearFade ("Camera Near Fade (m)", Range(0,6)) = 1.2
        [HideInInspector] _SrcBlend ("Src", Float) = 1
        [HideInInspector] _DstBlend ("Dst", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Flipbook"
            Tags { "LightMode"="UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; float4 uv : TEXCOORD0; float blend : TEXCOORD1; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float4 uv : TEXCOORD0;
                float blend : TEXCOORD1;
                float4 screen : TEXCOORD2;
                float fog : TEXCOORD3;
            };
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _Heat;
                float _HeatStart;
                float _Additive;
                float _SoftFade;
                float _NearFade;
                float _SrcBlend;
                float _DstBlend;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.color = IN.color * _BaseColor;
                OUT.uv = IN.uv;
                OUT.blend = IN.blend;
                OUT.screen = ComputeScreenPos(OUT.positionCS);
                OUT.fog = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float4 a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv.xy);
                float4 b = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv.zw);
                float4 t = lerp(a, b, saturate(IN.blend));
                float luma = max(t.r, max(t.g, t.b));
                float3 rgb = t.rgb * (1.0 + _Heat * smoothstep(_HeatStart, 1.0, luma)) * IN.color.rgb;
                float alpha = t.a * IN.color.a;

                // soft particles + fade when the camera flies through
                float2 uv = IN.screen.xy / IN.screen.w;
                float scene = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                float self = IN.screen.w;
                float fade = _SoftFade > 0.0 ? saturate((scene - self) / _SoftFade) : 1.0;
                fade *= _NearFade > 0.0 ? saturate((self - _ProjectionParams.y) / _NearFade) : 1.0;
                alpha *= fade;

                // glow fades out in fog, occluding smoke takes the fog colour
                rgb = _Additive > 0.5 ? MixFogColor(rgb, half3(0, 0, 0), IN.fog) : MixFog(rgb, IN.fog);
                return half4(rgb * alpha, alpha * (1.0 - _Additive));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
