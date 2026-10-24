Shader "Arture/MachineFocus"
{
    Properties
    {
        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1, 1, 1, 1)
        _Color("Legacy Color", Color) = (1, 1, 1, 1)
        _OutlineColor("Outline Color", Color) = (1, 0.85, 0.1, 1)
        _OutlineWidth("Outline Width (Pixels)", Range(0.0, 20.0)) = 4.5
        _GlowIntensity("Glow Intensity", Range(0.0, 4.0)) = 1.35
        _RimPower("Rim Power", Range(0.5, 8.0)) = 2.2
        _FadeOpacity("Fade Opacity", Range(0.0, 1.0)) = 0.12
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
        }

        // Pass 0: Silhouette Outline Extrusion Pass
        // Renders backfaces extruded in clip space with depth bias so it forms a clean,
        // bloom-independent outline halo around the actual mesh silhouette, even inside faded casings.
        Pass
        {
            Name "MachineOutline"
            Tags { "LightMode" = "UniversalForward" }

            Cull Front
            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vertOutline
            #pragma fragment fragOutline
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _Color;
                half4 _OutlineColor;
                float _OutlineWidth;
                float _GlowIntensity;
                float _RimPower;
                float _FadeOpacity;
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
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vertOutline(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                ZERO_INITIALIZE(Varyings, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 posWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

                // Extrude slightly in world space to guarantee separation on flat/hard edges,
                // plus screen-space pixel offset for distance-independent silhouette readability in AR.
                float4 clipPos = TransformWorldToHClip(posWS);
                float3 clipNormal = TransformWorldToHClipDir(normalWS, true);

                float2 screenPixels = max(_ScreenParams.xy, float2(1.0, 1.0));
                float2 ndcOffset = normalize(clipNormal.xy + float2(1e-5, 1e-5)) * (_OutlineWidth * 2.0 / screenPixels);
                clipPos.xy += ndcOffset * clipPos.w;

                output.positionCS = clipPos;
                return output;
            }

            half4 fragOutline(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 rgb = _OutlineColor.rgb * max(1.0h, (half)_GlowIntensity);
                return half4(rgb, _OutlineColor.a);
            }
            ENDHLSL
        }

        // Pass 1: Selected Component Rim Halo Overlay Pass
        // Adds a subtle fresnel rim glow on front faces of the selected component so internal contours also pop.
        Pass
        {
            Name "MachineRimHalo"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Back
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vertRim
            #pragma fragment fragRim
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _Color;
                half4 _OutlineColor;
                float _OutlineWidth;
                float _GlowIntensity;
                float _RimPower;
                float _FadeOpacity;
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
                float3 normalWS   : TEXCOORD0;
                float3 viewDirWS  : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vertRim(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                ZERO_INITIALIZE(Varyings, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 posWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(posWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewDirWS = GetWorldSpaceNormalizeViewDir(posWS);
                return output;
            }

            half4 fragRim(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 N = normalize(input.normalWS);
                float3 V = normalize(input.viewDirWS);
                half ndotv = saturate(dot(N, V));
                half rim = pow(1.0h - ndotv, max(0.5h, (half)_RimPower));
                half alpha = saturate(rim * 0.65h * _OutlineColor.a);
                half3 rgb = _OutlineColor.rgb * max(1.0h, (half)_GlowIntensity);
                return half4(rgb, alpha);
            }
            ENDHLSL
        }

        // Pass 2: Faded Ghost Casing Pass
        // Used when fading non-selected machine components so internal parts (e.g. Impeller) are clearly revealed.
        Pass
        {
            Name "MachineFade"
            Tags { "LightMode" = "UniversalForwardOnly" }

            Cull Back
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vertFade
            #pragma fragment fragFade
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _Color;
                half4 _OutlineColor;
                float _OutlineWidth;
                float _GlowIntensity;
                float _RimPower;
                float _FadeOpacity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float2 uv         : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vertFade(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                ZERO_INITIALIZE(Varyings, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 posWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(posWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 fragFade(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 texColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half3 baseRgb = texColor.rgb * _BaseColor.rgb;

                Light mainLight = GetMainLight();
                float3 N = normalize(input.normalWS);
                half ndotl = saturate(dot(N, mainLight.direction)) * 0.6h + 0.4h;
                half3 litColor = baseRgb * mainLight.color * ndotl;

                return half4(litColor, saturate(_FadeOpacity));
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
