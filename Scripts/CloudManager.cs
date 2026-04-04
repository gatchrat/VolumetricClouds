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
    public int DetailTextureSize = 32;
    public RenderTexture DetailRenderTexture;
    public int[] ShapeWosleyCellSize = new int[] { 32, 16, 8, 4 };
    public int[] DetailWosleyCellSize = new int[] { 16, 8, 4 };
    public ComputeShader WorleyComputer;
    //Buffer
    private ComputeBuffer ShapeWorleyPointsA;
    private ComputeBuffer ShapeWorleyPointsR;
    private ComputeBuffer ShapeWorleyPointsG;
    private ComputeBuffer ShapeWorleyPointsB;
    private int[] CellsPerRow;


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
        CellsPerRow = new int[4];
        CellsPerRow[0] = ShapeTextureSize / ShapeWosleyCellSize[0];
        CellsPerRow[1] = ShapeTextureSize / ShapeWosleyCellSize[1];
        CellsPerRow[2] = ShapeTextureSize / ShapeWosleyCellSize[2];
        CellsPerRow[3] = ShapeTextureSize / ShapeWosleyCellSize[3];

        ShapeWorleyPointsA = new ComputeBuffer((int)Math.Pow(CellsPerRow[0], 3), sizeof(float) * 3);
        Vector3[] WorleyPoints = CreateWorleyPoints(ShapeTextureSize, ShapeWosleyCellSize[0]);
        ShapeWorleyPointsA.SetData(WorleyPoints);

        ShapeWorleyPointsR = new ComputeBuffer((int)Math.Pow(CellsPerRow[1], 3), sizeof(float) * 3);
        WorleyPoints = CreateWorleyPoints(ShapeTextureSize, ShapeWosleyCellSize[1]);
        ShapeWorleyPointsR.SetData(WorleyPoints);

        ShapeWorleyPointsG = new ComputeBuffer((int)Math.Pow(CellsPerRow[2], 3), sizeof(float) * 3);
        WorleyPoints = CreateWorleyPoints(ShapeTextureSize, ShapeWosleyCellSize[2]);
        ShapeWorleyPointsG.SetData(WorleyPoints);

        ShapeWorleyPointsB = new ComputeBuffer((int)Math.Pow(CellsPerRow[3], 3), sizeof(float) * 3);
        WorleyPoints = CreateWorleyPoints(ShapeTextureSize, ShapeWosleyCellSize[3]);
        ShapeWorleyPointsB.SetData(WorleyPoints);

        CurrentKernel = WorleyComputer.FindKernel("GenerateWorley");
        WorleyComputer.SetTexture(CurrentKernel, "ShapeRenderTexture", ShapeRenderTexture);
        WorleyComputer.SetBuffer(CurrentKernel, "ShapeWorleyPointsA", ShapeWorleyPointsA);
        WorleyComputer.SetBuffer(CurrentKernel, "ShapeWorleyPointsR", ShapeWorleyPointsR);
        WorleyComputer.SetBuffer(CurrentKernel, "ShapeWorleyPointsG", ShapeWorleyPointsG);
        WorleyComputer.SetBuffer(CurrentKernel, "ShapeWorleyPointsB", ShapeWorleyPointsB);


        int CurCellsPerRow = CellsPerRow[0];
        WorleyComputer.SetInt("CellsPerRow", CurCellsPerRow);
        WorleyComputer.SetInt("CellSize", ShapeWosleyCellSize[0]);
        WorleyComputer.SetInt("CurLayer", 0);
        WorleyComputer.Dispatch(CurrentKernel, CurCellsPerRow, CurCellsPerRow, CurCellsPerRow);

        CurCellsPerRow = CellsPerRow[1];
        WorleyComputer.SetInt("CellsPerRow", CurCellsPerRow);
        WorleyComputer.SetInt("CellSize", ShapeWosleyCellSize[1]);
        WorleyComputer.SetInt("CurLayer", 1);
        WorleyComputer.Dispatch(CurrentKernel, CurCellsPerRow, CurCellsPerRow, CurCellsPerRow);

        CurCellsPerRow = CellsPerRow[2];
        WorleyComputer.SetInt("CellsPerRow", CurCellsPerRow);
        WorleyComputer.SetInt("CellSize", ShapeWosleyCellSize[2]);
        WorleyComputer.SetInt("CurLayer", 2);
        WorleyComputer.Dispatch(CurrentKernel, CurCellsPerRow, CurCellsPerRow, CurCellsPerRow);

        CurCellsPerRow = CellsPerRow[3];
        WorleyComputer.SetInt("CellsPerRow", CurCellsPerRow);
        WorleyComputer.SetInt("CellSize", ShapeWosleyCellSize[3]);
        WorleyComputer.SetInt("CurLayer", 3);
        WorleyComputer.Dispatch(CurrentKernel, CurCellsPerRow, CurCellsPerRow, CurCellsPerRow);


        ShapeWorleyPointsA.Dispose();
        ShapeWorleyPointsR.Dispose();
        ShapeWorleyPointsG.Dispose();
        ShapeWorleyPointsB.Dispose();

        Debug.Log(WorleyPoints[0]);
        Debug.Log(WorleyPoints[1]);
    }

    //Creates Randomly positioned Points in each of the cells
    private Vector3[] CreateWorleyPoints(int TextureSize, int CellSize)
    {
        int curIndex = 0;
        int CellsPerRow = TextureSize / CellSize;
        Debug.Log($"Generating Worley Points. {CellsPerRow} Cells Per Row. {TextureSize} TextureSize {CellSize} CelllSize");
        Vector3[] WorleyPoints = new Vector3[(int)Math.Pow(CellsPerRow, 3)];
        for (int x = 0; x < CellsPerRow; x++)
        {
            for (int y = 0; y < CellsPerRow; y++)
            {
                for (int z = 0; z < CellsPerRow; z++)
                {
                    WorleyPoints[curIndex] = new Vector3(x * CellSize + UnityEngine.Random.Range(0, CellSize), y * CellSize + UnityEngine.Random.Range(0, CellSize), z * CellSize + UnityEngine.Random.Range(0, CellSize));
                    curIndex++;
                }
            }
        }
        return WorleyPoints;
    }
}
