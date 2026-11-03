Shader "MobileIdleBuilder/AtomGenerator"
{
    // Solid nucleus body for the Atom Generator building — the revolved bulb built by
    // AtomNucleusMeshBuilder. URP Unlit (no geometry stage, mobile-safe). This shader only shades:
    //   * body emission tinted by _BaseColor (driven per-instance by PresenceReceiver)
    //   * a hot nucleus core that glows brightest at the equator (uv.y ~ 0.45) and the apex
    //   * _PulseAmp flares the whole nucleus on each production tick
    // The breathe / pulse MOTION and the orbiting electron rings live on the CPU side
    // (AtomGeneratorStructure) so the children inherit the surface motion.
    Properties
    {
        _BaseColor ("Base Color", Color) = (1.0, 0.66, 0.22, 1)
        _CoreColor ("Core Color", Color) = (1.0, 0.85, 0.5, 1)
        _Emission  ("Body Emission", Float) = 1.0
        _CoreGlow  ("Core Glow", Float) = 2.2
        _PulseAmp  ("Pulse Amplitude", Float) = 0.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }

        // Cull Off keeps the body solid regardless of revolution winding (unlit, so back-face shading
        // is identical); one small mesh makes the extra fill negligible.
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
                float4 _CoreColor;
                float  _Emission;
                float  _CoreGlow;
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
                half pulse = 1.0 + _PulseAmp * 2.5;

                // Body: PresenceReceiver-tinted base, fuller toward the equator.
                half body = lerp(0.55, 1.0, IN.h);
                half3 col = _BaseColor.rgb * _Emission * body * pulse;

                // Hot nucleus core: brightest at the equator (h ~ 0.45), a softer second lobe at the apex.
                half core = saturate(1.0 - abs(IN.h - 0.45) * 1.7);
                half tip  = pow(saturate(IN.h), 5.0);
                col += _CoreColor.rgb * _CoreGlow * (core * core + tip * 0.6) * pulse;

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
