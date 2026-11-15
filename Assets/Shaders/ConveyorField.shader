Shader "MobileIdleBuilder/ConveyorField"
{
    // Belt surface: a quantum field lattice that carries particles. Grid lines over the bare floor (no bed)
    // bend and light up as wave packets roll downstream; the packets are what push the particles along.
    //
    // Drawn on the ConveyorFlowMeshBuilder ribbon: uv.y runs 0 (inbound edge) -> 1 (outbound edge), uv.x
    // runs 0..1 across. Every uv.y pattern repeats a WHOLE number of times per cell, so it meets itself
    // at cell edges, bends and merges.
    //
    // Sync with the items: _BeltPhase is a GLOBAL set by ConveyorVisualizer each frame. It advances by
    // (belt speed in cells/s) * dt, the same rate ConveyorSystem advances ConveyorItem.Progress, so a
    // packet crest travels exactly with the particles riding it and speeds up with conveyor research.
    Properties
    {
        _LineColor  ("Line Color", Color) = (1.0, 0.95, 0.84, 1)
        _GlowColor  ("Packet Glow Color", Color) = (1.0, 0.70, 0.42, 1)
        _LineAlpha  ("Line Alpha (rest)", Range(0, 1)) = 0.45
        _GlowAlpha  ("Packet Glow Alpha", Range(0, 1)) = 0.35
        _Emission   ("Emission Boost", Float) = 1.3
        _CrossLines ("Cross Lines / Cell", Float) = 8
        _Lanes      ("Lengthwise Lines", Float) = 5
        _Bend       ("Packet Bend", Float) = 0.45
        _EdgeFade   ("Edge Softness", Range(0.001, 0.5)) = 0.18
    }

    SubShader
    {
        // Transparent+50: above the transparent grid tiles, which otherwise win the per-object sort at some angles.
        Tags { "RenderType"="Transparent" "Queue"="Transparent+50" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #define TAU 6.2831853

            CBUFFER_START(UnityPerMaterial)
                float4 _LineColor;
                float4 _GlowColor;
                float  _LineAlpha;
                float  _GlowAlpha;
                float  _Emission;
                float  _CrossLines;
                float  _Lanes;
                float  _Bend;
                float  _EdgeFade;
            CBUFFER_END

            float _BeltPhase;   // global, in cells travelled (see header)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float2 arm        : TEXCOORD1;   // x = 1 on a merge's secondary arm (ConveyorFlowMeshBuilder)
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float  secondary   : TEXCOORD1;
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv          = IN.uv;
                OUT.secondary   = IN.arm.x;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float2 uv = IN.uv;
                float  ph = _BeltPhase;

                // One main packet per cell, plus a smaller one at twice the frequency; both move one cell
                // per unit of phase, i.e. at item speed.
                float env  = pow(0.5 + 0.5 * cos(TAU * (uv.y - ph)), 5.0);
                float env2 = pow(0.5 + 0.5 * cos(TAU * (uv.y * 2.0 - ph * 2.0 + 0.4)), 8.0) * 0.5;
                float e    = env + env2;

                // Cross lines bulge downstream through a packet; lengthwise lines pinch toward the centre.
                float across = sin(PI * uv.x);
                float yl = frac(uv.y * _CrossLines - e * _Bend * across + 0.5) - 0.5;
                float xl = frac((0.5 + (uv.x - 0.5) * (1.0 - e * 0.25)) * _Lanes) - 0.5;
                float lines = max(smoothstep(0.08, 0.0, abs(yl)), smoothstep(0.07, 0.0, abs(xl)) * 0.7);

                float glow = e * (0.35 + 0.65 * across);

                // A merge's secondary arm hands over to the main arm's lattice where they overlap (exit
                // half), so the junction shows one grid; its glow halves there so the overlap isn't brighter.
                float handover = IN.secondary * smoothstep(0.2, 0.55, uv.y);
                lines *= 1.0 - handover;
                glow  *= 1.0 - 0.5 * handover;

                float3 col = _LineColor.rgb * lines * (0.7 + e * 0.8) + _GlowColor.rgb * glow * 0.6;
                float  a   = lines * (_LineAlpha + e * (1.0 - _LineAlpha)) + glow * _GlowAlpha;

                float edge = smoothstep(0.0, _EdgeFade, uv.x) * (1.0 - smoothstep(1.0 - _EdgeFade, 1.0, uv.x));
                return half4(col * _Emission, saturate(a) * edge);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
