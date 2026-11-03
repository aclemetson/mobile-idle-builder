Shader "MobileIdleBuilder/FieldWireMesh"
{
    // Wire-mesh overlay for resource fields. Drawn as a line-topology mesh
    // (MeshTopology.Lines) built by FieldWireMeshBuilder, so this shader only
    // displaces and tints the grid points — no geometry stage (mobile-safe).
    //   * ambient fluctuation: sum-of-sines noise on local XZ, driven by _Time
    //   * tap bounce: a radial pulse whose envelope (_BounceAmp) is decayed in C#
    Properties
    {
        _BaseColor  ("Base Color", Color) = (0.4, 0.8, 1, 1)
        _NoiseAmp   ("Noise Amplitude", Float) = 0.07
        _NoiseFreq  ("Noise Frequency", Float) = 6.0
        _NoiseSpeed ("Noise Speed", Float) = 1.4
        _BounceAmp  ("Bounce Amplitude", Float) = 0.0
        _BounceFreq ("Bounce Frequency", Float) = 9.0
        _Emission   ("Emission Boost", Float) = 3.6
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Blend SrcAlpha One   // additive glow over the tile
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float  _NoiseAmp;
                float  _NoiseFreq;
                float  _NoiseSpeed;
                float  _BounceAmp;
                float  _BounceFreq;
                float  _Emission;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float  wave        : TEXCOORD0;
            };

            // Cheap hand-written noise (sum of sines) — no texture lookup.
            float FieldNoise(float2 p, float t)
            {
                float n = sin(p.x * _NoiseFreq + t)
                        + sin(p.y * _NoiseFreq * 1.3 - t * 0.8)
                        + sin((p.x + p.y) * _NoiseFreq * 0.7 + t * 1.3);
                return n * 0.33333;
            }

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                float3 posOS = IN.positionOS.xyz;
                float  t     = _Time.y * _NoiseSpeed;

                float ambient = FieldNoise(posOS.xz, t) * _NoiseAmp;

                // Radial pulse from the tile centre; _BounceAmp is the decaying
                // envelope written from FieldWireMesh on tap (0 at rest).
                float r      = length(posOS.xz);
                float bounce = _BounceAmp * cos(r * _BounceFreq - _Time.y * 12.0);

                posOS.y += ambient + bounce;

                OUT.positionHCS = TransformObjectToHClip(posOS);
                OUT.wave        = ambient + bounce;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                // High floor keeps the whole grid visible; crests brighten further on top.
                half glow = saturate(1.0 + IN.wave * 5.0);
                half4 col = _BaseColor;
                col.rgb  *= _Emission * glow;
                col.a     = _BaseColor.a * glow;
                return col;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
