Shader "MobileIdleBuilder/ElectronCloud"
{
    // An electron on a belt: a soft additive haze built from several overlapping puffs
    // (BeltParticleMeshes.ElectronCloud). The vertex shader breathes the whole cloud, swirls it about Y
    // and wobbles each puff on its own seed (TEXCOORD1.x). The fragment fades toward each puff's
    // silhouette, so overlaps glow brighter in the middle and the edge has no hard line.
    // Phase comes from the object's world position, as in BeltParticle.
    Properties
    {
        _BaseColor   ("Base Color", Color) = (0.306, 0.804, 0.769, 1)
        _Intensity   ("Intensity", Float) = 0.45
        _Breathe     ("Breathe Amplitude", Float) = 0.12
        _BreatheFreq ("Breathe Frequency", Float) = 1.6
        _SwirlSpeed  ("Swirl Speed (rad/s)", Float) = 0.8
        _WobbleAmp   ("Puff Wobble", Float) = 0.025
        _WobbleFreq  ("Puff Wobble Frequency", Float) = 2.3
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Blend SrcAlpha One   // additive glow
        ZWrite Off
        Cull Back

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float  _Intensity;
                float  _Breathe;
                float  _BreatheFreq;
                float  _SwirlSpeed;
                float  _WobbleAmp;
                float  _WobbleFreq;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 jitter     : TEXCOORD1;   // seed in x
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                float3 viewWS      : TEXCOORD1;
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                float3 originWS = TransformObjectToWorld(float3(0, 0, 0));
                float  phase = dot(originWS.xz, float2(2.17, 3.41));
                float  t     = _Time.y;
                float  seed  = IN.jitter.x;

                float3 pos = IN.positionOS.xyz;
                pos *= 1.0 + _Breathe * sin(t * _BreatheFreq + phase);

                // Each puff drifts on its own slow orbit, so the cloud's outline keeps changing.
                float  w = t * _WobbleFreq + seed * 2.39 + phase;
                pos += float3(sin(w), 0.6 * sin(w * 1.31 + 1.7), cos(w * 0.87)) * _WobbleAmp;

                float a = t * _SwirlSpeed + phase;
                float s = sin(a), c = cos(a);
                pos = float3(c * pos.x + s * pos.z, pos.y, -s * pos.x + c * pos.z);
                float3 nrm = float3(c * IN.normalOS.x + s * IN.normalOS.z, IN.normalOS.y,
                                    -s * IN.normalOS.x + c * IN.normalOS.z);

                float3 posWS = TransformObjectToWorld(pos);
                OUT.positionHCS = TransformWorldToHClip(posWS);
                OUT.normalWS = TransformObjectToWorldNormal(nrm);
                OUT.viewWS   = GetWorldSpaceViewDir(posWS);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                half facing = saturate(dot(normalize(IN.normalWS), normalize(IN.viewWS)));
                half a = pow(facing, 1.5) * _Intensity;
                return half4(_BaseColor.rgb, a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
