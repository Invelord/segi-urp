// from SEGI.hlsl
int StochasticSampling;
int TraceDirections;
int TraceSteps;
float TraceLength;
float ConeSize;
float OcclusionStrength;
float OcclusionPower;
float GIGain;
float NearLightGain;
float NearOcclusionStrength;
float SEGISoftSunlight;
float FarOcclusionStrength;
float FarthestOcclusionStrength;
//int SEGISphericalSkylight;
float4 SEGISunlightVector;
half4 GISunColor;
int HalfResolution;
//TEXTURE3D(SEGIVolumeLevel0);
//TEXTURE3D(SEGIVolumeLevel1);
//TEXTURE3D(SEGIVolumeLevel2);
//TEXTURE3D(SEGIVolumeLevel3);
//TEXTURE3D(SEGIVolumeLevel4);
//TEXTURE3D(SEGIVolumeLevel5);
//TEXTURE3D(SEGIVolumeLevel6);
//TEXTURE3D(SEGIVolumeLevel7);


float4 ConeTrace(float3 voxelOrigin, float3 kernel, float3 worldNormal, float2 uv, float noise, int steps, float width, float lengthMult, float skyMult,
int enableDithering, int SEGISphericalSkylight, UnityTexture3D SEGIVolumeLevel0, UnityTexture3D SEGIVolumeLevel1,
UnityTexture3D SEGIVolumeLevel2, UnityTexture3D SEGIVolumeLevel3, UnityTexture3D SEGIVolumeLevel4,
UnityTexture3D SEGIVolumeLevel5, UnitySamplerState _sampler)
{
	float skyVisibility = 1.0;

	float3 gi = float3(0, 0, 0);

	int numSteps = (int)(steps * lerp(SEGIVoxelScaleFactor, 1.0, 0.5));

	float3 adjustedKernel = normalize(kernel.xyz + worldNormal.xyz * 0.00 * width);

	for (int i = 0; i < numSteps; i++)
	{
		float fi = ((float)i) / numSteps;
		fi = lerp(fi, 1.0, 0.01);

		float coneDistance = (exp2(fi * 4.0) - 0.9) / 8.0;

		coneDistance -= 0.00;

		float coneSize = fi * width * lerp(SEGIVoxelScaleFactor, 1.0, 0.5);

		float3 voxelCheckCoord = voxelOrigin.xyz + adjustedKernel.xyz * (coneDistance * 0.12 * TraceLength * lengthMult + 0.001);

		float4 _sample = float4(0.0, 0.0, 0.0, 0.0);
		int mipLevel = floor(coneSize);
		if (mipLevel == 0)
            _sample = SAMPLE_TEXTURE3D(SEGIVolumeLevel0, _sampler.samplerstate, float4(voxelCheckCoord.xyz, coneSize));
		else if (mipLevel == 1)
            _sample = SAMPLE_TEXTURE3D(SEGIVolumeLevel1, _sampler.samplerstate, float4(voxelCheckCoord.xyz, coneSize));
		else if (mipLevel == 2)
            _sample = SAMPLE_TEXTURE3D(SEGIVolumeLevel2, _sampler.samplerstate, float4(voxelCheckCoord.xyz, coneSize));
		else if (mipLevel == 3)
            _sample = SAMPLE_TEXTURE3D(SEGIVolumeLevel3, _sampler.samplerstate, float4(voxelCheckCoord.xyz, coneSize));
		else if (mipLevel == 4)
            _sample = SAMPLE_TEXTURE3D(SEGIVolumeLevel4, _sampler.samplerstate, float4(voxelCheckCoord.xyz, coneSize));
		else if (mipLevel == 5)
            _sample = SAMPLE_TEXTURE3D(SEGIVolumeLevel5, _sampler.samplerstate, float4(voxelCheckCoord.xyz, coneSize));
        else
        {
            _sample = float4(1, 1, 1, 0);  // _sample = float4(1, 1, 1, 0); //normalnya yg ini
        }


		float occlusion = skyVisibility * skyVisibility;

		float falloffFix = pow(fi, 1.0) * 4.0 + NearLightGain;

		_sample.a *= lerp(saturate(coneSize / 1.0), 1.0, NearOcclusionStrength);
		gi.rgb += _sample.rgb * (coneSize * 1.0 + 1.0) * occlusion * falloffFix;
		if (enableDithering > 0)
            gi.rgb *= noise;
		
		skyVisibility *= pow(saturate(1.0 - (_sample.a) * (coneSize * 0.2 * FarOcclusionStrength + 1.0 + coneSize * coneSize * 0.05 * FarthestOcclusionStrength) * OcclusionStrength), lerp(0.014, 1.5 * OcclusionPower, min(1.0, coneSize / 5.0)));

    }

	float NdotL = pow(saturate(dot(worldNormal, kernel) * 1.0 - 0.0), 0.5);

	gi *= NdotL;
	skyVisibility *= NdotL;
	if (StochasticSampling > 0)
	{
		skyVisibility *= lerp(saturate(dot(kernel, float3(0.0, 1.0, 0.0)) * 10.0 + 0.0), 1.0, SEGISphericalSkylight);
	}
	else
	{
		skyVisibility *= lerp(saturate(dot(kernel, float3(0.0, 1.0, 0.0)) * 10.0 + 0.0), 1.0, SEGISphericalSkylight);
	}

	float3 skyColor = float3(0.0, 0.0, 0.0);

	float upGradient = saturate(dot(kernel, float3(0.0, 1.0, 0.0)));
	float sunGradient = saturate(dot(kernel, -SEGISunlightVector.xyz));
	skyColor += lerp(SEGISkyColor.rgb * 1.0, SEGISkyColor.rgb * 0.5, pow(upGradient, (0.5).xxx));
	skyColor += GISunColor.rgb * pow(sunGradient, (4.0).xxx) * SEGISoftSunlight;

	gi.rgb *= GIGain * 0.25;

	gi += skyColor * skyVisibility * skyMult;

	return float4(gi.rgb * 0.8, 0.0f);
}

