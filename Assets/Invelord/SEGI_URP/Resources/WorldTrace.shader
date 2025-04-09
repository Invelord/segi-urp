Shader "Hidden/WorldTrace" {
	Properties
	{
		_Color ("Main Color", Color) = (1,1,1,1)
		_MainTex ("Base (RGB)", 2D) = "white" {}
		_EmissionColor("Color", Color) = (0,0,0)
		_Cutoff ("Alpha Cutoff", Range(0,1)) = 0.333
	}
	SubShader 
	{
		Cull Off
		ZTest Always
		Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
		Pass
		{
			Name "ForwardLit"
			Tags { "LightMode" = "UniversalForward" }

			HLSLPROGRAM
			
				#pragma target 5.0
				#pragma require geometry
				#pragma vertex Vertex
				#pragma fragment Fragment
				#pragma geometry Geometry

				// Lighting and shadow keywords
				#pragma multi_compile _ _MAIN_LIGHT_SHADOWS
				#pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
				#pragma multi_compile _ _ADDITIONAL_LIGHTS
				#pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
				#pragma multi_compile _ _SHADOWS_SOFT

				#define SHADOW_CASTER_PASS
				#define PI 3.14159265		

				#include "WorldTrace.hlsl"
			ENDHLSL
		}
	} 
	FallBack Off
}
