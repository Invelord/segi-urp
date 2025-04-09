
using UnityEngine;
using System.Collections;
using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal.Internal;

public class SEGI_URP : MonoBehaviour
{

    public int sundepthRendererIndex = 1;
    public int voxelizationRendererIndex = 2;
    public int voxeltracingRendererIndex = 3;

    [Serializable]
	public enum VoxelResolution
	{
        low = 64,
        medium = 128,
		high = 256
	}

	public LayerMask giCullingMask = 2147483647;
	public float shadowSpaceSize = 25.0f;
	public Light sun;

	public Color skyColor;

	public float voxelSpaceSize = 55.0f;

	[Range(0, 2)]
	public int innerOcclusionLayers = 2;

	public VoxelResolution voxelResolution = VoxelResolution.high;

	public bool infiniteBounces = false;
	public Transform followTransform;
	[Range(1, 128)]
	public int cones = 24;
	[Range(1, 32)]
	public int coneTraceSteps = 8;
	[Range(0.1f, 2.0f)]
	public float coneLength = 0.287f;
	[Range(0.5f, 6.0f)]
	public float coneWidth = 3.67f;
	[Range(0.0f, 4.0f)]
	public float occlusionStrength = 0.67f;
	[Range(0.0f, 4.0f)]
	public float nearOcclusionStrength = 0.0f;
	[Range(0.001f, 4.0f)]
	public float occlusionPower = 4.0f;
	[Range(0.0f, 4.0f)]
	public float coneTraceBias = 1.0f;
	[Range(0.0f, 4.0f)]
	public float nearLightGain = 0.0f;
	[Range(0.0f, 4.0f)]
	public float giGain = 1.0f;
	[Range(0.0f, 4.0f)]
	public float secondaryBounceGain = 3.0f;
	[Range(0.0f, 16.0f)]
	public float softSunlight = 0.0f;

	[Range(0.0f, 8.0f)]
	public float skyIntensity = 1.0f;

	public bool doReflections = true;
	public int reflectionSteps = 16;
	public bool enableDithering = false;
	public bool reduceShinyArtifact_Reflections = false;

    public float reflectionConeTraceBias = 1.0f;
    public float reflectionOcclusionPower = 10.0f;
	public float reflectionOcclusionStrength = 30.0f;
	public float reflectionFarOcclusionStrength = 100.0f;
	public float reflectionFarthestOcclusionStrength = 100.0f;
	public float reflectionDitherOcclusionPower = 100.0f;
    [Range(0.0f, 1.0f)]
	public float skyReflectionIntensity = 1.0f;

    public float reflectionsNearOcclusionStrength = 0.0f;
    [Range(0.1f, 10.0f)]
    public float reflectionConeLength = 1.55f;
    public float reflectionConeWidth = 6.0f;

    public bool voxelAA = true;

	public bool gaussianMipFilter = true;


	[Range(0.1f, 4.0f)]
	public float farOcclusionStrength = 4.0f;
	[Range(0.1f, 4.0f)]
	public float farthestOcclusionStrength = 4.0f;

	[Range(3, 16)]
	public int secondaryCones = 16;
    [Range(0, 32)]
    public int secondaryTraces = 32;
	[Range(0.0f,3.0f)]
	public float secondaryLength = 1.0f;
    [Range(0.0f, 6.0f)]
    public float secondaryWidth = 0.86f;

    [Range(0.1f, 4.0f)]
	public float secondaryOcclusionStrength = 1.12f;

	public bool sphericalSkylight = false;

	Camera attachedCamera;
	Transform shadowCamTransform;
	Camera shadowCam;
    UniversalAdditionalCameraData shadowCameraAdditionalData;
    GameObject shadowCamGameObject;
    Texture2D[] blueNoise;
	
	int sunShadowResolution = 256;
	int prevSunShadowResolution;

	Shader sunDepthShader;

	float shadowSpaceDepthRatio = 10.0f;

	int frameCounter = 0;


	[SerializeField] RenderTexture sunDepthTexture;

	///<summary>This is a volume texture that is immediately written to in the voxelization shader. The RInt format enables atomic writes to avoid issues where multiple fragments are trying to write to the same voxel in the volume.</summary>
	RenderTexture integerVolume;

    ///<summary>An array of volume textures where each element is a mip/LOD level. Each volume is half the resolution of the previous volume. Separate textures for each mip level are required for manual mip-mapping of the main GI volume texture.</summary>
    [SerializeField] RenderTexture[] volumeTextures;

