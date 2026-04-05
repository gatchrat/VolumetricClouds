using UnityEngine;
using System;
using UnityEngine.Experimental.Rendering;
using System.Collections.Generic;
using UnityEngine.SocialPlatforms;

public class CloudManager : MonoBehaviour
{
    public static int seed = 42;
    public int ShapeTextureSize = 128;
    public RenderTexture ShapeRenderTexture;
    public int[] ShapeWosleyCellCount = new int[] { 16, 24, 32, 48 };
    public float[] fBmWeights = new float[] { 1, 0.5f, 0.2f, 0.2f };
    public ComputeShader WorleyComputer;
    public float DensityThreshold = 0.7f; //Used in Renderpass
    public int StepCount = 4;
    public Transform CloudsBounds;
    //Buffer
    private ComputeBuffer ShapeWorleyPointsA;
    private ComputeBuffer ShapeWorleyPointsR;
    private ComputeBuffer ShapeWorleyPointsG;
    private ComputeBuffer ShapeWorleyPointsB;

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
            filterMode = FilterMode.Bilinear
        };

        ShapeRenderTexture.Create();

        int CurrentKernel;

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
        WorleyComputer.SetTexture(CurrentKernel, "ShapeRenderTexture", ShapeRenderTexture);
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
        CurCellsPerRow = ShapeWosleyCellCount[3];
        WorleyComputer.SetInt("CellsPerRow", CurCellsPerRow);
        WorleyComputer.SetInt("CurLayer", 3);
        WorleyComputer.SetFloats("fmbWeights", fBmWeights[0], fBmWeights[1], fBmWeights[2], fBmWeights[3]);
        WorleyComputer.Dispatch(CurrentKernel, CurCellsPerRow, CurCellsPerRow, CurCellsPerRow);


        ShapeWorleyPointsA.Dispose();
        ShapeWorleyPointsR.Dispose();
        ShapeWorleyPointsG.Dispose();
        ShapeWorleyPointsB.Dispose();

        Debug.Log(WorleyPoints[0]);
        Debug.Log(WorleyPoints[1]);
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
