using UnityEngine;
using System;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.GlobalIllumination;
using Unity.Mathematics;
using System.Collections.Generic;
using UnityEngine.UIElements;
using Unity.VisualScripting;

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct Lightning
{
    public float3 origin;
    public float _pad0; //Padding to align buffer in memory, otherwise refuses to work
    public float3 direction;
    public float length;
}
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct CloudSettings
{
    public Vector3 Offset;
    public float _pad0; //Padding to align buffer in memory, otherwise refuses to work
    public Vector3 Scale;
    public float _pad1;
    public float CloudDensity;
    public float BrightnesMultiplier;
    public float TransmittanceFalloff;
    public float PowderStrength;
    public float BeersEffect;
    public float SunBlindingEffectSize;
    public float _pad4;
    public float _pad5;
    public float3 SunColor;
    public float SunIntensity;
    public float3 SunDirection;
    public float _pad3;

}

public class CloudManager : MonoBehaviour
{
    public int seed = 42;
    public int ShapeTextureSize = 128;
    public RenderTexture ShapeRenderTexture;
    public RenderTexture DetailRenderTexture;
    public Texture2D BlueNoise;
    public int[] ShapeWosleyCellCount = new int[] { 4, 6, 8, 12 };
    public int[] DetailWosleyCellCount = new int[] { 16, 24, 32 };
    public float[] fBmWeights = new float[] { 1, 0.5f, 0.2f };
    public ComputeShader WorleyComputer;
    [Range(0f, 1f)]
    public float CloudDensity = 0.55f; //Used in Renderpass
    public int StepCount = 4;
    [Range(1f, 10f)]
    public float BrightnesMultiplier = 1f;
    [Range(0.1f, 20f)]
    public float TransmittanceFalloff = 1f;
    [Range(0f, 1)]
    public float PowderEffect = 0.35f;
    [Range(0f, 100f)]
    public float BeersEffect = 100f;
    [Range(0f, 1f)]
    public float SunBlindingEffectStrengh = 1f;
    [Range(0f, 10f)]
    public float CloudMovementSpeed = 1f;
    public Vector3 Scale = new Vector3(1, 1, 1);
    public float3 Offset;
    public Transform CloudsBounds;
    public Transform Sun;
    private Light SunLight;

    public List<Lightning> Lightnings;

    public CloudSettings cloudSettings;

    private float3 CurFrameMovement;

    private float LightningTimer = 3f;
    public GameObject LightningAudioPrefab;

    void Update()
    {
        cloudSettings.BrightnesMultiplier = BrightnesMultiplier;
        cloudSettings.TransmittanceFalloff = TransmittanceFalloff;
        cloudSettings.PowderStrength = PowderEffect;
        cloudSettings.BeersEffect = BeersEffect;
        cloudSettings.SunBlindingEffectSize = SunBlindingEffectStrengh;
        cloudSettings.Scale = Scale;
        cloudSettings.CloudDensity = CloudDensity;
        CurFrameMovement = new Vector3(1, 0, 1) * ((Time.deltaTime / 60) / 3) * CloudMovementSpeed;
        Offset += CurFrameMovement;
        cloudSettings.Offset = Offset;
        cloudSettings.SunDirection = Sun.transform.forward * -1;
        cloudSettings.SunColor = new Vector3(SunLight.color.r, SunLight.color.g, SunLight.color.b);
        cloudSettings.SunIntensity = SunLight.intensity;

        LightningTimer -= Time.deltaTime;
        if (LightningTimer < 0)
        {
            LightningTimer = 3f;
            CreateLightning();
        }
    }
    public float3 GetMovementOffset()
    {
        return CurFrameMovement;
    }

