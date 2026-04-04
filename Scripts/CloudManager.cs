using UnityEngine;
using System;
using UnityEngine.Experimental.Rendering;
using System.Collections.Generic;
using UnityEngine.SocialPlatforms;

public class CloudManager : MonoBehaviour
{
    public int ShapeTextureSize = 128;
    public RenderTexture ShapeRenderTexture;
    public int DetailTextureSize = 32;
    public RenderTexture DetailRenderTexture;
    public int[] ShapeWosleyCellSize = new int[] { 32, 16, 8, 4 };
    public int[] DetailWosleyCellSize = new int[] { 16, 8, 4 };
    public ComputeShader WorleyComputer;
    //Buffer
    private ComputeBuffer ShapeWorleyPointsA;

    void Start()
    {


        if (ShapeRenderTexture != null)
        {
            ShapeRenderTexture.Release(); //Falls per Editor erstellt
        }

        ShapeRenderTexture = new RenderTexture(ShapeTextureSize, ShapeTextureSize, 0, RenderTextureFormat.RFloat)
        {
            enableRandomWrite = true,
            dimension = UnityEngine.Rendering.TextureDimension.Tex3D,
            volumeDepth = ShapeTextureSize,
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear
        };

        ShapeRenderTexture.Create();

        int CurrentKernel;
        int CellsPerRow = ShapeTextureSize / ShapeWosleyCellSize[0];
        ShapeWorleyPointsA = new ComputeBuffer((int)Math.Pow(CellsPerRow, 3), sizeof(float) * 3);
        Vector3[] WorleyPoints = CreateWorleyPoints(ShapeTextureSize, ShapeWosleyCellSize[0]);
        ShapeWorleyPointsA.SetData(WorleyPoints);

        CurrentKernel = WorleyComputer.FindKernel("GenerateWorley");
        WorleyComputer.SetTexture(CurrentKernel, "ShapeRenderTexture", ShapeRenderTexture);
        WorleyComputer.SetBuffer(CurrentKernel, "ShapeWorleyPointsA", ShapeWorleyPointsA);
        WorleyComputer.SetInt("CellSize", ShapeWosleyCellSize[0]);
        WorleyComputer.SetInt("CellsPerRow", CellsPerRow);

        WorleyComputer.Dispatch(CurrentKernel, CellsPerRow, CellsPerRow, CellsPerRow);

        ShapeWorleyPointsA.Dispose();
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
