using UnityEngine;
using System;
using UnityEditor;
using Unity.VisualScripting;

public class CloudManager : MonoBehaviour
{
    public static int seed = 42;
    public int ShapeTextureSize = 128;
    public RenderTexture ShapeRenderTexture;
    public RenderTexture DetailRenderTexture;
    public int[] ShapeWosleyCellCount = new int[] { 16, 24, 32, 48 };
    public float[] fBmWeights = new float[] { 1, 0.5f, 0.2f, 0.2f };
    public ComputeShader WorleyComputer;
    public float DensityThreshold = 0.7f; //Used in Renderpass
    public int StepCount = 4;
    public float DensityMultiplier = 1f;
    public Transform CloudsBounds;
    public Transform Sun;
    //Buffer
    private ComputeBuffer PerlinNoiseBuffer;
    private ComputeBuffer ShapeWorleyPointsA;
    private ComputeBuffer ShapeWorleyPointsR;
    private ComputeBuffer ShapeWorleyPointsG;
    private ComputeBuffer ShapeWorleyPointsB;
    private ComputeBuffer DetailWorleyPointsR;
    private ComputeBuffer DetailWorleyPointsG;

    void Start()
    {
        UnityEngine.Random.InitState(seed);

        if (ShapeRenderTexture != null)
        {
            ShapeRenderTexture.Release(); //Falls per Editor erstellt
        }

        ShapeRenderTexture = new RenderTexture(ShapeTextureSize, ShapeTextureSize, 0, RenderTextureFormat.ARGBFloat)
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

        DetailRenderTexture = new RenderTexture(32, 32, 0, RenderTextureFormat.ARGBFloat)
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

        PerlinNoiseBuffer = new ComputeBuffer(ShapeTextureSize * ShapeTextureSize * ShapeTextureSize, sizeof(float));
        float[] PerlinNoise = CreatePerlinNoise(ShapeRenderTexture);
        PerlinNoiseBuffer.SetData(PerlinNoise);

        ShapeWorleyPointsA = new ComputeBuffer((int)Math.Pow(ShapeWosleyCellCount[0], 3), sizeof(float) * 3);
        Vector3[] WorleyPoints = CreateWorleyPoints(ShapeTextureSize, ShapeWosleyCellCount[0]);
        ShapeWorleyPointsA.SetData(WorleyPoints);

        ShapeWorleyPointsR = new ComputeBuffer((int)Math.Pow(ShapeWosleyCellCount[1], 3), sizeof(float) * 3);
        WorleyPoints = CreateWorleyPoints(ShapeTextureSize, ShapeWosleyCellCount[1]);
        ShapeWorleyPointsR.SetData(WorleyPoints);

        ShapeWorleyPointsG = new ComputeBuffer((int)Math.Pow(ShapeWosleyCellCount[2], 3), sizeof(float) * 3);
        WorleyPoints = CreateWorleyPoints(ShapeTextureSize, ShapeWosleyCellCount[2]);
        ShapeWorleyPointsG.SetData(WorleyPoints);

        ShapeWorleyPointsB = new ComputeBuffer((int)Math.Pow(ShapeWosleyCellCount[3], 3), sizeof(float) * 3);
        WorleyPoints = CreateWorleyPoints(ShapeTextureSize, ShapeWosleyCellCount[3]);
        ShapeWorleyPointsB.SetData(WorleyPoints);

        CurrentKernel = WorleyComputer.FindKernel("GenerateWorley");
        WorleyComputer.SetInt("Mode", 0);
        WorleyComputer.SetTexture(CurrentKernel, "ShapeRenderTexture", ShapeRenderTexture);
        WorleyComputer.SetTexture(CurrentKernel, "DetailRenderTexture", DetailRenderTexture);
        WorleyComputer.SetBuffer(CurrentKernel, "PerlinNoise", PerlinNoiseBuffer);
        WorleyComputer.SetBuffer(CurrentKernel, "ShapeWorleyPointsA", ShapeWorleyPointsA);
        WorleyComputer.SetBuffer(CurrentKernel, "ShapeWorleyPointsR", ShapeWorleyPointsR);
        WorleyComputer.SetBuffer(CurrentKernel, "ShapeWorleyPointsG", ShapeWorleyPointsG);
        WorleyComputer.SetBuffer(CurrentKernel, "ShapeWorleyPointsB", ShapeWorleyPointsB);
        WorleyComputer.SetInt("TextureSize", ShapeTextureSize);


        int CurCellsPerRow = ShapeWosleyCellCount[0];
        WorleyComputer.SetInt("CellsPerRow", CurCellsPerRow);
        WorleyComputer.SetInt("CurLayer", 0);
        WorleyComputer.Dispatch(CurrentKernel, CurCellsPerRow, CurCellsPerRow, CurCellsPerRow);

        CurCellsPerRow = ShapeWosleyCellCount[1];
        WorleyComputer.SetInt("CellsPerRow", CurCellsPerRow);
        WorleyComputer.SetInt("CurLayer", 1);
        WorleyComputer.Dispatch(CurrentKernel, CurCellsPerRow, CurCellsPerRow, CurCellsPerRow);

        CurCellsPerRow = ShapeWosleyCellCount[2];
        WorleyComputer.SetInt("CellsPerRow", CurCellsPerRow);
        WorleyComputer.SetInt("CurLayer", 2);
        WorleyComputer.Dispatch(CurrentKernel, CurCellsPerRow, CurCellsPerRow, CurCellsPerRow);

        CurCellsPerRow = ShapeWosleyCellCount[3];
        WorleyComputer.SetInt("CellsPerRow", CurCellsPerRow);
        WorleyComputer.SetInt("CurLayer", 3);
        WorleyComputer.Dispatch(CurrentKernel, CurCellsPerRow, CurCellsPerRow, CurCellsPerRow);

        CurrentKernel = WorleyComputer.FindKernel("CombineWorley");
        WorleyComputer.SetTexture(CurrentKernel, "ShapeRenderTexture", ShapeRenderTexture);
        WorleyComputer.SetTexture(CurrentKernel, "DetailRenderTexture", DetailRenderTexture);
        WorleyComputer.SetFloats("fmbWeights", fBmWeights[0], fBmWeights[1], fBmWeights[2], fBmWeights[3]);
        WorleyComputer.SetInt("Mode", 0);

        int groups = Mathf.CeilToInt(ShapeTextureSize / 8f);
        WorleyComputer.Dispatch(CurrentKernel, groups, groups, groups);

        /////////////////////////////DETAIL///////////////////////////////////////////////
        DetailWorleyPointsR = new ComputeBuffer((int)Math.Pow(ShapeWosleyCellCount[1], 3), sizeof(float) * 3);
        WorleyPoints = CreateWorleyPoints(32, ShapeWosleyCellCount[0]);
        DetailWorleyPointsR.SetData(WorleyPoints);

        DetailWorleyPointsG = new ComputeBuffer((int)Math.Pow(ShapeWosleyCellCount[2], 3), sizeof(float) * 3);
        WorleyPoints = CreateWorleyPoints(32, ShapeWosleyCellCount[1]);
        DetailWorleyPointsG.SetData(WorleyPoints);

        CurrentKernel = WorleyComputer.FindKernel("GenerateWorley");

        WorleyComputer.SetBuffer(CurrentKernel, "DetailWorleyPointsR", DetailWorleyPointsR);
        WorleyComputer.SetBuffer(CurrentKernel, "DetailWorleyPointsG", DetailWorleyPointsG);
        CurCellsPerRow = ShapeWosleyCellCount[0];
        WorleyComputer.SetInt("TextureSize", 32);
        WorleyComputer.SetInt("CellsPerRow", CurCellsPerRow);
        WorleyComputer.SetInt("CurLayer", 0);
        WorleyComputer.SetInt("Mode", 1);
        WorleyComputer.Dispatch(CurrentKernel, CurCellsPerRow, CurCellsPerRow, CurCellsPerRow);

        CurCellsPerRow = ShapeWosleyCellCount[1];
        WorleyComputer.SetInt("CellsPerRow", CurCellsPerRow);
        WorleyComputer.SetInt("CurLayer", 1);
        WorleyComputer.Dispatch(CurrentKernel, CurCellsPerRow, CurCellsPerRow, CurCellsPerRow);

        DetailWorleyPointsG = new ComputeBuffer((int)Math.Pow(ShapeWosleyCellCount[2], 3), sizeof(float) * 3);
        WorleyPoints = CreateWorleyPoints(32, ShapeWosleyCellCount[2]);
        DetailWorleyPointsG.SetData(WorleyPoints);

        WorleyComputer.SetBuffer(CurrentKernel, "DetailWorleyPointsG", DetailWorleyPointsG);

        CurCellsPerRow = ShapeWosleyCellCount[2];
        WorleyComputer.SetInt("CellsPerRow", CurCellsPerRow);
        WorleyComputer.SetInt("CurLayer", 2);
        WorleyComputer.Dispatch(CurrentKernel, CurCellsPerRow, CurCellsPerRow, CurCellsPerRow);

        CurrentKernel = WorleyComputer.FindKernel("CombineWorley");
        WorleyComputer.SetTexture(CurrentKernel, "DetailRenderTexture", DetailRenderTexture);
        WorleyComputer.SetTexture(CurrentKernel, "ShapeRenderTexture", ShapeRenderTexture);
        WorleyComputer.SetInt("Mode", 1);
        WorleyComputer.SetFloats("fmbWeights", fBmWeights[0], fBmWeights[1], fBmWeights[2], fBmWeights[3]);

        groups = Mathf.CeilToInt(32 / 8f);
        WorleyComputer.Dispatch(CurrentKernel, groups, groups, groups);

        ShapeRenderTexture.GenerateMips();


        ShapeWorleyPointsA.Dispose();
        ShapeWorleyPointsR.Dispose();
        ShapeWorleyPointsG.Dispose();
        ShapeWorleyPointsB.Dispose();
        DetailWorleyPointsR.Dispose();
        DetailWorleyPointsG.Dispose();
        PerlinNoiseBuffer.Dispose();
    }

