using UnityEngine;
using UnityEngine.Rendering.Universal;

public class CloudRendererFeature : ScriptableRendererFeature
{
    public ComputeShader cloudShader;

    private CloudRenderPass _pass;

    public override void Create()
    {
        if (cloudShader == null) return;
        _pass = new CloudRenderPass(cloudShader);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (_pass == null) return;
        var Manager = Object.FindAnyObjectByType<CloudManager>();
        if (Manager == null || Manager.ShapeRenderTexture == null) return;

        _pass.SetShapeTexture(Manager.ShapeRenderTexture);
        _pass.DensityThreshold = Manager.DensityThreshold;
        _pass.StepCount = Manager.StepCount;
        Bounds CloudBounds = new Bounds();
        CloudBounds.size = Manager.CloudsBounds.localScale;
        CloudBounds.center = Manager.CloudsBounds.localPosition;
        _pass.Bounds = CloudBounds;
        renderer.EnqueuePass(_pass);
    }
}