int ReflectionSteps;
float ReflectionNearOcclusionStrength;
float ReflectionOcclusionPower;
float ReflectionOcclusionStrength;
float ReflectionDitherOcclusionPower;
float ReflectionConeLength;
float ReflectionConeWidth;
float ReflectionFarOcclusionStrength;
float ReflectionFarthestOcclusionStrength;

int reduceShinyArtifact_Reflections;

void Pass4_float(float4 voxelSpacePosition, float3 voxelOrigin, float3 kernel, float3 worldNormal, float smoothness, float2 uv, float dither, float blueNoise,
int enableDithering, int SEGISphericalSkylight, UnityTexture3D SEGIVolumeLevel0, UnityTexture3D SEGIVolumeLevel1,
UnityTexture3D SEGIVolumeLevel2, UnityTexture3D SEGIVolumeLevel3, UnityTexture3D SEGIVolumeLevel4,
UnityTexture3D SEGIVolumeLevel5, UnitySamplerState _sampler, out
float4 Out)
{
	float skyVisibility = 1.0;

	float3 gi = float3(0, 0, 0);

    float coneLength = ReflectionConeLength; //originalnya 6
	float coneSizeScalar = lerp(1.3, 0.05, smoothness) * coneLength;

    int numSamples = (int) (ReflectionSteps * lerp(SEGIVoxelScaleFactor, 1.0, 0.5));
    
    float3 adjustedKernel = normalize(kernel.xyz + worldNormal.xyz * 0.2 * (1.0 - smoothness)); // kode aslinya tidak pakai "* ReflectionConeWidth"

	//int numSamples = (int)((lerp(uint(ReflectionSteps) / uint(5), ReflectionSteps, smoothness))); // ini originalnya

    
	
	[loop]
	for (int i = 0; i < numSamples; i++)
	{
		float fi = ((float)i) / numSamples;
        
        float coneSize = fi * coneSizeScalar * lerp(SEGIVoxelScaleFactor, 1.0, 0.5); // Aslinya tanpa pakai * lerp(SEGIVoxelScaleFactor, 1.0, 0.5) 

		float coneDistance = (exp2(fi * coneSizeScalar) - 0.998) / exp2(coneSizeScalar);
        
		float3 voxelCheckCoord = voxelOrigin.xyz + adjustedKernel.xyz * (coneDistance * 0.12 * coneLength + 0.001);

		float4 _sample = float4(0.0, 0.0, 0.0, 0.0);
        coneSize = pow(coneSize / ReflectionConeWidth, 2.0) * ReflectionConeWidth;
		int mipLevel = floor(coneSize);
		if (mipLevel == 0)
		{
            _sample = SAMPLE_TEXTURE3D(SEGIVolumeLevel0, _sampler.samplerstate, float4(voxelCheckCoord.xyz, coneSize));
            _sample = lerp(_sample, SAMPLE_TEXTURE3D(SEGIVolumeLevel1, _sampler.samplerstate, float4(voxelCheckCoord.xyz, coneSize + 1.0)), frac(coneSize));
        }
		else if (mipLevel == 1)
		{
            _sample = SAMPLE_TEXTURE3D(SEGIVolumeLevel1, _sampler.samplerstate, float4(voxelCheckCoord.xyz, coneSize));
            _sample = lerp(_sample, SAMPLE_TEXTURE3D(SEGIVolumeLevel2, _sampler.samplerstate, float4(voxelCheckCoord.xyz, coneSize + 1.0)), frac(coneSize));
        }
		else if (mipLevel == 2)
		{
            _sample = SAMPLE_TEXTURE3D(SEGIVolumeLevel2, _sampler.samplerstate, float4(voxelCheckCoord.xyz, coneSize));
            _sample = lerp(_sample, SAMPLE_TEXTURE3D(SEGIVolumeLevel3, _sampler.samplerstate, float4(voxelCheckCoord.xyz, coneSize + 1.0)), frac(coneSize));
        }
		else if (mipLevel == 3)
		{
            _sample = SAMPLE_TEXTURE3D(SEGIVolumeLevel3, _sampler.samplerstate, float4(voxelCheckCoord.xyz, coneSize));
            _sample = lerp(_sample, SAMPLE_TEXTURE3D(SEGIVolumeLevel4, _sampler.samplerstate, float4(voxelCheckCoord.xyz, coneSize + 1.0)), frac(coneSize));
        }
		else if (mipLevel == 4)
		{
            _sample = SAMPLE_TEXTURE3D(SEGIVolumeLevel4, _sampler.samplerstate, float4(voxelCheckCoord.xyz, coneSize));
            _sample = lerp(_sample, SAMPLE_TEXTURE3D(SEGIVolumeLevel5, _sampler.samplerstate, float4(voxelCheckCoord.xyz, coneSize + 1.0)), frac(coneSize));
        }
        else if (mipLevel == 5)
        {
            _sample = SAMPLE_TEXTURE3D(SEGIVolumeLevel5, _sampler.samplerstate, float4(voxelCheckCoord.xyz, coneSize));
        }
        else
        {
            _sample = float4(0, 0, 0, 0);
        }

		float occlusion = skyVisibility;

		//float falloffFix = fi * 6.0 + 0.6;

		gi.rgb += _sample.rgb * (coneSize * 5.0 + 1.0) * occlusion * 0.5;
        //gi.rgb = max(gi.rgb, _sample.rgb * (coneSize * 5.0 + 1.0) * occlusion * 0.5); // punya dd
		
		// Tambahan perlu agar tidak ada shiny artifact
        if (reduceShinyArtifact_Reflections > 0 && (i == 0 || i == 1))
            gi.rgb *= 0;
        
		// extra added
        _sample.a *= lerp(saturate(fi / 0.2), 1.0, ReflectionNearOcclusionStrength);
        if (enableDithering > 0)
        {
            gi.rgb *= (dither+0.02);
            //skyVisibility = pow(saturate(1.0 - _sample.a * 0.5), (lerp(4.0, 1.0, smoothness) + coneSize * 0.5) * ReflectionDitherOcclusionPower);
            skyVisibility *= pow(saturate(1.0 - (_sample.a) * (coneSize * 0.2 * ReflectionFarOcclusionStrength + 1.0 + coneSize * coneSize * 0.05 * ReflectionFarthestOcclusionStrength) * ReflectionOcclusionStrength), lerp(0.014, 1.5 * ReflectionOcclusionPower, min(1.0, coneSize / 5.0)));
            //skyVisibility *= pow(saturate(1.0 - _sample.a * (coneSize * 0.2 * ReflectionOcclusionStrength  + 1.0 + coneSize * coneSize * 0.05) * ReflectionOcclusionStrength), lerp(0.014, 1.5 * ReflectionOcclusionPower, min(1.0, coneSize / 5.0)));
        }
        else
        {
            skyVisibility *= pow(saturate(1.0 - (_sample.a) * (coneSize * 0.2 * ReflectionFarOcclusionStrength + 1.0 + coneSize * coneSize * 0.05 * ReflectionFarthestOcclusionStrength) * ReflectionOcclusionStrength), lerp(0.014, 1.5 * ReflectionOcclusionPower, min(1.0, coneSize / 5.0)));
            //skyVisibility *= pow(saturate(1.0 - _sample.a * (coneSize * 0.2 * ReflectionOcclusionStrength  + 1.0 + coneSize * coneSize * 0.05) * ReflectionOcclusionStrength), lerp(0.014, 1.5 * ReflectionOcclusionPower, min(1.0, coneSize / 5.0)));

            //skyVisibility = pow(saturate(1.0 - _sample.a * 0.5), (lerp(4.0, 1.0, smoothness) + coneSize * 0.5) * ReflectionOcclusionPower); // original, gak dipakai karena kalau dipakai dia ngeleak dan tidak sesuai dgn diffuse tracingnya awowkkw
        }
    }

	skyVisibility *= saturate(dot(worldNormal, kernel) * 0.7 + 0.3);
	skyVisibility *= lerp(saturate(dot(kernel, float3(0.0, 1.0, 0.0)) * 10.0), 1.0, SEGISphericalSkylight);
    
    gi *= saturate(dot(worldNormal, kernel) * 10.0);
    

	Out = float4(gi.rgb * 4.0, skyVisibility);
}