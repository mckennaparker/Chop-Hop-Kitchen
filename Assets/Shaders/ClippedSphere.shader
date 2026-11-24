Shader "Custom/ClippedSphere"
{
    Properties
    {
        _BaseColor("Outer Color", Color) = (1,0,0,1)
        _CapColor("Cap Color", Color) = (1,1,1,1)
        _CutPlane("Cut Plane (xyz normal, w offset)", Vector) = (0,1,0,0)
        _CutSide("Cut Side (+1 or -1)", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _CapColor;
                float4 _CutPlane;
                float _CutSide;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.positionOS = v.positionOS.xyz;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS   = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 frag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                // Signed distance to the cut plane; discard the +side*d half
                float d = dot(_CutPlane.xyz, i.positionOS) + _CutPlane.w;
                clip(-d * _CutSide);

                bool isFront = IS_FRONT_VFACE(face, true, false);

                // Back faces = the inside of the far wall, shown as a flat cap
                float3 n = isFront
                    ? normalize(i.normalWS)
                    : TransformObjectToWorldDir(_CutSide * _CutPlane.xyz);
                half3 albedo = isFront ? _BaseColor.rgb : _CapColor.rgb;

                Light mainLight = GetMainLight();
                half3 light = mainLight.color * saturate(dot(n, mainLight.direction));

                #ifdef _ADDITIONAL_LIGHTS
                uint count = GetAdditionalLightsCount();
                for (uint li = 0; li < count; li++)
                {
                    Light l = GetAdditionalLight(li, i.positionWS);
                    light += l.color * l.distanceAttenuation * saturate(dot(n, l.direction));
                }
                #endif

                light += SampleSH(n);
                return half4(albedo * light, 1);
            }
            ENDHLSL
        }
    }
}