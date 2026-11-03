Shader "MobileIdleBuilder/CollectorStructure"
{
    // Solid structure for field-collector buildings — the revolved "spindle" mesh built by
    // CollectorMeshBuilder. URP Unlit (no geometry stage, mobile-safe). This shader only shades:
    //   * body emission tinted by _BaseColor (driven per-instance by PresenceReceiver)
    //   * a singularity tip that glows toward the apex (uv.y -> 1)
    //   * _PulseAmp flares the tip glow on each harvest tick
    // The bob / breathe / pulse MOTION is applied on the CPU to the host transform (CollectorStructure)
    // so child objects (the emission door + particles) move with the surface and never detach from it.
    Properties
    {
        _BaseColor   ("Base Color", Color) = (0.55, 0.8, 1, 1)
        _TipColor    ("Tip Color", Color)  = (0.7, 0.92, 1, 1)
        _Emission    ("Body Emission", Float) = 1.0
        _TipGlow     ("Tip Glow", Float) = 4.0
        _PulseAmp    ("Pulse Amplitude", Float) = 0.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }

        // Cull Off keeps the structure solid regardless of revolution winding (it is unlit, so
        // back-face shading is identical); one small mesh makes the extra fill negligible.
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
                float4 _TipColor;
                float  _Emission;
                float  _TipGlow;
                float  _PulseAmp;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float  h           : TEXCOORD0; // 0 at base -> 1 at apex
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.h           = IN.uv.y;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                // Body: PresenceReceiver-tinted base, a touch darker at the planted base.
                half  body = lerp(0.6, 1.0, IN.h);
                half3 col  = _BaseColor.rgb * _Emission * body;

                // Singularity tip: glow ramps hard toward the apex and flares on a production pulse.
                half tip   = pow(saturate(IN.h), 6.0);
                half pulse = 1.0 + _PulseAmp * 3.0;
                col += _TipColor.rgb * _TipGlow * tip * pulse;

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
