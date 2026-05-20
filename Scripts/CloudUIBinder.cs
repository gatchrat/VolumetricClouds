using UnityEngine;
using UnityEngine.UIElements;

public class CloudUIBinder : MonoBehaviour
{
    public UIDocument document;
    public UIDocument SecondaryDocument;
    public UIDocument ColorSettings;
    public Light sunLight;
    public CloudManager clouds;
    private Label FPSLabel;
    private float FPSTimer = 1f;

    private void Start()
    {
        var root = document.rootVisualElement;

        Slider sunStrengthSlider =
            root.Q<Slider>("SunStrengthSlider");

        sunStrengthSlider.value = clouds.BrightnesMultiplier;

        sunStrengthSlider.RegisterValueChangedCallback(evt =>
        {
            clouds.BrightnesMultiplier = evt.newValue;
        });

        Slider SunBlockSlider =
            root.Q<Slider>("SunBlockSlider");

        SunBlockSlider.value = clouds.CloudSunBlocking;

        SunBlockSlider.RegisterValueChangedCallback(evt =>
        {
            clouds.CloudSunBlocking = evt.newValue;
        });

        Slider AmbiantSlider =
            root.Q<Slider>("AmbiantSlider");

        AmbiantSlider.value = clouds.AmbiantLight;

        AmbiantSlider.RegisterValueChangedCallback(evt =>
        {
            clouds.AmbiantLight = evt.newValue;
        });

        Slider CloudThicknessSlider =
            root.Q<Slider>("CloudThicknessSlider");

        CloudThicknessSlider.value = clouds.CloudThickness;

        CloudThicknessSlider.RegisterValueChangedCallback(evt =>
        {
            clouds.CloudThickness = evt.newValue;
        });

        Slider CloudDensitySlider =
            root.Q<Slider>("CloudDensitySlider");

        CloudDensitySlider.value = clouds.CloudDensity;

        CloudDensitySlider.RegisterValueChangedCallback(evt =>
        {
            clouds.CloudDensity = evt.newValue;
        });

        Slider SunbBlindingSlider =
            root.Q<Slider>("SunBlindingSlider");

        SunbBlindingSlider.value = clouds.SunBlindingEffectStrengh;

        SunbBlindingSlider.RegisterValueChangedCallback(evt =>
        {
            clouds.SunBlindingEffectStrengh = evt.newValue;
        });

        Slider MovementSpeedSlider =
            root.Q<Slider>("MovementSpeedSlider");

        MovementSpeedSlider.value = clouds.CloudMovementSpeed;

        MovementSpeedSlider.RegisterValueChangedCallback(evt =>
        {
            clouds.CloudMovementSpeed = evt.newValue;
        });

        //FPS
        root = SecondaryDocument.rootVisualElement;
        FPSLabel = root.Q<Label>("FPSLabel");

        Toggle EnableCloudsToggle =
           root.Q<Toggle>("EnableCloudsToggle");

        EnableCloudsToggle.value = clouds.EnableClouds;

        EnableCloudsToggle.RegisterValueChangedCallback(evt =>
        {
            clouds.EnableClouds = evt.newValue;
        });
        Slider CloudQualitySlider =
           root.Q<Slider>("CloudQuality");

        CloudQualitySlider.value = clouds.CloudStepSize;

        CloudQualitySlider.RegisterValueChangedCallback(evt =>
        {
            clouds.CloudStepSize = evt.newValue;
        });
        Slider ShadowQualitySlider =
          root.Q<Slider>("ShadowQuality");

        ShadowQualitySlider.value = clouds.ShadowStepSize;

        ShadowQualitySlider.RegisterValueChangedCallback(evt =>
        {
            clouds.ShadowStepSize = evt.newValue;
        });
        EnumField UpscalingModeEnum =
            root.Q<EnumField>("UpscalingMode");
        UpscalingModeEnum.value = clouds.upscalingMode;
        UpscalingModeEnum.RegisterValueChangedCallback(evt =>
        {
            clouds.upscalingMode = (UpscalingMode)evt.newValue;
        });
        ///COLOR SETTINGS
        root = ColorSettings.rootVisualElement;
        Debug.Log(sunLight.color);
        //SUNPOSITION
        Slider SunPosX =
          root.Q<Slider>("SunPoxX");

        SunPosX.value = sunLight.transform.position.y;

        SunPosX.RegisterValueChangedCallback(evt =>
        {
            sunLight.transform.position = new Vector3(sunLight.transform.position.x, evt.newValue, sunLight.transform.position.z);
        });
        Slider SunPosY =
        root.Q<Slider>("SunPoxY");

        SunPosY.value = sunLight.transform.position.x;

        SunPosY.RegisterValueChangedCallback(evt =>
        {
            sunLight.transform.position = new Vector3(evt.newValue, sunLight.transform.position.y, sunLight.transform.position.z);
        });
        //SUN COLOR
        Slider SunColorR =
        root.Q<Slider>("SUNCOLORR");

        SunColorR.value = sunLight.color.r;

        SunColorR.RegisterValueChangedCallback(evt =>
        {
            sunLight.color = new Color(evt.newValue, sunLight.color.g, sunLight.color.b);
            Debug.Log(sunLight.color);
        });
        Slider SunColorG =
       root.Q<Slider>("SUNCOLORG");

        SunColorG.value = sunLight.color.g;


        SunColorG.RegisterValueChangedCallback(evt =>
        {
            sunLight.color = new Color(sunLight.color.r, evt.newValue, sunLight.color.b);
        });
        Slider SunColorB =
       root.Q<Slider>("SUNCOLORB");

        SunColorB.value = sunLight.color.b;

        SunColorB.RegisterValueChangedCallback(evt =>
        {
            sunLight.color = new Color(sunLight.color.r, sunLight.color.g, evt.newValue);
        });
        //AMBIENT COLOR
        Slider AmbientColorR =
        root.Q<Slider>("AmbientColorR");

        AmbientColorR.value = RenderSettings.ambientLight.r;

        AmbientColorR.RegisterValueChangedCallback(evt =>
        {
            RenderSettings.ambientLight = new Color(evt.newValue, RenderSettings.ambientLight.g, RenderSettings.ambientLight.b);
        });
        Slider AmbientColorG =
       root.Q<Slider>("AmbientColorG");

        AmbientColorG.value = RenderSettings.ambientLight.g;

        AmbientColorG.RegisterValueChangedCallback(evt =>
        {
            RenderSettings.ambientLight = new Color(RenderSettings.ambientLight.r, evt.newValue, RenderSettings.ambientLight.b);
        });
        Slider AmbientColorB =
       root.Q<Slider>("AmbientColorB");

        AmbientColorB.value = RenderSettings.ambientLight.b;

        AmbientColorB.RegisterValueChangedCallback(evt =>
        {
            RenderSettings.ambientLight = new Color(RenderSettings.ambientLight.r, RenderSettings.ambientLight.g, evt.newValue);
        });

    }
    private int _hitchCount;
    private float _worstFrameMs;

    private void Update()
    {
        float frameMs = Time.unscaledDeltaTime * 1000f;
        if (frameMs > 10f) _hitchCount++;
        if (frameMs > _worstFrameMs) _worstFrameMs = frameMs;

        FPSTimer -= Time.unscaledDeltaTime;
        if (FPSTimer <= 0f)
        {
            FPSTimer = 0.5f;
            int fps = Mathf.RoundToInt(1f / Time.unscaledDeltaTime);
            FPSLabel.text = $"FPS: {fps}  Hitches(>10ms): {_hitchCount}  Worst: {_worstFrameMs:F0}ms";
        }
    }
}