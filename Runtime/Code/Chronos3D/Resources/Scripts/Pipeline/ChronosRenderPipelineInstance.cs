using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using System.Collections.Generic;
using System;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using System.Runtime.CompilerServices;

public class ChronosRenderPipelineInstance : RenderPipeline
{
    public ChronosRenderPipelineInstance(float renderScaleSet, int MSAA, ChronosPostProcessingStack postStack, bool HDR)
    {
        hdr = HDR;
        msaaSamples = MSAA;
        renderScale = renderScaleSet;
        postProcessingStack = postStack;
                
        QualitySettings.antiAliasing = msaaSamples;
        this.msaaSamples = Mathf.Max(QualitySettings.antiAliasing, 1);
    }
     
    public class RenderTargetGroup
    {
        //Array of cameras
        public List<Camera> cameras = new();
        public CameraType cameraType = 0;
        //REnder target or null
        public RenderTexture renderTexture;
    };

    public MaterialPropertyBlock globalPropertyBlock;
    private float renderScale = 1;
    private int msaaSamples = 4;
    ChronosPostProcessingStack postProcessingStack;
    static int cameraColorTextureId = Shader.PropertyToID("_CameraColorTexture");
    static int cameraColorTextureMrtId = Shader.PropertyToID("_CameraColorTextureMrt");
    static int cameraDepthTextureId = Shader.PropertyToID("_CameraDepthTexture");
    static int resolvedCameraColorTextureId = Shader.PropertyToID("_ResolvedCameraColorTexture");
    static int resolvedCameraColorTextureMrtId = Shader.PropertyToID("_ResolvedCameraColorTextureMrt");
    static int resolvedCameraDepthTextureId = Shader.PropertyToID("_ResolvedCameraDepthTexture");

    static int globalShadowTexture0Id = Shader.PropertyToID("_GlobalShadowTexture0");
    static int globalShadowTexture1Id = Shader.PropertyToID("_GlobalShadowTexture1");

    static int blurColorTextureId = Shader.PropertyToID("_BlurColorTexture");
    static int mainTexId = Shader.PropertyToID("_CameraOpaqueTexture");
    static int mainTexMrtId = Shader.PropertyToID("_CameraOpaqueTextureMrt");

    static int halfSizeTexId = Shader.PropertyToID("_CameraHalfSize");
    static int quarterSizeTexId = Shader.PropertyToID("_CameraQuarterSize");
    static int halfSizeTexMrtId = Shader.PropertyToID("_CameraHalfSizeMrt");
    static int quarterSizeTexMrtId = Shader.PropertyToID("_CameraQuarterSizeMrt");
    static int halfSizeDepthTextureId = Shader.PropertyToID("_CameraHalfSizeDepthTexture");
    static int quarterSizeDepthTextureId = Shader.PropertyToID("_CameraQuarterSizeDepthTexture");

    //static ambient light data
    Vector4[] shAmbientData = new Vector4[9];
    class MeshRendererDesc
    {
        public MeshRenderer renderer;
        public uint filter;
        public MeshRendererDesc(MeshRenderer renderer, uint filter)
        {
            this.renderer = renderer;
            this.filter = filter;
        }
    }
    List<MeshRendererDesc> meshRenderersToReenable = new List<MeshRendererDesc>();
    
    const int shadowWidth = 2048;
    const int shadowHeight = 2048;
    readonly int[] cascadeSize = new int[] { 8, 64 };


    [NonSerialized]
    Camera[] shadowMapCamera = new Camera[2];
    [NonSerialized]
    GameObject[] shadowMapCameraObject = new GameObject[2];

    bool scaledRendering = false;
    int finalColorTextureId = 0;
    int finalColorTextureMrtId = 0;

    bool hdr = true;
    static int numBlurPasses = 6;
        
    [NonSerialized]
    static int horizontalTextureId = Shader.PropertyToID("_DownscaleHorizontalId");
    [NonSerialized]
    static int verticalTextureId = Shader.PropertyToID("_DownscaleVerticalId");
    [NonSerialized]
    private Material downscaleMaterial;
    [NonSerialized]
    private Material horizontalBlurMaterial;
    [NonSerialized]
    private Material verticalBlurMaterial;
    [NonSerialized]
    private Material bloomMaterial;
    [NonSerialized]
    Material errorMaterial;
    [NonSerialized]
    Material depthMaterial;
    
    [NonSerialized]
    private Texture2D ditherTexture;
    [NonSerialized]
    private string ditherTexturePath = "BaseTextures/BayerDither8x8";

 
    CommandBuffer cameraCmdBuffer = new()
    {
        name = "RenderTarget"
    };

    protected override void Dispose(bool disposing)
    {

    }

    private void SetupGlobalTextures()
    {
        //Think: Move this to the SRP?
        if (ditherTexture == null)
        {
            this.ditherTexture = Resources.Load<Texture2D>(ditherTexturePath); // AssetBridge.LoadAssetInternal<Texture2D>(this.ditherTexturePath, true);
        }

        Shader.SetGlobalTexture("_DitherTexture", ditherTexture);
    }

