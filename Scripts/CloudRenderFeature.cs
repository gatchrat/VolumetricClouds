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
        var generator = Object.FindAnyObjectByType<CloudManager>();
        if (generator == null || generator.ShapeRenderTexture == null) return;

        _pass.SetShapeTexture(generator.ShapeRenderTexture);
        renderer.EnqueuePass(_pass);
    }
}