Shader "MobileIdleBuilder/BeltParticle"
{
    // Animated belt particles (quarks, nucleon cores and shells, electron core). URP Unlit with a fake
    // top light and a small glossy highlight. All motion is in the vertex shader, so a belt full of
    // particles costs no CPU per frame:
    //   * jitter: each vertex shakes with the noise of its seed (TEXCOORD1.x), blended toward a second
    //     seed (TEXCOORD1.y) by TEXCOORD1.z so gluon tubes stay attached to the quarks they join
    //   * squash: the whole object stretches and squeezes (loose quarks)
    //   * spin: the object turns about its local Y (quarks circling inside a nucleon)
    //   * fade: alpha drops for a moment every _FadePeriod seconds (nucleon shell); a fresnel rim keeps
    //     the outline visible while it is see-through
    // Per-item variety comes from the object's world position (a phase hash), not a property block, so
    // items need no per-instance setup and stay SRP-batchable. Blend / ZWrite / Cull are properties so
    // the same shader serves the opaque parts and the transparent shell.
    Properties
    {
        _BaseColor   ("Base Color (multiplies vertex colour)", Color) = (1, 1, 1, 1)
        _TopLight    ("Top Light", Range(0, 1)) = 0.35
        _Gloss       ("Gloss", Range(0, 1)) = 0.35
        _JitterAmp   ("Jitter Amplitude", Float) = 0.0
        _JitterFreq  ("Jitter Frequency", Float) = 30.0
        _SquashAmp   ("Squash Amplitude", Float) = 0.0
        _SpinSpeed   ("Spin Speed (rad/s)", Float) = 0.0
        // 1 = start each item's spin at a random angle (from its position); 0 = keep the mesh's own orientation
        // (atom rings built facing the camera, a comet tail or wave pointing along the belt).
        _PhaseSpin   ("Phase Spin", Float) = 1.0
        _FadePeriod  ("Fade Period (s, 0 = never)", Float) = 0.0
        _FadeOpen    ("Fade Open Fraction", Range(0.01, 0.9)) = 0.2
        _FadeMin     ("Fade Min Alpha", Range(0, 1)) = 0.06
        _RimAlpha    ("Rim Alpha", Range(0, 1)) = 0.85
        // Look (defaults = plain shaded colour)
        _Emission    ("Brightness", Float) = 1.0
        _Saturation  ("Saturation", Float) = 1.0
        _RimGlow     ("Rim Glow", Float) = 0.0
        _RimPower    ("Rim Power", Float) = 3.0
        _CenterGlow  ("Center Glow", Float) = 0.0
        _Metal       ("Metallic (fake env)", Range(0, 1)) = 0.0
        _SkyColor    ("Env Sky", Color) = (0.75, 0.85, 1.0, 1)
        _GroundColor ("Env Ground", Color) = (0.05, 0.06, 0.08, 1)
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 0
        [Enum(Off, 0, On, 1)] _ZWrite ("ZWrite", Float) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }

        Blend [_SrcBlend] [_DstBlend]
        ZWrite [_ZWrite]
        Cull [_Cull]

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float  _TopLight;
                float  _Gloss;
                float  _JitterAmp;
                float  _JitterFreq;
                float  _SquashAmp;
                float  _SpinSpeed;
                float  _PhaseSpin;
                float  _FadePeriod;
                float  _FadeOpen;
                float  _FadeMin;
                float  _RimAlpha;
                float  _Emission;
                float  _Saturation;
                float  _RimGlow;
                float  _RimPower;
                float  _CenterGlow;
                float  _Metal;
                float4 _SkyColor;
                float4 _GroundColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
                float4 jitter     : TEXCOORD1;   // seedA, seedB, blend, unused
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                half4  color       : COLOR;
                float3 normalWS    : TEXCOORD0;
                float3 viewWS      : TEXCOORD1;
                half   alpha       : TEXCOORD2;
            };

            // Irregular shake: two incommensurate sines per axis, offset by seed and item phase.
            float3 Shake(float seed, float t)
            {
                float3 p = float3(1.0, 1.37, 0.83) * t + seed * float3(7.13, 3.31, 5.97);
                return float3(sin(p.x) + 0.6 * sin(p.x * 2.31 + 1.1),
                              sin(p.y) + 0.6 * sin(p.y * 1.93 + 2.7),
                              sin(p.z) + 0.6 * sin(p.z * 2.67 + 0.4)) * 0.625;
            }

            float3 RotateY(float3 p, float a)
            {
                float s = sin(a), c = cos(a);
                return float3(c * p.x + s * p.z, p.y, -s * p.x + c * p.z);
            }

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                float3 originWS = TransformObjectToWorld(float3(0, 0, 0));
                // Stable per item: the item's position moves smoothly, so its phase never jumps.
                float  phase = dot(originWS.xz, float2(2.17, 3.41));
                float  t     = _Time.y;

                float3 pos = IN.positionOS.xyz;
                float3 nrm = IN.normalOS;

                // Squash: stretch along one wobbling axis, preserving rough volume.
                if (_SquashAmp > 0.0)
                {
                    float3 s = 1.0 + _SquashAmp * Shake(phase + 11.0, t * _JitterFreq * 0.5);
                    pos *= s;
                }

                float spin = _SpinSpeed * t + phase * _PhaseSpin;
                pos = RotateY(pos, spin);
                nrm = RotateY(nrm, spin);

                float jt = t * _JitterFreq;
                float3 j = lerp(Shake(IN.jitter.x + phase, jt), Shake(IN.jitter.y + phase, jt), IN.jitter.z);
                pos += j * _JitterAmp;

                float3 posWS  = TransformObjectToWorld(pos);
                OUT.positionHCS = TransformWorldToHClip(posWS);
                OUT.normalWS  = TransformObjectToWorldNormal(nrm);
                OUT.viewWS    = GetWorldSpaceViewDir(posWS);
                OUT.color     = IN.color * _BaseColor;

                // Fade window: alpha dips to _FadeMin for _FadeOpen of each period, with soft edges.
                half a = 1.0;
                if (_FadePeriod > 0.0)
                {
                    float c    = frac(t / _FadePeriod + phase * 0.159);
                    float edge = _FadeOpen * 0.3;
                    float open = smoothstep(0.0, edge, c) * (1.0 - smoothstep(_FadeOpen - edge, _FadeOpen, c));
                    a = lerp(1.0, _FadeMin, open);
                }
                OUT.alpha = a;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float3 n = normalize(IN.normalWS);
                float3 v = normalize(IN.viewWS);

                half  ndv    = saturate(dot(n, v));
                half3 albedo = IN.color.rgb;
                half  luma   = dot(albedo, half3(0.299, 0.587, 0.114));
                albedo = max(0.0, lerp(luma.xxx, albedo, _Saturation));

                half  lit  = lerp(1.0 - _TopLight, 1.0 + _TopLight * 0.5, n.y * 0.5 + 0.5);
                half3 col  = albedo * lit * _Emission;

                // Fake metal: tint a sky/ground gradient reflected off the surface by the albedo.
                if (_Metal > 0.0)
                {
                    float3 r   = reflect(-v, n);
                    half   sky = smoothstep(-0.15, 0.55, r.y);
                    half3  env = lerp(_GroundColor.rgb, _SkyColor.rgb, sky);
                    env += _SkyColor.rgb * 0.6 * exp(-abs(r.y - 0.05) * 18.0);   // bright horizon band
                    col = lerp(col, albedo * env * 1.5 * _Emission, _Metal);
                }

                half  spec = pow(saturate(dot(n, normalize(float3(-0.35, 1.0, -0.45)))), 18.0) * _Gloss;
                col += spec;

                half rimGlow = pow(1.0 - ndv, _RimPower);
                col += (albedo * 0.6 + 0.4) * rimGlow * _RimGlow;
                col += albedo * pow(ndv, 3.0) * _CenterGlow;

                half rim   = pow(1.0 - ndv, 2.0);
                half alpha = max(IN.alpha, rim * _RimAlpha) * IN.color.a;
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