    void Start()
    {
        CreateLightning();

        UnityEngine.Random.InitState(seed);
        SunLight = Sun.GetComponent<Light>();

        if (ShapeRenderTexture != null)
        {
            ShapeRenderTexture.Release();
        }

        if (DetailRenderTexture != null)
        {
            DetailRenderTexture.Release();
        }

        ShapeRenderTexture = new RenderTexture(ShapeTextureSize, ShapeTextureSize, 0, GraphicsFormat.R8G8B8A8_UNorm) //INSANE Importance. No noticable difference to 32bit float in quality but 360 vs 620 fps
        {
            enableRandomWrite = true,
            dimension = UnityEngine.Rendering.TextureDimension.Tex3D,
            volumeDepth = ShapeTextureSize,
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Trilinear,
            useMipMap = false
        };
        ShapeRenderTexture.Create();

        DetailRenderTexture = new RenderTexture(32, 32, 0, GraphicsFormat.R8G8B8A8_UNorm)
        {
            enableRandomWrite = true,
            dimension = UnityEngine.Rendering.TextureDimension.Tex3D,
            volumeDepth = 32,
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Trilinear,
            useMipMap = false
        };
        DetailRenderTexture.Create();

        int CurrentKernel;



        CurrentKernel = WorleyComputer.FindKernel("GenerateWorley");
        WorleyComputer.SetInt("Mode", 0);
        WorleyComputer.SetTexture(CurrentKernel, "ShapeRenderTexture", ShapeRenderTexture);
        WorleyComputer.SetInt("TextureSize", ShapeTextureSize);


        int CurCellsPerRow = ShapeWosleyCellCount[0];
        int groups = Mathf.CeilToInt(ShapeTextureSize / 8f);
        WorleyComputer.SetInt("CellsPerRow", CurCellsPerRow);
        WorleyComputer.SetInt("CurLayer", 0);
        WorleyComputer.Dispatch(CurrentKernel, groups, groups, groups);

        CurCellsPerRow = ShapeWosleyCellCount[1];
        WorleyComputer.SetInt("CellsPerRow", CurCellsPerRow);
        WorleyComputer.SetInt("CurLayer", 1);
        WorleyComputer.Dispatch(CurrentKernel, groups, groups, groups);

        CurCellsPerRow = ShapeWosleyCellCount[2];
        WorleyComputer.SetInt("CellsPerRow", CurCellsPerRow);
        WorleyComputer.SetInt("CurLayer", 2);
        WorleyComputer.Dispatch(CurrentKernel, groups, groups, groups);

        CurCellsPerRow = ShapeWosleyCellCount[3];
        WorleyComputer.SetInt("CellsPerRow", CurCellsPerRow);
        WorleyComputer.SetInt("CurLayer", 3);
        WorleyComputer.Dispatch(CurrentKernel, groups, groups, groups);

        CurrentKernel = WorleyComputer.FindKernel("CombineWorley");
        WorleyComputer.SetTexture(CurrentKernel, "ShapeRenderTexture", ShapeRenderTexture);
        WorleyComputer.SetFloats("fmbWeights", fBmWeights[0], fBmWeights[1], fBmWeights[2]);

        WorleyComputer.Dispatch(CurrentKernel, groups, groups, groups);

        /////////////////////////////DETAIL///////////////////////////////////////////////
        groups = Mathf.CeilToInt(32 / 8f);

        CurrentKernel = WorleyComputer.FindKernel("GenerateWorleyDetail");
        CurCellsPerRow = DetailWosleyCellCount[0];
        WorleyComputer.SetInt("TextureSize", 32);
        WorleyComputer.SetInt("CellsPerRow", CurCellsPerRow);
        WorleyComputer.SetInt("CurLayer", 0);
        WorleyComputer.SetInt("Mode", 1);
        WorleyComputer.SetTexture(CurrentKernel, "DetailRenderTexture", DetailRenderTexture);
        WorleyComputer.Dispatch(CurrentKernel, groups, groups, groups);



        CurCellsPerRow = DetailWosleyCellCount[1];
        WorleyComputer.SetInt("CellsPerRow", CurCellsPerRow);
        WorleyComputer.SetInt("CurLayer", 1);
        WorleyComputer.Dispatch(CurrentKernel, groups, groups, groups);



        CurCellsPerRow = DetailWosleyCellCount[2];
        WorleyComputer.SetInt("CellsPerRow", CurCellsPerRow);
        WorleyComputer.SetInt("CurLayer", 2);
        WorleyComputer.Dispatch(CurrentKernel, groups, groups, groups);

        CurrentKernel = WorleyComputer.FindKernel("CombineWorleyDetail");
        WorleyComputer.SetTexture(CurrentKernel, "DetailRenderTexture", DetailRenderTexture);
        WorleyComputer.SetInt("Mode", 1);
        WorleyComputer.SetFloats("fmbWeights", fBmWeights[0], fBmWeights[1], fBmWeights[2]);


        WorleyComputer.Dispatch(CurrentKernel, groups, groups, groups);
    }
    void CreateLightning()
    {
        int layers = 4;
        Lightning Root = new Lightning();
        Root.origin = new float3(UnityEngine.Random.Range(-10000f, 10000f), 0, UnityEngine.Random.Range(-10000f, 10000f));
        Root.direction = new float3(1, 0, 0);
        Root.length = 1000;
        Lightnings = GetLightningLayer(Root, layers);

        //Vector3 Position = Unity.Mathematics.math.normalize(Lightnings[0].origin) * 100;
        //GameObject.Instantiate(LightningAudioPrefab, Position, this.transform.rotation);
        //https://freesound.org/people/fattirewhitey/sounds/523905/
    }
    List<Lightning> GetLightningLayer(Lightning l, int layers)
    {
        List<Lightning> ls = new List<Lightning>();
        ls.Add(l);
        if (layers == 0)
        {
            return ls;
        }
        Lightning A = new Lightning();
        A.origin = l.origin + l.direction * l.length;
        A.direction = l.direction + new float3(UnityEngine.Random.Range(0, 0.5f), 0, UnityEngine.Random.Range(-0.5f, 0.5f));
        A.direction = Unity.Mathematics.math.normalize(A.direction);
        A.length = l.length * UnityEngine.Random.Range(0.3f, 1f);
        ls.AddRange(GetLightningLayer(A, layers - 1));

        Lightning B = new Lightning();
        B.origin = l.origin + l.direction * l.length;
        B.direction = l.direction + new float3(UnityEngine.Random.Range(-0.5f, 0), 0, UnityEngine.Random.Range(-0.5f, 0.5f));
        B.direction = Unity.Mathematics.math.normalize(B.direction);
        B.length = l.length * UnityEngine.Random.Range(0.3f, 1f);
        ls.AddRange(GetLightningLayer(B, layers - 1));
        return ls;
    }
}
