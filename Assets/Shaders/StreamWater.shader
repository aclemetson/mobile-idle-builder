Shader "MobileIdleBuilder/StreamWater"
{
    // Chemistry's streams (StreamSegment belts): clear shallow water over a pebble bed, with light caustics
    // drifting downstream (concept F2, 2026-11-22). Drawn on the ConveyorFlowMeshBuilder ribbon, like
    // ConveyorField: uv.x runs 0..1 across the ribbon, TEXCOORD3 carries the flow direction per vertex.
    //
    // The pebbles are a world-space cell pattern, so they line up across tiles, corners and merges. The
    // caustics are laid along the ribbon (uv.y) and scroll by _BeltPhase (global, cells travelled at item
    // speed, set by ConveyorVisualizer), so the light moves with the items and speeds up with research.
    Properties
    {
        _WaterDeep    ("Water (middle)", Color) = (0.05, 0.30, 0.50, 1)
        _WaterShallow ("Water (banks)", Color) = (0.25, 0.62, 0.78, 1)
        _StoneA       ("Pebble A", Color) = (0.36, 0.43, 0.47, 1)
        _StoneB       ("Pebble B", Color) = (0.24, 0.30, 0.34, 1)
        _Moss         ("Pebble Moss", Color) = (0.25, 0.49, 0.35, 1)
        _Gap          ("Bed between pebbles", Color) = (0.05, 0.12, 0.15, 1)
        _Caustic      ("Caustic Light", Color) = (0.75, 0.97, 1.0, 1)
        _WaterTint    ("Water Over Bed", Range(0, 1)) = 0.62
        _CausticStr   ("Caustic Strength", Float) = 0.45
        _PebblesPerCell ("Pebbles / Cell", Float) = 7
        _CausticsPerCell ("Caustic Cells / Cell (whole number)", Float) = 3
        _CausticsAcross ("Caustic Cells Across", Float) = 2.4
        _CellSize     ("Cell Size (world)", Float) = 1.5
        _EdgeFade     ("Edge Softness", Range(0.001, 0.5)) = 0.12
        _Alpha        ("Opacity", Range(0, 1)) = 0.95
    }

    SubShader
    {
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
            #define TWO_PI 6.2831853

            CBUFFER_START(UnityPerMaterial)
                float4 _WaterDeep;
                float4 _WaterShallow;
                float4 _StoneA;
                float4 _StoneB;
                float4 _Moss;
                float4 _Gap;
                float4 _Caustic;
                float  _WaterTint;
                float  _CausticStr;
                float  _PebblesPerCell;
                float  _CausticsPerCell;
                float  _CausticsAcross;
                float  _CellSize;
                float  _EdgeFade;
                float  _Alpha;
            CBUFFER_END

            float _BeltPhase;   // global, in cells travelled (see ConveyorField)
            float _StreamDry;   // global: 1 dims streams that no Spring feeds (unused for now)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float2 flowOS     : TEXCOORD3;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float2 posWS       : TEXCOORD1;
                float2 flowWS      : TEXCOORD2;
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv     = IN.uv;
                OUT.posWS  = TransformObjectToWorld(IN.positionOS.xyz).xz;
                OUT.flowWS = TransformObjectToWorldDir(float3(IN.flowOS.x, 0.0, IN.flowOS.y), false).xz;
                return OUT;
            }

            float2 Hash2(float2 p)
            {
                p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
                return frac(sin(p) * 43758.5453);
            }

            // Voronoi: x = distance to the nearest point, y = distance to the edge between cells,
            // zw = the nearest cell's id (for a per-pebble colour). periodY > 0 repeats the pattern every
            // periodY cells along y, so a pattern laid along each belt piece meets itself at cell edges.
            float4 Voronoi(float2 p, float periodY)
            {
                float2 n = floor(p), f = frac(p);
                float  d1 = 8.0, d2 = 8.0;
                float2 id = n;
                for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++)
                {
                    float2 g = float2(i, j);
                    float2 c = n + g;
                    if (periodY > 0.5) c.y = c.y - periodY * floor(c.y / periodY);
                    float2 o = Hash2(c);
                    float  d = length(g + o - f);
                    if (d < d1) { d2 = d1; d1 = d; id = n + g; }
                    else if (d < d2) d2 = d;
                }
                return float4(d1, d2 - d1, id);
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float2 cellPos = IN.posWS / _CellSize;

                // ── Pebble bed (static, world space) ──
                float4 peb  = Voronoi(cellPos * _PebblesPerCell, 0.0);
                float  pick = Hash2(peb.zw).x;
                float3 stone = pick < 0.45 ? _StoneA.rgb : (pick < 0.85 ? _StoneB.rgb : _Moss.rgb);
                // Rounded pebbles: lighter in the middle, gaps between them.
                float  roundness = saturate(1.0 - peb.x * 1.1);
                stone *= 0.7 + 0.45 * roundness;
                float  gap  = 1.0 - smoothstep(0.03, 0.12, peb.y);
                float3 bed  = lerp(stone, _Gap.rgb, gap);

                // ── Water tint: deeper in the middle of the channel ──
                float  across = sin(PI * saturate(IN.uv.x));
                float3 water  = lerp(_WaterShallow.rgb, _WaterDeep.rgb, across);
                float3 col    = lerp(bed, water, _WaterTint * (0.6 + 0.4 * across));

                // ── Caustics drifting downstream with the items ──
                // Laid along the ribbon (uv.y = along the flow, one cell per 0..1) and repeating a whole number
                // of times per cell, so pieces meet seamlessly and bends never shear the pattern.
                float  per = max(1.0, round(_CausticsPerCell));
                float  ph  = frac(_BeltPhase);
                float2 cp  = float2(IN.uv.x * _CausticsAcross, (IN.uv.y - ph) * per);
                // A gentle wobble that also repeats per cell.
                cp.x += 0.12 * sin(TWO_PI * (IN.uv.y - ph) * 2.0 + IN.uv.x * 5.0);
                float  c  = 1.0 - smoothstep(0.0, 0.05, Voronoi(cp, per).y);
                col += _Caustic.rgb * c * _CausticStr * (0.35 + 0.65 * across);

                // Soft edges against the banks.
                float edge = smoothstep(0.0, _EdgeFade, IN.uv.x) * smoothstep(0.0, _EdgeFade, 1.0 - IN.uv.x);
                return half4(col, _Alpha * edge);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
