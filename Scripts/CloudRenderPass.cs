using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

public class CloudRenderPass : ScriptableRenderPass
{
    private ComputeShader _shader;
    private Bounds _bounds;
    private int _kernel;
    private RTHandle ShapeHandle;

    public float DensityThreshold;

    public void SetShapeTexture(RenderTexture Shape)
    {
        ShapeHandle?.Release();
        ShapeHandle = RTHandles.Alloc(Shape);
    }

    public CloudRenderPass(ComputeShader shader, Bounds bounds)
    {
        _shader = shader;
        _bounds = bounds;
        _kernel = shader.FindKernel("CloudRaymarch");
        renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
    }

    // Data container passed into the render graph lambda
    private class PassData
    {
        public ComputeShader shader;
        public int kernel;
        public float DensityThreshold;
        public Bounds bounds;
        public Camera camera;
        public TextureHandle src;
        public TextureHandle dst;
        public TextureHandle ShapeHandle;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
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
            data.kernel = _kernel;
            data.bounds = _bounds;
            data.DensityThreshold = DensityThreshold;
            data.camera = cameraData.camera;
            data.src = resourceData.activeColorTexture;
            data.dst = dst;
            data.ShapeHandle = LocalShapeHandle;

            builder.UseTexture(data.src);
            builder.UseTexture(data.dst, AccessFlags.WriteAll);
            builder.UseTexture(data.ShapeHandle);

            builder.SetRenderFunc((PassData d, ComputeGraphContext ctx) =>
            {
                var cmd = ctx.cmd;
                var camera = d.camera;
                int width = camera.pixelWidth;
                int height = camera.pixelHeight;

                cmd.SetComputeMatrixParam(d.shader, "_CameraToWorld", camera.cameraToWorldMatrix);
                cmd.SetComputeMatrixParam(d.shader, "_CameraInverseProjection", camera.projectionMatrix.inverse);
                cmd.SetComputeVectorParam(d.shader, "_Resolution", new Vector2(width, height));
                cmd.SetComputeVectorParam(d.shader, "_BoundsMin", d.bounds.min);
                cmd.SetComputeVectorParam(d.shader, "_BoundsMax", d.bounds.max);
                cmd.SetComputeFloatParam(d.shader, "DensityThreshold", d.DensityThreshold);
                cmd.SetComputeTextureParam(d.shader, d.kernel, "_SrcTex", d.src);
                cmd.SetComputeTextureParam(d.shader, d.kernel, "_OutputTex", d.dst);
                cmd.SetComputeTextureParam(d.shader, d.kernel, "ShapeTexture", d.ShapeHandle);

                int groupsX = Mathf.CeilToInt(width / 8f);
                int groupsY = Mathf.CeilToInt(height / 8f);
                cmd.DispatchCompute(d.shader, d.kernel, groupsX, groupsY, 1);
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
        ShapeHandle?.Release();
        if (ShapeHandle != null) ShapeHandle.Release();
    }
}