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
    public float3 CurFrameMovement;
    private int _raymarchKernel;
    private int _upscaleKernel;
    private int _mergeKernel;
    public RenderTexture ShapeRenderTexture;
    public RenderTexture DetailRenderTexture;
    public Texture2D BlueNoiseTexture;
    private CloudSettings _lastSettings;

    private RenderTexture[] _quarterCloudBuffers = new RenderTexture[2];
    private RTHandle[] _quarterCloudHandles = new RTHandle[2];

    // Full resolution - upscale target (2 ping-pong buffers per eye = 4 total)
    // Index: eye * 2 + pingPong  (eye 0=left, 1=right)
    private RenderTexture[] _fullCloudBuffers = new RenderTexture[4];
    private RTHandle[] _fullCloudHandles = new RTHandle[4];
    private int[] _currentBuffer = new int[2] { 0, 0 }; // per-eye ping-pong index

    // Per-eye previous VP matrix for TAA reprojection
    private Matrix4x4[] _prevViewProj = new Matrix4x4[2];

    private RenderTexture[] _quarterDepthBuffers = new RenderTexture[2];
    private RTHandle[] _quarterDepthHandles = new RTHandle[2];

    private RTHandle _blueNoiseHandle;

    private static readonly int _flipY = SystemInfo.graphicsUVStartsAtTop ? 1 : 0;

    private void EnsureBuffers(int fullWidth, int fullHeight, int eyeCount)
    {
        if (_blueNoiseHandle == null)
            _blueNoiseHandle = RTHandles.Alloc(BlueNoiseTexture);

        int qWidth = Mathf.CeilToInt(fullWidth / 4f);
        int qHeight = Mathf.CeilToInt(fullHeight / 4f);

        for (int eye = 0; eye < eyeCount; eye++)
        {
            // Quarter res cloud buffer per eye
            if (_quarterCloudBuffers[eye] == null ||
                _quarterCloudBuffers[eye].width != qWidth ||
                _quarterCloudBuffers[eye].height != qHeight)
            {
                _quarterCloudBuffers[eye]?.Release();
                _quarterCloudHandles[eye]?.Release();

                _quarterCloudBuffers[eye] = new RenderTexture(qWidth, qHeight, 0, RenderTextureFormat.ARGBHalf);
                _quarterCloudBuffers[eye].enableRandomWrite = true;
                _quarterCloudBuffers[eye].Create();
                _quarterCloudHandles[eye] = RTHandles.Alloc(_quarterCloudBuffers[eye]);
            }

            // Quarter res depth buffer per eye
            if (_quarterDepthBuffers[eye] == null ||
                _quarterDepthBuffers[eye].width != qWidth ||
                _quarterDepthBuffers[eye].height != qHeight)
            {
                _quarterDepthBuffers[eye]?.Release();
                _quarterDepthHandles[eye]?.Release();

                _quarterDepthBuffers[eye] = new RenderTexture(qWidth, qHeight, 0, RenderTextureFormat.RHalf);
                _quarterDepthBuffers[eye].enableRandomWrite = true;
                _quarterDepthBuffers[eye].Create();
                _quarterDepthHandles[eye] = RTHandles.Alloc(_quarterDepthBuffers[eye]);
            }

            // Full res ping-pong buffers per eye (indices eye*2 and eye*2+1)
            for (int p = 0; p < 2; p++)
            {
                int idx = eye * 2 + p;
                if (_fullCloudBuffers[idx] == null ||
                    _fullCloudBuffers[idx].width != fullWidth ||
                    _fullCloudBuffers[idx].height != fullHeight)
                {
                    _fullCloudBuffers[idx]?.Release();
                    _fullCloudHandles[idx]?.Release();

                    _fullCloudBuffers[idx] = new RenderTexture(fullWidth, fullHeight, 0,
                                                               RenderTextureFormat.ARGBHalf);
                    _fullCloudBuffers[idx].enableRandomWrite = true;
                    _fullCloudBuffers[idx].Create();
                    _fullCloudHandles[idx] = RTHandles.Alloc(_fullCloudBuffers[idx]);
                }
            }
        }
    }

    private ComputeBuffer _settingsBuffer;
    private GraphicsBuffer _LightningBuffer;

    public void UpdateLightning(List<Lightning> Lightnings)
    {
        if (lightningCount != Lightnings.Count)
        {
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
            _settingsBuffer = new ComputeBuffer(1, System.Runtime.InteropServices.Marshal.SizeOf<CloudSettings>());

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
        public Camera camera;
        public TextureHandle src;
        public TextureHandle dst;
        public TextureHandle quarterCloudBuffer;
        public TextureHandle quarterDepthBuffer;
        public TextureHandle depthBuffer;
        public TextureHandle blueNoiseHandle;
        public Vector3 SunPos;
        public ComputeBuffer settingsBuffer;
        public GraphicsBuffer lightningBuffer;
        public int fullWidth;
        public int fullHeight;
        public int quarterWidth;
        public int quarterHeight;
        public TextureHandle historyBuffer;
        public TextureHandle fullCloudBuffer;
        public Matrix4x4 prevViewProj;
        public Matrix4x4 currViewProj;
        public Matrix4x4 currInvViewProj;
        // Per-eye camera matrices (already GPU-corrected)
        public Matrix4x4 cameraToWorld;
        public Matrix4x4 cameraInverseProjection;
        public Vector3 cameraPos;
        public Vector3 CurFrameMovement;
        // XR / platform
        public int eyeIndex;   // 0 = left, 1 = right
        public int flipY;      // 1 on Vulkan/Metal, 0 on OpenGL
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        _shader.SetTexture(_raymarchKernel, "ShapeTexture", ShapeRenderTexture);
        _shader.SetTexture(_raymarchKernel, "DetailTexture", DetailRenderTexture);

        var resourceData = frameData.Get<UniversalResourceData>();
        var cameraData = frameData.Get<UniversalCameraData>();

        if (cameraData.camera.cameraType != CameraType.Game) return;

        // ── XR multipass: which eye are we rendering? ─────────────────────────
        bool isXR = cameraData.xr.enabled;
        int eyeIndex = isXR ? cameraData.xr.multipassId : 0;
        int eyeCount = isXR ? 2 : 1;

        Camera.StereoscopicEye stereoEye = eyeIndex == 0
            ? Camera.StereoscopicEye.Left
            : Camera.StereoscopicEye.Right;

        var cam = cameraData.camera;

        // ── Per-eye view & projection matrices ────────────────────────────────
        // Always use stereo matrices when XR is active so each eye gets its
        // own IPD offset and lens projection. Fall back to mono when not in XR.
        Matrix4x4 viewMatrix = isXR
            ? cam.GetStereoViewMatrix(stereoEye)
            : cam.worldToCameraMatrix;

        Matrix4x4 projMatrix = isXR
            ? cam.GetStereoProjectionMatrix(stereoEye)
            : cam.projectionMatrix;

        // GetGPUProjectionMatrix flips Y on Vulkan/Metal when rendering into a
        // texture (renderIntoTexture = true), so the VP we build here is correct
        // for sampling depth and writing to our RenderTextures on Quest.
        Matrix4x4 gpuProj = GL.GetGPUProjectionMatrix(projMatrix, true);
        Matrix4x4 currVP = gpuProj * viewMatrix;
        Matrix4x4 invVP = currVP.inverse;

        // _CameraInverseProjection for ray-direction reconstruction in the
        // raymarch shader must also use the GPU projection matrix so ray
        // directions are consistent with the depth buffer on Vulkan.
        Matrix4x4 invGpuProj = gpuProj.inverse;
        // cameraToWorldMatrix is the inverse of the view matrix.
        Matrix4x4 camToWorld = viewMatrix.inverse;

        int fullWidth = cameraData.cameraTargetDescriptor.width;
        int fullHeight = cameraData.cameraTargetDescriptor.height;
        int qWidth = Mathf.CeilToInt(fullWidth / 4f);
        int qHeight = Mathf.CeilToInt(fullHeight / 4f);

        EnsureBuffers(fullWidth, fullHeight, eyeCount);

        TextureDesc desc = new TextureDesc(fullWidth, fullHeight)
        {
            colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
            enableRandomWrite = true,
            name = "CloudOutput",
            clearBuffer = false
        };
        TextureHandle dst = renderGraph.CreateTexture(desc);

        TextureDesc copyDesc = new TextureDesc(fullWidth, fullHeight)
        {
            colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
            enableRandomWrite = false,
            name = "CameraColorCopy"
        };

        TextureHandle cameraCopy = renderGraph.CreateTexture(copyDesc);
        using (var builder = renderGraph.AddRasterRenderPass<PassData>("Copy Camera Color", out var d))
        {
            d.src = resourceData.cameraColor;
            d.dst = cameraCopy;

            builder.UseTexture(d.src);
            builder.SetRenderAttachment(d.dst, 0);

            builder.SetRenderFunc((PassData data, RasterGraphContext ctx) =>
            {
                Blitter.BlitTexture(ctx.cmd, data.src, new Vector4(1, 1, 0, 0), 0, false);
            });
        }

        // ── Per-eye ping-pong buffer selection ────────────────────────────────
        int prevPing = _currentBuffer[eyeIndex];
        int currPing = 1 - prevPing;
        _currentBuffer[eyeIndex] = currPing;

        int prevBufIdx = eyeIndex * 2 + prevPing;
        int currBufIdx = eyeIndex * 2 + currPing;

        using (var builder = renderGraph.AddComputePass<PassData>("Cloud Raymarch + Upscale", out var data))
        {
            data.shader = _shader;
            data.upscaleShader = UpscaleShader;
            data.mergeShader = MergeShader;
            data.raymarchKernel = _raymarchKernel;
            data.upscaleKernel = _upscaleKernel;
            data.mergeKernel = _mergeKernel;
            data.bounds = Bounds;
            data.CurFrameMovement = CurFrameMovement;
            data.camera = cam;

            data.src = cameraCopy;
            data.dst = dst;
            data.settingsBuffer = _settingsBuffer;
            data.lightningBuffer = _LightningBuffer;
            data.fullWidth = fullWidth;
            data.fullHeight = fullHeight;
            data.quarterWidth = qWidth;
            data.quarterHeight = qHeight;
            data.cameraPos = cam.transform.position;

            // Corrected per-eye matrices
            data.cameraToWorld = camToWorld;
            data.cameraInverseProjection = invGpuProj;
            data.prevViewProj = _prevViewProj[eyeIndex];
            data.currViewProj = currVP;
            data.currInvViewProj = invVP;

            // XR / Vulkan
            data.eyeIndex = eyeIndex;
            data.flipY = _flipY;

            data.blueNoiseHandle = renderGraph.ImportTexture(_blueNoiseHandle);
            data.quarterCloudBuffer = renderGraph.ImportTexture(_quarterCloudHandles[eyeIndex]);
            data.quarterDepthBuffer = renderGraph.ImportTexture(_quarterDepthHandles[eyeIndex]);
            data.depthBuffer = resourceData.cameraDepthTexture;
            data.historyBuffer = renderGraph.ImportTexture(_fullCloudHandles[prevBufIdx]);
            data.fullCloudBuffer = renderGraph.ImportTexture(_fullCloudHandles[currBufIdx]);

            builder.UseTexture(data.blueNoiseHandle);
            builder.AllowPassCulling(false);
            builder.UseTexture(data.src, AccessFlags.Read);
            builder.UseTexture(data.dst, AccessFlags.WriteAll);
            builder.UseTexture(data.depthBuffer);
            builder.UseTexture(data.quarterCloudBuffer, AccessFlags.ReadWrite);
            builder.UseTexture(data.quarterDepthBuffer, AccessFlags.ReadWrite);
            builder.UseTexture(data.historyBuffer);
            builder.UseTexture(data.fullCloudBuffer, AccessFlags.WriteAll);

            builder.SetRenderFunc((PassData d, ComputeGraphContext ctx) =>
            {
                var cmd = ctx.cmd;

                // ── RAYMARCHING ────────────────────────────────────────────────
                // Corrected camera matrices - GPU proj-aware, per-eye
                cmd.SetComputeMatrixParam(d.shader, "_CameraToWorld", d.cameraToWorld);
                cmd.SetComputeMatrixParam(d.shader, "_CameraInverseProjection", d.cameraInverseProjection);
                cmd.SetComputeIntParam(d.shader, "_FlipY", d.flipY);
                cmd.SetComputeIntParam(d.shader, "_EyeIndex", d.eyeIndex);
                cmd.SetComputeIntParam(d.shader, "_FrameIndex", Time.frameCount);
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
                cmd.DispatchCompute(d.shader, d.raymarchKernel, qGroupsX, qGroupsY, 1);

                //////////////////////////////////////TAA/////////////////////////////////////////////
                cmd.SetComputeMatrixParam(d.upscaleShader, "_CurrInvViewProj", d.currInvViewProj);
                cmd.SetComputeMatrixParam(d.upscaleShader, "_CurrViewProj", d.currViewProj);
                cmd.SetComputeMatrixParam(d.upscaleShader, "_PrevViewProj", d.prevViewProj);
                cmd.SetComputeIntParam(d.upscaleShader, "_FrameIndex", Time.frameCount);
                cmd.SetComputeVectorParam(d.upscaleShader, "_QuarterResolution", new Vector2(d.quarterWidth, d.quarterHeight));
                cmd.SetComputeVectorParam(d.upscaleShader, "_Resolution", new Vector2(d.fullWidth, d.fullHeight));
                cmd.SetComputeVectorParam(d.upscaleShader, "_FullResolution", new Vector2(d.fullWidth, d.fullHeight));
                cmd.SetComputeVectorParam(d.upscaleShader, "_CameraPos", d.cameraPos);
                cmd.SetComputeVectorParam(d.upscaleShader, "MovementOffset", d.CurFrameMovement);
                cmd.SetComputeTextureParam(d.upscaleShader, d.upscaleKernel, "_HistoryBuffer", d.historyBuffer);
                cmd.SetComputeTextureParam(d.upscaleShader, d.upscaleKernel, "_CloudBuffer", d.fullCloudBuffer);
                cmd.SetComputeTextureParam(d.upscaleShader, d.upscaleKernel, "_QuarterCloudBuffer", d.quarterCloudBuffer);
                cmd.SetComputeTextureParam(d.upscaleShader, d.upscaleKernel, "_CloudDepthTex", d.quarterDepthBuffer);
                cmd.SetComputeTextureParam(d.upscaleShader, d.upscaleKernel, "_DepthTex", d.depthBuffer);

                int groupsX = Mathf.CeilToInt(d.fullWidth / 8f);
                int groupsY = Mathf.CeilToInt(d.fullHeight / 8f);
                cmd.DispatchCompute(d.upscaleShader, d.upscaleKernel, groupsX, groupsY, 1);

                /////////////////////////////////////MERGE//////////////////////////////////////////////
                cmd.SetComputeIntParam(d.mergeShader, "_FlipY", d.flipY);
                cmd.SetComputeVectorParam(d.mergeShader, "_Resolution", new Vector2(d.fullWidth, d.fullHeight));
                cmd.SetComputeTextureParam(d.mergeShader, d.mergeKernel, "_CloudBuffer", d.fullCloudBuffer);
                cmd.SetComputeTextureParam(d.mergeShader, d.mergeKernel, "_DepthTex", d.depthBuffer);
                cmd.SetComputeTextureParam(d.mergeShader, d.mergeKernel, "_SrcTex", d.src);
                cmd.SetComputeTextureParam(d.mergeShader, d.mergeKernel, "_OutputTex", d.dst);

                cmd.DispatchCompute(d.mergeShader, d.mergeKernel, groupsX, groupsY, 1);
            });
            _prevViewProj[eyeIndex] = currVP;
        }

        using (var builder = renderGraph.AddRasterRenderPass<PassData>("Cloud Blit Back", out var blitData))
        {
            blitData.src = dst;
            blitData.dst = resourceData.cameraColor;

            builder.UseTexture(blitData.src);
            builder.SetRenderAttachment(blitData.dst, 0, AccessFlags.WriteAll);

            builder.SetRenderFunc((PassData d, RasterGraphContext ctx) =>
            {
                Blitter.BlitTexture(ctx.cmd, d.src, new Vector4(1, 1, 0, 0), 0, false);
            });
        }
    }

    public void Dispose()
    {
        for (int eye = 0; eye < 2; eye++)
        {
            _quarterCloudHandles[eye]?.Release();
            _quarterCloudBuffers[eye]?.Release();
            _quarterDepthHandles[eye]?.Release();
            _quarterDepthBuffers[eye]?.Release();
        }
        for (int i = 0; i < 4; i++)
        {
            _fullCloudHandles[i]?.Release();
            _fullCloudBuffers[i]?.Release();
        }
        _blueNoiseHandle?.Release();
        _settingsBuffer?.Release();
        _LightningBuffer?.Release();
        DetailRenderTexture?.Release();
        ShapeRenderTexture?.Release();
    }
}