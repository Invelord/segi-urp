Shader "Hidden/SEGIRenderSunDepth" {
Properties {
	_Color ("Main Color", Color) = (1,1,1,1)
	_MainTex ("Base (RGB)", 2D) = "white" {}
	_Cutoff ("Alpha Cutoff", Range(0,1)) = 0.333
}
SubShader 
{
	Tags {"RenderPipeline" = "UniversalPipeline" }
	Pass
	{
		Name "ForwardLit"
		Tags { "LightMode" = "UniversalForward" }

		HLSLPROGRAM
			
			#pragma vertex Vertex
			#pragma fragment Fragment
			#pragma target 5.0
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

			//#include "UnityCG.cginc"
			
			TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex); float4 _MainTex_ST;
			float4 _Color;
			float _Cutoff;
			float4 UnityObjectToClipPos(float3 pos)
			{
				return mul(UNITY_MATRIX_VP, mul(UNITY_MATRIX_M, float4 (pos, 1)));
			}
			inline float3 UnityObjectToWorldNormal(in float3 norm)
			{
#ifdef UNITY_ASSUME_UNIFORM_SCALING
				return UnityObjectToWorldDir(norm);
#else
				// mul(IT_M, norm) => mul(norm, I_M) => {dot(norm, I_M.col0), dot(norm, I_M.col1), dot(norm, I_M.col2)}
				return normalize(mul(norm, (float3x3)unity_WorldToObject));
#endif
			}

			struct Attributes { //appdata_base
				float4 vertex : POSITION;
				float3 normal : NORMAL;
				float2 uv : TEXCOORD0;
				float4 color : COLOR;
			};
			struct VertexOutput
			{
				float4 pos : SV_POSITION;
				float4 uv : TEXCOORD0;
				float3 normal : TEXCOORD1;
				half4 color : COLOR;
			};
			VertexOutput Vertex(Attributes v)
			{
				VertexOutput o;
				
				o.pos = UnityObjectToClipPos(v.vertex);
				
				float3 pos = o.pos;
				
				o.pos.xy = (o.pos.xy);
				
				
				o.uv = float4(TRANSFORM_TEX(v.uv, _MainTex), 1.0, 1.0);
				o.normal = UnityObjectToWorldNormal(v.normal);
				
				o.color = v.color;
				
				return o;
			}
			
			
			TEXTURE2D(GILightCookie);
			float4x4 GIProjection;
			
			float4 Fragment (VertexOutput input) : SV_Target
			{
				float depth = input.pos.z;
				
				return depth;
			}
			
		ENDHLSL
	}
}

Fallback "Legacy Shaders/VertexLit"
}
