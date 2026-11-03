Shader "MobileIdleBuilder/ConveyorChannel"
{
    // Opaque, flat channel bed under the flowing water ribbon. Built to the same footprint as the water
    // by ConveyorFlowMeshBuilder (no raised banks/rails). It exists only to give the translucent ribbon
    // an opaque dark base and to write depth so the (also-transparent) grid tiles beneath are occluded
    // and the water (Transparent+50) draws cleanly on top.
    Properties
    {
        _FloorColor ("Channel Bed", Color) = (0.10, 0.12, 0.16, 1)
    }

    SubShader
    {
        // Opaque, drawn before the transparent grid tiles; it writes depth so the tiles beneath it are
        // correctly occluded, and the translucent water (Transparent+50) draws on top.
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }

        ZWrite On
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _FloorColor;
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

            half4 frag (Varyings IN) : SV_Target
            {
                return half4(_FloorColor.rgb, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
