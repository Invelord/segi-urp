using System.Reflection;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RendererUtils;
using UnityEngine.Experimental.Rendering;
using UnityEditor;

#if UNITY_6000_0_OR_NEWER
using UnityEngine.Rendering.RenderGraphModule;
#endif

[DisallowMultipleRendererFeature("SEGI URP Renderer Feature")]
public class SEGI_URP_ForwardBuffers : ScriptableRendererFeature
{
    //private TransparentGBufferPass m_TransparentGBufferPass;
    //private static readonly int _TransparentGBuffer0 = Shader.PropertyToID("_TransparentGBuffer0");
    //private static readonly int _TransparentGBuffer1 = Shader.PropertyToID("_TransparentGBuffer1");
    //private static readonly int _TransparentGBuffer2 = Shader.PropertyToID("_TransparentGBuffer2");
    private readonly string[] m_GBufferPassNames = new string[] { "UniversalGBuffer" };
    private ForwardGBufferPass m_ForwardGBufferPass;


    private static readonly int _GBuffer0 = Shader.PropertyToID("_GBuffer0");
    private static readonly int _GBuffer1 = Shader.PropertyToID("_GBuffer1");
    private static readonly int _GBuffer2 = Shader.PropertyToID("_GBuffer2");
    private readonly static FieldInfo gBufferFieldInfo = typeof(UniversalRenderer).GetField("m_GBufferPass", BindingFlags.NonPublic | BindingFlags.Instance);

    public override void Create()
    {
        /*
        if (m_TransparentGBufferPass == null)
        {
            m_TransparentGBufferPass = new TransparentGBufferPass(m_GBufferPassNames);
            m_TransparentGBufferPass.renderPassEvent = RenderPassEvent.AfterRenderingSkybox + 1;
        }*/

        if (m_ForwardGBufferPass == null)
        {
            m_ForwardGBufferPass = new ForwardGBufferPass(m_GBufferPassNames);
            // Set this to "After Opaques" so that we can enable GBuffers Depth Priming on non-GL platforms.
            m_ForwardGBufferPass.renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
        }
    }
    protected override void Dispose(bool disposing)
    {
        /*
        if (m_TransparentGBufferPass! != null)
            m_TransparentGBufferPass.Dispose();*/
        if (m_ForwardGBufferPass! != null)
            m_ForwardGBufferPass.Dispose();
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        // If GBuffer exists, URP is in Deferred path. (Actual rendering mode can be different from settings, such as URP forces Forward on OpenGL)
        bool isUsingDeferred = gBufferFieldInfo.GetValue(renderer) != null;
        // OpenGL won't use deferred path.
        isUsingDeferred &= (SystemInfo.graphicsDeviceType != GraphicsDeviceType.OpenGLES3) & (SystemInfo.graphicsDeviceType != GraphicsDeviceType.OpenGLCore);  // GLES 2 is deprecated.

        // Render Forward GBuffer pass if the current device supports MRT.
        if (!isUsingDeferred)
        {
            if (SystemInfo.supportedRenderTargetCount >= 3) { renderer.EnqueuePass(m_ForwardGBufferPass); }
            else { Debug.LogError("The current device does not support rendering to multiple render targets."); }
        }
        if (renderingData.cameraData.camera.cameraType == CameraType.Game)
        {
            Shader.SetGlobalFloat("SEGI_INIT", 1);
        }
        else
        {
            Shader.SetGlobalFloat("SEGI_INIT", 0);
        }
    }

    public class ForwardGBufferPass : ScriptableRenderPass
    {
        const string m_ProfilerTag = "Forward & Forward+ GBuffer Pass";
        private readonly ProfilingSampler m_ProfilingSampler = new ProfilingSampler(m_ProfilerTag);

        private List<ShaderTagId> m_ShaderTagIdList = new List<ShaderTagId>();
        private FilteringSettings m_filter;

