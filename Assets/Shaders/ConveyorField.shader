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
                float2 arm        : TEXCOORD1;   // x = 1 on a merge's secondary arm; y = arm length in cells
                float2 mainAxis   : TEXCOORD2;   // secondary arm: main belt's sideways axis / half-width (0 = none)
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float  secondary   : TEXCOORD1;
                float  crossLines  : TEXCOORD2;
                float2 posXZ       : TEXCOORD3;
                float2 mainAxis    : TEXCOORD4;
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv          = IN.uv;
                OUT.secondary   = IN.arm.x;
                OUT.posXZ       = IN.positionOS.xz;
                OUT.mainAxis    = IN.mainAxis;
                // Whole number of cross lines per piece (so cells still meet line-to-line), scaled to the
                // piece's centreline length: a quarter-circle corner gets ~6 instead of 8, same spacing.
                float len       = IN.arm.y > 0.0 ? IN.arm.y : 1.0;
                OUT.crossLines  = max(1.0, round(_CrossLines * len));
                return OUT;
            }

            // An anti-aliased grid line every whole number of c. halfWidth is in line periods, but the line
            // never gets thinner than ~1.5 px (fwidth), so it stays crisp far away and where a corner squeezes
            // the lattice; where lines would be packed tighter than ~3 px it fades instead of shimmering.
            float GridLine(float c, float halfWidth)
            {
                float d  = abs(frac(c + 0.5) - 0.5);    // distance to the nearest line, in periods
                float w  = max(fwidth(c), 1e-4);        // one pixel, in periods
                float hw = max(halfWidth, w * 0.75);
                float l  = 1.0 - smoothstep(hw - w * 0.5, hw + w * 0.5, d);
                return l * saturate(2.0 - w * 4.0);
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
                float cy = uv.y * IN.crossLines - e * _Bend * across;
                float cx = (0.5 + (uv.x - 0.5) * (1.0 - e * 0.25)) * _Lanes;
                float lines = max(GridLine(cy, 0.07), GridLine(cx, 0.06) * 0.7);

                float glow = e * (0.35 + 0.65 * across);

                // A merge's secondary arm hands over to the main arm wherever the main belt covers it: off inside
                // the main belt, fading back in exactly as the main belt's soft edge fades out (_EdgeFade), so the
                // junction shows one grid and is no brighter than a belt. Without a straight main arm, fall back
                // to fading along the arm.
                float handover;
                if (dot(IN.mainAxis, IN.mainAxis) > 0.0)
                {
                    float lat   = abs(dot(IN.posXZ, IN.mainAxis));           // 0 at main centre, 1 at its edge
                    float inner = 1.0 - 2.0 * _EdgeFade;                     // where the main belt is fully drawn
                    handover = IN.secondary * smoothstep(1.0, inner, lat);
                }
                else
                {
                    handover = IN.secondary * smoothstep(0.15, 0.5, uv.y);
                }
                lines *= 1.0 - handover;
                glow  *= 1.0 - handover;

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
