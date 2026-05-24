using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using Unity.Mathematics;
using System.Collections.Generic;
using UnityEngine.Experimental.Rendering;

public class CloudRenderPass : ScriptableRenderPass
{
    private ComputeShader _shader;
    private ComputeShader UpscaleShader;
    public int lightningCount;
    public Bounds Bounds;
    public Vector3 CloudWorldMotion;
    private int _raymarchKernel;
    private int _upscaleKernel;
    public RenderTexture ShapeRenderTexture;
    public RenderTexture DetailRenderTexture;
    public Texture2D BlueNoiseTexture;

    private RenderTexture _quarterCloudColor;
    private RenderTexture _quarterCloudAlpha;
    private RenderTexture _quarterDepthArray;
    private RenderTexture[] _fullCloudColors = new RenderTexture[2];
    private RenderTexture[] _fullCloudAlphas = new RenderTexture[2];

    // RTHandles for RenderGraph import
    private RTHandle _quarterCloudColorHandle;
    private RTHandle _quarterCloudAlphaHandle;
    private RTHandle _quarterDepthArrayHandle;
    private RTHandle[] _fullCloudColorHandles = new RTHandle[2];
    private RTHandle[] _fullCloudAlphaHandles = new RTHandle[2];

    // Single ping-pong index (SPI = one RecordRenderGraph call for both eyes)
    private int _currentBuffer = 0;

    public int BigDivider = 4;

    // Per-eye matrix/vector arrays reused every frame to avoid GC allocations.
    // _viewProjPing ping-pongs so the previous frame's VP survives without
    // dropping the old array to the GC each frame.
    private Matrix4x4[][] _viewProjPing = new Matrix4x4[2][];
    private Matrix4x4[] _invViewProj;
    private Matrix4x4[] _camToWorld;
    private Matrix4x4[] _invGpuProj;
    private Vector4[] _camPos;
    private int _perEyeArraysEyeCount = -1;

    private RTHandle _blueNoiseHandle;
    private static readonly int _flipY = SystemInfo.graphicsUVStartsAtTop ? 1 : 0;

    private bool _xrKeywordsApplied;
    private bool _xrKeywordsInitialized;

    private void EnsurePerEyeArrays(int eyeCount)
    {
        if (_perEyeArraysEyeCount == eyeCount) return;
        _viewProjPing[0] = new Matrix4x4[eyeCount];
        _viewProjPing[1] = new Matrix4x4[eyeCount];
        _invViewProj = new Matrix4x4[eyeCount];
        _camToWorld = new Matrix4x4[eyeCount];
        _invGpuProj = new Matrix4x4[eyeCount];
        _camPos = new Vector4[eyeCount];
        _perEyeArraysEyeCount = eyeCount;
    }

    private void EnsureBuffers(int fullWidth, int fullHeight, int eyeCount)
    {
        if (_blueNoiseHandle == null)
            _blueNoiseHandle = RTHandles.Alloc(BlueNoiseTexture);

        int qWidth = Mathf.CeilToInt(fullWidth / BigDivider);
        int qHeight = Mathf.CeilToInt(fullHeight / BigDivider);

        void EnsureArray(ref RenderTexture rt, ref RTHandle handle, int w, int h, RenderTextureFormat fmt, int eyeCount)
        {
            if (rt != null && rt.width == w && rt.height == h && rt.volumeDepth == eyeCount) return;
            handle?.Release();
            rt?.Release();
            Debug.Log($"Creating RT {w}x{h}x{eyeCount} fmt {fmt}");
            rt = new RenderTexture(w, h, 0, fmt)
            {
                dimension = TextureDimension.Tex2DArray,
                volumeDepth = eyeCount,
                enableRandomWrite = true,
                useMipMap = false
            };
            rt.Create();
            handle = RTHandles.Alloc(rt);
        }

        EnsureArray(ref _quarterCloudColor, ref _quarterCloudColorHandle, qWidth, qHeight, RenderTextureFormat.RGB111110Float, eyeCount);
        EnsureArray(ref _quarterCloudAlpha, ref _quarterCloudAlphaHandle, qWidth, qHeight, RenderTextureFormat.RHalf, eyeCount);
        EnsureArray(ref _quarterDepthArray, ref _quarterDepthArrayHandle, qWidth, qHeight, RenderTextureFormat.RHalf, eyeCount);
        EnsureArray(ref _fullCloudColors[0], ref _fullCloudColorHandles[0], fullWidth, fullHeight, RenderTextureFormat.RGB111110Float, eyeCount);
        EnsureArray(ref _fullCloudColors[1], ref _fullCloudColorHandles[1], fullWidth, fullHeight, RenderTextureFormat.RGB111110Float, eyeCount);
        EnsureArray(ref _fullCloudAlphas[0], ref _fullCloudAlphaHandles[0], fullWidth, fullHeight, RenderTextureFormat.RHalf, eyeCount);
        EnsureArray(ref _fullCloudAlphas[1], ref _fullCloudAlphaHandles[1], fullWidth, fullHeight, RenderTextureFormat.RHalf, eyeCount);
    }

