

void rand_float(float2 coord, out float2 Out)
{
	float noiseX = saturate(frac(sin(dot(coord, float2(12.9898, 78.223))) * 43758.5453));
	float noiseY = saturate(frac(sin(dot(coord, float2(12.9898, 78.223)*2.0)) * 43758.5453));

	Out = float2(noiseX, noiseY);
}