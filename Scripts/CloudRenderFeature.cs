using UnityEngine;
using UnityEngine.Rendering.Universal;

public class CloudRendererFeature : ScriptableRendererFeature
{
    public ComputeShader cloudShader;
    public Bounds cloudVolumeBounds;

    private CloudRenderPass _pass;

    public override void Create()
    {
        if (cloudShader == null) return;
        _pass = new CloudRenderPass(cloudShader, cloudVolumeBounds);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (_pass == null) return;
        var Manager = Object.FindAnyObjectByType<CloudManager>();
        if (Manager == null || Manager.ShapeRenderTexture == null) return;

        _pass.SetShapeTexture(Manager.ShapeRenderTexture);
        _pass.DensityThreshold = Manager.DensityThreshold;
        renderer.EnqueuePass(_pass);
    }
}