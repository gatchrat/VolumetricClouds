using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

public class CloudRenderPass : ScriptableRenderPass
{
    private ComputeShader _shader;
    private ComputeShader UpscaleShader;
    private ComputeShader MergeShader;
    public Bounds Bounds;
    private int _raymarchKernel;
    private int _upscaleKernel;
    private int _mergeKernel;
    public RenderTexture ShapeRenderTexture;
    public RenderTexture DetailRenderTexture;
    public Texture2D BlueNoiseTexture;
    public Vector3 SunPos;
    private CloudSettings _lastSettings;

    // Quarter resolution - raymarch target
    private RenderTexture _quarterCloudBuffer;
    private RTHandle _quarterCloudHandle;

    // Full resolution - upscale target
    private RenderTexture[] _fullCloudBuffers = new RenderTexture[2];
    private RTHandle[] _fullCloudHandles = new RTHandle[2];
    private int _currentBuffer = 0;
    private Matrix4x4 _prevViewProj;
    private RenderTexture _quarterDepthBuffer;
    private RTHandle _quarterDepthHandle;


    private RTHandle _blueNoiseHandle;

    private void EnsureBuffers(int fullWidth, int fullHeight)
    {
        if (_blueNoiseHandle == null)
            _blueNoiseHandle = RTHandles.Alloc(BlueNoiseTexture);

        int qWidth = Mathf.CeilToInt(fullWidth / 4f);
        int qHeight = Mathf.CeilToInt(fullHeight / 4f);

        // Quarter res buffer
        if (_quarterCloudBuffer == null ||
            _quarterCloudBuffer.width != qWidth ||
            _quarterCloudBuffer.height != qHeight)
        {
            _quarterCloudBuffer?.Release();
            _quarterCloudHandle?.Release();

            _quarterCloudBuffer = new RenderTexture(qWidth, qHeight, 0, RenderTextureFormat.ARGBFloat);
            _quarterCloudBuffer.enableRandomWrite = true;
            _quarterCloudBuffer.Create();
            _quarterCloudHandle = RTHandles.Alloc(_quarterCloudBuffer);
        }

        // Full res buffer
        for (int i = 0; i < 2; i++)
        {
            if (_fullCloudBuffers[i] == null ||
                _fullCloudBuffers[i].width != fullWidth)
            {
                _fullCloudBuffers[i]?.Release();
                _fullCloudHandles[i]?.Release();
                _fullCloudBuffers[i] = new RenderTexture(fullWidth, fullHeight, 0,
                                                          RenderTextureFormat.ARGBFloat);
                _fullCloudBuffers[i].enableRandomWrite = true;
                _fullCloudBuffers[i].Create();
                _fullCloudHandles[i] = RTHandles.Alloc(_fullCloudBuffers[i]);
            }
        }
        if (_quarterDepthBuffer == null ||
            _quarterDepthBuffer.width != qWidth ||
            _quarterDepthBuffer.height != qHeight)
        {
            _quarterDepthBuffer?.Release();
            _quarterDepthHandle?.Release();

            _quarterDepthBuffer = new RenderTexture(qWidth, qHeight, 0, RenderTextureFormat.RFloat);
            _quarterDepthBuffer.enableRandomWrite = true;
            _quarterDepthBuffer.Create();
            _quarterDepthHandle = RTHandles.Alloc(_quarterDepthBuffer);
        }
    }