void Pass4_float(float4 voxelSpacePosition, float3 voxelOrigin, float4 viewSpacePosition, float2 coord, float3 worldNormal, float blueNoise, float2 dither, 
int enableDithering, int SEGISphericalSkylight, UnityTexture3D SEGIVolumeLevel0, UnityTexture3D SEGIVolumeLevel1,
UnityTexture3D SEGIVolumeLevel2, UnityTexture3D SEGIVolumeLevel3, UnityTexture3D SEGIVolumeLevel4,
UnityTexture3D SEGIVolumeLevel5, UnitySamplerState _sampler, out float3 Out)
{
	
	float3 gi = float3(0.0, 0.0, 0.0);
	float4 traceResult = float4(0, 0, 0, 0);
	const float phi = 1.618033988;
	const float gAngle = phi * PI * 1.0;
	
	if (enableDithering)
        StochasticSampling = 1;
	else
        StochasticSampling = 0;
	
	//Trace GI cones
	int numSamples = TraceDirections;
	for (int i = 0; i < numSamples; i++)
	{
		float fi = (float)i + blueNoise * StochasticSampling;
		float fiN = fi / numSamples;
		float longitude = gAngle * fi;
		float latitude = asin(fiN * 2.0 - 1.0);

		float3 kernel;
		kernel.x = cos(latitude) * cos(longitude);
		kernel.z = cos(latitude) * sin(longitude);
		kernel.y = sin(latitude);

		kernel = normalize(kernel + worldNormal.xyz * 1.0);

        traceResult += ConeTrace(voxelOrigin.xyz, kernel.xyz, worldNormal.xyz, coord, dither.y, TraceSteps, ConeSize, 1.0, 1.0,
		enableDithering, SEGISphericalSkylight, SEGIVolumeLevel0, SEGIVolumeLevel1, SEGIVolumeLevel2, SEGIVolumeLevel3, SEGIVolumeLevel4, SEGIVolumeLevel5, _sampler);
    }
	traceResult /= numSamples;
	gi = traceResult.rgb * 20.0;


	float fadeout = saturate((distance(voxelSpacePosition.xyz, float3(0.5, 0.5, 0.5)) - 0.5f) * 5.0);

    float3 fakeGI = saturate(dot(worldNormal, float3(0, 1, 0)) * 0.5 + 0.5) * SEGISkyColor.rgb * 5.0;

	gi.rgb = lerp(gi.rgb, fakeGI, fadeout);

	gi *= 0.75 + (float)HalfResolution * 0.25;

	Out = gi.rgb;
}
