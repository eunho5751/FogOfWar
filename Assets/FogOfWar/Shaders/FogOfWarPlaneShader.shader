Shader"FogOfWar/Plane"
{
    Properties
    {
        _MainTex("Texture", 2D) = "black" {}
        _FOWColor("Color", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags 
        { 
            "RenderType" = "Opaque"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalRenderPipeline"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 camRelativeWorldPos : TEXCOORD1;
                float4 screenPos : TEXCOORD2;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _FOWColor;
            float3 _PlanePos;
            float3 _PlaneSize;

            float GetLinearEyeDepth(float depth)
            {
                float perspective = LinearEyeDepth(depth, _ZBufferParams);
                float orthgraphic = (_ProjectionParams.z - _ProjectionParams.y) * (1.0f - depth) + _ProjectionParams.y;
                return lerp(perspective, orthgraphic, unity_OrthoParams.w);
            }

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = input.uv;
                o.camRelativeWorldPos = mul(unity_ObjectToWorld, float4(input.positionOS.xyz, 1.0f)).xyz - _WorldSpaceCameraPos;
                o.screenPos = ComputeScreenPos(o.positionHCS);
                return o;
            }

            float4 frag(Varyings input) : SV_Target
            {
                // 1. Calculate linear eye depth
                float2 screenUV = input.screenPos.xy / input.screenPos.w;
                float rawDepth = SampleSceneDepth(screenUV);
                float linearDepth = GetLinearEyeDepth(rawDepth);
                
                // 2. Reconstruct world position from the depth
                float3 viewPlane = input.camRelativeWorldPos.xyz / dot(input.camRelativeWorldPos.xyz, unity_WorldToCamera._m20_m21_m22);
                float3 worldPos = _WorldSpaceCameraPos + viewPlane * linearDepth;
    
                // 3. Convert the world position to Plane UV coordinates
                //float3 planeRelativePos = worldPos - _PlanePos;
                //float u = planeRelativePos.x / _PlaneSize.x;
                //float v = planeRelativePos.z / _PlaneSize.z;
    
                float3 objectPos = mul(unity_WorldToObject, float4(worldPos, 1)).xyz;
                objectPos += 5;
                objectPos /= 10;
                float2 uv = objectPos.xz;
    
                // 4. Sample FOW texture using the uv coordinates
                float fowValue = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).r;
                float4 fowColor = _FOWColor;
                fowColor.a *= fowValue;
                return fowColor;
            }

            ENDHLSL
        }
    }
}