        // Depth Priming.
        private RenderStateBlock m_RenderStateBlock = new RenderStateBlock(RenderStateMask.Nothing);

        public RTHandle m_GBuffer0;
        public RTHandle m_GBuffer1;
        public RTHandle m_GBuffer2;
        private RTHandle[] m_GBuffers;

        public ForwardGBufferPass(string[] PassNames)
        {
            RenderQueueRange queue = RenderQueueRange.opaque;
            m_filter = new FilteringSettings(queue);
            if (PassNames != null && PassNames.Length > 0)
            {
                foreach (var passName in PassNames)
                    m_ShaderTagIdList.Add(new ShaderTagId(passName));
            }
        }

        // From "URP-Package/Runtime/DeferredLights.cs".
        public GraphicsFormat GetGBufferFormat(int index)
        {
            if (index == 0) // sRGB albedo, materialFlags
                return QualitySettings.activeColorSpace == ColorSpace.Linear ? GraphicsFormat.R8G8B8A8_SRGB : GraphicsFormat.R8G8B8A8_UNorm;
            else if (index == 1) // sRGB specular, occlusion
                return GraphicsFormat.R8G8B8A8_UNorm;
            else if (index == 2) // normal normal normal packedSmoothness
                                 // NormalWS range is -1.0 to 1.0, so we need a signed render texture.
            #if UNITY_2023_2_OR_NEWER
                if (SystemInfo.IsFormatSupported(GraphicsFormat.R8G8B8A8_SNorm, GraphicsFormatUsage.Render))
            #else
                if (SystemInfo.IsFormatSupported(GraphicsFormat.R8G8B8A8_SNorm, FormatUsage.Render))
            #endif
                    return GraphicsFormat.R8G8B8A8_SNorm;
                else
                    return GraphicsFormat.R16G16B16A16_SFloat;
            else
                return GraphicsFormat.None;
        }

