using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

public class CloudRenderPass : ScriptableRenderPass
{
    private ComputeShader _shader;
    private ComputeShader InterpolateShader;
    private ComputeShader MergeShader;
    public Bounds Bounds;
    private int _kernel;
    private RTHandle ShapeHandle;
    public RenderTexture ShapeRenderTexture;
    public float DensityThreshold;
    public int StepCount;
    public Vector3 SunPos;
    public float DensityMultiplier;
    private RenderTexture _cloudBuffer;

    private RTHandle _cloudHandle; // persistent field

    private void EnsureCloudBuffer(int width, int height)
    {
        if (_cloudBuffer != null && _cloudBuffer.width == width && _cloudBuffer.height == height)
            return;

        if (_cloudBuffer != null) _cloudBuffer.Release();
        _cloudHandle?.Release();

        _cloudBuffer = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBFloat);
        _cloudBuffer.enableRandomWrite = true;
        _cloudBuffer.Create();

        _cloudHandle = RTHandles.Alloc(_cloudBuffer);
    }

    public CloudRenderPass(ComputeShader shader, ComputeShader InterShader, ComputeShader MShader)
    {
        _shader = shader;
        InterpolateShader = InterShader;
        MergeShader = MShader;
        _kernel = shader.FindKernel("CloudRaymarch");
        renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
    }

    // Data container passed into the render graph lambda
    private class PassData
    {
        public ComputeShader shader;
        public ComputeShader InterpolateShader;
        public ComputeShader MergeShader;
        public int kernel;
        public float DensityThreshold;
        public Bounds bounds;
        public Camera camera;
        public TextureHandle src;
        public TextureHandle dst;
        public TextureHandle cloudBuffer;
        public TextureHandle ShapeHandle;
        public int StepCount;
        public Vector3 SunPos;
        public float DensityMultiplier;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        _shader.SetTexture(_kernel, "ShapeTexture", ShapeRenderTexture);
        var resourceData = frameData.Get<UniversalResourceData>();
        var cameraData = frameData.Get<UniversalCameraData>();
        TextureHandle LocalShapeHandle = renderGraph.ImportTexture(ShapeHandle);

        if (cameraData.camera.cameraType != CameraType.Game) return;

        // Describe a new temp texture the same size as the camera
        var desc = renderGraph.GetTextureDesc(resourceData.activeColorTexture);
        desc.enableRandomWrite = true;
        desc.name = "CloudOutput";
        TextureHandle dst = renderGraph.CreateTexture(desc);

        using (var builder = renderGraph.AddComputePass<PassData>("Cloud Raymarch", out var data))
        {
            data.shader = _shader;
            data.InterpolateShader = InterpolateShader;
            data.MergeShader = MergeShader;
            data.kernel = _kernel;
            data.bounds = Bounds;
            data.DensityThreshold = DensityThreshold;
            data.StepCount = StepCount;
            data.camera = cameraData.camera;
            data.src = resourceData.activeColorTexture;
            data.dst = dst;
            data.ShapeHandle = LocalShapeHandle;
            data.SunPos = SunPos;
            data.DensityMultiplier = DensityMultiplier;

            builder.UseTexture(data.src);
            builder.UseTexture(data.dst, AccessFlags.WriteAll);
            builder.UseTexture(data.ShapeHandle);

            EnsureCloudBuffer(cameraData.camera.pixelWidth, cameraData.camera.pixelHeight);
            TextureHandle cloudTexture = renderGraph.ImportTexture(_cloudHandle);

            data.cloudBuffer = cloudTexture;

            builder.UseTexture(data.cloudBuffer, AccessFlags.ReadWrite);

            builder.SetRenderFunc((PassData d, ComputeGraphContext ctx) =>
            {
                var cmd = ctx.cmd;
                var camera = d.camera;
                int width = camera.pixelWidth;
                int height = camera.pixelHeight;

                cmd.SetComputeMatrixParam(d.shader, "_CameraToWorld", camera.cameraToWorldMatrix);
                cmd.SetComputeIntParam(d.shader, "_FrameIndex", Time.frameCount);
                cmd.SetComputeMatrixParam(d.shader, "_CameraInverseProjection", camera.projectionMatrix.inverse);
                cmd.SetComputeVectorParam(d.shader, "_Resolution", new Vector2(width, height));
                cmd.SetComputeVectorParam(d.shader, "_BoundsMin", d.bounds.min);
                cmd.SetComputeVectorParam(d.shader, "_BoundsMax", d.bounds.max);
                cmd.SetComputeVectorParam(d.shader, "SunPostion", d.SunPos);
                cmd.SetComputeFloatParam(d.shader, "DensityThreshold", d.DensityThreshold);
                cmd.SetComputeIntParam(d.shader, "StepCount", d.StepCount);
                cmd.SetComputeFloatParam(d.shader, "DensityMultiplier", d.DensityMultiplier);
                cmd.SetComputeTextureParam(d.shader, d.kernel, "_SrcTex", d.src);
                cmd.SetComputeTextureParam(d.shader, d.kernel, "_OutputTex", d.dst);
                cmd.SetComputeTextureParam(d.shader, d.kernel, "_CloudBuffer", d.cloudBuffer);

                int groupsX = Mathf.CeilToInt(width / 8f);
                int groupsY = Mathf.CeilToInt(height / 8f);
                cmd.DispatchCompute(d.shader, d.kernel, groupsX, groupsY, 1);
                ///////////////////////////////////////////

                cmd.SetComputeTextureParam(d.InterpolateShader, d.kernel, "_CloudBuffer", d.cloudBuffer);
                cmd.SetComputeIntParam(d.InterpolateShader, "_FrameIndex", Time.frameCount);
                cmd.SetComputeVectorParam(d.InterpolateShader, "_Resolution", new Vector2(width, height));

                cmd.DispatchCompute(d.InterpolateShader, d.kernel, groupsX, groupsY, 1);
                ///////////////////////////////////////////

                cmd.SetComputeTextureParam(d.MergeShader, d.kernel, "_CloudBuffer", d.cloudBuffer);
                cmd.SetComputeTextureParam(d.MergeShader, d.kernel, "_SrcTex", d.src);
                cmd.SetComputeTextureParam(d.MergeShader, d.kernel, "_OutputTex", d.dst);
                cmd.SetComputeVectorParam(d.MergeShader, "_Resolution", new Vector2(width, height));

                cmd.DispatchCompute(d.MergeShader, d.kernel, groupsX, groupsY, 1);


            });
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
        _cloudHandle?.Release();
        _cloudBuffer?.Release();
        ShapeHandle?.Release();
    }
}