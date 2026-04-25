using UnityEngine;
using System;
using UnityEngine.Experimental.Rendering;

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct CloudSettings
{
    public Vector3 Offset;
    public float _pad0; //Padding to align buffer in memory, otherwise refuses to work
    public Vector3 Scale;
    public float _pad1;
    public float DensityThreshold;
    public float DensityMultiplier;
    public float TransmittanceFalloff;
    public float PowderEffect;
    public float BeersEffect;
    public float SunDensityImpact;
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
    public float DensityThreshold = 0.7f; //Used in Renderpass
    public int StepCount = 4;
    public float DensityMultiplier = 1f;
    [Range(0.1f, 1f)]
    public float TransmittanceFalloff = 0.3f;
    [Range(0f, 100f)]
    public float PowderEffect = 100f;
    [Range(0f, 100f)]
    public float BeersEffect = 100f;
    [Range(0.2f, 2f)]
    public float SunDensityImpact = 0.8f;
    public Vector3 Scale = new Vector3(1, 1, 1);
    public Vector3 Offset;
    public Transform CloudsBounds;
    public Transform Sun;

    public CloudSettings cloudSettings;

    void Update()
    {
        cloudSettings.DensityMultiplier = DensityMultiplier;
        cloudSettings.TransmittanceFalloff = TransmittanceFalloff;
        cloudSettings.PowderEffect = PowderEffect;
        cloudSettings.BeersEffect = BeersEffect;
        cloudSettings.SunDensityImpact = SunDensityImpact;
        cloudSettings.Scale = Scale;
        cloudSettings.DensityThreshold = DensityThreshold;
        Offset += new Vector3(1, 0, 1) * (Time.deltaTime / 60) / 3;
        cloudSettings.Offset = Offset;
    }

    void Start()
    {
        UnityEngine.Random.InitState(seed);

        if (ShapeRenderTexture != null)
        {
            ShapeRenderTexture.Release();
        }

        if (DetailRenderTexture != null)
        {
            DetailRenderTexture.Release();
        }

        ShapeRenderTexture = new RenderTexture(ShapeTextureSize, ShapeTextureSize, 0, GraphicsFormat.R32G32B32A32_SFloat)
        {
            enableRandomWrite = true,
            dimension = UnityEngine.Rendering.TextureDimension.Tex3D,
            volumeDepth = ShapeTextureSize,
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Trilinear,
            useMipMap = true,
            autoGenerateMips = false
        };
        ShapeRenderTexture.Create();

        DetailRenderTexture = new RenderTexture(32, 32, 0, GraphicsFormat.R32G32B32A32_SFloat)
        {
            enableRandomWrite = true,
            dimension = UnityEngine.Rendering.TextureDimension.Tex3D,
            volumeDepth = 32,
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Trilinear,
            useMipMap = true,
            autoGenerateMips = false
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

        ShapeRenderTexture.GenerateMips();
        DetailRenderTexture.GenerateMips();
    }
}
