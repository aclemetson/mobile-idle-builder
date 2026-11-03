Shader "MobileIdleBuilder/ConveyorFlow"
{
    // Translucent flowing "river" surface for conveyor cells. Drawn on the flat ribbon mesh built by
    // ConveyorFlowMeshBuilder, where uv.y runs 0 (inbound edge) -> 1 (outbound edge) along the flow
    // and uv.x runs 0..1 across the channel. Highlights are LENGTHWISE streaks (functions of uv.x)
    // that run along the flow and drift downstream, so the current reads as streamlines moving in the
    // travel direction. The channel banks fade out (soft edges) and the whole surface is translucent.
    //
    // Edge alignment: every uv.y-driven wave uses a WHOLE number of cycles per cell (× TAU), so the
    // pattern is continuous across collinear cell edges (cell A's uv.y=1 phase == cell B's uv.y=0
    // phase); _Time is global so adjacent cells also stay in sync over time. No textures (mobile-safe).
    Properties
    {
        _BaseColor    ("Deep Color", Color)  = (0.06, 0.20, 0.35, 1)
        _FlowColor    ("Crest Color", Color) = (0.45, 0.85, 1.00, 1)
        _FlowSpeed    ("Flow Speed", Float)  = 1.4
        _StreakCycles ("Streak Cycles / Cell", Float) = 1.0
        _DriftCycles  ("Drift Cycles / Cell", Float)  = 2.0
        _LaneCount    ("Streak Lanes", Float) = 5.0
        _LineWidth    ("Streak Width", Range(0.01, 0.5)) = 0.20
        _Meander      ("Streak Meander", Float) = 0.06
        _EdgeFade     ("Edge Softness", Range(0.001, 0.5)) = 0.24
        _BaseAlpha    ("Base Alpha", Range(0, 1)) = 0.45
        _LineAlpha    ("Streak Alpha", Range(0, 1)) = 0.5
        _Emission     ("Emission Boost", Float) = 1.4
    }

    SubShader
    {
        // Transparent+50 keeps belts above the (also-transparent) grid tiles, which otherwise win the
        // per-object sort at some camera angles and overdraw the ribbon, making cells flicker out.
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
                float4 _BaseColor;
                float4 _FlowColor;
                float  _FlowSpeed;
                float  _StreakCycles;
                float  _DriftCycles;
                float  _LaneCount;
                float  _LineWidth;
                float  _Meander;
                float  _EdgeFade;
                float  _BaseAlpha;
                float  _LineAlpha;
                float  _Emission;
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

            // A set of bright lengthwise streaks across the channel width. The lane offset meanders
            // with uv.y (a whole number of cycles per cell, so it matches at edges) and that meander
            // travels downstream over time (the flow).
            float Streaks(float2 uv, float t, float lanes, float width, float meanderAmt, float cycles)
            {
                float meander = sin(uv.y * TAU * cycles - t) * meanderAmt;
                float lanePos = frac((uv.x + meander) * lanes);
                float dist    = abs(lanePos - 0.5);          // 0 at a lane centre
                return smoothstep(width, 0.0, dist);          // soft thin line per lane
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float t = _Time.y * _FlowSpeed;

                // Primary streaks + a finer, faster secondary set for richer water motion.
                float streak  = Streaks(IN.uv, t,       _LaneCount,       _LineWidth,       _Meander,       _StreakCycles);
                float streak2 = Streaks(IN.uv, t * 1.7, _LaneCount * 2.0, _LineWidth * 0.6, _Meander * 0.6, _StreakCycles * 2.0) * 0.5;

                // Brightness drifts along the flow (whole cycles per cell → seamless across edges).
                float drift = sin(IN.uv.y * TAU * _DriftCycles - t * 1.6) * 0.5 + 0.5;
                float crest = saturate((streak + streak2) * lerp(0.55, 1.0, drift));

                // Soft (blurred) channel banks: fade alpha out toward uv.x = 0 and 1.
                float edge = smoothstep(0.0, _EdgeFade, IN.uv.x) *
                             (1.0 - smoothstep(1.0 - _EdgeFade, 1.0, IN.uv.x));

                half3 col   = lerp(_BaseColor.rgb, _FlowColor.rgb, crest) * _Emission;
                float alpha = (_BaseAlpha + crest * _LineAlpha) * edge;
                return half4(col, saturate(alpha));
            }
            ENDHLSL
        }
    }

    Fallback Off
}
