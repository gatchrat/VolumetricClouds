using UnityEngine;
using UnityEngine.Rendering.Universal;

public class CloudRendererFeature : ScriptableRendererFeature
{
    public ComputeShader cloudShader;
    public ComputeShader InterpolateShader;
    public ComputeShader MergeShader;

    private CloudRenderPass _pass;

    public override void Create()
    {
        if (cloudShader == null) return;
        _pass = new CloudRenderPass(cloudShader, InterpolateShader, MergeShader);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (_pass == null) return;
        var Manager = Object.FindAnyObjectByType<CloudManager>();
        if (Manager == null || Manager.ShapeRenderTexture == null) return;

        _pass.ShapeRenderTexture = Manager.ShapeRenderTexture;
        _pass.UpdateSettings(Manager.cloudSettings);
        _pass.BlueNoiseTexture = Manager.BlueNoise;
        _pass.DetailRenderTexture = Manager.DetailRenderTexture;
        Bounds CloudBounds = new Bounds();
        CloudBounds.size = Manager.CloudsBounds.localScale;
        CloudBounds.center = Manager.CloudsBounds.localPosition;
        _pass.Bounds = CloudBounds;
        _pass.SunPos = Manager.Sun.position;
        renderer.EnqueuePass(_pass);
    }
    protected override void Dispose(bool disposing)
    {
        _pass?.Dispose();
        _pass = null;
    }
}