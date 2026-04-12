using UnityEngine;
using System;
using UnityEditor;
using Unity.VisualScripting;
using UnityEngine.UIElements;

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct CloudSettings
{
    public Vector3 Offset;
    public float _pad0;
    public Vector3 Scale;
    public float _pad1;
    public float DensityThreshold;
    public float DensityMultiplier;
    public int StepCount;
}

public class CloudManager : MonoBehaviour
{
    public int seed = 42;
    public int ShapeTextureSize = 128;
    public RenderTexture ShapeRenderTexture;
    public RenderTexture DetailRenderTexture;
    public Texture2D BlueNoise;
    public int[] ShapeWosleyCellCount = new int[] { 16, 24, 32, 48 };
    public float[] fBmWeights = new float[] { 1, 0.5f, 0.2f, 0.2f };
    public ComputeShader WorleyComputer;
    public float DensityThreshold = 0.7f; //Used in Renderpass
    public int StepCount = 4;
    public float DensityMultiplier = 1f;
    public Vector3 Scale = new Vector3(1, 1, 1);
    public Vector3 Offset;
    public Transform CloudsBounds;
    public Transform Sun;

    public CloudSettings cloudSettings;
    //Buffer
    private ComputeBuffer PerlinNoiseBuffer;
    private ComputeBuffer ShapeWorleyPointsA;
    private ComputeBuffer ShapeWorleyPointsR;
    private ComputeBuffer ShapeWorleyPointsG;
    private ComputeBuffer ShapeWorleyPointsB;
    private ComputeBuffer DetailWorleyPoints;

    void Update()
    {
        cloudSettings.DensityMultiplier = DensityMultiplier;
        cloudSettings.StepCount = StepCount;
        cloudSettings.Scale = Scale;
        cloudSettings.DensityThreshold = DensityThreshold;
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

        ShapeWorleyPointsA = new ComputeBuffer((int)Math.Pow(ShapeWosleyCellCount[0] + 2, 3), sizeof(float) * 3);
        Vector3[] WorleyPoints = CreateWorleyPoints(ShapeTextureSize, ShapeWosleyCellCount[0]);
        ShapeWorleyPointsA.SetData(WorleyPoints);

        ShapeWorleyPointsR = new ComputeBuffer((int)Math.Pow(ShapeWosleyCellCount[1] + 2, 3), sizeof(float) * 3);
        WorleyPoints = CreateWorleyPoints(ShapeTextureSize, ShapeWosleyCellCount[1]);
        ShapeWorleyPointsR.SetData(WorleyPoints);

        ShapeWorleyPointsG = new ComputeBuffer((int)Math.Pow(ShapeWosleyCellCount[2] + 2, 3), sizeof(float) * 3);
        WorleyPoints = CreateWorleyPoints(ShapeTextureSize, ShapeWosleyCellCount[2]);
        ShapeWorleyPointsG.SetData(WorleyPoints);

        ShapeWorleyPointsB = new ComputeBuffer((int)Math.Pow(ShapeWosleyCellCount[3] + 2, 3), sizeof(float) * 3);
        WorleyPoints = CreateWorleyPoints(ShapeTextureSize, ShapeWosleyCellCount[3]);
        ShapeWorleyPointsB.SetData(WorleyPoints);

        CurrentKernel = WorleyComputer.FindKernel("GenerateWorley");
        WorleyComputer.SetInt("Mode", 0);
        WorleyComputer.SetTexture(CurrentKernel, "ShapeRenderTexture", ShapeRenderTexture);
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
        WorleyComputer.SetFloats("fmbWeights", fBmWeights[0], fBmWeights[1], fBmWeights[2], fBmWeights[3]);
        WorleyComputer.SetInt("Mode", 0);

        int groups = Mathf.CeilToInt(ShapeTextureSize / 8f);
        WorleyComputer.Dispatch(CurrentKernel, groups, groups, groups);

        /////////////////////////////DETAIL///////////////////////////////////////////////
        DetailWorleyPoints = new ComputeBuffer((int)Math.Pow(ShapeWosleyCellCount[0] + 2, 3), sizeof(float) * 3);
        WorleyPoints = CreateWorleyPoints(32, ShapeWosleyCellCount[0]);
        DetailWorleyPoints.SetData(WorleyPoints);

        CurrentKernel = WorleyComputer.FindKernel("GenerateWorleyDetail");

        WorleyComputer.SetBuffer(CurrentKernel, "DetailWorleyPoints", DetailWorleyPoints);
        CurCellsPerRow = ShapeWosleyCellCount[0];
        WorleyComputer.SetInt("TextureSize", 32);
        WorleyComputer.SetInt("CellsPerRow", CurCellsPerRow);
        WorleyComputer.SetInt("CurLayer", 0);
        WorleyComputer.SetInt("Mode", 1);
        WorleyComputer.SetTexture(CurrentKernel, "DetailRenderTexture", DetailRenderTexture);
        WorleyComputer.Dispatch(CurrentKernel, CurCellsPerRow, CurCellsPerRow, CurCellsPerRow);

        DetailWorleyPoints.Dispose();
        DetailWorleyPoints = new ComputeBuffer((int)Math.Pow(ShapeWosleyCellCount[1] + 2, 3), sizeof(float) * 3);
        WorleyPoints = CreateWorleyPoints(32, ShapeWosleyCellCount[1]);
        DetailWorleyPoints.SetData(WorleyPoints);
        WorleyComputer.SetBuffer(CurrentKernel, "DetailWorleyPoints", DetailWorleyPoints);

        CurCellsPerRow = ShapeWosleyCellCount[1];
        WorleyComputer.SetInt("CellsPerRow", CurCellsPerRow);
        WorleyComputer.SetInt("CurLayer", 1);
        WorleyComputer.Dispatch(CurrentKernel, CurCellsPerRow, CurCellsPerRow, CurCellsPerRow);

        DetailWorleyPoints.Dispose();
        DetailWorleyPoints = new ComputeBuffer((int)Math.Pow(ShapeWosleyCellCount[2] + 2, 3), sizeof(float) * 3);
        WorleyPoints = CreateWorleyPoints(32, ShapeWosleyCellCount[2]);
        DetailWorleyPoints.SetData(WorleyPoints);
        WorleyComputer.SetBuffer(CurrentKernel, "DetailWorleyPoints", DetailWorleyPoints);

        CurCellsPerRow = ShapeWosleyCellCount[2];
        WorleyComputer.SetInt("CellsPerRow", CurCellsPerRow);
        WorleyComputer.SetInt("CurLayer", 2);
        WorleyComputer.Dispatch(CurrentKernel, CurCellsPerRow, CurCellsPerRow, CurCellsPerRow);

        CurrentKernel = WorleyComputer.FindKernel("CombineWorleyDetail");
        WorleyComputer.SetTexture(CurrentKernel, "DetailRenderTexture", DetailRenderTexture);
        WorleyComputer.SetInt("Mode", 1);
        WorleyComputer.SetFloats("fmbWeights", fBmWeights[0], fBmWeights[1], fBmWeights[2], fBmWeights[3]);

        groups = Mathf.CeilToInt(32 / 8f);
        WorleyComputer.Dispatch(CurrentKernel, groups, groups, groups);

        ShapeRenderTexture.GenerateMips();
        DetailRenderTexture.GenerateMips();

        ShapeWorleyPointsA.Dispose();
        ShapeWorleyPointsR.Dispose();
        ShapeWorleyPointsG.Dispose();
        ShapeWorleyPointsB.Dispose();
        DetailWorleyPoints.Dispose();
        PerlinNoiseBuffer.Dispose();
    }

