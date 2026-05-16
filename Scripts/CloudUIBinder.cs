using UnityEngine;
using UnityEngine.UIElements;

public class CloudUIBinder : MonoBehaviour
{
    public UIDocument document;
    public CloudManager clouds;

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
    }
}