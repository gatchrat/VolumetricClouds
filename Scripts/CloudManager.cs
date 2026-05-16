using UnityEngine;
using System;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.GlobalIllumination;
using Unity.Mathematics;
using System.Collections.Generic;
using UnityEngine.UIElements;
using Unity.VisualScripting;
using System.Linq;

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
    public float AmbiantLight;
    public float SunBlindingEffectSize;
    public float _pad4;
    public float _pad5;
    public float3 SunColor;
    public float SunIntensity;
    public float3 SunDirection;
    public float CLOUD_TOP;
    public float CLOUD_BOTTOM;

}

public class CloudManager : MonoBehaviour
{
    [Header("Settings (Use Light settings color and intensity)")]
    [Range(1f, 10f)]
    public float BrightnesMultiplier = 1f;
    [Range(0.1f, 20f)]
    public float CloudSunBlocking = 1f;
    [Range(0f, 1)]
    public float DarkOutline = 0.35f;
    [Range(0f, 5)]
    public float AmbiantLight = 100f;
    [Range(500f, 10000f)]
    public float CloudThickness;
    [Range(0f, 1f)]
    public float CloudDensity = 0.55f;
    public bool Lightning = false;
    [Range(0f, 1f)]
    public float SunBlindingEffectStrengh = 1f;
    [Range(0f, 10f)]
    public float CloudMovementSpeed = 1f;
    public Vector3 Scale = new Vector3(1, 1, 1);
    [Header("Technical Stuff")]
    public int seed = 42;
    public int ShapeTextureSize = 128;
    public RenderTexture ShapeRenderTexture;
    public RenderTexture DetailRenderTexture;
    public Texture2D BlueNoise;
    public int[] ShapeWosleyCellCount = new int[] { 4, 6, 8, 12 };
    public int[] DetailWosleyCellCount = new int[] { 16, 24, 32 };
    public float[] fBmWeights = new float[] { 1, 0.5f, 0.2f };
    public ComputeShader WorleyComputer;
    private float CLOUD_BOTTOM = 500f; public float3 Offset;
    public Transform CloudsBounds;
    public Transform Sun;
    private Light SunLight;
    public List<Lightning> Lightnings;

    public CloudSettings cloudSettings;
    private float3 CurFrameMovement;
    private float LightningTimer = 3f;
    private int curLightningLayer = 0;
    private float flickerTimer = 0.1f;
    private List<int> LightningCounttLayer;
    public GameObject LightningAudioPrefab;
    public LineRenderer LightningLine;

    void Update()
    {
        cloudSettings.BrightnesMultiplier = BrightnesMultiplier;
        cloudSettings.TransmittanceFalloff = CloudSunBlocking;
        cloudSettings.PowderStrength = DarkOutline;
        cloudSettings.AmbiantLight = AmbiantLight;
        cloudSettings.SunBlindingEffectSize = SunBlindingEffectStrengh;
        cloudSettings.Scale = Scale;
        cloudSettings.CloudDensity = CloudDensity;
        CurFrameMovement = new Vector3(1, 0, 1) * ((Time.deltaTime / 60) / 3) * CloudMovementSpeed;
        Offset += CurFrameMovement;
        cloudSettings.Offset = Offset;
        cloudSettings.SunDirection = Sun.transform.forward * -1;
        cloudSettings.SunColor = new Vector3(SunLight.color.r, SunLight.color.g, SunLight.color.b);
        cloudSettings.SunIntensity = SunLight.intensity;
        cloudSettings.CLOUD_TOP = CloudThickness;
        cloudSettings.CLOUD_BOTTOM = CLOUD_BOTTOM;

        if (Lightning)
        {
            handleLightning();
        }

    }
    private void handleLightning()
    {
        LightningTimer -= Time.deltaTime;
        //layer 1
        if (LightningTimer < 3f && LightningTimer > 1.5f && curLightningLayer == 0)
        {
            CreateLightning();
        }
        //layer 2
        if (LightningTimer < 2.75f && LightningTimer > 1.5f && curLightningLayer == 1)
        {
            AddLightningLayer();
        }
        //layer 3
        if (LightningTimer < 2.5f && LightningTimer > 1.5f && curLightningLayer == 2)
        {
            AddLightningLayer();
        }
        //flicker 4
        if (LightningTimer < 2.25f && curLightningLayer == 4)
        {
            flickerTimer -= Time.deltaTime;
            if (flickerTimer < 0)
            {
                flickerTimer = 0.1f;
                RemoveLightningLayer();
            }
        }
        if (LightningTimer < 2.25f && curLightningLayer == 3)
        {
            flickerTimer -= Time.deltaTime;
            if (flickerTimer < 0)
            {
                flickerTimer = 0.1f;
                AddLightningLayer();
            }
        }

        //undo 4
        if (LightningTimer < 1.5f && curLightningLayer == 4)
        {
            RemoveLightningLayer();
        }
        //undo 3
        if (LightningTimer < 1.25f && curLightningLayer == 3)
        {
            RemoveLightningLayer();
        }
        //undo 2
        if (LightningTimer < 1f && curLightningLayer == 2)
        {
            RemoveLightningLayer();
        }
        if (LightningTimer < 1f && curLightningLayer == 1)
        {
            LightningTimer = UnityEngine.Random.Range(1f, 5f) + 3f;
            RemoveLightningLayer();
        }


    }
    public float3 GetMovementOffset()
    {
        return CurFrameMovement;
    }

