// Signature bloom: procedural petal/ring linework unfolded entirely in the vertex shader.
// The mesh stores each stroke in its element-local frame; one clock (_Bloom, 0..1) drives every element
// through its own delayed, eased unfold, so the whole animation is one draw call with no per-frame mesh work.
//
// Per vertex:
//   POSITION   q            element-local point (x radial from element base, y up, z lateral)
//   TEXCOORD1  xyz = neighbour point (element-local), w = side (-1, 0, +1) of the screen-facing ribbon
//   TEXCOORD2  (theta, base radius, tilt, delay)
//   TEXCOORD3  (lift, width scale, alpha scale, start scale)
//   TEXCOORD0  (draw-on key 0..1, layer 0..1)
//   COLOR.a    soft ribbon edge
//
// Colour (Swiss line art: paper lines, one vermilion): the bloom starts in the solved target's own colour
// (_SeedColor, vermilion) and _Warmth carries it to the resting palette as it opens: paper petals, a vermilion heart
// at the inner bases and faint ivory tips.
Shader "ShadowBloom/BloomPetal"
{
    Properties
    {
        _ColorA ("Heart (vermilion)", Color) = (0.894, 0.314, 0.235, 1)
        _ColorB ("Petal (paper)", Color) = (0.949, 0.941, 0.922, 1)
        _ColorC ("Tip (ivory)", Color) = (1, 0.93, 0.8, 1)
        _SeedColor ("Solved target (start of the shift)", Color) = (0.894, 0.314, 0.235, 1)
        _Intensity ("Intensity", Float) = 1.4
        _Alpha ("Alpha", Range(0, 1)) = 1
        _Bloom ("Bloom clock", Range(0, 1.2)) = 1
        _Stagger ("Max element delay", Range(0, 0.9)) = 0.35
        _Twist ("Unfold twist (rad)", Float) = 0.6
        _Overshoot ("Ease overshoot", Range(0, 2)) = 0.6
        _Width ("Line width (m)", Float) = 0.0022
        _Warmth ("Seed to resting palette shift", Range(0, 1)) = 1
        _Front ("Unfolding front glow", Range(0, 2)) = 0.8
        _SeedYaw ("Seed spin compensation (rad)", Float) = 0
        _Backing ("Contrast backing (halo pass)", Color) = (0, 0.01, 0.03, 1)
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 10
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
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ColorA;
                half4 _ColorB;
                half4 _ColorC;
                half4 _SeedColor;
                half _Intensity;
                half _Alpha;
                float _Bloom;
                float _Stagger;
                float _Twist;
                float _Overshoot;
                float _Width;
                half _Warmth;
                half _Front;
                float _SeedYaw;
                half4 _Backing;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 neighbour : TEXCOORD1;
                float4 element : TEXCOORD2;
                float4 style : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float4 data : TEXCOORD0;   // key, layer, unfold progress, backing flag
                half facing : TEXCOORD1;   // 0 = stroke runs across the view, 1 = it points at the viewer
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // easeOutBack: 1 + (s+1)(u-1)^3 + s(u-1)^2, s = 0 gives easeOutCubic.
            float EaseOutBack(float u, float s)
            {
                float v = u - 1.0;
                return 1.0 + (s + 1.0) * v * v * v + s * v * v;
            }

            float3 Deform(float3 q, float4 element, float4 style, float e)
            {
                float sc = lerp(style.w, 1.0, e);
                float s, c;
                sincos(element.z * e, s, c);                       // petal lifts its tip about the lateral axis
                float3 p = float3(q.x * c - q.y * s, q.x * s + q.y * c, q.z) * sc;
                p.x += element.y * sc;                              // out to the element's base radius
                p.y += style.x * e;                                 // lifted elements rise into place
                sincos(element.x + _Twist * (1.0 - e), s, c);       // radial placement, unwinding as it opens
                return float3(p.x * c - p.z * s, p.y, p.x * s + p.z * c);
            }

            // One view direction for both eyes. Per-eye directions differ most for strokes pointing at the viewer,
            // which would give each eye its own sheen (shimmer / rivalry); the ribbon's width keeps facing each eye.
            float3 CentreEyeViewDir(float3 positionWS)
            {
            #if defined(USING_STEREO_MATRICES)
                float3 eye = 0.5 * (unity_StereoWorldSpaceCameraPos[0] + unity_StereoWorldSpaceCameraPos[1]);
                return normalize(eye - positionWS);
            #else
                return GetWorldSpaceNormalizeViewDir(positionWS);
            #endif
            }

            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float u = saturate((_Bloom - v.element.w) / max(1e-3, 1.0 - _Stagger));
                float e = EaseOutBack(u, _Overshoot);

                float3 p = TransformObjectToWorld(Deform(v.positionOS.xyz, v.element, v.style, e));
                float3 n = TransformObjectToWorld(Deform(v.neighbour.xyz, v.element, v.style, e));
                if (v.uv.y < 0.0)
                {
                    // The solved shadow stays anchored to its exact outline while petals lift from it.
                    float sn, cs;
                    sincos(_SeedYaw, sn, cs);
                    float3 a = v.positionOS.xyz, b = v.neighbour.xyz;
                    a.xz = float2(a.x * cs - a.z * sn, a.x * sn + a.z * cs);
                    b.xz = float2(b.x * cs - b.z * sn, b.x * sn + b.z * cs);
                    p = TransformObjectToWorld(a);
                    n = TransformObjectToWorld(b);
                    u = 1.0;
                    e = 1.0;
                }
                float3 viewDir = GetWorldSpaceNormalizeViewDir(p);
                float3 along = n - p;
                float alongLen = length(along);
                o.facing = alongLen > 1e-7 ? (half)abs(dot(along / alongLen, CentreEyeViewDir(p))) : 0.0h;
                float3 side = cross(along, viewDir);
                float len = length(side);
                side = len > 1e-7 ? side / len : float3(0, 0, 0);
                p += side * (_Width * v.style.y * 0.5 * lerp(0.5, 1.0, saturate(e)) * v.neighbour.w);

                o.positionCS = TransformWorldToHClip(p);
                o.color = v.color;
                o.color.a *= (half)v.style.z;
                // Wide strokes are the dark backing pass: a soft outline that keeps lines legible on light tables.
                o.data = float4(v.uv.x, max(0.0, v.uv.y), u, v.style.y > 1.5 ? 1.0 : 0.0);
                return o;
            }

            // Resting palette at one point of the opened bloom.
            //   key    0 at a stroke's base -> 1 at its tip (the seed outline is 0)
            //   layer  0 inner ring -> 1 outer ring (the seed outline is 0)
            //   facing 0 stroke across the view -> 1 pointing at the viewer; changes as the bloom slowly turns
            half3 PetalColor(half key, half layer, half facing)
            {
                // TODO(you): shape of the red heart -- see PLAN.md (falloffs 2.5 across layers, 1.4 along the stroke)
                half heart = (1.0h - saturate(layer * 2.5h)) * (1.0h - saturate(key * 1.4h));
                half3 c = lerp(_ColorB.rgb, _ColorA.rgb, heart);                       // paper petals, vermilion heart
                c = lerp(c, _ColorC.rgb, saturate((layer - 0.35h) * 1.5h) * key);      // outer tips breathe ivory
                return lerp(c, _ColorB.rgb, facing * facing * 0.35h);                 // soft sheen toward paper
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                // Strokes draw on from their base during the first 60% of their unfold.
                clip(saturate(i.data.z * 1.65) - i.data.x - 1e-3);

                if (i.data.w > 0.5) return half4(_Backing.rgb, i.color.a * _Alpha * _Backing.a);

                half3 rgb = lerp(_SeedColor.rgb, PetalColor((half)i.data.x, (half)i.data.y, i.facing), _Warmth);
                // The leading edge of an opening element burns toward the vermilion heart (a red pen tip drawing),
                // then settles to its own colour.
                half front = _Front * (half)(1.0 - i.data.z) * (half)step(1e-3, i.data.z);
                rgb = lerp(rgb, _ColorA.rgb, saturate(front * 0.6h));
                // The controller scales _Intensity so the brightest level (the success swell) lands at 1, leaving the
                // rest level and breath visible below it.
                rgb *= _Intensity * (1.0h + front);
                // Safety cap for what still overshoots (the leading-edge boost): no post-processing and an LDR target,
                // so per-channel clipping would wash vermilion to orange. Scale the whole colour down
                // instead, so brightness caps without changing the hue.
                rgb /= max(1.0h, max(rgb.r, max(rgb.g, rgb.b)));
                return half4(rgb, i.color.a * _Alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
