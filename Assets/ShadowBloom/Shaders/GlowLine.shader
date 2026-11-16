// Unlit line shader for procedural ribbons and tubes.
// uv.x = reveal key (0..1, used for draw-on animation), uv.y = arc length in metres (used for dashes).
// Vertex colour alpha carries the soft ribbon edge, so no textures or post-processing are needed.
Shader "ShadowBloom/GlowLine"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (1, 1, 1, 1)
        _Intensity ("Intensity", Float) = 1
        _CoreGlow ("Core Glow (tubes)", Range(0, 1)) = 0
        _DashFrequency ("Dashes per metre (0 = solid)", Float) = 0
        _DashFill ("Dash Fill", Range(0.05, 1)) = 0.55
        _Reveal ("Reveal", Range(0, 1)) = 1
        _Pulse ("Pulse", Range(0, 1)) = 0
        _Outline ("Projection contrast edge", Range(0, 1)) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 10
        [Enum(Off, 0, On, 1)] _ZWrite ("ZWrite", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }
            // Alpha channel accumulates coverage so passthrough is occluded where lines are drawn.
            Blend [_SrcBlend] [_DstBlend], One OneMinusSrcAlpha
            ZWrite [_ZWrite]
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Intensity;
                half _CoreGlow;
                float _DashFrequency;
                half _DashFill;
                half _Reveal;
                half _Pulse;
                half _Outline;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                half core : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(positionWS);
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
                float3 viewWS = GetWorldSpaceNormalizeViewDir(positionWS);
                o.core = (half)abs(dot(normalWS, viewWS));
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                clip(_Reveal - i.uv.x);
                if (_DashFrequency > 0)
                {
                    clip(_DashFill - frac(i.uv.y * _DashFrequency));
                }

                half glow = lerp(1.0h, 0.4h + 0.8h * i.core * i.core, _CoreGlow);
                half4 c = _Color * i.color;
                c.rgb *= _Intensity * glow * (1.0h + 0.6h * _Pulse);
                if (_Outline > 0.0h)
                {
                    // Use the ribbon's existing edge coordinates: a dark rim on light/busy surfaces,
                    // a luminous centre on dark surfaces. No extra geometry, pass, or texture sample.
                    half core = smoothstep(0.25h, 0.65h, i.color.a);
                    c.rgb = lerp(c.rgb, lerp(half3(0.025h, 0.05h, 0.08h), c.rgb, core), _Outline);
                    c.a = _Color.a * smoothstep(0.0h, 0.2h, i.color.a);
                }
                return c;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