    private float[] CreatePerlinNoise(RenderTexture renderTexture)
    {
        int curIndex = 0;
        float[] PerlinValues = new float[ShapeTextureSize * ShapeTextureSize * ShapeTextureSize];
        FastNoiseLite noise = new FastNoiseLite();
        float noiseScale = 10f;
        noise.SetNoiseType(FastNoiseLite.NoiseType.Perlin);

        for (int x = 0; x < ShapeTextureSize; x++)
            for (int y = 0; y < ShapeTextureSize; y++)
                for (int z = 0; z < ShapeTextureSize; z++)
                {
                    PerlinValues[curIndex++] = (noise.GetNoise(x * noiseScale, y * noiseScale, z * noiseScale) + 1) / 2; //Function is -1 till +1 we want 0 to 1      
                    if (PerlinValues[curIndex - 1] > 1 || PerlinValues[curIndex - 1] <= 0)
                    {
                        Debug.Log(PerlinValues[curIndex - 1]);
                    }
                }
        return PerlinValues;
    }

    //Creates Randomly positioned Points in each of the cells
    private Vector3[] CreateWorleyPoints(int TextureSize, int CellsPerRow)
    {
        int curIndex = 0;
        Vector3[] WorleyPoints = new Vector3[(int)Math.Pow(CellsPerRow + 2, 3)];
        float cellSize = (float)TextureSize / CellsPerRow;

        for (int x = -1; x < CellsPerRow + 1; x++)
            for (int y = -1; y < CellsPerRow + 1; y++)
                for (int z = -1; z < CellsPerRow + 1; z++)
                {


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
        // Helper to convert (x,y,z) in [-1, CellsPerRow] to flat index
        int ToIndex(int x, int y, int z) =>
            (x + 1) * (CellsPerRow + 2) * (CellsPerRow + 2) +
            (y + 1) * (CellsPerRow + 2) +
            (z + 1);

        // Second pass: override border cells with wrapped core points + tile offset
        for (int x = -1; x < CellsPerRow + 1; x++)
            for (int y = -1; y < CellsPerRow + 1; y++)
                for (int z = -1; z < CellsPerRow + 1; z++)
                {
                    bool isBorder = x == -1 || x == CellsPerRow ||
                                    y == -1 || y == CellsPerRow ||
                                    z == -1 || z == CellsPerRow;
                    if (!isBorder) continue;

                    int cx = ((x % CellsPerRow) + CellsPerRow) % CellsPerRow;
                    int cy = ((y % CellsPerRow) + CellsPerRow) % CellsPerRow;
                    int cz = ((z % CellsPerRow) + CellsPerRow) % CellsPerRow;

                    Vector3 core = WorleyPoints[ToIndex(cx, cy, cz)];

                    float offsetX = (x < 0 ? -1 : x >= CellsPerRow ? 1 : 0) * TextureSize;
                    float offsetY = (y < 0 ? -1 : y >= CellsPerRow ? 1 : 0) * TextureSize;
                    float offsetZ = (z < 0 ? -1 : z >= CellsPerRow ? 1 : 0) * TextureSize;

                    WorleyPoints[ToIndex(x, y, z)] = core + new Vector3(offsetX, offsetY, offsetZ);
                }
        return WorleyPoints;
    }
}