    private ComputeBuffer _settingsBuffer;
    private GraphicsBuffer _LightningBuffer;
    private readonly CloudSettings[] _settingsScratch = new CloudSettings[1];

    public void UpdateLightning(List<Lightning> Lightnings)
    {
        if (lightningCount != Lightnings.Count)
        {
            _LightningBuffer?.Release();
            _LightningBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                Lightnings.Count,
                System.Runtime.InteropServices.Marshal.SizeOf<Lightning>());
            lightningCount = Lightnings.Count;
            _LightningBuffer.SetData(Lightnings);
        }
    }

    public void UpdateSettings(CloudSettings settings)
    {
        if (_settingsBuffer == null)
            _settingsBuffer = new ComputeBuffer(1,
                System.Runtime.InteropServices.Marshal.SizeOf<CloudSettings>());

        _settingsScratch[0] = settings;
        _settingsBuffer.SetData(_settingsScratch);
    }

    public CloudRenderPass(ComputeShader shader, ComputeShader upscaleShader, ComputeShader mergeShader)
    {
        _shader = shader;
        UpscaleShader = upscaleShader;
        _raymarchKernel = shader.FindKernel("CloudRaymarch");
        _upscaleKernel = upscaleShader.FindKernel("TemporalUpscaleAndMerge");
        //Make sure to render AFTER cirrus clouds
        renderPassEvent = RenderPassEvent.AfterRenderingSkybox + 1;
    }

    private class PassData
    {
        public ComputeShader shader;
        public ComputeShader upscaleShader;
        public int raymarchKernel;
        public int upscaleKernel;
        public Bounds bounds;
        public TextureHandle src;
        public TextureHandle dst;
        public TextureHandle quarterCloudColor;
        public TextureHandle quarterCloudAlpha;
        public TextureHandle quarterDepthBuffer;
        public TextureHandle depthBuffer;
        public TextureHandle blueNoiseHandle;
        public Vector3 SunPos;
        public ComputeBuffer settingsBuffer;
        public GraphicsBuffer lightningBuffer;
        public int fullWidth, fullHeight;
        public int quarterWidth, quarterHeight;
        public TextureHandle historyColor;
        public TextureHandle historyAlpha;
        public TextureHandle fullCloudColor;
        public TextureHandle fullCloudAlpha;
        public Matrix4x4[] prevViewProj;
        public Matrix4x4[] currViewProj;
        public Matrix4x4[] currInvViewProj;
        public Matrix4x4[] cameraToWorld;
        public Matrix4x4[] cameraInverseProjection;
        public Vector4[] cameraPosPerEye;
        public Vector3 cloudWorldMotion;
        public int flipY;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        _shader.SetTexture(_raymarchKernel, "ShapeTexture", ShapeRenderTexture);
        _shader.SetTexture(_raymarchKernel, "DetailTexture", DetailRenderTexture);

        var resourceData = frameData.Get<UniversalResourceData>();
        var cameraData = frameData.Get<UniversalCameraData>();

        if (cameraData.camera.cameraType != CameraType.Game) return;

        var cam = cameraData.camera;
        bool isXR = cameraData.xr.enabled;

        int eyeCount = isXR ? 2 : 1;

        //Fix last frame error
        if (!_xrKeywordsInitialized || isXR != _xrKeywordsApplied)
        {
            if (isXR)
            {
                _shader.EnableKeyword("RENDERTARGET_XR");
                UpscaleShader.EnableKeyword("RENDERTARGET_XR");
            }
            else
            {
                _shader.DisableKeyword("RENDERTARGET_XR");
                UpscaleShader.DisableKeyword("RENDERTARGET_XR");
            }
            _xrKeywordsApplied = isXR;
            _xrKeywordsInitialized = true;
        }

        int fullWidth = cameraData.cameraTargetDescriptor.width;
        int fullHeight = cameraData.cameraTargetDescriptor.height;
        int qWidth = Mathf.CeilToInt(fullWidth / BigDivider);
        int qHeight = Mathf.CeilToInt(fullHeight / BigDivider);

        EnsureBuffers(fullWidth, fullHeight, eyeCount);
        EnsurePerEyeArrays(eyeCount);

        int prevPing = _currentBuffer;
        int currPing = 1 - prevPing;
        _currentBuffer = currPing;

        var currViewProj = _viewProjPing[currPing];
        for (int eye = 0; eye < eyeCount; eye++)
        {
            var stereoEye = eye == 0 ? Camera.StereoscopicEye.Left : Camera.StereoscopicEye.Right;

            Matrix4x4 view = isXR ? cam.GetStereoViewMatrix(stereoEye) : cam.worldToCameraMatrix;
            Matrix4x4 proj = isXR ? cam.GetStereoProjectionMatrix(stereoEye) : cam.projectionMatrix;
            Matrix4x4 gpuProj = GL.GetGPUProjectionMatrix(proj, true);

            Matrix4x4 vp = gpuProj * view;
            Matrix4x4 viewInv = view.inverse;
            currViewProj[eye] = vp;
            _invViewProj[eye] = vp.inverse;
            _camToWorld[eye] = viewInv;
            _invGpuProj[eye] = gpuProj.inverse;
            _camPos[eye] = viewInv.GetColumn(3);
        }

        TextureDesc desc = new TextureDesc(fullWidth, fullHeight)
        {
            colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
            enableRandomWrite = true,
            name = "CloudOutput",
            clearBuffer = false,
            dimension = eyeCount == 2 ? TextureDimension.Tex2DArray : TextureDimension.Tex2D,
            slices = eyeCount
        };
        TextureHandle dst = renderGraph.CreateTexture(desc);

        using (var builder = renderGraph.AddComputePass<PassData>("Cloud Raymarch + Upscale + Merge", out var data))
        {
            data.shader = _shader;
            data.upscaleShader = UpscaleShader;
            data.raymarchKernel = _raymarchKernel;
            data.upscaleKernel = _upscaleKernel;
            data.bounds = Bounds;
            data.cloudWorldMotion = CloudWorldMotion;
            data.flipY = _flipY;
            data.fullWidth = fullWidth;
            data.fullHeight = fullHeight;
            data.quarterWidth = qWidth;
            data.quarterHeight = qHeight;
            data.settingsBuffer = _settingsBuffer;
            data.lightningBuffer = _LightningBuffer;

            data.cameraToWorld = _camToWorld;
            data.cameraInverseProjection = _invGpuProj;
            data.cameraPosPerEye = _camPos;
            data.prevViewProj = _viewProjPing[prevPing];
            data.currViewProj = _viewProjPing[currPing];
            data.currInvViewProj = _invViewProj;

            data.blueNoiseHandle = renderGraph.ImportTexture(_blueNoiseHandle);
            data.quarterCloudColor = renderGraph.ImportTexture(_quarterCloudColorHandle);
            data.quarterCloudAlpha = renderGraph.ImportTexture(_quarterCloudAlphaHandle);
            data.quarterDepthBuffer = renderGraph.ImportTexture(_quarterDepthArrayHandle);
            data.historyColor = renderGraph.ImportTexture(_fullCloudColorHandles[prevPing]);
            data.historyAlpha = renderGraph.ImportTexture(_fullCloudAlphaHandles[prevPing]);
            data.fullCloudColor = renderGraph.ImportTexture(_fullCloudColorHandles[currPing]);
            data.fullCloudAlpha = renderGraph.ImportTexture(_fullCloudAlphaHandles[currPing]);

            data.depthBuffer = resourceData.activeDepthTexture;
            data.src = resourceData.cameraColor;
            data.dst = dst;

            builder.AllowPassCulling(false);
            builder.UseTexture(data.blueNoiseHandle, AccessFlags.Read);
            builder.UseTexture(data.src, AccessFlags.Read);
            builder.UseTexture(data.dst, AccessFlags.WriteAll);
            builder.UseTexture(data.depthBuffer, AccessFlags.Read);
            builder.UseTexture(data.quarterCloudColor, AccessFlags.ReadWrite);
            builder.UseTexture(data.quarterCloudAlpha, AccessFlags.ReadWrite);
            builder.UseTexture(data.quarterDepthBuffer, AccessFlags.ReadWrite);
            builder.UseTexture(data.historyColor, AccessFlags.Read);
            builder.UseTexture(data.historyAlpha, AccessFlags.Read);
            builder.UseTexture(data.fullCloudColor, AccessFlags.WriteAll);
            builder.UseTexture(data.fullCloudAlpha, AccessFlags.WriteAll);

            builder.SetRenderFunc((PassData d, ComputeGraphContext ctx) =>
            {
                var cmd = ctx.cmd;

                //////////////////////////////////////RAYMARCH/////////////////////////////////////////////
                cmd.SetComputeMatrixArrayParam(d.shader, "_CameraToWorldPerEye", d.cameraToWorld);
                cmd.SetComputeMatrixArrayParam(d.shader, "_CameraInvProjPerEye", d.cameraInverseProjection);
                cmd.SetComputeMatrixArrayParam(d.shader, "_CurrViewProj", d.currViewProj);
                cmd.SetComputeIntParam(d.shader, "_FlipY", d.flipY);
                cmd.SetComputeIntParam(d.shader, "_FrameIndex", Time.frameCount);
                cmd.SetComputeIntParam(d.shader, "_BigDivider", BigDivider);
                cmd.SetComputeVectorParam(d.shader, "_Resolution", new Vector2(d.quarterWidth, d.quarterHeight));
                cmd.SetComputeVectorParam(d.shader, "_FullResolution", new Vector2(d.fullWidth, d.fullHeight));
                cmd.SetComputeVectorParam(d.shader, "_BoundsMin", d.bounds.min);
                cmd.SetComputeVectorParam(d.shader, "_BoundsMax", d.bounds.max);
                cmd.SetComputeVectorParam(d.shader, "SunPostion", d.SunPos);
                cmd.SetComputeTextureParam(d.shader, d.raymarchKernel, "_CloudColorBuffer", d.quarterCloudColor);
                cmd.SetComputeTextureParam(d.shader, d.raymarchKernel, "_CloudAlphaBuffer", d.quarterCloudAlpha);
                cmd.SetComputeTextureParam(d.shader, d.raymarchKernel, "BlueNoise", d.blueNoiseHandle);
                cmd.SetComputeTextureParam(d.shader, d.raymarchKernel, "_DepthTex", d.depthBuffer);
                cmd.SetComputeTextureParam(d.shader, d.raymarchKernel, "_CloudDepthTex", d.quarterDepthBuffer);
                cmd.SetComputeConstantBufferParam(d.shader, "_CloudSettings", d.settingsBuffer,
                    0, System.Runtime.InteropServices.Marshal.SizeOf<CloudSettings>());
                cmd.SetComputeBufferParam(d.shader, d.raymarchKernel, "Lightnings", d.lightningBuffer);
                cmd.SetComputeIntParam(d.shader, "_LightningCount", lightningCount);

                int qGroupsX = Mathf.CeilToInt(d.quarterWidth / 8f);
                int qGroupsY = Mathf.CeilToInt(d.quarterHeight / 8f);
                cmd.DispatchCompute(d.shader, d.raymarchKernel, qGroupsX, qGroupsY, eyeCount);

                //////////// Upscale and Merge////////////
                cmd.SetComputeMatrixArrayParam(d.upscaleShader, "_CameraToWorldPerEye", d.cameraToWorld);
                cmd.SetComputeMatrixArrayParam(d.upscaleShader, "_CameraInvProjPerEye", d.cameraInverseProjection);
                cmd.SetComputeMatrixArrayParam(d.upscaleShader, "_PrevViewProj", d.prevViewProj);
                cmd.SetComputeVectorArrayParam(d.upscaleShader, "_CameraPos", d.cameraPosPerEye);
                cmd.SetComputeIntParam(d.upscaleShader, "_BigDivider", BigDivider);
                cmd.SetComputeIntParam(d.upscaleShader, "_FrameIndex", Time.frameCount);
                cmd.SetComputeIntParam(d.upscaleShader, "_FlipY", d.flipY);
                cmd.SetComputeVectorParam(d.upscaleShader, "_QuarterResolution", new Vector2(d.quarterWidth, d.quarterHeight));
                cmd.SetComputeVectorParam(d.upscaleShader, "_Resolution", new Vector2(d.fullWidth, d.fullHeight));
                cmd.SetComputeVectorParam(d.upscaleShader, "_CloudWorldMotion", d.cloudWorldMotion);
                cmd.SetComputeTextureParam(d.upscaleShader, d.upscaleKernel, "_HistoryColor", d.historyColor);
                cmd.SetComputeTextureParam(d.upscaleShader, d.upscaleKernel, "_HistoryAlpha", d.historyAlpha);
                cmd.SetComputeTextureParam(d.upscaleShader, d.upscaleKernel, "_CloudColorBuffer", d.fullCloudColor);
                cmd.SetComputeTextureParam(d.upscaleShader, d.upscaleKernel, "_CloudAlphaBuffer", d.fullCloudAlpha);
                cmd.SetComputeTextureParam(d.upscaleShader, d.upscaleKernel, "_QuarterCloudColor", d.quarterCloudColor);
                cmd.SetComputeTextureParam(d.upscaleShader, d.upscaleKernel, "_QuarterCloudAlpha", d.quarterCloudAlpha);
                cmd.SetComputeTextureParam(d.upscaleShader, d.upscaleKernel, "_CloudDepthTex", d.quarterDepthBuffer);
                cmd.SetComputeTextureParam(d.upscaleShader, d.upscaleKernel, "_SrcTex", d.src);
                cmd.SetComputeTextureParam(d.upscaleShader, d.upscaleKernel, "_DepthTex", d.depthBuffer);
                cmd.SetComputeTextureParam(d.upscaleShader, d.upscaleKernel, "_OutputTex", d.dst);

                int groupsX = Mathf.CeilToInt(d.fullWidth / 8f);
                int groupsY = Mathf.CeilToInt(d.fullHeight / 8f);
                cmd.DispatchCompute(d.upscaleShader, d.upscaleKernel, groupsX, groupsY, eyeCount);
            });
        }

        using (var builder = renderGraph.AddRasterRenderPass<PassData>("Cloud Blit Back", out var blitData))
        {
            blitData.src = dst;
            builder.UseTexture(blitData.src);
            builder.SetRenderAttachment(resourceData.cameraColor, 0, AccessFlags.WriteAll);
            builder.SetRenderFunc((PassData d, RasterGraphContext ctx) =>
            {
                Blitter.BlitTexture(ctx.cmd, d.src, new Vector4(1, 1, 0, 0), 0, false);
            });
        }
    }

    public void Dispose()
    {
        _quarterCloudColorHandle?.Release(); _quarterCloudColor?.Release();
        _quarterCloudAlphaHandle?.Release(); _quarterCloudAlpha?.Release();
        _quarterDepthArrayHandle?.Release(); _quarterDepthArray?.Release();
        for (int i = 0; i < 2; i++)
        {
            _fullCloudColorHandles[i]?.Release();
            _fullCloudColors[i]?.Release();
            _fullCloudAlphaHandles[i]?.Release();
            _fullCloudAlphas[i]?.Release();
        }
        _blueNoiseHandle?.Release();
        _settingsBuffer?.Release();
        _LightningBuffer?.Release();
        DetailRenderTexture?.Release();
        ShapeRenderTexture?.Release();
    }
}