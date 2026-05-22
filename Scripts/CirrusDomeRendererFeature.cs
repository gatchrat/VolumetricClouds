using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;


public class CirrusDomeRendererFeature : ScriptableRendererFeature
{
    private CirrusDomePass _pass;

    public override void Create()
    {
        _pass = new CirrusDomePass();
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (_pass == null) return;
        renderer.EnqueuePass(_pass);
    }

    private class CirrusDomePass : ScriptableRenderPass
    {
        private static readonly ShaderTagId s_ShaderTag = new ShaderTagId("CirrusDome");

        private class PassData
        {
            public RendererListHandle rendererList;
        }

        public CirrusDomePass()
        {
            //Render before 3D Clouds, otherwise will overlap, thats all this whole script does
            renderPassEvent = (RenderPassEvent)(int)RenderPassEvent.AfterRenderingSkybox;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            var resourceData = frameData.Get<UniversalResourceData>();
            var renderingData = frameData.Get<UniversalRenderingData>();
            var lightData = frameData.Get<UniversalLightData>();

            if (cameraData.camera.cameraType != CameraType.Game) return;

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Cirrus Dome", out var passData))
            {
                var drawingSettings = RenderingUtils.CreateDrawingSettings(
                    s_ShaderTag,
                    renderingData,
                    cameraData,
                    lightData,
                    SortingCriteria.CommonTransparent);

                var filteringSettings = new FilteringSettings(RenderQueueRange.all);

                var listParams = new RendererListParams(renderingData.cullResults, drawingSettings, filteringSettings);

                passData.rendererList = renderGraph.CreateRendererList(listParams);

                builder.UseRendererList(passData.rendererList);
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.Write);
                builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.Read);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((PassData d, RasterGraphContext ctx) =>
                {
                    ctx.cmd.DrawRendererList(d.rendererList);
                });
            }
        }
    }
}