	///<summary>The secondary volume texture that holds irradiance calculated during the in-volume GI tracing that occurs when Infinite Bounces is enabled. </summary>
	RenderTexture secondaryIrradianceVolume;

	///<summary>The alternate mip level 0 main volume texture needed to avoid simultaneous read/write errors while performing temporal stabilization on the main voxel volume.</summary>
	RenderTexture volumeTextureB;

	///<summary>The current active volume texture that holds GI information to be read during GI tracing.</summary>
	RenderTexture activeVolume;

	///<summary>The volume texture that holds GI information to be read during GI tracing that was used in the previous frame.</summary>
	RenderTexture previousActiveVolume;

	///<summary>A 2D texture with the size of [voxel resolution, voxel resolution] that must be used as the active render texture when rendering the scene for voxelization. This texture scales depending on whether Voxel AA is enabled to ensure correct voxelization.</summary>
	RenderTexture dummyVoxelTextureAAScaled;

	///<summary>A 2D texture with the size of [voxel resolution, voxel resolution] that must be used as the active render texture when rendering the scene for voxelization. This texture is always the same size whether Voxel AA is enabled or not.</summary>
	RenderTexture dummyVoxelTextureFixed;

	Shader voxelizationShader;
	Shader voxelTracingShader;

	ComputeShader clearCompute;
	ComputeShader transferIntsCompute;
	ComputeShader mipFilterCompute;

	const int numMipLevels = 6;

	Camera voxelCamera;
    UniversalAdditionalCameraData voxelCameraAdditionalData;
	GameObject voxelCameraGO;
	GameObject leftViewPoint;
	GameObject topViewPoint;

	float voxelScaleFactor
	{
		get
		{
			return (float)voxelResolution / 256.0f;
		}
	}

	Vector3 voxelSpaceOrigin;
	Vector3 previousVoxelSpaceOrigin;
	Vector3 voxelSpaceOriginDelta;


	Quaternion rotationFront = new Quaternion(0.0f, 0.0f, 0.0f, 1.0f);
	Quaternion rotationLeft = new Quaternion(0.0f, 0.7f, 0.0f, 0.7f);
	Quaternion rotationTop = new Quaternion(0.7f, 0.0f, 0.0f, 0.7f);

	int voxelFlipFlop = 0;

	enum RenderState
	{
		Voxelize,
		Bounce
	}

	RenderState renderState = RenderState.Voxelize;

	struct Pass
	{
		public static int DiffuseTrace = 0;
		public static int BilateralBlur = 1;
		public static int BlendWithScene = 2;
		public static int TemporalBlend = 3;
		public static int SpecularTrace = 4;
		public static int GetCameraDepthTexture = 5;
		public static int GetWorldNormals = 6;
		public static int VisualizeGI = 7;
		public static int WriteBlack = 8;
		public static int VisualizeVoxels = 10;
		public static int BilateralUpsample = 11;
	}


	int mipFilterKernel
	{
		get
		{
			return gaussianMipFilter ? 1 : 0;
		}
	}

	int dummyVoxelResolution
	{
		get
		{
			return (int)voxelResolution * (voxelAA ? 2 : 1);
		}
	}

    void Start()
	{
        Init();
    }

