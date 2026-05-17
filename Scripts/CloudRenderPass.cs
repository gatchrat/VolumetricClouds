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
    private ComputeShader MergeShader;
    public int lightningCount;
    public Bounds Bounds;
    public Vector3 CloudWorldMotion;
    private int _raymarchKernel;
    private int _upscaleKernel;
    private int _mergeKernel;
    public RenderTexture ShapeRenderTexture;
    public RenderTexture DetailRenderTexture;
    public Texture2D BlueNoiseTexture;
    private CloudSettings _lastSettings;

    // ── SPI array textures (depth=2, one slice per eye) ───────────────────
    private RenderTexture _quarterCloudArray;
    private RenderTexture _quarterDepthArray;
    private RenderTexture[] _fullCloudArrays = new RenderTexture[2]; // ping-pong

    // RTHandles for RenderGraph import
    private RTHandle _quarterCloudArrayHandle;
    private RTHandle _quarterDepthArrayHandle;
    private RenderTexture _mergeOutputArray;
    private RTHandle _mergeOutputArrayHandle;
    private RTHandle[] _fullCloudArrayHandles = new RTHandle[2];

    // Single ping-pong index (SPI = one RecordRenderGraph call for both eyes)
    private int _currentBuffer = 0;

    public int BigDivider = 4;

    // Both eyes' previous VP matrices stored each frame
    private Matrix4x4[] _prevViewProj = new Matrix4x4[2];

    private RTHandle _blueNoiseHandle;
    private static readonly int _flipY = SystemInfo.graphicsUVStartsAtTop ? 1 : 0;

    private void EnsureBuffers(int fullWidth, int fullHeight)
    {
        if (_blueNoiseHandle == null)
            _blueNoiseHandle = RTHandles.Alloc(BlueNoiseTexture);

        int qWidth = Mathf.CeilToInt(fullWidth / BigDivider);
        int qHeight = Mathf.CeilToInt(fullHeight / BigDivider);

        void EnsureArray(ref RenderTexture rt, ref RTHandle handle, int w, int h, RenderTextureFormat fmt)
        {
            if (rt != null && rt.width == w && rt.height == h) return;
            handle?.Release();
            rt?.Release();
            rt = new RenderTexture(w, h, 0, fmt)
            {
                dimension = TextureDimension.Tex2DArray,
                volumeDepth = 2,
                enableRandomWrite = true,
                useMipMap = false
            };
            rt.Create();
            handle = RTHandles.Alloc(rt);
        }

        EnsureArray(ref _quarterCloudArray, ref _quarterCloudArrayHandle,
                    qWidth, qHeight, RenderTextureFormat.ARGBHalf);
        EnsureArray(ref _quarterDepthArray, ref _quarterDepthArrayHandle,
                    qWidth, qHeight, RenderTextureFormat.RHalf);
        EnsureArray(ref _fullCloudArrays[0], ref _fullCloudArrayHandles[0],
                    fullWidth, fullHeight, RenderTextureFormat.ARGBHalf);
        EnsureArray(ref _fullCloudArrays[1], ref _fullCloudArrayHandles[1],
                    fullWidth, fullHeight, RenderTextureFormat.ARGBHalf);
        EnsureArray(ref _mergeOutputArray, ref _mergeOutputArrayHandle,
fullWidth, fullHeight, RenderTextureFormat.ARGBHalf);
    }

    private ComputeBuffer _settingsBuffer;
    private GraphicsBuffer _LightningBuffer;

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
        }
        _LightningBuffer.SetData(Lightnings);
    }

    public void UpdateSettings(CloudSettings settings)
    {
        if (_settingsBuffer == null)
            _settingsBuffer = new ComputeBuffer(1,
                System.Runtime.InteropServices.Marshal.SizeOf<CloudSettings>());

        if (!settings.Equals(_lastSettings))
        {
            _settingsBuffer.SetData(new CloudSettings[] { settings });
            _lastSettings = settings;
        }
    }

    public CloudRenderPass(ComputeShader shader, ComputeShader upscaleShader, ComputeShader mergeShader)
    {
        _shader = shader;
        UpscaleShader = upscaleShader;
        MergeShader = mergeShader;
        _raymarchKernel = shader.FindKernel("CloudRaymarch");
        _upscaleKernel = upscaleShader.FindKernel("TemporalUpscaling");
        _mergeKernel = mergeShader.FindKernel("Merge");
        renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
    }

    private class PassData
    {
        public ComputeShader shader;
        public ComputeShader upscaleShader;
        public ComputeShader mergeShader;
        public int raymarchKernel;
        public int upscaleKernel;
        public int mergeKernel;
        public Bounds bounds;
        public TextureHandle src;
        public TextureHandle dst;
        public TextureHandle quarterCloudBuffer;  // Tex2DArray, 2 slices
        public TextureHandle quarterDepthBuffer;  // Tex2DArray, 2 slices
        public TextureHandle depthBuffer;
        public TextureHandle blueNoiseHandle;
        public Vector3 SunPos;
        public ComputeBuffer settingsBuffer;
        public GraphicsBuffer lightningBuffer;
        public int fullWidth, fullHeight;
        public int quarterWidth, quarterHeight;
        public TextureHandle historyBuffer;       // Tex2DArray, 2 slices
        public TextureHandle fullCloudBuffer;     // Tex2DArray, 2 slices
        public Matrix4x4[] prevViewProj;          // [2]
        public Matrix4x4[] currViewProj;          // [2]
        public Matrix4x4[] currInvViewProj;       // [2]
        public Matrix4x4[] cameraToWorld;         // [2]
        public Matrix4x4[] cameraInverseProjection; // [2]
        public Vector4[] cameraPosPerEye;       // [2]
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

        // ── Collect both eyes' matrices in one call ────────────────────────
        var viewProj = new Matrix4x4[2];
        var invViewProj = new Matrix4x4[2];
        var camToWorld = new Matrix4x4[2];
        var invGpuProj = new Matrix4x4[2];
        var camPos = new Vector4[2];

        for (int eye = 0; eye < 2; eye++)
        {
            var stereoEye = eye == 0 ? Camera.StereoscopicEye.Left : Camera.StereoscopicEye.Right;

            Matrix4x4 view = isXR ? cam.GetStereoViewMatrix(stereoEye) : cam.worldToCameraMatrix;
            Matrix4x4 proj = isXR ? cam.GetStereoProjectionMatrix(stereoEye) : cam.projectionMatrix;
            Matrix4x4 gpuProj = GL.GetGPUProjectionMatrix(proj, true);

            viewProj[eye] = gpuProj * view;
            invViewProj[eye] = viewProj[eye].inverse;
            camToWorld[eye] = view.inverse;
            invGpuProj[eye] = gpuProj.inverse;
            camPos[eye] = view.inverse.GetColumn(3);
        }

        int fullWidth = cameraData.cameraTargetDescriptor.width;
        int fullHeight = cameraData.cameraTargetDescriptor.height;
        int qWidth = Mathf.CeilToInt(fullWidth / BigDivider);
        int qHeight = Mathf.CeilToInt(fullHeight / BigDivider);

        EnsureBuffers(fullWidth, fullHeight);

        // ── Single ping-pong (both eyes live in one Tex2DArray) ───────────
        int prevPing = _currentBuffer;
        int currPing = 1 - prevPing;
        _currentBuffer = currPing;

        TextureDesc desc = new TextureDesc(fullWidth, fullHeight)
        {
            colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
            enableRandomWrite = true,
            name = "CloudOutput",
            clearBuffer = false,
            dimension = TextureDimension.Tex2DArray,
            slices = 2
        };
        TextureHandle dst = renderGraph.CreateTexture(desc);



        using (var builder = renderGraph.AddComputePass<PassData>("Cloud Raymarch + Upscale + Merge", out var data))
        {
            data.shader = _shader;
            data.upscaleShader = UpscaleShader;
            data.mergeShader = MergeShader;
            data.raymarchKernel = _raymarchKernel;
            data.upscaleKernel = _upscaleKernel;
            data.mergeKernel = _mergeKernel;
            data.bounds = Bounds;
            data.cloudWorldMotion = CloudWorldMotion;
            data.flipY = _flipY;
            data.fullWidth = fullWidth;
            data.fullHeight = fullHeight;
            data.quarterWidth = qWidth;
            data.quarterHeight = qHeight;
            data.settingsBuffer = _settingsBuffer;
            data.lightningBuffer = _LightningBuffer;

            data.cameraToWorld = camToWorld;
            data.cameraInverseProjection = invGpuProj;
            data.cameraPosPerEye = camPos;
            data.prevViewProj = _prevViewProj;  // previous frame, both eyes
            data.currViewProj = viewProj;
            data.currInvViewProj = invViewProj;

            // ── Import Tex2DArray handles ──────────────────────────────────
            data.blueNoiseHandle = renderGraph.ImportTexture(_blueNoiseHandle);
            data.quarterCloudBuffer = renderGraph.ImportTexture(_quarterCloudArrayHandle);
            data.quarterDepthBuffer = renderGraph.ImportTexture(_quarterDepthArrayHandle);
            data.historyBuffer = renderGraph.ImportTexture(_fullCloudArrayHandles[prevPing]);
            data.fullCloudBuffer = renderGraph.ImportTexture(_fullCloudArrayHandles[currPing]);
            data.depthBuffer = resourceData.cameraDepthTexture;
            data.src = resourceData.cameraColor;
            data.dst = dst;

            builder.AllowPassCulling(false);
            builder.UseTexture(data.blueNoiseHandle, AccessFlags.Read);
            builder.UseTexture(data.src, AccessFlags.Read);
            builder.UseTexture(data.dst, AccessFlags.WriteAll);
            builder.UseTexture(data.depthBuffer, AccessFlags.Read);
            builder.UseTexture(data.quarterCloudBuffer, AccessFlags.ReadWrite);
            builder.UseTexture(data.quarterDepthBuffer, AccessFlags.ReadWrite);
            builder.UseTexture(data.historyBuffer, AccessFlags.Read);
            builder.UseTexture(data.fullCloudBuffer, AccessFlags.WriteAll);

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
                cmd.SetComputeTextureParam(d.shader, d.raymarchKernel, "_SrcTex", d.src);
                cmd.SetComputeTextureParam(d.shader, d.raymarchKernel, "_CloudBuffer", d.quarterCloudBuffer);
                cmd.SetComputeTextureParam(d.shader, d.raymarchKernel, "BlueNoise", d.blueNoiseHandle);
                cmd.SetComputeTextureParam(d.shader, d.raymarchKernel, "_DepthTex", d.depthBuffer);
                cmd.SetComputeTextureParam(d.shader, d.raymarchKernel, "_CloudDepthTex", d.quarterDepthBuffer);
                cmd.SetComputeConstantBufferParam(d.shader, "_CloudSettings", d.settingsBuffer,
                    0, System.Runtime.InteropServices.Marshal.SizeOf<CloudSettings>());
                cmd.SetComputeBufferParam(d.shader, d.raymarchKernel, "Lightnings", d.lightningBuffer);
                cmd.SetComputeIntParam(d.shader, "_LightningCount", lightningCount);

                int qGroupsX = Mathf.CeilToInt(d.quarterWidth / 8f);
                int qGroupsY = Mathf.CeilToInt(d.quarterHeight / 8f);
                cmd.DispatchCompute(d.shader, d.raymarchKernel, qGroupsX, qGroupsY, 2);

                //////////////////////////////////////TAA/////////////////////////////////////////////
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
                cmd.SetComputeTextureParam(d.upscaleShader, d.upscaleKernel, "_HistoryBuffer", d.historyBuffer);
                cmd.SetComputeTextureParam(d.upscaleShader, d.upscaleKernel, "_CloudBuffer", d.fullCloudBuffer);
                cmd.SetComputeTextureParam(d.upscaleShader, d.upscaleKernel, "_QuarterCloudBuffer", d.quarterCloudBuffer);
                cmd.SetComputeTextureParam(d.upscaleShader, d.upscaleKernel, "_CloudDepthTex", d.quarterDepthBuffer);

                int groupsX = Mathf.CeilToInt(d.fullWidth / 8f);
                int groupsY = Mathf.CeilToInt(d.fullHeight / 8f);
                cmd.DispatchCompute(d.upscaleShader, d.upscaleKernel, groupsX, groupsY, 2);

                /////////////////////////////////////MERGE//////////////////////////////////////////////
                cmd.SetComputeIntParam(d.mergeShader, "_FlipY", d.flipY);
                cmd.SetComputeVectorParam(d.mergeShader, "_Resolution", new Vector2(d.fullWidth, d.fullHeight));
                cmd.SetComputeTextureParam(d.mergeShader, d.mergeKernel, "_CloudBuffer", d.fullCloudBuffer);
                cmd.SetComputeTextureParam(d.mergeShader, d.mergeKernel, "_DepthTex", d.depthBuffer);
                cmd.SetComputeTextureParam(d.mergeShader, d.mergeKernel, "_SrcTex", d.src);
                cmd.SetComputeTextureParam(d.mergeShader, d.mergeKernel, "_OutputTex", d.dst);
                cmd.DispatchCompute(d.mergeShader, d.mergeKernel, groupsX, groupsY, 2);
            });

            // Store both eyes' VP for next frame AFTER the pass is set up
            _prevViewProj = viewProj;
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
        _quarterCloudArrayHandle?.Release(); _quarterCloudArray?.Release();
        _quarterDepthArrayHandle?.Release(); _quarterDepthArray?.Release();
        for (int i = 0; i < 2; i++)
        {
            _fullCloudArrayHandles[i]?.Release();
            _fullCloudArrays[i]?.Release();
        }
        _blueNoiseHandle?.Release();
        _settingsBuffer?.Release();
        _LightningBuffer?.Release();
        DetailRenderTexture?.Release();
        ShapeRenderTexture?.Release();
    }
}