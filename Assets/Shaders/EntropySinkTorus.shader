Shader "MobileIdleBuilder/EntropySinkTorus"
{
    // Gold torus structure for Maxwell's Demon — the flat donut mesh built by TorusMeshBuilder.
    // URP Unlit (no geometry stage, mobile-safe). The surface is a gold base with brighter accent
    // highlights tracing one of several procedural patterns that continuously swirl (driven by _Time)
    // around the ring/tube. EntropySinkStructure (CPU) picks the patterns at random and crossfades
    // between them by ramping _Blend from 0 (pattern A) to 1 (pattern B).
    //   uv.x = around the main ring (0..1),  uv.y = around the tube cross-section (0..1)
    Properties
    {
        _BaseColor   ("Base Color", Color)   = (1.0, 0.78, 0.30, 1)   // gold; PresenceReceiver may tint per-instance
        _AccentColor ("Accent Color", Color) = (1.0, 0.95, 0.70, 1)   // pale/white-gold highlight
        _Emission    ("Emission", Float)     = 1.15
        _PatternA    ("Pattern A Index", Float) = 0
        _PatternB    ("Pattern B Index", Float) = 1
        _Blend       ("A->B Blend", Range(0,1)) = 0
        _SwirlSpeed  ("Swirl Speed", Float)  = 0.25
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }

        // Cull Off keeps the ring solid from both faces (unlit, so back-face shading is identical).
        Cull Off
        ZWrite On

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _AccentColor;
                float  _Emission;
                float  _PatternA;
                float  _PatternB;
                float  _Blend;
                float  _SwirlSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv          = IN.uv;
                return OUT;
            }

            // Cheap hand-rolled sum-of-sines value noise (no texture lookup -> mobile safe), mirroring
            // FieldWireMesh's FieldNoise approach.
            float SinNoise(float2 p, float t)
            {
                float n = sin(p.x * 6.2831 + t)
                        + sin(p.y * 6.2831 - t * 0.7)
                        + sin((p.x + p.y) * 4.7124 + t * 1.3)
                        + sin((p.x - p.y) * 9.4247 - t * 0.5);
                return n * 0.25; // roughly -1..1
            }

            // Returns a 0..1 highlight mask for the given pattern. uv.x = around the ring, uv.y = tube.
            float PatternValue(int mode, float2 uv, float t)
            {
                if (mode == 0)
                {
                    // Spiral stripes winding around the ring and tube.
                    float s = frac(uv.x * 6.0 + uv.y * 3.0 + t);
                    return smoothstep(0.45, 0.55, abs(s - 0.5) * 2.0);
                }
                else if (mode == 1)
                {
                    // Radial bands marching around the tube cross-section.
                    return saturate(0.5 + 0.5 * sin(uv.y * 6.2831 * 5.0 + t * 2.5));
                }
                else if (mode == 2)
                {
                    // Swirling woven checker (product of two travelling sines).
                    float a = sin(uv.x * 6.2831 * 8.0 + t * 1.5);
                    float b = sin(uv.y * 6.2831 * 6.0 - t * 1.5);
                    return saturate(0.5 + 0.5 * a * b);
                }
                else if (mode == 3)
                {
                    // Rotating sunburst — spokes around the ring.
                    return saturate(0.5 + 0.5 * sin(uv.x * 6.2831 * 16.0 + t * 3.0));
                }
                else
                {
                    // Drifting marble/noise.
                    return saturate(0.5 + 0.5 * SinNoise(uv * 2.0, t));
                }
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float t = _Time.y * _SwirlSpeed * 6.2831;

                float ma = PatternValue((int)round(_PatternA), IN.uv, t);
                float mb = PatternValue((int)round(_PatternB), IN.uv, t);
                float mask = lerp(ma, mb, saturate(_Blend));

                half3 col = lerp(_BaseColor.rgb, _AccentColor.rgb, mask) * _Emission;
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