    void CreateVolumeTextures()
	{
		volumeTextures = new RenderTexture[numMipLevels];

		for (int i = 0; i < numMipLevels; i++)
		{
			if (volumeTextures[i])
			{
				volumeTextures[i].DiscardContents();
				volumeTextures[i].Release();
				Destroy(volumeTextures[i]);
			}
			int resolution = (int)voxelResolution / Mathf.RoundToInt(Mathf.Pow((float)2, (float)i));
			volumeTextures[i] = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
			volumeTextures[i].dimension = TextureDimension.Tex3D;
			volumeTextures[i].volumeDepth = resolution;
			volumeTextures[i].enableRandomWrite = true;
			volumeTextures[i].filterMode = FilterMode.Bilinear;
			volumeTextures[i].autoGenerateMips = false;
			volumeTextures[i].useMipMap = false;
			volumeTextures[i].Create();
			volumeTextures[i].hideFlags = HideFlags.HideAndDontSave;
		}

		if (volumeTextureB)
		{
			volumeTextureB.DiscardContents();
			volumeTextureB.Release();
			Destroy(volumeTextureB);
		}
		volumeTextureB = new RenderTexture((int)voxelResolution, (int)voxelResolution, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
		volumeTextureB.dimension = TextureDimension.Tex3D;
		volumeTextureB.volumeDepth = (int)voxelResolution;
		volumeTextureB.enableRandomWrite = true;
		volumeTextureB.filterMode = FilterMode.Bilinear;
		volumeTextureB.autoGenerateMips = false;
		volumeTextureB.useMipMap = false;
		volumeTextureB.Create();
		volumeTextureB.hideFlags = HideFlags.HideAndDontSave;

		if (secondaryIrradianceVolume)
		{
			secondaryIrradianceVolume.DiscardContents();
			secondaryIrradianceVolume.Release();
			Destroy(secondaryIrradianceVolume);
		}
		secondaryIrradianceVolume = new RenderTexture((int)voxelResolution, (int)voxelResolution, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
		secondaryIrradianceVolume.dimension = TextureDimension.Tex3D;
		secondaryIrradianceVolume.volumeDepth = (int)voxelResolution;
		secondaryIrradianceVolume.enableRandomWrite = true;
		secondaryIrradianceVolume.filterMode = FilterMode.Point;
		secondaryIrradianceVolume.autoGenerateMips = false;
		secondaryIrradianceVolume.useMipMap = false;
		secondaryIrradianceVolume.antiAliasing = 1;
		secondaryIrradianceVolume.Create();
		secondaryIrradianceVolume.hideFlags = HideFlags.HideAndDontSave;



		if (integerVolume)
		{
			integerVolume.DiscardContents();
			integerVolume.Release();
			Destroy(integerVolume);
		}
		integerVolume = new RenderTexture((int)voxelResolution, (int)voxelResolution, 0, RenderTextureFormat.RInt, RenderTextureReadWrite.Linear);
		integerVolume.dimension = TextureDimension.Tex3D;
		integerVolume.volumeDepth = (int)voxelResolution;
		integerVolume.enableRandomWrite = true;
		integerVolume.filterMode = FilterMode.Point;
		integerVolume.Create();
		integerVolume.hideFlags = HideFlags.HideAndDontSave;

		ResizeDummyTexture();

	}
	void ResizeDummyTexture()
	{
		if (dummyVoxelTextureAAScaled)
		{
			dummyVoxelTextureAAScaled.DiscardContents();
			dummyVoxelTextureAAScaled.Release();
			Destroy(dummyVoxelTextureAAScaled);
		}
		dummyVoxelTextureAAScaled = new RenderTexture(dummyVoxelResolution, dummyVoxelResolution, 16, RenderTextureFormat.R16);
		
		dummyVoxelTextureAAScaled.Create();
		dummyVoxelTextureAAScaled.hideFlags = HideFlags.HideAndDontSave;

		if (dummyVoxelTextureFixed)
		{
			dummyVoxelTextureFixed.DiscardContents();
			dummyVoxelTextureFixed.Release();
			Destroy(dummyVoxelTextureFixed);
		}
		dummyVoxelTextureFixed = new RenderTexture((int)voxelResolution, (int)voxelResolution, 16, RenderTextureFormat.R16);
		dummyVoxelTextureFixed.Create();
		dummyVoxelTextureFixed.hideFlags = HideFlags.HideAndDontSave;
	}
	void Init()
	{

		//Setup shaders and materials
		sunDepthShader = Shader.Find("Hidden/SEGIRenderSunDepth");

		clearCompute = Resources.Load("SEGIClear") as ComputeShader;
		transferIntsCompute = Resources.Load("SEGITransferInts") as ComputeShader;
		mipFilterCompute = Resources.Load("SEGIMipFilter") as ComputeShader;

		voxelizationShader = Shader.Find("Hidden/SEGIVoxelizeScene");
		voxelTracingShader = Shader.Find("Hidden/WorldTrace");

        //Get the camera attached to this game object
        //Apply depth render flags
        attachedCamera = GetComponent<Camera>();

		//Find the proxy shadow rendering camera if it exists
		GameObject scgo = GameObject.Find("SEGI_SHADOWCAM");

		//If not, create it
		if (!scgo)
		{
			shadowCamGameObject = new GameObject("SEGI_SHADOWCAM");
			shadowCam = shadowCamGameObject.AddComponent<Camera>();
            shadowCameraAdditionalData = shadowCamGameObject.AddComponent<UniversalAdditionalCameraData>();
            shadowCamGameObject.hideFlags = HideFlags.HideAndDontSave;


			shadowCam.enabled = false;
			shadowCam.depth = attachedCamera.depth - 1;
			shadowCam.orthographic = true;
			shadowCam.orthographicSize = shadowSpaceSize;
			shadowCam.clearFlags = CameraClearFlags.SolidColor;
			shadowCam.backgroundColor = new Color(0.0f, 0.0f, 0.0f, 1.0f);
			shadowCam.farClipPlane = shadowSpaceSize * 2.0f * shadowSpaceDepthRatio;
			shadowCam.cullingMask = giCullingMask;
			shadowCam.useOcclusionCulling = false;

			shadowCamTransform = shadowCamGameObject.transform;
		}
		else	//Otherwise, it already exists, just get it
		{
			shadowCamGameObject = scgo;
			shadowCam = scgo.GetComponent<Camera>();
			shadowCamTransform = shadowCamGameObject.transform;
		}


		
		//Create the proxy camera objects responsible for rendering the scene to voxelize the scene. If they already exist, destroy them
		GameObject vcgo = GameObject.Find("SEGI_VOXEL_CAMERA");
		if (vcgo)
			Destroy(vcgo);

		voxelCameraGO = new GameObject("SEGI_VOXEL_CAMERA");
		voxelCameraGO.hideFlags = HideFlags.HideAndDontSave;

		voxelCamera = voxelCameraGO.AddComponent<Camera>();
        voxelCameraAdditionalData = voxelCameraGO.AddComponent<UniversalAdditionalCameraData>();
        voxelCamera.enabled = false;
		voxelCamera.orthographic = true;
		voxelCamera.orthographicSize = voxelSpaceSize * 0.5f;
		voxelCamera.nearClipPlane = 0.0f;
		voxelCamera.farClipPlane = voxelSpaceSize;
		voxelCamera.depth = -2;
		voxelCamera.renderingPath = RenderingPath.Forward;
		voxelCamera.clearFlags = CameraClearFlags.Color;
		voxelCamera.backgroundColor = Color.black;
		voxelCamera.useOcclusionCulling = false;

		GameObject lvp = GameObject.Find("SEGI_LEFT_VOXEL_VIEW");
		if (lvp)
			Destroy(lvp);

		leftViewPoint = new GameObject("SEGI_LEFT_VOXEL_VIEW");
		leftViewPoint.hideFlags = HideFlags.HideAndDontSave;

		GameObject tvp = GameObject.Find("SEGI_TOP_VOXEL_VIEW");
		if (tvp)
			Destroy(tvp);

		topViewPoint = new GameObject("SEGI_TOP_VOXEL_VIEW");
		topViewPoint.hideFlags = HideFlags.HideAndDontSave;


        //Get blue noise textures
        blueNoise = null;
        blueNoise = new Texture2D[64];
        for (int i = 0; i < 64; i++)
        {
            string fileName = "LDR_RGBA_" + i.ToString();
            Texture2D blueNoiseTexture = Resources.Load("Noise Textures/" + fileName) as Texture2D;

            if (blueNoiseTexture == null)
            {
                Debug.LogWarning("Unable to find noise texture \"Assets/SEGI/Resources/Noise Textures/" + fileName + "\" for SEGI!");
            } 

            blueNoise[i] = blueNoiseTexture;

        }


		//Setup sun depth texture
		if (sunDepthTexture)
		{
			sunDepthTexture.DiscardContents();
			sunDepthTexture.Release();
			Destroy(sunDepthTexture);
		}
		sunDepthTexture = new RenderTexture(sunShadowResolution, sunShadowResolution, 16, RenderTextureFormat.RHalf, RenderTextureReadWrite.Linear);
		sunDepthTexture.wrapMode = TextureWrapMode.Clamp;
		sunDepthTexture.filterMode = FilterMode.Point;
		sunDepthTexture.Create();
		sunDepthTexture.hideFlags = HideFlags.HideAndDontSave;


		//Create the volume textures
		CreateVolumeTextures();
    }

	void OnDrawGizmosSelected()
	{
		Color prevColor = Gizmos.color;
		Gizmos.color = new Color(1.0f, 0.25f, 0.0f, 0.5f);

		Gizmos.DrawCube(voxelSpaceOrigin, new Vector3(voxelSpaceSize, voxelSpaceSize, voxelSpaceSize));

		Gizmos.color = new Color(1.0f, 0.0f, 0.0f, 0.1f);

		Gizmos.color = prevColor;
	}

	void CleanupTexture(ref RenderTexture texture)
	{
		texture.DiscardContents();
		Destroy(texture);
	}
	void CleanupTextures()
	{
		CleanupTexture(ref sunDepthTexture);
		CleanupTexture(ref integerVolume);
		for (int i = 0; i < volumeTextures.Length; i++)
		{
			CleanupTexture(ref volumeTextures[i]);
		}
		CleanupTexture(ref secondaryIrradianceVolume);
		CleanupTexture(ref volumeTextureB);
		CleanupTexture(ref dummyVoxelTextureAAScaled);
		CleanupTexture(ref dummyVoxelTextureFixed);
	}
	void Cleanup()
	{
		Destroy(voxelCameraGO);
		Destroy(leftViewPoint);
		Destroy(topViewPoint);
		Destroy(shadowCamGameObject);

		CleanupTextures();
	}

	void OnDestroy()
	{
        Cleanup();
	}

	void ResizeSunShadowBuffer()
	{

		if (sunDepthTexture)
		{
			sunDepthTexture.DiscardContents();
			sunDepthTexture.Release();
			Destroy(sunDepthTexture);
		}
		sunDepthTexture = new RenderTexture(sunShadowResolution, sunShadowResolution, 16, RenderTextureFormat.RHalf, RenderTextureReadWrite.Linear);
		sunDepthTexture.wrapMode = TextureWrapMode.Clamp;
		sunDepthTexture.filterMode = FilterMode.Point;
		sunDepthTexture.Create();
		sunDepthTexture.hideFlags = HideFlags.HideAndDontSave;
	}

	void Update()
	{

		if ((int)sunShadowResolution != prevSunShadowResolution)
		{
			ResizeSunShadowBuffer();
		}

		prevSunShadowResolution = (int)sunShadowResolution;

		if (volumeTextures[0].width != (int)voxelResolution)
		{
			CreateVolumeTextures();
		}

		if (dummyVoxelTextureAAScaled.width != dummyVoxelResolution)
		{
			ResizeDummyTexture();
		}
        RenderExternalCamera();
        RenderMainCamera();
    }

    Matrix4x4 TransformViewMatrix(Matrix4x4 mat)
    {
		//Since the third column of the view matrix needs to be reversed if using reversed z-buffer, do so here
        if (SystemInfo.usesReversedZBuffer)
        {
            mat[2, 0] = -mat[2, 0];
            mat[2, 1] = -mat[2, 1];
            mat[2, 2] = -mat[2, 2];
            mat[2, 3] = -mat[2, 3];
        }
        return mat;
    }

	void RenderExternalCamera()
	{
		//Cache the previous active render texture to avoid issues with other Unity rendering going on
		RenderTexture previousActive = RenderTexture.active;

		Shader.SetGlobalInt("SEGIVoxelAA", voxelAA ? 1 : 0);

		//Main voxelization work
		if (renderState == RenderState.Voxelize)
		{
			activeVolume = voxelFlipFlop == 0 ? volumeTextures[0] : volumeTextureB;				//Flip-flopping volume textures to avoid simultaneous read and write errors in shaders
			previousActiveVolume = voxelFlipFlop == 0 ? volumeTextureB : volumeTextures[0];

			float voxelTexel = (1.0f * voxelSpaceSize) / (int)voxelResolution * 0.5f;			//Calculate the size of a voxel texel in world-space units



			//Setup the voxel volume origin position
			float interval = voxelSpaceSize / 8.0f;												//The interval at which the voxel volume will be "locked" in world-space
			Vector3 origin;
			if (followTransform)
			{
				origin = followTransform.position;
			}
			else
			{
				origin = transform.position + transform.forward * voxelSpaceSize / 4.0f;
			}
			//Lock the voxel volume origin based on the interval
			voxelSpaceOrigin = new Vector3(Mathf.Round(origin.x / interval) * interval, Mathf.Round(origin.y / interval) * interval, Mathf.Round(origin.z / interval) * interval);

			//Calculate how much the voxel origin has moved since last voxelization pass. Used for scrolling voxel data in shaders to avoid ghosting when the voxel volume moves in the world
			voxelSpaceOriginDelta = voxelSpaceOrigin - previousVoxelSpaceOrigin;
			Shader.SetGlobalVector("SEGIVoxelSpaceOriginDelta", voxelSpaceOriginDelta / voxelSpaceSize);

			previousVoxelSpaceOrigin = voxelSpaceOrigin;


			//Set the voxel camera (proxy camera used to render the scene for voxelization) parameters
			voxelCamera.enabled = false;
			voxelCamera.orthographic = true;
			voxelCamera.orthographicSize = voxelSpaceSize * 0.5f;
			voxelCamera.nearClipPlane = 0.0f;
			voxelCamera.farClipPlane = voxelSpaceSize;
			voxelCamera.depth = -2;
			voxelCamera.renderingPath = RenderingPath.Forward;
			voxelCamera.clearFlags = CameraClearFlags.Color;
			voxelCamera.backgroundColor = Color.black;
			voxelCamera.cullingMask = giCullingMask;


			//Move the voxel camera game object and other related objects to the above calculated voxel space origin
			voxelCameraGO.transform.position = voxelSpaceOrigin - Vector3.forward * voxelSpaceSize * 0.5f;
			voxelCameraGO.transform.rotation = rotationFront;

			leftViewPoint.transform.position = voxelSpaceOrigin + Vector3.left * voxelSpaceSize * 0.5f;
			leftViewPoint.transform.rotation = rotationLeft;
			topViewPoint.transform.position = voxelSpaceOrigin + Vector3.up * voxelSpaceSize * 0.5f;
			topViewPoint.transform.rotation = rotationTop;



			//Set matrices needed for voxelization
			Shader.SetGlobalMatrix("WorldToCamera", attachedCamera.worldToCameraMatrix);
			Shader.SetGlobalMatrix("SEGIVoxelViewFront", TransformViewMatrix(voxelCamera.transform.worldToLocalMatrix));
			Shader.SetGlobalMatrix("SEGIVoxelViewLeft", TransformViewMatrix(leftViewPoint.transform.worldToLocalMatrix));
			Shader.SetGlobalMatrix("SEGIVoxelViewTop", TransformViewMatrix(topViewPoint.transform.worldToLocalMatrix));
			Shader.SetGlobalMatrix("SEGIWorldToVoxel", voxelCamera.worldToCameraMatrix);
			Shader.SetGlobalMatrix("SEGIVoxelProjection", voxelCamera.projectionMatrix);
			Shader.SetGlobalMatrix("SEGIVoxelProjectionInverse", voxelCamera.projectionMatrix.inverse);

			Shader.SetGlobalInt("SEGIVoxelResolution", (int)voxelResolution);

			Matrix4x4 voxelToGIProjection = (shadowCam.projectionMatrix) * (shadowCam.worldToCameraMatrix) * (voxelCamera.cameraToWorldMatrix);
			Shader.SetGlobalMatrix("SEGIVoxelToGIProjection", voxelToGIProjection);
			Shader.SetGlobalVector("SEGISunlightVector", sun ? Vector3.Normalize(sun.transform.forward) : Vector3.up);

			//Set paramteters
			Shader.SetGlobalColor("GISunColor", sun == null ? Color.black : new Color(Mathf.Pow(sun.color.r, 2.2f), Mathf.Pow(sun.color.g, 2.2f), Mathf.Pow(sun.color.b, 2.2f), Mathf.Pow(sun.intensity, 2.2f)));
			Shader.SetGlobalColor("SEGISkyColor", new Color(Mathf.Pow(skyColor.r * skyIntensity * 0.5f, 2.2f), Mathf.Pow(skyColor.g * skyIntensity * 0.5f, 2.2f), Mathf.Pow(skyColor.b * skyIntensity * 0.5f, 2.2f), Mathf.Pow(skyColor.a, 2.2f)));
			Shader.SetGlobalFloat("GIGain", giGain);
			Shader.SetGlobalFloat("SEGISecondaryBounceGain", infiniteBounces ? secondaryBounceGain : 0.0f);
			Shader.SetGlobalFloat("SEGISoftSunlight", softSunlight);
			Shader.SetGlobalInt("SEGISphericalSkylight", sphericalSkylight ? 1 : 0);
			Shader.SetGlobalInt("SEGIInnerOcclusionLayers", innerOcclusionLayers);


			//Render the depth texture from the sun's perspective in order to inject sunlight with shadows during voxelization
			if (sun != null)
			{
				shadowCam.cullingMask = giCullingMask;

				Vector3 shadowCamPosition = voxelSpaceOrigin + Vector3.Normalize(-sun.transform.forward) * shadowSpaceSize * 0.5f * shadowSpaceDepthRatio;

				shadowCamTransform.position = shadowCamPosition;
				shadowCamTransform.LookAt(voxelSpaceOrigin, Vector3.up);

				shadowCam.renderingPath = RenderingPath.Forward;
				shadowCam.depthTextureMode |= DepthTextureMode.None;

				shadowCam.orthographicSize = shadowSpaceSize;
				shadowCam.farClipPlane = shadowSpaceSize * 2.0f * shadowSpaceDepthRatio;
                shadowCam.targetTexture = sunDepthTexture;
                shadowCameraAdditionalData.SetRenderer(sundepthRendererIndex);
				
                shadowCam.Render();

				Shader.SetGlobalTexture("SEGISunDepth", sunDepthTexture);
			}

			//Clear the volume texture that is immediately written to in the voxelization scene shader
			clearCompute.SetTexture(0, "RG0", integerVolume);
			clearCompute.SetInt("Res", (int)voxelResolution);
			clearCompute.Dispatch(0, (int)voxelResolution / 16, (int)voxelResolution / 16, 1);


			//Render the scene with the voxel proxy camera object with the voxelization shader to voxelize the scene to the volume integer texture
			Graphics.SetRandomWriteTarget(1, integerVolume);
            voxelCamera.targetTexture = dummyVoxelTextureAAScaled;
            voxelCameraAdditionalData.SetRenderer(voxelizationRendererIndex);
            voxelCamera.Render();
            Graphics.ClearRandomWriteTargets();


			//Transfer the data from the volume integer texture to the main volume texture used for GI tracing. 
			transferIntsCompute.SetTexture(0, "Result", activeVolume);
			transferIntsCompute.SetTexture(0, "PrevResult", previousActiveVolume);
			transferIntsCompute.SetTexture(0, "RG0", integerVolume);
			transferIntsCompute.SetInt("VoxelAA", voxelAA ? 1 : 0);
			transferIntsCompute.SetInt("Resolution", (int)voxelResolution);
			transferIntsCompute.SetVector("VoxelOriginDelta", (voxelSpaceOriginDelta / voxelSpaceSize) * (int)voxelResolution);
			transferIntsCompute.Dispatch(0, Mathf.Max((int)voxelResolution / 16,1), Mathf.Max((int)voxelResolution / 16, 1), 1);

			Shader.SetGlobalTexture("SEGIVolumeLevel0", activeVolume);

			//Manually filter/render mip maps
			for (int i = 0; i < numMipLevels - 1; i++)
			{
				RenderTexture source = volumeTextures[i];

				if (i == 0)
				{
					source = activeVolume;
				}

				int destinationRes = Mathf.Max((int)voxelResolution / Mathf.RoundToInt(Mathf.Pow((float)2, (float)i + 1.0f)),4);
				mipFilterCompute.SetInt("destinationRes", destinationRes);
				mipFilterCompute.SetTexture(mipFilterKernel, "Source", source);
				mipFilterCompute.SetTexture(mipFilterKernel, "Destination", volumeTextures[i + 1]);
				mipFilterCompute.Dispatch(mipFilterKernel, destinationRes / 4, destinationRes / 4, 1);
				Shader.SetGlobalTexture("SEGIVolumeLevel" + (i + 1).ToString(), volumeTextures[i + 1]);
			}

			//Advance the voxel flip flop counter
			voxelFlipFlop += 1;
			voxelFlipFlop = voxelFlipFlop % 2;

			if (infiniteBounces)
			{
				renderState = RenderState.Bounce;
			}
		}
		else if (renderState == RenderState.Bounce)
		{

			//Clear the volume texture that is immediately written to in the voxelization scene shader
			clearCompute.SetTexture(0, "RG0", integerVolume);
			clearCompute.Dispatch(0, (int)voxelResolution / 16, (int)voxelResolution / 16, 1);

			//Set secondary tracing parameters
			Shader.SetGlobalInt("SEGISecondaryCones", secondaryCones);
            Shader.SetGlobalInt("SecondaryTraceSteps", secondaryTraces);
            Shader.SetGlobalFloat("SecondaryLength", secondaryLength);
            Shader.SetGlobalFloat("SecondaryWidth", secondaryWidth);
            Shader.SetGlobalFloat("SEGISecondaryOcclusionStrength", secondaryOcclusionStrength);

			//Render the scene from the voxel camera object with the voxel tracing shader to render a bounce of GI into the irradiance volume
			Graphics.SetRandomWriteTarget(1, integerVolume);
            voxelCamera.targetTexture = dummyVoxelTextureFixed;
            voxelCameraAdditionalData.SetRenderer(voxeltracingRendererIndex);
            voxelCamera.Render();
            Graphics.ClearRandomWriteTargets();


			//Transfer the data from the volume integer texture to the irradiance volume texture. This result is added to the next main voxelization pass to create a feedback loop for infinite bounces
			transferIntsCompute.SetTexture(1, "Result", secondaryIrradianceVolume);
			transferIntsCompute.SetTexture(1, "RG0", integerVolume);
			transferIntsCompute.SetInt("Resolution", (int)voxelResolution);
			transferIntsCompute.Dispatch(1, (int)voxelResolution / 16, (int)voxelResolution / 16, 1);

			Shader.SetGlobalTexture("SEGIVolumeTexture1", secondaryIrradianceVolume);

			renderState = RenderState.Voxelize;
		}

		RenderTexture.active = previousActive;
	}

	void RenderMainCamera()
	{
		Shader.SetGlobalFloat("SEGIVoxelScaleFactor", voxelScaleFactor);

		Shader.SetGlobalMatrix("CameraToWorld", attachedCamera.cameraToWorldMatrix);
		Shader.SetGlobalMatrix("WorldToCamera", attachedCamera.worldToCameraMatrix);
		Shader.SetGlobalMatrix("ProjectionMatrixInverse", attachedCamera.projectionMatrix.inverse);
		Shader.SetGlobalMatrix("ProjectionMatrix", attachedCamera.projectionMatrix);
		Shader.SetGlobalInteger("FrameSwitch", frameCounter);
		Shader.SetGlobalInt("SEGIFrameSwitch", frameCounter);
		Shader.SetGlobalVector("CameraPosition", transform.position);
		Shader.SetGlobalFloat("DeltaTime", Time.deltaTime);

		//Shader.SetGlobalInteger("StochasticSampling", stochasticSampling ? 1 : 0);
		Shader.SetGlobalInteger("StochasticSampling", 1);
		Shader.SetGlobalInteger("TraceDirections", cones);
		Shader.SetGlobalInteger("TraceSteps", coneTraceSteps);
		Shader.SetGlobalFloat("TraceLength", coneLength);
		Shader.SetGlobalFloat("ConeSize", coneWidth);
		Shader.SetGlobalFloat("OcclusionStrength", occlusionStrength);
		Shader.SetGlobalFloat("OcclusionPower", occlusionPower);
		Shader.SetGlobalFloat("ConeTraceBias", coneTraceBias);
		Shader.SetGlobalFloat("ReflectionConeTraceBias", reflectionConeTraceBias);
		Shader.SetGlobalFloat("GIGain", giGain);
		Shader.SetGlobalFloat("NearLightGain", nearLightGain);
		Shader.SetGlobalFloat("NearOcclusionStrength", nearOcclusionStrength);
		Shader.SetGlobalInteger("DoReflections", doReflections ? 1 : 0);
		Shader.SetGlobalInteger("ReflectionSteps", reflectionSteps);
        Shader.SetGlobalInteger("enableDithering", enableDithering ? 1 : 0);
        Shader.SetGlobalInteger("reduceShinyArtifact_Reflections", reduceShinyArtifact_Reflections ? 1 : 0);
        Shader.SetGlobalFloat("ReflectionConeLength", reflectionConeLength);
        Shader.SetGlobalFloat("ReflectionConeWidth", reflectionConeWidth);
        Shader.SetGlobalFloat("ReflectionNearOcclusionStrength", reflectionsNearOcclusionStrength);
        Shader.SetGlobalFloat("ReflectionOcclusionPower", reflectionOcclusionPower);
        Shader.SetGlobalFloat("ReflectionOcclusionStrength", reflectionOcclusionStrength);
        Shader.SetGlobalFloat("ReflectionFarOcclusionStrength", reflectionFarOcclusionStrength);
        Shader.SetGlobalFloat("ReflectionFarthestOcclusionStrength", reflectionFarthestOcclusionStrength);
        Shader.SetGlobalFloat("ReflectionDitherOcclusionPower", reflectionDitherOcclusionPower);
        Shader.SetGlobalFloat("SkyReflectionIntensity", skyReflectionIntensity);
		Shader.SetGlobalFloat("FarOcclusionStrength", farOcclusionStrength);
		Shader.SetGlobalFloat("FarthestOcclusionStrength", farthestOcclusionStrength);
        Shader.SetGlobalTexture("NoiseTexture", blueNoise[frameCounter % 64]);

		Shader.SetGlobalMatrix("ProjectionPrev", attachedCamera.projectionMatrix);
		Shader.SetGlobalMatrix("ProjectionPrevInverse", attachedCamera.projectionMatrix.inverse);
		Shader.SetGlobalMatrix("WorldToCameraPrev", attachedCamera.worldToCameraMatrix);
		Shader.SetGlobalMatrix("CameraToWorldPrev", attachedCamera.cameraToWorldMatrix);
		Shader.SetGlobalVector("CameraPositionPrev", transform.position);

		//Advance the frame counter
		frameCounter = (frameCounter + 1) % (64);
	}
}