        #region Non Render Graph Pass
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            // GBuffer cannot store surface data from transparent objects.
            SortingCriteria sortingCriteria = renderingData.cameraData.defaultOpaqueSortFlags;

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, m_ProfilingSampler))
            {
                RendererListDesc rendererListDesc = new RendererListDesc(m_ShaderTagIdList[0], renderingData.cullResults, renderingData.cameraData.camera);
                rendererListDesc.stateBlock = m_RenderStateBlock;
                rendererListDesc.sortingCriteria = sortingCriteria;
                rendererListDesc.renderQueueRange = m_filter.renderQueueRange;
                RendererList rendererList = context.CreateRendererList(rendererListDesc);

                cmd.DrawRendererList(rendererList);
            }

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0; // Color and depth cannot be combined in RTHandles
            desc.stencilFormat = GraphicsFormat.None;
            desc.msaaSamples = 1; // Do not enable MSAA for GBuffers.

            // Albedo.rgb + MaterialFlags.a
            desc.graphicsFormat = GetGBufferFormat(0);
            RenderingUtils.ReAllocateIfNeeded(ref m_GBuffer0, desc, FilterMode.Point, TextureWrapMode.Clamp, name: "_GBuffer0");
            cmd.SetGlobalTexture(_GBuffer0, m_GBuffer0);

            // Specular.rgb + Occlusion.a
            desc.graphicsFormat = GetGBufferFormat(1);
            RenderingUtils.ReAllocateIfNeeded(ref m_GBuffer1, desc, FilterMode.Point, TextureWrapMode.Clamp, name: "_GBuffer1");
            cmd.SetGlobalTexture(_GBuffer1, m_GBuffer1);

            // [Resolve Later] The "_CameraNormalsTexture" still exists after disabling DepthNormals Prepass, which may cause issue during rendering.
            // So instead of checking the RTHandle, we need to check if DepthNormals Prepass is enqueued.

            /*
            // If "_CameraNormalsTexture" exists (lacking smoothness info), set the target to it instead of creating a new RT.
            if (normalsTextureFieldInfo.GetValue(renderingData.cameraData.renderer) is not RTHandle normalsTextureHandle)
            {
                // NormalWS.rgb + Smoothness.a
                desc.graphicsFormat = GetGBufferFormat(2);
                RenderingUtils.ReAllocateIfNeeded(ref m_GBuffer2, desc, FilterMode.Point, TextureWrapMode.Clamp, name: "_GBuffer2");
                cmd.SetGlobalTexture("_GBuffer2", m_GBuffer2);
                m_GBuffers = new RTHandle[] { m_GBuffer0, m_GBuffer1, m_GBuffer2 };
            }
            else
            {
                cmd.SetGlobalTexture("_GBuffer2", normalsTextureHandle);
                m_GBuffers = new RTHandle[] { m_GBuffer0, m_GBuffer1, normalsTextureHandle };
            }
            */

            // NormalWS.rgb + Smoothness.a
            desc.graphicsFormat = GetGBufferFormat(2);
            RenderingUtils.ReAllocateIfNeeded(ref m_GBuffer2, desc, FilterMode.Point, TextureWrapMode.Clamp, name: "_GBuffer2");
            cmd.SetGlobalTexture(_GBuffer2, m_GBuffer2);
            m_GBuffers = new RTHandle[] { m_GBuffer0, m_GBuffer1, m_GBuffer2 };

            ConfigureTarget(m_GBuffers, renderingData.cameraData.renderer.cameraDepthTargetHandle);

            // Require Depth Texture in Forward pipeline.
            ConfigureInput(ScriptableRenderPassInput.Depth);

            // [OpenGL] Reusing the depth buffer seems to cause black glitching artifacts, so clear the existing depth.
            bool isOpenGL = (SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES3) || (SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLCore); // GLES 2 is deprecated.
            if (isOpenGL)
                ConfigureClear(ClearFlag.Color | ClearFlag.Depth, Color.black);
            else
                // We have to also clear previous color so that the "background" will remain empty (black) when moving the camera.
                ConfigureClear(ClearFlag.Color, Color.clear);

            // Reduce GBuffer overdraw using the depth from opaque pass. (excluding OpenGL platforms)
            if (!isOpenGL && (renderingData.cameraData.renderType == CameraRenderType.Base || renderingData.cameraData.clearDepth))
            {
                m_RenderStateBlock.depthState = new DepthState(false, CompareFunction.Equal);
                m_RenderStateBlock.mask |= RenderStateMask.Depth;
            }
            else if (m_RenderStateBlock.depthState.compareFunction == CompareFunction.Equal)
            {
                m_RenderStateBlock.depthState = new DepthState(true, CompareFunction.LessEqual);
                m_RenderStateBlock.mask |= RenderStateMask.Depth;
            }
        }
        #endregion

    #if UNITY_6000_0_OR_NEWER
        #region Render Graph Pass

        // This class stores the data needed by the pass, passed as parameter to the delegate function that executes the pass
        private class PassData
        {
            internal bool isOpenGL;

            internal RendererListHandle rendererListHandle;
        }

        // This static method is used to execute the pass and passed as the RenderFunc delegate to the RenderGraph render pass
        static void ExecutePass(PassData data, RasterGraphContext context)
        {
            if (data.isOpenGL)
                context.cmd.ClearRenderTarget(true, true, Color.black);
            else
                // We have to also clear previous color so that the "background" will remain empty (black) when moving the camera.
                context.cmd.ClearRenderTarget(false, true, Color.clear);

            context.cmd.DrawRendererList(data.rendererListHandle);
        }

        // This is where the renderGraph handle can be accessed.
        // Each ScriptableRenderPass can use the RenderGraph handle to add multiple render passes to the render graph
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            // add a raster render pass to the render graph, specifying the name and the data type that will be passed to the ExecutePass function
            using (var builder = renderGraph.AddRasterRenderPass<PassData>(m_ProfilerTag, out var passData))
            {
                // UniversalResourceData contains all the texture handles used by the renderer, including the active color and depth textures
                // The active color and depth textures are the main color and depth buffers that the camera renders into
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                UniversalRenderingData universalRenderingData = frameData.Get<UniversalRenderingData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                UniversalLightData lightData = frameData.Get<UniversalLightData>();

                RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;
                desc.msaaSamples = 1;
                desc.depthBufferBits = 0;

                // Albedo.rgb + MaterialFlags.a
                desc.graphicsFormat = GetGBufferFormat(0);
                TextureHandle gBuffer0Handle = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, name: "_GBuffer0", false, FilterMode.Point, TextureWrapMode.Clamp);

                // Specular.rgb + Occlusion.a
                desc.graphicsFormat = GetGBufferFormat(1);
                TextureHandle gBuffer1Handle = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, name: "_GBuffer1", false, FilterMode.Point, TextureWrapMode.Clamp);

                // [Resolve Later] The "_CameraNormalsTexture" still exists after disabling DepthNormals Prepass, which may cause issue during rendering.
                // So instead of checking the RTHandle, we need to check if DepthNormals Prepass is enqueued.

                /*
                TextureHandle gBuffer2Handle;
                // If "_CameraNormalsTexture" exists (lacking smoothness info), set the target to it instead of creating a new RT.
                if (normalsTextureFieldInfo.GetValue(cameraData.renderer) is not RTHandle normalsTextureHandle)
                {
                    // NormalWS.rgb + Smoothness.a
                    desc.graphicsFormat = GetGBufferFormat(2);
                    gBuffer2Handle = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, name: "_GBuffer2", false, FilterMode.Point, TextureWrapMode.Clamp);
                }
                else
                {
                    gBuffer2Handle = resourceData.cameraNormalsTexture;
                }
                */

                // NormalWS.rgb + Smoothness.a
                desc.graphicsFormat = GetGBufferFormat(2);
                TextureHandle gBuffer2Handle = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, name: "_GBuffer2", false, FilterMode.Point, TextureWrapMode.Clamp);

                // [OpenGL] Reusing the depth buffer seems to cause black glitching artifacts, so clear the existing depth.
                bool isOpenGL = (SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES3) || (SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLCore); // GLES 2 is deprecated.

                // Reduce GBuffer overdraw using the depth from opaque pass. (excluding OpenGL platforms)
                if (!isOpenGL && (cameraData.renderType == CameraRenderType.Base || cameraData.clearDepth))
                {
                    m_RenderStateBlock.depthState = new DepthState(false, CompareFunction.Equal);
                    m_RenderStateBlock.mask |= RenderStateMask.Depth;
                }
                else if (m_RenderStateBlock.depthState.compareFunction == CompareFunction.Equal)
                {
                    m_RenderStateBlock.depthState = new DepthState(true, CompareFunction.LessEqual);
                    m_RenderStateBlock.mask |= RenderStateMask.Depth;
                }

                // GBuffer cannot store surface data from transparent objects.
                SortingCriteria sortingCriteria = cameraData.defaultOpaqueSortFlags;
                RendererListDesc rendererListDesc = new RendererListDesc(m_ShaderTagIdList[0], universalRenderingData.cullResults, cameraData.camera);
                DrawingSettings drawSettings = RenderingUtils.CreateDrawingSettings(m_ShaderTagIdList[0], universalRenderingData, cameraData, lightData, sortingCriteria);
                var param = new RendererListParams(universalRenderingData.cullResults, drawSettings, m_filter);
                rendererListDesc.stateBlock = m_RenderStateBlock;
                rendererListDesc.sortingCriteria = sortingCriteria;
                rendererListDesc.renderQueueRange = m_filter.renderQueueRange;

                // Set pass data
                passData.isOpenGL = isOpenGL;
                passData.rendererListHandle = renderGraph.CreateRendererList(rendererListDesc);

                // We declare the RendererList we just created as an input dependency to this pass, via UseRendererList()
                builder.UseRendererList(passData.rendererListHandle);

                // Set render targets
                builder.SetRenderAttachment(gBuffer0Handle, 0);
                builder.SetRenderAttachment(gBuffer1Handle, 1);
                builder.SetRenderAttachment(gBuffer2Handle, 2);
                builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.ReadWrite);

                // Set global textures after this pass
                builder.SetGlobalTextureAfterPass(gBuffer0Handle, _GBuffer0);
                builder.SetGlobalTextureAfterPass(gBuffer1Handle, _GBuffer1);
                builder.SetGlobalTextureAfterPass(gBuffer2Handle, _GBuffer2);

                // We disable culling for this pass for the demonstrative purpose of this sample, as normally this pass would be culled,
                // since the destination texture is not used anywhere else
                //builder.AllowGlobalStateModification(true);
                //builder.AllowPassCulling(false);

                // Assign the ExecutePass function to the render pass delegate, which will be called by the render graph when executing the pass
                builder.SetRenderFunc((PassData data, RasterGraphContext context) => ExecutePass(data, context));
            }
        }

        #endregion
    #endif

        #region Shared
        public void Dispose()
        {
            m_GBuffer0?.Release();
            m_GBuffer1?.Release();
            m_GBuffer2?.Release();
        }
        #endregion
    }


    /*
    public class TransparentGBufferPass : ScriptableRenderPass
    {
        const string m_ProfilerTag = "Forward & Forward+ Transparent GBuffer Pass";
        private readonly ProfilingSampler m_ProfilingSampler = new ProfilingSampler(m_ProfilerTag);

        private List<ShaderTagId> m_ShaderTagIdList = new List<ShaderTagId>();
        private FilteringSettings m_filter;

        // Depth Priming.
        private RenderStateBlock m_RenderStateBlock = new RenderStateBlock(RenderStateMask.Nothing);

        public RTHandle m_TransparentGBuffer0;
        public RTHandle m_TransparentGBuffer1;
        public RTHandle m_TransparentGBuffer2;
        private RTHandle[] m_TransparentGBuffers;

        public TransparentGBufferPass(string[] PassNames)
        {
            RenderQueueRange queue = RenderQueueRange.transparent;// new RenderQueueRange(3000, 3000);
            m_filter = new FilteringSettings(queue);
            if (PassNames != null && PassNames.Length > 0)
            {
                foreach (var passName in PassNames)
                    m_ShaderTagIdList.Add(new ShaderTagId(passName));
            }
        }

        // From "URP-Package/Runtime/DeferredLights.cs".
        public GraphicsFormat GetGBufferFormat(int index)
        {
            if (index == 0) // sRGB albedo, materialFlags
                return QualitySettings.activeColorSpace == ColorSpace.Linear ? GraphicsFormat.R8G8B8A8_SRGB : GraphicsFormat.R8G8B8A8_UNorm;
            else if (index == 1) // sRGB specular, occlusion
                return GraphicsFormat.R8G8B8A8_UNorm;
            else if (index == 2) // normal normal normal packedSmoothness
                // NormalWS range is -1.0 to 1.0, so we need a signed render texture.
            #if UNITY_2023_2_OR_NEWER
                if (SystemInfo.IsFormatSupported(GraphicsFormat.R8G8B8A8_SNorm, GraphicsFormatUsage.Render))
            #else
                if (SystemInfo.IsFormatSupported(GraphicsFormat.R8G8B8A8_SNorm, FormatUsage.Render))
            #endif
                    return GraphicsFormat.R8G8B8A8_SNorm;
                else
                    return GraphicsFormat.R16G16B16A16_SFloat;
            else
                return GraphicsFormat.None;
        }

        #region Non Render Graph Pass
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            // GBuffer cannot store surface data from transparent objects.
            SortingCriteria sortingCriteria = renderingData.cameraData.defaultOpaqueSortFlags;

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, m_ProfilingSampler))
            {
                RendererListDesc rendererListDesc = new RendererListDesc(m_ShaderTagIdList[0], renderingData.cullResults, renderingData.cameraData.camera);
                rendererListDesc.stateBlock = m_RenderStateBlock;
                rendererListDesc.sortingCriteria = sortingCriteria;
                rendererListDesc.renderQueueRange = m_filter.renderQueueRange;
                RendererList rendererList = context.CreateRendererList(rendererListDesc);

                cmd.DrawRendererList(rendererList);
            }

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0; // Color and depth cannot be combined in RTHandles
            desc.stencilFormat = GraphicsFormat.None;
            desc.msaaSamples = 1; // Do not enable MSAA for GBuffers.

            // Albedo.rgb + MaterialFlags.a
            desc.graphicsFormat = GetGBufferFormat(0);
            RenderingUtils.ReAllocateIfNeeded(ref m_TransparentGBuffer0, desc, FilterMode.Point, TextureWrapMode.Clamp, name: "_TransparentGBuffer0");
            cmd.SetGlobalTexture(_TransparentGBuffer0, m_TransparentGBuffer0);

            // Specular.rgb + Occlusion.a
            desc.graphicsFormat = GetGBufferFormat(1);
            RenderingUtils.ReAllocateIfNeeded(ref m_TransparentGBuffer1, desc, FilterMode.Point, TextureWrapMode.Clamp, name: "_TransparentGBuffer1");
            cmd.SetGlobalTexture(_TransparentGBuffer1, m_TransparentGBuffer1);

            // NormalWS.rgb + Smoothness.a
            desc.graphicsFormat = GetGBufferFormat(2);
            RenderingUtils.ReAllocateIfNeeded(ref m_TransparentGBuffer2, desc, FilterMode.Point, TextureWrapMode.Clamp, name: "_TransparentGBuffer2");
            cmd.SetGlobalTexture(_TransparentGBuffer2, m_TransparentGBuffer2);

            m_TransparentGBuffers = new RTHandle[] { m_TransparentGBuffer0, m_TransparentGBuffer1, m_TransparentGBuffer2 };

            ConfigureTarget(m_TransparentGBuffers, renderingData.cameraData.renderer.cameraDepthTargetHandle);

            // Require Depth Texture in Forward pipeline.
            ConfigureInput(ScriptableRenderPassInput.Depth);

            // [OpenGL] Reusing the depth buffer seems to cause black glitching artifacts, so clear the existing depth.
            bool isOpenGL = (SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES3) || (SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLCore); // GLES 2 is deprecated.
            if (isOpenGL)
                ConfigureClear(ClearFlag.Color | ClearFlag.Depth, Color.clear);
            else
                // We have to also clear previous color so that the "background" will remain empty (black) when moving the camera.
                ConfigureClear(ClearFlag.Color, Color.clear);

            // Reduce GBuffer overdraw using the depth from opaque pass. (excluding OpenGL platforms)
            if (!isOpenGL && (renderingData.cameraData.renderType == CameraRenderType.Base || renderingData.cameraData.clearDepth))
            {
                m_RenderStateBlock.depthState = new DepthState(true, CompareFunction.LessEqual);
                m_RenderStateBlock.mask |= RenderStateMask.Depth;
            }
            else if (m_RenderStateBlock.depthState.compareFunction == CompareFunction.Equal)
            {
                m_RenderStateBlock.depthState = new DepthState(true, CompareFunction.LessEqual);
                m_RenderStateBlock.mask |= RenderStateMask.Depth;
            }

            m_RenderStateBlock.blendState = new BlendState
            {
                blendState0 = new RenderTargetBlendState
                {

                    destinationColorBlendMode = BlendMode.Zero,
                    sourceColorBlendMode = BlendMode.One,
                    destinationAlphaBlendMode = BlendMode.Zero,
                    sourceAlphaBlendMode = BlendMode.One,
                    colorBlendOperation = BlendOp.Add,
                    alphaBlendOperation = BlendOp.Add,
                    writeMask = ColorWriteMask.All
                },

                blendState1 = new RenderTargetBlendState
                {

                    destinationColorBlendMode = BlendMode.Zero,
                    sourceColorBlendMode = BlendMode.One,
                    destinationAlphaBlendMode = BlendMode.Zero,
                    sourceAlphaBlendMode = BlendMode.One,
                    colorBlendOperation = BlendOp.Add,
                    alphaBlendOperation = BlendOp.Add,
                    writeMask = ColorWriteMask.All
                },

                blendState2 = new RenderTargetBlendState
                {

                    destinationColorBlendMode = BlendMode.Zero,
                    sourceColorBlendMode = BlendMode.One,
                    destinationAlphaBlendMode = BlendMode.Zero,
                    sourceAlphaBlendMode = BlendMode.One,
                    colorBlendOperation = BlendOp.Add,
                    alphaBlendOperation = BlendOp.Add,
                    writeMask = ColorWriteMask.All
                }
            };
            m_RenderStateBlock.mask |= RenderStateMask.Blend;
        }
        #endregion

    #if UNITY_6000_0_OR_NEWER
        #region Render Graph Pass
        // This class stores the data needed by the pass, passed as parameter to the delegate function that executes the pass
        private class PassData
        {
            internal bool isOpenGL;

            internal RendererListHandle rendererListHandle;
        }

        // This static method is used to execute the pass and passed as the RenderFunc delegate to the RenderGraph render pass
        static void ExecutePass(PassData data, RasterGraphContext context)
        {
            if (data.isOpenGL)
                context.cmd.ClearRenderTarget(true, true, Color.black);
            else
                // We have to also clear previous color so that the "background" will remain empty (black) when moving the camera.
                context.cmd.ClearRenderTarget(false, true, Color.clear);

            context.cmd.DrawRendererList(data.rendererListHandle);
        }

        // This is where the renderGraph handle can be accessed.
        // Each ScriptableRenderPass can use the RenderGraph handle to add multiple render passes to the render graph
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            // add a raster render pass to the render graph, specifying the name and the data type that will be passed to the ExecutePass function
            using (var builder = renderGraph.AddRasterRenderPass<PassData>(m_ProfilerTag, out var passData))
            {
                // UniversalResourceData contains all the texture handles used by the renderer, including the active color and depth textures
                // The active color and depth textures are the main color and depth buffers that the camera renders into
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                UniversalRenderingData universalRenderingData = frameData.Get<UniversalRenderingData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                UniversalLightData lightData = frameData.Get<UniversalLightData>();

                RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;
                desc.msaaSamples = 1;
                desc.depthBufferBits = 0;

                // Albedo.rgb + MaterialFlags.a
                desc.graphicsFormat = GetGBufferFormat(0);
                TextureHandle gBuffer0Handle = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, name: "_TransparentGBuffer0", false, FilterMode.Point, TextureWrapMode.Clamp);

                // Specular.rgb + Occlusion.a
                desc.graphicsFormat = GetGBufferFormat(1);
                TextureHandle gBuffer1Handle = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, name: "_TransparentGBuffer1", false, FilterMode.Point, TextureWrapMode.Clamp);

                // NormalWS.rgb + Smoothness.a
                desc.graphicsFormat = GetGBufferFormat(2);
                TextureHandle gBuffer2Handle = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, name: "_TransparentGBuffer2", false, FilterMode.Point, TextureWrapMode.Clamp);

                // [OpenGL] Reusing the depth buffer seems to cause black glitching artifacts, so clear the existing depth.
                bool isOpenGL = (SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES3) || (SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLCore); // GLES 2 is deprecated.

                m_RenderStateBlock.depthState = new DepthState(true, CompareFunction.LessEqual);
                m_RenderStateBlock.mask |= RenderStateMask.Depth;

                m_RenderStateBlock.blendState = new BlendState
                {
                    blendState0 = new RenderTargetBlendState
                    {

                        destinationColorBlendMode = BlendMode.Zero,
                        sourceColorBlendMode = BlendMode.One,
                        destinationAlphaBlendMode = BlendMode.Zero,
                        sourceAlphaBlendMode = BlendMode.One,
                        colorBlendOperation = BlendOp.Add,
                        alphaBlendOperation = BlendOp.Add,
                        writeMask = ColorWriteMask.All
                    },

                    blendState1 = new RenderTargetBlendState
                    {

                        destinationColorBlendMode = BlendMode.Zero,
                        sourceColorBlendMode = BlendMode.One,
                        destinationAlphaBlendMode = BlendMode.Zero,
                        sourceAlphaBlendMode = BlendMode.One,
                        colorBlendOperation = BlendOp.Add,
                        alphaBlendOperation = BlendOp.Add,
                        writeMask = ColorWriteMask.All
                    },

                    blendState2 = new RenderTargetBlendState
                    {

                        destinationColorBlendMode = BlendMode.Zero,
                        sourceColorBlendMode = BlendMode.One,
                        destinationAlphaBlendMode = BlendMode.Zero,
                        sourceAlphaBlendMode = BlendMode.One,
                        colorBlendOperation = BlendOp.Add,
                        alphaBlendOperation = BlendOp.Add,
                        writeMask = ColorWriteMask.All
                    }
                };
                m_RenderStateBlock.mask |= RenderStateMask.Blend;

                // GBuffer cannot store surface data from transparent objects.
                SortingCriteria sortingCriteria = cameraData.defaultOpaqueSortFlags;
                RendererListDesc rendererListDesc = new RendererListDesc(m_ShaderTagIdList[0], universalRenderingData.cullResults, cameraData.camera);
                rendererListDesc.stateBlock = m_RenderStateBlock;
                rendererListDesc.sortingCriteria = sortingCriteria;
                rendererListDesc.renderQueueRange = m_filter.renderQueueRange;

                // Setup pass data
                passData.isOpenGL = isOpenGL;
                passData.rendererListHandle = renderGraph.CreateRendererList(rendererListDesc);

                // We declare the RendererList we just created as an input dependency to this pass, via UseRendererList()
                builder.UseRendererList(passData.rendererListHandle);

                builder.SetRenderAttachment(gBuffer0Handle, 0);
                builder.SetRenderAttachment(gBuffer1Handle, 1);
                builder.SetRenderAttachment(gBuffer2Handle, 2);
                builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.ReadWrite);

                builder.SetGlobalTextureAfterPass(gBuffer0Handle, _TransparentGBuffer0);
                builder.SetGlobalTextureAfterPass(gBuffer1Handle, _TransparentGBuffer1);
                builder.SetGlobalTextureAfterPass(gBuffer2Handle, _TransparentGBuffer2);

                // We disable culling for this pass for the demonstrative purpose of this sample, as normally this pass would be culled,
                // since the destination texture is not used anywhere else
                //builder.AllowGlobalStateModification(true);
                //builder.AllowPassCulling(false);

                // Assign the ExecutePass function to the render pass delegate, which will be called by the render graph when executing the pass
                builder.SetRenderFunc((PassData data, RasterGraphContext context) => ExecutePass(data, context));
            }
        }
        #endregion
    #endif

        #region Shared
        public void Dispose()
        {
            m_TransparentGBuffer0?.Release();
            m_TransparentGBuffer1?.Release();
            m_TransparentGBuffer2?.Release();
        }
        #endregion
    }*/
}