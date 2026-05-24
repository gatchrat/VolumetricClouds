using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public class CloudUIBinder : MonoBehaviour
{
    public UIDocument document;
    public UIDocument SecondaryDocument;
    public UIDocument ColorSettings;
    public Light sunLight;
    public CloudManager clouds;
    private Label FPSLabel;

    Label StatsLabel;
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

        Toggle EnablelightningToggle =
           root.Q<Toggle>("LightningToggle");

        EnablelightningToggle.value = clouds.Lightning;

        EnablelightningToggle.RegisterValueChangedCallback(evt =>
        {
            clouds.Lightning = evt.newValue;
        });

        //FPS

        root = SecondaryDocument.rootVisualElement;
        StatsLabel = root.Q<Label>("Stats");
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
            StatsLabel.text = $"Cloud Step Size: {clouds.CloudStepSize:F0}m";
        });
        Slider SizeMultiplierSlider =
           root.Q<Slider>("SizeMultiplierSlider");

        SizeMultiplierSlider.value = clouds.BigStepMultiplier;

        SizeMultiplierSlider.RegisterValueChangedCallback(evt =>
        {
            clouds.BigStepMultiplier = evt.newValue;
            StatsLabel.text = $"Big Step Multiplier: {clouds.BigStepMultiplier:F1}x";
        });
        Slider ShadowQualitySlider =
          root.Q<Slider>("ShadowQuality");

        ShadowQualitySlider.value = clouds.ShadowStepSize;

        ShadowQualitySlider.RegisterValueChangedCallback(evt =>
        {
            clouds.ShadowStepSize = evt.newValue;
            StatsLabel.text = $"Shadow Step Size: {clouds.ShadowStepSize:F0}m";
        });
        EnumField UpscalingModeEnum =
            root.Q<EnumField>("UpscalingMode");
        UpscalingModeEnum.value = clouds.upscalingMode;
        UpscalingModeEnum.RegisterValueChangedCallback(evt =>
        {
            clouds.upscalingMode = (UpscalingMode)evt.newValue;
        });
        EnumField SceneEnumField =
            root.Q<EnumField>("Scene");
        Debug.Log(SceneManager.GetActiveScene().name);
        switch (SceneManager.GetActiveScene().name)
        {
            case "Normal":
                SceneEnumField.value = CloudLevel.Standard;
                break;
            case "OrangeEvening":
                SceneEnumField.value = CloudLevel.BeachEvening;
                break;
            case "Normal Sunny Day":
                SceneEnumField.value = CloudLevel.BrightDay;
                break;
            case "Pink":
                SceneEnumField.value = CloudLevel.CottonCandy;
                break;
            case "Night Rain":
                SceneEnumField.value = CloudLevel.LonelyNight;
                break;
            case "BLue night":
                SceneEnumField.value = CloudLevel.StormyNight;
                break;
        }
        SceneEnumField.RegisterValueChangedCallback(evt =>
        {
            CloudLevel cloudLevel = (CloudLevel)evt.newValue;
            switch (cloudLevel)
            {
                case CloudLevel.Standard:
                    SceneManager.LoadScene("Normal");
                    break;
                case CloudLevel.BeachEvening:
                    SceneManager.LoadScene("OrangeEvening");
                    break;
                case CloudLevel.BrightDay:
                    SceneManager.LoadScene("Normal Sunny Day");
                    break;
                case CloudLevel.CottonCandy:
                    SceneManager.LoadScene("Pink");
                    break;
                case CloudLevel.LonelyNight:
                    SceneManager.LoadScene("Night Rain");
                    break;
                case CloudLevel.StormyNight:
                    SceneManager.LoadScene("BLue night");
                    break;
                default:
                    break;
            }
        });
        ///COLOR SETTINGS
        root = ColorSettings.rootVisualElement;
        //SUNPOSITION
        float sunY = sunLight.transform.eulerAngles.y;
        float sunX = sunLight.transform.eulerAngles.x;
        float sunZ = sunLight.transform.eulerAngles.z;

        Slider SunPosX =
          root.Q<Slider>("SunPoxX");

        SunPosX.value = sunLight.transform.eulerAngles.y;

        SunPosX.RegisterValueChangedCallback(evt =>
        {
            sunY = evt.newValue;
            sunLight.transform.rotation = Quaternion.Euler(sunX, sunY, sunZ);
        });
        Slider SunPosY =
        root.Q<Slider>("SunPoxY");

        SunPosY.value = sunLight.transform.eulerAngles.x;

        SunPosY.RegisterValueChangedCallback(evt =>
        {
            sunX = evt.newValue;
            sunLight.transform.rotation = Quaternion.Euler(sunX, sunY, sunZ);
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

    private void Update()
    {
        float frameMs = Time.unscaledDeltaTime * 1000f;
        if (frameMs > 32f) _hitchCount++;

        FPSTimer -= Time.unscaledDeltaTime;
        if (FPSTimer <= 0f)
        {
            FPSTimer = 0.5f;
            int fps = Mathf.RoundToInt(1f / Time.unscaledDeltaTime);
            FPSLabel.text = $"FPS: {fps}  Hitches(>32ms): {_hitchCount}";
        }
    }
}