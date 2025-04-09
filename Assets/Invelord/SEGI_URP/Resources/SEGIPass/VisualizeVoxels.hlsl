TEXTURE3D(SEGIVolumeLevel0); SAMPLER(SamplerState_Point_Clamp);

void Pass10_float(float3 voxelOrigin, float3 kernel, out float4 Out)
{
	float skyVisibility = 1.0;

	float3 gi = float3(0, 0, 0);

	float coneLength = 6.0;
	float coneSizeScalar = 0.25 * coneLength;

	for (int i = 0; i < 423; i++)
	{
		float fi = ((float)i) / 423;

		float coneSize = fi * coneSizeScalar;

		float3 voxelCheckCoord = voxelOrigin.xyz + kernel.xyz * (0.12 * coneLength * fi * fi + 0.005);

		float4 _sample = float4(0.0, 0.0, 0.0, 0.0);

		_sample = SAMPLE_TEXTURE3D(SEGIVolumeLevel0, UnityBuildSamplerStateStruct(SamplerState_Point_Clamp).samplerstate, float4(voxelCheckCoord.xyz, coneSize));

		float occlusion = skyVisibility;

		float falloffFix = fi * 6.0 + 0.6;

		gi.rgb += _sample.rgb * (coneSize * 5.0 + 1.0) * occlusion * 0.5;
		skyVisibility *= saturate(1.0 - _sample.a);
	}

	Out = float4(gi.rgb, skyVisibility);
}