    private float[] CreatePerlinNoise(RenderTexture renderTexture)
    {
        int curIndex = 0;
        float[] PerlinValues = new float[renderTexture.width * renderTexture.height * renderTexture.depth];
        for (int x = 0; x < renderTexture.width; x++)
            for (int y = 0; y < renderTexture.height; y++)
                for (int z = 0; z < renderTexture.depth; z++)
                {
                    PerlinValues[curIndex++] = (Perlin.Noise(x / 128, y / 128, z / 128) + 1) / 2; //Function is -1 till +1 we want 0 to 1                                                              
                }
        return PerlinValues;
    }

    //Creates Randomly positioned Points in each of the cells
    private Vector3[] CreateWorleyPoints(int TextureSize, int CellsPerRow)
    {
        int curIndex = 0;
        Vector3[] WorleyPoints = new Vector3[(int)Math.Pow(CellsPerRow, 3)];

        for (int x = 0; x < CellsPerRow; x++)
            for (int y = 0; y < CellsPerRow; y++)
                for (int z = 0; z < CellsPerRow; z++)
                {
                    float cellSize = (float)TextureSize / CellsPerRow;

                    float lowerX = Mathf.Floor(x * cellSize);
                    float upperX = Mathf.Min(Mathf.Ceil((x + 1) * cellSize), TextureSize);
                    float sizeX = upperX - lowerX;

                    float lowerY = Mathf.Floor(y * cellSize);
                    float upperY = Mathf.Min(Mathf.Ceil((y + 1) * cellSize), TextureSize);
                    float sizeY = upperY - lowerY;

                    float lowerZ = Mathf.Floor(z * cellSize);
                    float upperZ = Mathf.Min(Mathf.Ceil((z + 1) * cellSize), TextureSize);
                    float sizeZ = upperZ - lowerZ;

                    WorleyPoints[curIndex++] = new Vector3(
                        lowerX + UnityEngine.Random.Range(0f, sizeX),
                        lowerY + UnityEngine.Random.Range(0f, sizeY),
                        lowerZ + UnityEngine.Random.Range(0f, sizeZ)
                    );
                }
        return WorleyPoints;
    }
}
