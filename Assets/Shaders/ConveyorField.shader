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
        // 0 = lattice stretched along each belt piece (ribbon UVs); 1 = one square lattice in world space,
        // aligned to the tiles, that every belt reveals; 2 = the world lattice turned 45 degrees (diamond).
        _Mapping    ("Lattice Mapping", Float) = 0
        _CellSize   ("Cell Size (world)", Float) = 1.5
        _GridPerCell ("World Lattice Lines / Cell", Float) = 8
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
                float  _Mapping;
                float  _CellSize;
                float  _GridPerCell;
            CBUFFER_END

            float _BeltPhase;   // global, in cells travelled (see header)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float2 arm        : TEXCOORD1;   // x = 1 on a merge's secondary arm; y = arm length in cells
                float2 mainAxis   : TEXCOORD2;   // secondary arm: main belt's sideways axis / half-width (0 = none)
                float2 flowOS     : TEXCOORD3;   // flow direction (xz) at this vertex
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float  secondary   : TEXCOORD1;
                float  crossLines  : TEXCOORD2;
                float2 posXZ       : TEXCOORD3;
                float2 mainAxis    : TEXCOORD4;
                float2 posWS       : TEXCOORD5;
                float2 flowWS      : TEXCOORD6;
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv          = IN.uv;
                OUT.secondary   = IN.arm.x;
                OUT.posXZ       = IN.positionOS.xz;
                OUT.mainAxis    = IN.mainAxis;
                OUT.posWS       = TransformObjectToWorld(IN.positionOS.xyz).xz;
                OUT.flowWS      = TransformObjectToWorldDir(float3(IN.flowOS.x, 0.0, IN.flowOS.y), false).xz;
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

                float across = sin(PI * uv.x);
                float lines;
                if (_Mapping < 0.5)
                {
                    // Ribbon lattice: cross lines bulge downstream through a packet; lengthwise lines pinch.
                    float cy = uv.y * IN.crossLines - e * _Bend * across;
                    float cx = (0.5 + (uv.x - 0.5) * (1.0 - e * 0.25)) * _Lanes;
                    lines = max(GridLine(cy, 0.07), GridLine(cx, 0.06) * 0.7);
                }
                else
                {
                    // World lattice: one grid on the ground, so corners stay square and merges/forks share the
                    // very same lines. Packets push the lines along the belt's flow direction, which the mesh
                    // carries per vertex (smoothly interpolated round curves and forks).
                    float2 f = IN.flowWS;
                    float2 t = dot(f, f) > 1e-8 ? f * rsqrt(dot(f, f)) : float2(1.0, 0.0);

                    float2 q = IN.posWS / _CellSize * _GridPerCell - t * (e * _Bend * across);
                    if (_Mapping > 1.5)
                    {
                        q = float2(q.x + q.y, q.x - q.y) * 0.70710678;
                        lines = max(GridLine(q.x, 0.07), GridLine(q.y, 0.07)) * 0.9;
                    }
                    else
                    {
                        // Lines across the flow read stronger than lines along it, as on the ribbon lattice.
                        lines = max(GridLine(q.x, 0.07) * lerp(0.7, 1.0, abs(t.x)),
                                    GridLine(q.y, 0.07) * lerp(0.7, 1.0, abs(t.y)));
                    }
                }

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