    private ComputeBuffer _settingsBuffer;

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
        renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
    }

    private class PassData
    {
        public ComputeShader shader;
        public ComputeShader upscaleShader;
        public TextureHandle quarterDepthBuffer;
        public ComputeShader mergeShader;
        public int raymarchKernel;
        public int upscaleKernel;
        public int mergeKernel;
        public Bounds bounds;
        public Camera camera;
        public TextureHandle src;
        public TextureHandle dst;
        public TextureHandle quarterCloudBuffer;
        public TextureHandle depthBuffer;
        public TextureHandle blueNoiseHandle;
        public Vector3 SunPos;
        public ComputeBuffer settingsBuffer;
        public int fullWidth;
        public int fullHeight;
        public int quarterWidth;
        public int quarterHeight;
        public TextureHandle historyBuffer;     // previous frame - read
        public TextureHandle fullCloudBuffer;   // current frame  - write
        public Matrix4x4 prevViewProj;
        public Vector2 quarterResolution;
        public Vector2 fullResolution;
        public Matrix4x4 currInvViewProj;
        public Matrix4x4 currViewProj;
        public Vector3 cameraPos;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        _shader.SetTexture(_raymarchKernel, "ShapeTexture", ShapeRenderTexture);
        _shader.SetTexture(_raymarchKernel, "DetailTexture", DetailRenderTexture);

        var resourceData = frameData.Get<UniversalResourceData>();
        var cameraData = frameData.Get<UniversalCameraData>();

        if (cameraData.camera.cameraType != CameraType.Game) return;

        int fullWidth = cameraData.camera.pixelWidth;
        int fullHeight = cameraData.camera.pixelHeight;
        int qWidth = Mathf.CeilToInt(fullWidth / 4f);
        int qHeight = Mathf.CeilToInt(fullHeight / 4f);

        EnsureBuffers(fullWidth, fullHeight);

        var desc = renderGraph.GetTextureDesc(resourceData.activeColorTexture);
        desc.enableRandomWrite = true;
        desc.name = "CloudOutput";
        TextureHandle dst = renderGraph.CreateTexture(desc);

        using (var builder = renderGraph.AddComputePass<PassData>("Cloud Raymarch + Upscale", out var data))
        {
            data.shader = _shader;
            data.upscaleShader = UpscaleShader;
            data.mergeShader = MergeShader;
            data.raymarchKernel = _raymarchKernel;
            data.upscaleKernel = _upscaleKernel;
            data.mergeKernel = _mergeKernel;
            data.bounds = Bounds;
            data.camera = cameraData.camera;
            data.src = resourceData.activeColorTexture;
            data.dst = dst;
            data.SunPos = SunPos;
            data.settingsBuffer = _settingsBuffer;
            data.fullWidth = fullWidth;
            data.fullHeight = fullHeight;
            data.quarterWidth = qWidth;
            data.quarterHeight = qHeight;
            data.cameraPos = cameraData.camera.transform.position;
            Matrix4x4 gpuProj = GL.GetGPUProjectionMatrix(cameraData.camera.projectionMatrix, true);
            Matrix4x4 currVP = gpuProj * cameraData.camera.worldToCameraMatrix;
            data.prevViewProj = _prevViewProj;
            data.currViewProj = currVP;
            data.currInvViewProj = currVP.inverse;
            data.cameraPos = cameraData.camera.transform.position;

            data.blueNoiseHandle = renderGraph.ImportTexture(_blueNoiseHandle);
            data.quarterCloudBuffer = renderGraph.ImportTexture(_quarterCloudHandle);
            data.depthBuffer = resourceData.cameraDepthTexture;

            builder.UseTexture(data.blueNoiseHandle);
            builder.UseTexture(data.src);
            builder.UseTexture(data.dst, AccessFlags.WriteAll);
            builder.UseTexture(data.depthBuffer);
            builder.UseTexture(data.quarterCloudBuffer, AccessFlags.ReadWrite);

            data.quarterDepthBuffer = renderGraph.ImportTexture(_quarterDepthHandle);
            builder.UseTexture(data.quarterDepthBuffer, AccessFlags.ReadWrite);
            int prevBuffer = _currentBuffer;
            int currentBuffer = 1 - _currentBuffer;
            _currentBuffer = currentBuffer;    // flip for next frame

            data.historyBuffer = renderGraph.ImportTexture(_fullCloudHandles[prevBuffer]);
            data.fullCloudBuffer = renderGraph.ImportTexture(_fullCloudHandles[currentBuffer]);
            data.prevViewProj = _prevViewProj;
            data.quarterResolution = new Vector2(qWidth, qHeight);
            data.fullResolution = new Vector2(fullWidth, fullHeight);

            builder.UseTexture(data.historyBuffer);                          // read
            builder.UseTexture(data.fullCloudBuffer, AccessFlags.WriteAll);  // write

            builder.SetRenderFunc((PassData d, ComputeGraphContext ctx) =>
            {
                var cmd = ctx.cmd;
                var cam = d.camera;

                ////////////////////////////RAYMARCHING//////////////////////////////////
                cmd.SetComputeMatrixParam(d.shader, "_CameraToWorld", cam.cameraToWorldMatrix);
                cmd.SetComputeMatrixParam(d.shader, "_CameraInverseProjection", cam.projectionMatrix.inverse);
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
                cmd.SetComputeConstantBufferParam(d.shader, "_CloudSettings", d.settingsBuffer, 0, System.Runtime.InteropServices.Marshal.SizeOf<CloudSettings>());

                int qGroupsX = Mathf.CeilToInt(d.quarterWidth / 8f);
                int qGroupsY = Mathf.CeilToInt(d.quarterHeight / 8f);
                cmd.DispatchCompute(d.shader, d.raymarchKernel, qGroupsX, qGroupsY, 1);

                //////////////////////////////////////TAA/////////////////////////////////////////////
                cmd.SetComputeMatrixParam(d.upscaleShader, "_CurrInvViewProj", d.currInvViewProj);
                cmd.SetComputeMatrixParam(d.upscaleShader, "_CurrViewProj", d.currViewProj);
                cmd.SetComputeMatrixParam(d.upscaleShader, "_PrevViewProj", d.prevViewProj);
                cmd.SetComputeVectorParam(d.upscaleShader, "_QuarterResolution", d.quarterResolution);
                cmd.SetComputeVectorParam(d.upscaleShader, "_Resolution", d.fullResolution);
                cmd.SetComputeVectorParam(d.upscaleShader, "_FullResolution", d.fullResolution);
                cmd.SetComputeTextureParam(d.upscaleShader, d.upscaleKernel, "_HistoryBuffer", d.historyBuffer);
                cmd.SetComputeTextureParam(d.upscaleShader, d.upscaleKernel, "_CloudBuffer", d.fullCloudBuffer);
                cmd.SetComputeTextureParam(d.upscaleShader, d.upscaleKernel, "_QuarterCloudBuffer", d.quarterCloudBuffer);
                cmd.SetComputeTextureParam(d.upscaleShader, d.upscaleKernel, "_CloudDepthTex", d.quarterDepthBuffer);
                cmd.SetComputeTextureParam(d.upscaleShader, d.upscaleKernel, "_DepthTex", d.depthBuffer);
                cmd.SetComputeVectorParam(d.upscaleShader, "_CameraPos", d.cameraPos);
                cmd.SetComputeIntParam(d.upscaleShader, "_FrameIndex", Time.frameCount);

                int groupsX = Mathf.CeilToInt(d.fullWidth / 8f);
                int groupsY = Mathf.CeilToInt(d.fullHeight / 8f);
                cmd.DispatchCompute(d.upscaleShader, d.upscaleKernel, groupsX, groupsY, 1);

                /////////////////////////////////////MERGE//////////////////////////////////////////////
                cmd.SetComputeVectorParam(d.mergeShader, "_Resolution", new Vector2(d.fullWidth, d.fullHeight));
                cmd.SetComputeTextureParam(d.mergeShader, d.mergeKernel, "_CloudBuffer", d.fullCloudBuffer);
                cmd.SetComputeTextureParam(d.mergeShader, d.mergeKernel, "_DepthTex", d.depthBuffer);
                cmd.SetComputeTextureParam(d.mergeShader, d.mergeKernel, "_SrcTex", d.src);
                cmd.SetComputeTextureParam(d.mergeShader, d.mergeKernel, "_OutputTex", d.dst);

                cmd.DispatchCompute(d.mergeShader, d.mergeKernel, groupsX, groupsY, 1);
            });
            _prevViewProj = currVP;
        }

        using (var builder = renderGraph.AddRasterRenderPass<PassData>("Cloud Blit Back", out var blitData))
        {
            blitData.src = dst;
            blitData.dst = resourceData.activeColorTexture;

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
        _quarterCloudHandle?.Release();
        _quarterCloudBuffer?.Release();
        foreach (var h in _fullCloudHandles) h?.Release();
        foreach (var b in _fullCloudBuffers) b?.Release();
        _blueNoiseHandle?.Release();
        _settingsBuffer?.Release();
        DetailRenderTexture?.Release();
        ShapeRenderTexture?.Release();
        _quarterDepthHandle?.Release();
        _quarterDepthBuffer?.Release();
    }
}