    void Start()
    {
        Lightning fake = new Lightning();
        fake.origin = new float3(0, -1, 0);
        fake.direction = new float3(0, -1, 0);

        Lightnings = new List<Lightning>();

        Lightnings.Add(fake);

        LightningCounttLayer = new List<int>();
        LightningCounttLayer.Add(1);

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

        ShapeRenderTexture = new RenderTexture(ShapeTextureSize, ShapeTextureSize, 0, GraphicsFormat.R8G8_UNorm) //INSANE Importance. No noticable difference to 32bit float in quality but 360 vs 620 fps
        {
            enableRandomWrite = true,
            dimension = UnityEngine.Rendering.TextureDimension.Tex3D,
            volumeDepth = ShapeTextureSize,
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Trilinear,
            useMipMap = false
        };
        ShapeRenderTexture.Create();

        DetailRenderTexture = new RenderTexture(32, 32, 0, GraphicsFormat.R8_UNorm)
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
        WorleyComputer.SetTexture(CurrentKernel, "ShapeRenderTexture", ShapeRenderTexture);
        WorleyComputer.SetInt("TextureSize", ShapeTextureSize);


        int CurCellsPerRow = ShapeWosleyCellCount[0];
        int groups = Mathf.CeilToInt(ShapeTextureSize / 8f);
        WorleyComputer.SetInt("CellsPerRow", CurCellsPerRow);
        WorleyComputer.SetFloats("fmbWeights", fBmWeights[0], fBmWeights[1], fBmWeights[2]);
        WorleyComputer.Dispatch(CurrentKernel, groups, groups, groups);

        /////////////////////////////DETAIL///////////////////////////////////////////////
        groups = Mathf.CeilToInt(32 / 8f);

        CurrentKernel = WorleyComputer.FindKernel("GenerateWorleyDetail");
        CurCellsPerRow = DetailWosleyCellCount[0];
        WorleyComputer.SetInt("TextureSize", 32);
        WorleyComputer.SetInt("CellsPerRow", CurCellsPerRow);
        WorleyComputer.SetTexture(CurrentKernel, "DetailRenderTexture", DetailRenderTexture);
        WorleyComputer.Dispatch(CurrentKernel, groups, groups, groups);
    }
    void CreateLightning()
    {
        float shellInner = 6410 * 100f;
        float shellOuter = 6410 * 100f;

        // Random direction on upper hemisphere (y > 0)
        float theta = UnityEngine.Random.Range(0f, Mathf.PI * 2f); // longitude
        float phi = UnityEngine.Random.Range(0f, Mathf.PI * 0.03f); // latitude, 0=top, PI/2=horizon

        float3 dir = new float3(
            Mathf.Sin(phi) * Mathf.Cos(theta),
            Mathf.Cos(phi),
            Mathf.Sin(phi) * Mathf.Sin(theta)
        );

        // Random radius within cloud shell
        float radius = UnityEngine.Random.Range(shellInner, shellOuter);

        Lightning Root = new Lightning();
        Root.origin = Unity.Mathematics.math.normalize(dir) * radius;
        Root.direction = new float3(UnityEngine.Random.Range(-1f, 1f), 0, UnityEngine.Random.Range(-1f, 1f));
        Root.length = 10000;
        Lightnings.Add(Root);
        LightningCounttLayer.Add(1);
        curLightningLayer = 1;
        AddLightningToLineRenderer(Root);


        Vector3 Position = Unity.Mathematics.math.normalize(Lightnings[0].origin) * 100;
        GameObject.Instantiate(LightningAudioPrefab, Position, this.transform.rotation);
        //https://freesound.org/people/fattirewhitey/sounds/523905/
    }
    private void AddLightningToLineRenderer(Lightning l)
    {
        LightningLine.widthMultiplier = 1;
        LightningLine.endWidth = 0.5f;
        LightningLine.positionCount += 1;
        float3 CameraPos = Camera.main.transform.position;
        LightningLine.SetPosition(LightningLine.positionCount - 1, (l.origin - new float3(0, 6300 * 100, 0)) * 0.01f + CameraPos);
        LightningLine.positionCount += 1;
        LightningLine.SetPosition(LightningLine.positionCount - 1, (l.origin - new float3(0, 6300 * 100, 0) + l.direction * l.length) * 0.01f + CameraPos);
    }
    private void RemoveLightningToLineRenderer(int Count)
    {
        LightningLine.positionCount -= 2 * Count;
    }
    private void AddLightningLayer()
    {
        int curCount = Lightnings.Count;
        int LastLayerCount = LightningCounttLayer.Last();
        List<Lightning> New = new List<Lightning>();
        for (int i = 0; i < LastLayerCount; i++)
        {
            New.AddRange(GetLightningLayer(Lightnings[curCount - 1 - i], 1));
        }
        LightningCounttLayer.Add(New.Count);
        Lightnings.AddRange(New);
        curLightningLayer += 1;
    }
    private void RemoveLightningLayer()
    {
        int LastLayerCount = LightningCounttLayer.Last();
        RemoveLightningToLineRenderer(LastLayerCount);
        for (int i = 0; i < LastLayerCount; i++)
        {
            Lightnings.RemoveAt(Lightnings.Count - 1);
        }
        LightningCounttLayer.RemoveAt(LightningCounttLayer.Count - 1);
        curLightningLayer -= 1;
    }
    List<Lightning> GetLightningLayer(Lightning l, int layers)
    {
        List<Lightning> ls = new List<Lightning>();
        if (layers == 0)
        {
            return ls;
        }
        Lightning A = new Lightning();
        A.origin = l.origin + l.direction * l.length;
        A.direction = l.direction + new float3(UnityEngine.Random.Range(0, 0.5f), 0, UnityEngine.Random.Range(-0.5f, 0.5f));
        A.direction = Unity.Mathematics.math.normalize(A.direction);
        A.length = l.length * UnityEngine.Random.Range(0.3f, 1f);
        ls.Add(A);

        AddLightningToLineRenderer(A);

        Lightning B = new Lightning();
        B.origin = l.origin + l.direction * l.length;
        B.direction = l.direction + new float3(UnityEngine.Random.Range(-0.5f, 0), 0, UnityEngine.Random.Range(-0.5f, 0.5f));
        B.direction = Unity.Mathematics.math.normalize(B.direction);
        B.length = l.length * UnityEngine.Random.Range(0.3f, 1f);
        ls.Add(B);
        AddLightningToLineRenderer(B);
        return ls;
    }
}