    protected override void Render(ScriptableRenderContext renderContext, Camera[] cameras)
    {
        SetupGlobalTextures();

        SetupGlobalLightingPropertiesForRendering();

        //Cleanup
        CleanupShadowmapCameras();
        
        //create a array of render targets used by cameras, including a blank one for rendering directly to the backbuffer
        List<RenderTargetGroup> renderTargetGroup = new();

        foreach (var camera in cameras)
        {
#if UNITY_EDITOR            
            if (camera == null)
            {
                continue;
            }
            
            if (camera.gameObject.name == "Shadowmap Camera")
            {
                continue;
            }
#endif            

            //See if the camera is in the shadowMapCamera array, and if so skip
            if (shadowMapCamera != null)
            {
                bool foundCamera = false;
                for (int i = 0; i < shadowMapCamera.Length; i++)
                {
                    if (camera == shadowMapCamera[i])
                    {
                        foundCamera = true;
                        break;
                    }
                }
                if (foundCamera)
                {
                    continue;
                }
            }
            
            //add to the renderTargetGroup where the camera.renderTarget matches, or create a new renderTargetGroup
            bool found = false;
            foreach (var renderTarget in renderTargetGroup)
            {
                if (renderTarget.renderTexture == camera.targetTexture && renderTarget.cameraType == camera.cameraType)
                {
                    renderTarget.cameras.Add(camera);
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                renderTargetGroup.Add(new RenderTargetGroup() { renderTexture = camera.targetTexture, cameras = new() { camera }, cameraType = camera.cameraType });
            }
        }

        //render each renderTargetGroup
        foreach (RenderTargetGroup renderTarget in renderTargetGroup)
        {
            RenderGroup(renderContext, renderTarget);
        }


    }

    void DrawGizmos(ScriptableRenderContext context, Camera camera)
    {
#if UNITY_EDITOR    
        if (Handles.ShouldRenderGizmos())
        {
            context.DrawGizmos(camera, GizmoSubset.PreImageEffects);
            context.DrawGizmos(camera, GizmoSubset.PostImageEffects);
        }
#endif        
    }

    void RenderGroup(ScriptableRenderContext context, RenderTargetGroup group)
    {
        Camera rootCamera = group.cameras[0];

#if UNITY_EDITOR
        if (group.cameraType == CameraType.SceneView)
        {
            ScriptableRenderContext.EmitWorldGeometryForSceneView(rootCamera);
        }
#endif

        scaledRendering = renderScale != 1.0f && (group.cameraType == CameraType.SceneView || group.cameraType == CameraType.Game);

        //Resolution of the resolved texture
        int nativeScreenWidth = rootCamera.pixelWidth;
        int nativeScreenHeight = rootCamera.pixelHeight;

        //Resolution of the actual rendering
        int renderWidth = nativeScreenWidth;
        int renderHeight = nativeScreenHeight;
       
        if (scaledRendering)
        {
            renderWidth = (int)(nativeScreenWidth * renderScale);
            renderHeight = (int)(nativeScreenHeight * renderScale);
            
            finalColorTextureId = resolvedCameraColorTextureId;
            finalColorTextureMrtId = resolvedCameraColorTextureMrtId;
        }
        else
        {
            finalColorTextureId = cameraColorTextureId;
            finalColorTextureMrtId = cameraColorTextureMrtId;
        }
        
        //Quarter scale
        int blurBufferWidth  = (int)Mathf.Ceil(nativeScreenWidth / 4);
        int blurBufferHeight = (int)Mathf.Ceil(nativeScreenHeight / 4);
        
        //camerabuffer exists just to get the render targets if we're doing post
        cameraCmdBuffer.Clear();
        RenderTargetIdentifier[] cameraColorTextureArray = new RenderTargetIdentifier[2];
        cameraColorTextureArray[0] = cameraColorTextureId;
        cameraColorTextureArray[1] = cameraColorTextureMrtId;
   

        if (postProcessingStack)
        {
            //we render into this, possibly at double scale
            RenderTextureDescriptor textureDesc = new RenderTextureDescriptor();
            textureDesc.autoGenerateMips = false;
            textureDesc.colorFormat = RenderTextureFormat.ARGB32;
            textureDesc.msaaSamples = msaaSamples;
            textureDesc.sRGB = false;
            textureDesc.useMipMap = false;
            textureDesc.width = renderWidth;
            textureDesc.height = renderHeight;
            textureDesc.enableRandomWrite = false;
            textureDesc.volumeDepth = 1;
            textureDesc.depthBufferBits = 24;
            textureDesc.dimension = TextureDimension.Tex2D;
            cameraCmdBuffer.GetTemporaryRT(cameraColorTextureId, textureDesc, FilterMode.Bilinear);
                        
            RenderTextureDescriptor depthDesc = new RenderTextureDescriptor();
            depthDesc.autoGenerateMips = false;
            depthDesc.colorFormat = RenderTextureFormat.RFloat;
            depthDesc.msaaSamples = msaaSamples;
            depthDesc.sRGB = false;
            depthDesc.useMipMap = false;
            depthDesc.width = renderWidth;
            depthDesc.height = renderHeight;
            depthDesc.enableRandomWrite = false;
            depthDesc.volumeDepth = 1;
            depthDesc.depthBufferBits = 24;
            depthDesc.dimension = TextureDimension.Tex2D;
            cameraCmdBuffer.GetTemporaryRT(cameraDepthTextureId, depthDesc, FilterMode.Point);
            
            RenderTextureDescriptor textureDescMrt = new RenderTextureDescriptor();
            textureDescMrt.autoGenerateMips = false;
            textureDescMrt.colorFormat = RenderTextureFormat.ARGB32;
            textureDescMrt.msaaSamples = msaaSamples;
            textureDescMrt.sRGB = false;
            textureDescMrt.useMipMap = false;
            textureDescMrt.width = renderWidth;
            textureDescMrt.height = renderHeight;
            textureDescMrt.enableRandomWrite = false;
            textureDescMrt.volumeDepth = 1;
            textureDescMrt.depthBufferBits = 24;
            textureDescMrt.dimension = TextureDimension.Tex2D;
            cameraCmdBuffer.GetTemporaryRT(cameraColorTextureMrtId, textureDescMrt, FilterMode.Bilinear);
            
            //Final resolved texture
            if (scaledRendering)
            {
                RenderTextureDescriptor resolvedTextureDesc = new RenderTextureDescriptor();
                resolvedTextureDesc.autoGenerateMips = false;
                resolvedTextureDesc.colorFormat = RenderTextureFormat.ARGB32;
                resolvedTextureDesc.msaaSamples = 1;
                resolvedTextureDesc.sRGB = false;
                resolvedTextureDesc.useMipMap = false;
                resolvedTextureDesc.width = nativeScreenWidth;
                resolvedTextureDesc.height = nativeScreenHeight;
                resolvedTextureDesc.enableRandomWrite = false;
                resolvedTextureDesc.volumeDepth = 1;
                resolvedTextureDesc.depthBufferBits = 24;
                resolvedTextureDesc.dimension = TextureDimension.Tex2D;
                cameraCmdBuffer.GetTemporaryRT(resolvedCameraColorTextureId, resolvedTextureDesc, FilterMode.Bilinear);

                RenderTextureDescriptor resolvedTextureDescMrt = new RenderTextureDescriptor();
                resolvedTextureDescMrt.autoGenerateMips = false;
                resolvedTextureDescMrt.colorFormat = RenderTextureFormat.ARGB32;
                resolvedTextureDescMrt.msaaSamples = 1;
                resolvedTextureDescMrt.sRGB = false;
                resolvedTextureDescMrt.useMipMap = false;
                resolvedTextureDescMrt.width = nativeScreenWidth;
                resolvedTextureDescMrt.height = nativeScreenHeight;
                resolvedTextureDescMrt.enableRandomWrite = false;
                resolvedTextureDescMrt.volumeDepth = 1;
                resolvedTextureDescMrt.depthBufferBits = 24;
                resolvedTextureDescMrt.dimension = TextureDimension.Tex2D;
                cameraCmdBuffer.GetTemporaryRT(resolvedCameraColorTextureMrtId, resolvedTextureDescMrt, FilterMode.Bilinear);

                RenderTextureDescriptor textureDepthDesc = new RenderTextureDescriptor();
                textureDepthDesc.autoGenerateMips = false;
                textureDepthDesc.colorFormat = RenderTextureFormat.RFloat;
                textureDepthDesc.msaaSamples = 1;
                textureDepthDesc.sRGB = false;
                textureDepthDesc.useMipMap = false;
                textureDepthDesc.width = nativeScreenWidth;
                textureDepthDesc.height = nativeScreenHeight;
                textureDepthDesc.enableRandomWrite = false;
                textureDepthDesc.volumeDepth = 1;
                textureDepthDesc.depthBufferBits = 24;
                textureDepthDesc.dimension = TextureDimension.Tex2D;
                cameraCmdBuffer.GetTemporaryRT(resolvedCameraDepthTextureId, textureDepthDesc, FilterMode.Point);
            }
            
            //The blurred screen texture for frosting
            RenderTextureDescriptor textureDescBlur = new RenderTextureDescriptor();
            textureDescBlur.autoGenerateMips = false;
            textureDescBlur.colorFormat = RenderTextureFormat.ARGB32;
            textureDescBlur.msaaSamples = 1;
            textureDescBlur.sRGB = false;
            textureDescBlur.useMipMap = false;
            textureDescBlur.width = blurBufferWidth;
            textureDescBlur.height = blurBufferHeight;
            textureDescBlur.enableRandomWrite = false;
            textureDescBlur.volumeDepth = 1;
            textureDescBlur.depthBufferBits = 24;
            textureDescBlur.dimension = TextureDimension.Tex2D;
            cameraCmdBuffer.GetTemporaryRT(blurColorTextureId, textureDescBlur, FilterMode.Bilinear);
            
        }
  

        //render in each camera, either directly to the scene view or to their own render target
        //Todo: actually use their render target here!
        bool firstCamera = true;
        foreach (Camera camera in group.cameras)
        {

            if (firstCamera)
            {
               RenderShadowmap(camera, context, cameraCmdBuffer,0);
               RenderShadowmap(camera, context, cameraCmdBuffer, 1);
            }


            // Get the culling parameters from the current Camera
            camera.TryGetCullingParameters(out var cullingParameters);
            
            // Use the culling parameters to perform a cull operation, and store the results
            var cullingResults = context.Cull(ref cullingParameters);

            context.SetupCameraProperties(camera);

            CameraClearFlags clearFlags = camera.clearFlags;

            //Set the target as the render target
            if (postProcessingStack)
            {
                //Notes: I thought you'd be able to get away with just setting this once, but this is not the case
                //As each command buffer requires a new render target, we need to set it each time
                //or it just goes back to rendering directly to the scene
                cameraCmdBuffer.SetRenderTarget(cameraColorTextureArray, cameraDepthTextureId);
            }

            //if we're going to be rendering the skybox, or the first target in the chain, clear everything
            if (camera.clearFlags == CameraClearFlags.Skybox || firstCamera == true)
            {
                cameraCmdBuffer.ClearRenderTarget(
                    true,
                    true,
                    Color.black
                ) ;
                 
            }
            else
            {
                //Do what was requested on the camera itself
                if (clearFlags == CameraClearFlags.Depth)
                {
                    cameraCmdBuffer.ClearRenderTarget(RTClearFlags.Depth, camera.backgroundColor, 1, 0);
                }
                else
                if (clearFlags == CameraClearFlags.Color || clearFlags == CameraClearFlags.Skybox || clearFlags == CameraClearFlags.SolidColor)
                {
                    cameraCmdBuffer.ClearRenderTarget(RTClearFlags.ColorDepth, camera.backgroundColor, 1, 0);
                }
            }

            //Execute clears
            context.ExecuteCommandBuffer(cameraCmdBuffer);
            cameraCmdBuffer.Clear();
       
            // Tell Unity which geometry to draw, based on its LightMode Pass tag value
            ShaderTagId shaderTagId = new("ChronosForwardPass");

            // Tell Unity how to sort the geometry, based on the current Camera
            var sortingSettings = new SortingSettings(camera)
            {
                criteria = SortingCriteria.CommonOpaque // Enable front-to-back sorting
            };
            
            // Create a DrawingSettings struct that describes which geometry to draw and how to draw it
            DrawingSettings opaqueDrawingSettings = new(shaderTagId, sortingSettings);

            //Draw in opaque stuff
            FilteringSettings filteringSettings = FilteringSettings.defaultValue;
            filteringSettings.renderQueueRange = RenderQueueRange.opaque;
                        
            context.DrawRenderers(cullingResults, ref opaqueDrawingSettings, ref filteringSettings);

            // Schedule a command to draw the Skybox if required
            if (camera.clearFlags == CameraClearFlags.Skybox && RenderSettings.skybox != null)
            {
                context.DrawSkybox(camera);
            }

            //Draw in transparent stuff
            // Tell Unity how to sort the geometry, based on the current Camera
            var transparentSortingSettings = new SortingSettings(camera)
            {
                criteria = SortingCriteria.CommonTransparent
            };

            // Create a DrawingSettings struct that describes which geometry to draw and how to draw it
            
            DrawingSettings transparentDrawingSettings = new(shaderTagId, sortingSettings);
            
            filteringSettings.renderQueueRange = RenderQueueRange.transparent;
            context.DrawRenderers(cullingResults, ref transparentDrawingSettings, ref filteringSettings);

            //Draw in Canvases
            DrawCanvases(cullingResults, context, camera);

            //Draw in default stuff (pink objects)
            DrawDefaultPipeline(cullingResults, context, camera);
            

            //Draw in gizmos
            DrawGizmos(context, camera);

            firstCamera = false;
        }

        if (postProcessingStack)
        {
            // cameraCmdBuffer.BeginSample("Resolve textures");
            context.ExecuteCommandBuffer(cameraCmdBuffer);
            cameraCmdBuffer.Clear();

            //Resolve the render target if needs be
            //Build downscaled textures from our "resolved texture"
            BuildResolvedAndHalfAndQuarterSizedTextures(context, cameraCmdBuffer, nativeScreenWidth, nativeScreenHeight);
            context.ExecuteCommandBuffer(cameraCmdBuffer);
            cameraCmdBuffer.Clear();

            
            //Build our post textures
            BuildBlur(context, cameraCmdBuffer, blurBufferWidth, blurBufferHeight, blurColorTextureId, quarterSizeTexId);
            context.ExecuteCommandBuffer(cameraCmdBuffer);
            cameraCmdBuffer.Clear();

            // cameraCmdBuffer.EndSample("Resolve textures");

            //Let the post stack final composite run now  
            postProcessingStack.Render(context, finalColorTextureId, nativeScreenWidth, nativeScreenHeight, halfSizeTexId, quarterSizeTexId, halfSizeTexMrtId, quarterSizeTexMrtId, cameraDepthTextureId);

            //Execute the release of the textures
            context.ExecuteCommandBuffer(cameraCmdBuffer);
            cameraCmdBuffer.Clear();


            //Free it all up            
            cameraCmdBuffer.ReleaseTemporaryRT(cameraColorTextureId);
            cameraCmdBuffer.ReleaseTemporaryRT(halfSizeTexId);
            cameraCmdBuffer.ReleaseTemporaryRT(quarterSizeTexId);

            if (scaledRendering == true)
            {
                cameraCmdBuffer.ReleaseTemporaryRT(resolvedCameraColorTextureId);
            }

            //frosting is used by the UI
            //cameraCmdBuffer.ReleaseTemporaryRT(blurColorTextureId);
            context.ExecuteCommandBuffer(cameraCmdBuffer);
            cameraCmdBuffer.Clear();
          
            
        }
        
        context.Submit();

        //Free the shadow texture
        cameraCmdBuffer.ReleaseTemporaryRT(globalShadowTexture0Id);
        cameraCmdBuffer.ReleaseTemporaryRT(globalShadowTexture1Id);
    }


    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD"), System.Diagnostics.Conditional("UNITY_EDITOR")]
    void DrawDefaultPipeline(CullingResults cullingResults, ScriptableRenderContext context, Camera camera)
    {
        
        if (errorMaterial == null)
        {
            Shader errorShader = Shader.Find("Hidden/ChronosErrorShader");
            errorMaterial = new Material(errorShader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        var drawSettings = new DrawingSettings(new ShaderTagId("ForwardBase"), new SortingSettings(camera));

        drawSettings.SetShaderPassName(1, new ShaderTagId("Always"));
        drawSettings.SetShaderPassName(2, new ShaderTagId("ForwardAdd"));
        drawSettings.SetShaderPassName(3, new ShaderTagId("PrepassBase"));
        drawSettings.SetShaderPassName(4, new ShaderTagId("PrepassFinal"));
        drawSettings.SetShaderPassName(5, new ShaderTagId("Vertex"));
        drawSettings.SetShaderPassName(6, new ShaderTagId("VertexLMRGBM"));
        drawSettings.SetShaderPassName(7, new ShaderTagId("VertexLM"));
        
        drawSettings.overrideMaterial = errorMaterial;
                
        FilteringSettings filteringSettings = FilteringSettings.defaultValue;

        context.DrawRenderers(cullingResults, ref drawSettings, ref filteringSettings);
    }

    void DrawCanvases(CullingResults cullingResults, ScriptableRenderContext context, Camera camera)
    {
        var drawSettings = new DrawingSettings(new ShaderTagId("ForwardBase"), new SortingSettings(camera));
        drawSettings.SetShaderPassName(1, new ShaderTagId("SRPDefaultUnlit"));

        // Get the layer number from the layer name
        int layerMask = LayerMask.NameToLayer("UI");
                
        FilteringSettings filteringSettings = new FilteringSettings(RenderQueueRange.all, 1 << layerMask);
        
        context.DrawRenderers(cullingResults, ref drawSettings, ref filteringSettings);
    }


    public void BuildResolvedAndHalfAndQuarterSizedTextures(ScriptableRenderContext context, CommandBuffer cmd, int sourceTextureWidth, int sourceTextureHeight)
    {
        if (downscaleMaterial == null)
        {
            downscaleMaterial = Resources.Load("DownScaleMat") as Material;
            if (downscaleMaterial == null)
            {
                Debug.LogError("Missing Material: DownScaleMat");
            }
        }
        
        if (scaledRendering)
        {
            //Blit the source texture to the resolved texture
            RenderTargetIdentifier[] rt = new RenderTargetIdentifier[2];
            rt[0] = new RenderTargetIdentifier(resolvedCameraColorTextureId, 0, CubemapFace.Unknown, 0);
            rt[1] = new RenderTargetIdentifier(resolvedCameraColorTextureMrtId, 0, CubemapFace.Unknown, 0);
 
            cmd.SetRenderTarget(rt, resolvedCameraDepthTextureId);
            cmd.ClearRenderTarget(true, true, Color.black);
            //cmd.SetRenderTarget(resolvedCameraColorTextureId, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
            cmd.SetGlobalTexture(mainTexId, cameraColorTextureId); //texture to render with
            cmd.SetGlobalTexture(mainTexMrtId, cameraColorTextureMrtId); //texture to render with
            cmd.DrawMesh(RenderingUtils.fullscreenMesh, Matrix4x4.identity, downscaleMaterial);

            //Note: finalColorTextureId will either be this texture or the source texture
        }

        RenderTextureDescriptor halfSizeTextureDesc = new RenderTextureDescriptor();
        halfSizeTextureDesc.autoGenerateMips = false;
        halfSizeTextureDesc.colorFormat = RenderTextureFormat.Default;
        halfSizeTextureDesc.msaaSamples = 1;
        halfSizeTextureDesc.sRGB = false;
        halfSizeTextureDesc.useMipMap = false;
        halfSizeTextureDesc.width = sourceTextureWidth / 2;
        halfSizeTextureDesc.height = sourceTextureHeight / 2;
        halfSizeTextureDesc.enableRandomWrite = false;
        halfSizeTextureDesc.volumeDepth = 1;
        halfSizeTextureDesc.depthBufferBits = 24;
        halfSizeTextureDesc.dimension = TextureDimension.Tex2D;

        RenderTextureDescriptor halfSizeDepthDesc = new RenderTextureDescriptor();
        halfSizeDepthDesc.autoGenerateMips = false;
        halfSizeDepthDesc.colorFormat = RenderTextureFormat.RFloat;
        halfSizeDepthDesc.msaaSamples = 1;
        halfSizeDepthDesc.sRGB = false;
        halfSizeDepthDesc.useMipMap = false;
        halfSizeDepthDesc.width = sourceTextureWidth / 2; 
        halfSizeDepthDesc.height = sourceTextureHeight / 2;
        halfSizeDepthDesc.enableRandomWrite = false;
        halfSizeDepthDesc.volumeDepth = 1;
        halfSizeDepthDesc.depthBufferBits = 24;
        halfSizeDepthDesc.dimension = TextureDimension.Tex2D;
        
        RenderTextureDescriptor quarterSizeTextureDesc = new RenderTextureDescriptor();
        quarterSizeTextureDesc.autoGenerateMips = false;
        quarterSizeTextureDesc.colorFormat = RenderTextureFormat.Default;
        quarterSizeTextureDesc.msaaSamples = 1;
        quarterSizeTextureDesc.sRGB = false;
        quarterSizeTextureDesc.useMipMap = false;
        quarterSizeTextureDesc.width = sourceTextureWidth / 4;
        quarterSizeTextureDesc.height = sourceTextureHeight / 4;
        quarterSizeTextureDesc.enableRandomWrite = false;
        quarterSizeTextureDesc.volumeDepth = 1;
        quarterSizeTextureDesc.depthBufferBits = 24;
        quarterSizeTextureDesc.dimension = TextureDimension.Tex2D;

        RenderTextureDescriptor quarterSizeDepthDesc = new RenderTextureDescriptor();
        quarterSizeDepthDesc.autoGenerateMips = false;
        quarterSizeDepthDesc.colorFormat = RenderTextureFormat.RFloat;
        quarterSizeDepthDesc.msaaSamples = 1;
        quarterSizeDepthDesc.sRGB = false;
        quarterSizeDepthDesc.useMipMap = false;
        quarterSizeDepthDesc.width = sourceTextureWidth / 4;
        quarterSizeDepthDesc.height = sourceTextureHeight / 4;
        quarterSizeDepthDesc.enableRandomWrite = false;
        quarterSizeDepthDesc.volumeDepth = 1;
        quarterSizeDepthDesc.depthBufferBits = 24;
        quarterSizeDepthDesc.dimension = TextureDimension.Tex2D;
        

        cmd.GetTemporaryRT(halfSizeTexId, halfSizeTextureDesc, FilterMode.Bilinear);
        cmd.GetTemporaryRT(quarterSizeTexId, quarterSizeTextureDesc, FilterMode.Bilinear);
        cmd.GetTemporaryRT(halfSizeTexMrtId, halfSizeTextureDesc, FilterMode.Bilinear);
        cmd.GetTemporaryRT(quarterSizeTexMrtId, quarterSizeTextureDesc, FilterMode.Bilinear);
        cmd.GetTemporaryRT(halfSizeDepthTextureId, halfSizeDepthDesc, FilterMode.Point);
        cmd.GetTemporaryRT(quarterSizeDepthTextureId, quarterSizeDepthDesc, FilterMode.Point);

        RenderTargetIdentifier[] halfSizeRt = new RenderTargetIdentifier[2];
        halfSizeRt[0] = new RenderTargetIdentifier(halfSizeTexId, 0, CubemapFace.Unknown, 0);
        halfSizeRt[1] = new RenderTargetIdentifier(halfSizeTexMrtId, 0, CubemapFace.Unknown, 0);

        //Blit the source texture to the halfSize texture
        cmd.SetRenderTarget(halfSizeRt, halfSizeDepthTextureId);
        cmd.ClearRenderTarget(true, true, Color.black);
        cmd.SetGlobalTexture(mainTexId, finalColorTextureId); //texture to render with
        cmd.SetGlobalTexture(mainTexMrtId, finalColorTextureMrtId); //texture to render with
        cmd.DrawMesh(RenderingUtils.fullscreenMesh, Matrix4x4.identity, downscaleMaterial);

        RenderTargetIdentifier[] quarterSizeRt = new RenderTargetIdentifier[2];
        quarterSizeRt[0] = new RenderTargetIdentifier(quarterSizeTexId, 0, CubemapFace.Unknown, 0);
        quarterSizeRt[1] = new RenderTargetIdentifier(quarterSizeTexMrtId, 0, CubemapFace.Unknown, 0);

        //Blit the halfSize texture to the quarterSize texture
        cmd.SetRenderTarget(quarterSizeRt,quarterSizeDepthTextureId);
        cmd.ClearRenderTarget(true, true, Color.black);
        cmd.SetGlobalTexture(mainTexId, halfSizeTexId); //texture to render with
        cmd.SetGlobalTexture(mainTexMrtId, halfSizeTexMrtId); //texture to render with
        cmd.DrawMesh(RenderingUtils.fullscreenMesh, Matrix4x4.identity, downscaleMaterial);
    }

    public void BuildBlur(ScriptableRenderContext context, CommandBuffer cmd, int bufferWidth, int bufferHeight, int outputTextureId, int sourceTextureId)
   {
        if (horizontalBlurMaterial == null)
        {
            horizontalBlurMaterial = Resources.Load("HorizontalBlurMat") as Material;
            if (horizontalBlurMaterial == null)
            {
                Debug.LogError("Missing Material: HorizontalBlurMat");
            }

        }
        if (verticalBlurMaterial == null)
        {
            verticalBlurMaterial = Resources.Load("VerticalBlurMat") as Material;
            if (verticalBlurMaterial == null)
            {
                Debug.LogError("Missing Material: VerticalBlurMat");
            }

        }
 
        float kernelScale = 1.0f;
        horizontalBlurMaterial.SetVector("_BlurScale", new Vector4(1 * kernelScale, 0, 0, 0));
        horizontalBlurMaterial.SetVector("_TextureSize", new Vector4(bufferWidth, bufferHeight, 0, 0));
        
        verticalBlurMaterial.SetVector("_BlurScale", new Vector4(0, 1 * kernelScale, 0, 0));
        verticalBlurMaterial.SetVector("_TextureSize", new Vector4(bufferWidth, bufferHeight, 0, 0));
        
        RenderTextureDescriptor textureDescA = new RenderTextureDescriptor();
        textureDescA.autoGenerateMips = false;
        textureDescA.colorFormat = RenderTextureFormat.Default;
        textureDescA.msaaSamples = 1;
        textureDescA.sRGB = false;
        textureDescA.useMipMap = false;
        textureDescA.width = bufferWidth;
        textureDescA.height = bufferHeight;
        textureDescA.enableRandomWrite = false;
        textureDescA.volumeDepth = 1;
        textureDescA.depthBufferBits = 24;
        textureDescA.dimension = TextureDimension.Tex2D;
        
        cmd.GetTemporaryRT(horizontalTextureId, textureDescA, FilterMode.Bilinear);
        cmd.GetTemporaryRT(verticalTextureId, textureDescA, FilterMode.Bilinear);
        
        int inputTextureId = sourceTextureId;
        for (int j = 0; j < numBlurPasses; j++)
        {
            //On the final pass, write directly to the output texture
            int writeRT = verticalTextureId;
            if (j == numBlurPasses - 1)
            {
                writeRT = outputTextureId;
            }
            
            //Switch to the horizontal buffer, and render that 
            cmd.SetRenderTarget(horizontalTextureId, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
            cmd.ClearRenderTarget(true, true, Color.black);
            cmd.SetGlobalTexture(mainTexId, inputTextureId); //texture to render with
            cmd.DrawMesh(RenderingUtils.fullscreenMesh, Matrix4x4.identity, horizontalBlurMaterial);

            //Switch to the vertical buffer, and render that. 
            cmd.SetRenderTarget(writeRT, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
            cmd.ClearRenderTarget(true, true, Color.black);
            cmd.SetGlobalTexture(mainTexId, horizontalTextureId); //texture to render with
            cmd.DrawMesh(RenderingUtils.fullscreenMesh, Matrix4x4.identity, verticalBlurMaterial);

            //After the first pass the inputTextureId changes to the last rendered target
            inputTextureId = writeRT;
        }

        cmd.ReleaseTemporaryRT(horizontalTextureId);
        cmd.ReleaseTemporaryRT(verticalTextureId);
    }

    private Bounds CalculateFrustumBounds(Camera camera, Matrix4x4 worldToLightspace, float maxDistance)
    {
        var worldFrustumCorners = new Vector3[8];
        CalculateFrustumCorners(camera, camera.nearClipPlane, worldFrustumCorners);
        CalculateFrustumCorners(camera, maxDistance, worldFrustumCorners, 4);

        //Todo: trim this down 
        float maxZ = 256;
        Vector3 cameraPos = camera.transform.position;
        Vector3 cameraPosLightspace = worldToLightspace.MultiplyPoint(cameraPos);

        //Transform these points into light space to encapsulate them
        var lightViewSpaceCorners = new Vector3[8];
        for (int i = 0; i < 8; i++)
        {
            lightViewSpaceCorners[i] = worldToLightspace.MultiplyPoint(worldFrustumCorners[i]);
        }

        //Clip these points to the max distance on the X and Y axis
        for (int i = 0; i < 8; i++)
        {
           //lightViewSpaceCorners[i].x = Mathf.Clamp(lightViewSpaceCorners[i].x, cameraPosLightspace.x- maxDistance, cameraPosLightspace.x+maxDistance);
           //lightViewSpaceCorners[i].y = Mathf.Clamp(lightViewSpaceCorners[i].y, cameraPosLightspace.y - maxDistance, cameraPosLightspace.y+maxDistance);
         // lightViewSpaceCorners[i].z = Mathf.Clamp(lightViewSpaceCorners[i].z, cameraPosLightspace.z - maxZ, cameraPosLightspace.z +maxZ);
        }

        //Encapsulate the frustrum as seen from the light
        var frustumBounds = new Bounds(lightViewSpaceCorners[0], Vector3.zero);
        for (int i = 1; i < lightViewSpaceCorners.Length; i++)
        {
            frustumBounds.Encapsulate(lightViewSpaceCorners[i]);
        }

        //Add the corners to make the size always the same on xy
        // frustumBounds.Encapsulate(new Vector3(-maxDistance, -maxDistance, -maxDistance));
        //frustumBounds.Encapsulate(new Vector3(maxDistance, maxDistance, maxDistance));

        //Push the min up to capture more shadowcasters
        //Vector3 highestShadowWorldPoint = camera.transform.position + new Vector3(0, 50, 0);
        //frustumBounds.Encapsulate(worldToLightspace.MultiplyPoint(highestShadowWorldPoint));
        //Vector3 lowestShadowWorldPoint = camera.transform.position + new Vector3(0, -50, 0);
        //frustumBounds.Encapsulate(worldToLightspace.MultiplyPoint(lowestShadowWorldPoint));
        frustumBounds.min = new Vector3(frustumBounds.min.x, frustumBounds.min.y, cameraPosLightspace.z - maxZ);
        frustumBounds.max = new Vector3(frustumBounds.max.x, frustumBounds.max.y, cameraPosLightspace.z + maxZ);


        //Take the center
        Vector3 center = frustumBounds.center;
        //create a new bounds with fixed x and y size
        frustumBounds = new Bounds(center, new Vector3(maxDistance * 2, maxDistance * 2, maxZ * 2));


        //calculate worldTexelsize
        Vector3 worldTexelSize = new Vector3(frustumBounds.size.x / shadowWidth, frustumBounds.size.y/ shadowHeight, 1);
          
        //Snap the x and y mins to worldTexelSize 
        Vector3 min = frustumBounds.min;
        min.x = Mathf.Floor(min.x / worldTexelSize.x) * worldTexelSize.x;
        min.y = Mathf.Floor(min.y / worldTexelSize.y) * worldTexelSize.y;
        frustumBounds.min = min;
        //maxes
        Vector3 max = frustumBounds.max;
        max.x = Mathf.Floor(max.x / worldTexelSize.x) * worldTexelSize.x;
        max.y = Mathf.Floor(max.y / worldTexelSize.y) * worldTexelSize.y;
        frustumBounds.max = max;

        return frustumBounds;
    }

    private void CalculateFrustumCorners(Camera camera, float distance, Vector3[] corners, int startIndex = 0)
    {
        // Top left
        corners[startIndex] = camera.ViewportToWorldPoint(new Vector3(0, 1, distance));
        // Top right
        corners[startIndex + 1] = camera.ViewportToWorldPoint(new Vector3(1, 1, distance));
        // Bottom left
        corners[startIndex + 2] = camera.ViewportToWorldPoint(new Vector3(0, 0, distance));
        // Bottom right
        corners[startIndex + 3] = camera.ViewportToWorldPoint(new Vector3(1, 0, distance));
    }

    Camera[] CleanupShadowmapCameras()
    {
        var sceneCameras = GameObject.FindObjectsOfType<Camera>();

        //Sanitize the scene, we only want one of these.
        foreach (var camera in sceneCameras)
        {
            //See if it exists in the shadowmapCamera array
            bool found = false;
            foreach (var validCamera in shadowMapCamera)
            {
                if (validCamera == camera)
                {
                    found = true;
                    break;
                }
            }
            if (found)
            {
                continue;
            }
            

           
            if (camera.name == "Shadowmap Camera"  || camera.name == "Shadowmap Camera 0" || camera.name == "Shadowmap Camera 1")
            {
                if (Application.isPlaying == true)
                {
                    GameObject.Destroy(camera.gameObject);
                }
                else
                {
                    GameObject.DestroyImmediate(camera.gameObject);
                }
            }
        }
        return sceneCameras;
    }

    VoxelWorld GetVoxelWorld()
    {
        return GameObject.FindObjectOfType<VoxelWorld>();
    }


    void RenderShadowmap(Camera mainCamera, ScriptableRenderContext context, CommandBuffer commandBuffer, int index)
    {
        // commandBuffer.BeginSample("Shadowmaps");

        int renderTargetId = 0;
        if (index == 0)
        {
            renderTargetId = globalShadowTexture0Id;
        }
        if (index == 1)
        {
            renderTargetId = globalShadowTexture1Id;
        }
        

        //Generate a shadowmap rendertarget
        if (true)
        {
            
            RenderTextureDescriptor shadowTextureDesc = new RenderTextureDescriptor();
            shadowTextureDesc.autoGenerateMips = false;
            shadowTextureDesc.colorFormat = RenderTextureFormat.RFloat;
            shadowTextureDesc.msaaSamples = 1;
            shadowTextureDesc.sRGB = false;
            shadowTextureDesc.useMipMap = false;
            shadowTextureDesc.width = shadowWidth;
            shadowTextureDesc.height = shadowHeight;
            shadowTextureDesc.enableRandomWrite = false;
            shadowTextureDesc.volumeDepth = 1;
            shadowTextureDesc.depthBufferBits = 24;
            shadowTextureDesc.dimension = TextureDimension.Tex2D;

            cameraCmdBuffer.GetTemporaryRT(renderTargetId, shadowTextureDesc, FilterMode.Point);
            //cameraCmdBuffer.GetTemporaryRT(globalShadowTextureId,shadowWidth, shadowHeight,32,FilterMode.Point, RenderTextureFormat.Depth);

        }

        if (depthMaterial == null)
        {
            Shader depthShader = Shader.Find("Chronos/DepthToTexture");
            depthMaterial = new Material(depthShader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        //Generate a camera that will be used to render the shadowmap

        //Check to see if "Shadowmap camera" already exists, and use that
        if (shadowMapCamera[index] == null)
        {

            // Get all root objects in the scene
            shadowMapCameraObject[index] = GameObject.Find("Shadowmap Camera " + index);
            if (shadowMapCameraObject[index])
            {
                shadowMapCamera[index] = shadowMapCameraObject[index].GetComponent<Camera>();
            }

            if (shadowMapCamera[index] == null)
            {
                //Debug.Log("Didn't find shadowmap camera " + index);
            }
        } 
         
        //create it?
        if (shadowMapCamera[index] == null)
        {
            //Debug.Log("Creating shadowmap Camera");
            shadowMapCameraObject[index] = new GameObject("Shadowmap Camera "+ index);
            shadowMapCamera[index] = shadowMapCameraObject[index].AddComponent<Camera>();
            shadowMapCamera[index].gameObject.hideFlags = HideFlags.HideAndDontSave;

            //Move to destroyOnLoad
            //GameObject.DontDestroyOnLoad(shadowMapCameraObject);
        }

        //Set camera to orthagonal, and a size of 200
        Camera shadowCamera = shadowMapCamera[index];
        shadowCamera.orthographic = true;
     
        //Set the camera to render only to the depth buffer
        shadowCamera.clearFlags = 0;
        shadowCamera.depthTextureMode = DepthTextureMode.None;
        shadowCamera.renderingPath = RenderingPath.Forward;
        shadowCamera.allowMSAA = false;
        shadowCamera.allowHDR = false;
        shadowCamera.useOcclusionCulling = false;
        shadowCamera.cullingMask = -1;
        shadowCamera.aspect = 1;
        

        // Align shadowmap camera to sunlight direction
        Vector3 sunDir = Vector3.down;

        //If theres a voxel world grab that
        VoxelWorld world = GetVoxelWorld();
        if (world != null)
        {
            sunDir = world.globalSunDirection;
        }
        sunDir = Vector3.Normalize(sunDir);

        
        // Position the shadowmap camera to cover the main camera's frustum
        float maxDistance = cascadeSize[index]; // Set this to the number of units you want to capture

        shadowCamera.transform.position = Vector3.zero;

        if (Math.Abs(Vector3.Dot(Vector3.up, sunDir)) > 0.95f)
        {
            shadowCamera.transform.rotation = Quaternion.LookRotation(sunDir, Vector3.right);
        }
        else
        {
            shadowCamera.transform.rotation = Quaternion.LookRotation(sunDir, Vector3.up);
        }
 
        Matrix4x4 lightToWorld = shadowCamera.transform.localToWorldMatrix;
        Matrix4x4 worldToLight = shadowCamera.transform.worldToLocalMatrix;
              
   
        //Build a lightspace box covering what we want, enforced to be maxDistance
        Bounds frustumBoundsLightspace = CalculateFrustumBounds(mainCamera, worldToLight, maxDistance);
        Vector3 snappedLightspaceFrustumCenter = frustumBoundsLightspace.center;

        Vector3 frustumCenter = lightToWorld.MultiplyPoint(snappedLightspaceFrustumCenter);
        shadowCamera.transform.position = frustumCenter;

        // shadowCamera clipping planes to cover the bounds returned
        shadowCamera.nearClipPlane = -frustumBoundsLightspace.size.z / 2;
        shadowCamera.farClipPlane = frustumBoundsLightspace.size.z / 2;

        //This should always just be maxDistance, but we'll calculate it to be sure
        shadowCamera.orthographicSize = Mathf.Max(frustumBoundsLightspace.size.x, frustumBoundsLightspace.size.y) / 2;
        //Debug.Log("Size" + shadowMapCamera.orthographicSize);

        //We want to be able to turn shadow casting off on certain objects
        //Because we cant filter for this directly, we need to move stuff to a different renderFilterLayer
        MeshRenderer[] meshRenderers = GameObject.FindObjectsOfType<MeshRenderer>();
        meshRenderersToReenable.Clear();
        foreach (MeshRenderer renderer in meshRenderers)
        {
            if (renderer.shadowCastingMode == ShadowCastingMode.Off)
            {
                meshRenderersToReenable.Add(new MeshRendererDesc(renderer, renderer.renderingLayerMask));
                renderer.renderingLayerMask = 1 << 15;
            }
        }

        //Cull it
        shadowCamera.TryGetCullingParameters(out var cullingParameters);
        cullingParameters.cullingOptions = CullingOptions.ShadowCasters;
        
        CullingResults cullingResults = context.Cull(ref cullingParameters);
        
        context.SetupCameraProperties(shadowCamera);
        
        commandBuffer.SetRenderTarget(renderTargetId);
        commandBuffer.ClearRenderTarget(
                          true,
                          true,
                          Color.black
                      );

        //execute clear
        context.ExecuteCommandBuffer(commandBuffer);
        commandBuffer.Clear();

        // Tell Unity which geometry to draw, based on its LightMode Pass tag value
        ShaderTagId shaderTagId = new("ChronosForwardPass");

        // Tell Unity how to sort the geometry, based on the current Camera
        var sortingSettings = new SortingSettings(shadowCamera)
        {
            criteria = SortingCriteria.CommonOpaque // Enable front-to-back sorting
        };

        // Create a DrawingSettings struct that describes which geometry to draw and how to draw it
        DrawingSettings opaqueDrawingSettings = new(shaderTagId, sortingSettings);
        opaqueDrawingSettings.overrideMaterial = depthMaterial;

        //Draw in opaque stuff
        FilteringSettings filteringSettings = FilteringSettings.defaultValue;
        filteringSettings.renderQueueRange = RenderQueueRange.opaque;

        //Everything except the 15th bit
        filteringSettings.renderingLayerMask = (uint)(uint.MaxValue & (~(1 << 15)));
        context.DrawRenderers(cullingResults, ref opaqueDrawingSettings, ref filteringSettings);

        //Set the globals with our shiny new texture  projectionMatrix * viewMatrix;
 
        Matrix4x4 shadowMatrix = shadowCamera.projectionMatrix * shadowCamera.worldToCameraMatrix;
        Shader.SetGlobalMatrix("_ShadowmapMatrix"+index, shadowMatrix);

        /*  Matrix4x4 posToUV = new Matrix4x4();
          posToUV.SetRow(0, new Vector4(0.5f, 0, 0, 0.5f));
          posToUV.SetRow(1, new Vector4(0, 0.5f, 0, 0.5f));
          posToUV.SetRow(2, new Vector4(0, 0, 1, 0));
          posToUV.SetRow(3, new Vector4(0, 0, 0, 1));
          Matrix4x4 projectionMatrix = GL.GetGPUProjectionMatrix(shadowMapCamera.projectionMatrix, false);
          Matrix4x4 matrixVP = posToUV * projectionMatrix * shadowMapCamera.worldToCameraMatrix;
          Shader.SetGlobalMatrix("_ShadowmapMatrix", matrixVP);*/


        //Put the filters back
        for (int i = 0; i < meshRenderersToReenable.Count; i++)
        {
            meshRenderersToReenable[i].renderer.renderingLayerMask = meshRenderersToReenable[i].filter;
        }
        
        // commandBuffer.EndSample("Shadowmaps");
    }

    private void SetupGlobalLightingPropertiesForRendering()
    {
        float sunBrightness = 1.0f;
         
        Vector3 sunDirection= new Vector3(0, -1, 0);
        Color sunColor = Color.white;

        float ambientBrightness = 1.0f;
        Color ambientTint = Color.white;
        float ambientOcclusion = 1;

        float fogStart = 40;
        float fogEnd = 500;
        Color fogColor = Color.white;

        float skySaturation = 1;

        Cubemap cubeMap = null;


        //Fetch them from voxelworld if that exists
        VoxelWorld world = GameObject.FindObjectOfType<VoxelWorld>();
        if (world)
        {
            sunBrightness = world.globalSunBrightness;
            sunDirection = world.globalSunDirectionNormalized;
            sunColor = world.globalSunColor;
            ambientBrightness = world.globalAmbientBrightness;
            ambientTint = world.globalAmbientLight;
            ambientOcclusion = world.globalAmbientOcclusion;
            skySaturation = world.globalSkySaturation;
            cubeMap = world.cubeMap;

            fogStart = world.globalFogStart;
            fogEnd = world.globalFogEnd;
            fogColor = world.globalFogColor;
        }
        
        Shader.SetGlobalFloat("globalSunBrightness", sunBrightness);
        Shader.SetGlobalFloat("globalAmbientBrightness", ambientBrightness);
        Shader.SetGlobalVector("globalSunDirection", sunDirection);  
        Shader.SetGlobalVector("globalSunColor", sunColor * sunBrightness);
        Shader.SetGlobalFloat("globalAmbientOcclusion", ambientOcclusion);
        //Set fogs
        Shader.SetGlobalFloat("globalFogStart", fogStart);
        Shader.SetGlobalFloat("globalFogEnd", fogEnd);
        Shader.SetGlobalColor("globalFogColor", fogColor);

        Shader.SetGlobalTexture("_CubeTex", cubeMap);

        if (cubeMap == null)
        {
            if (shAmbientData == null)
            {
                shAmbientData = new Vector4[9];
            }
            
            //Add a bunch of cool random lights for ambient
            UnityEngine.Rendering.SphericalHarmonicsL2 ambientSH = new UnityEngine.Rendering.SphericalHarmonicsL2();
            Color blueish = new Color(0.9f, 0.9f, 1.0f);

            //Opposites!
            ambientSH.AddDirectionalLight(Vector3.down, Color.white * 0.75f, 1);
            ambientSH.AddDirectionalLight(Vector3.up, Color.white * 1.5f, 1); //downward light
            ambientSH.AddDirectionalLight(Vector3.left, Color.white * 1f, 1);
            ambientSH.AddDirectionalLight(Vector3.right, Color.white * 1f, 1);
            ambientSH.AddDirectionalLight(Vector3.forward, Color.white * 1.5f, 1);
            ambientSH.AddDirectionalLight(Vector3.back, Color.white * 1.5f, 1);

            //ambientSH.AddAmbientLight(Color.white);
            //pack it in
            for (int j = 0; j < 9; j++)
            {
                shAmbientData[j] = new Vector4(ambientSH[0, j], ambientSH[1, j], ambientSH[2, j], 0);
            }
        }
        else
        {  
            if (shAmbientData == null)
            {
                shAmbientData = new Vector4[9];
            }
            for (int j = 0; j < 9; j++)
            {
                shAmbientData[j] = new Vector4(world.cubeMapSHData[j].x, world.cubeMapSHData[j].y, world.cubeMapSHData[j].z, 0);
            }
        }

        //Adjust saturation
        for (int j = 0; j < 9; j++)
        {
            Color input = new Color(shAmbientData[j].x, shAmbientData[j].y, shAmbientData[j].z);
            Color.RGBToHSV(input, out float h, out float s, out float v);
            s *= skySaturation;
            Color res = Color.HSVToRGB(h, s, v);
            shAmbientData[j] = new Vector4(res.r, res.g, res.b, 0);

        }

        Shader.SetGlobalVectorArray("globalAmbientLight", shAmbientData);
        Shader.SetGlobalColor("globalAmbientTint", ambientTint * ambientBrightness);
    }

}