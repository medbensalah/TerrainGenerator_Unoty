using System;
using System.Threading;
using System.Threading.Tasks;
using NaughtyAttributes;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Serialization;
using Random = System.Random;

[ExecuteInEditMode]
public class TerrainGenMaster : MonoBehaviour
{
    [SerializeField] private ComputeShader _perlinShader;
    [SerializeField] private ComputeShader _blurShader;
    [SerializeField] private ComputeShader _colorShader;
    [SerializeField] private Terrain _terrain;
    private int _perlinShaderHandle;
    private int _heightCorrectionShaderHandle;
    private int _blurShaderHandle;
    private int _colorShaderHandle;
    private Texture2D _terrainHeightMap;

    private Random _prng;


    [Header("Global Terrain Settings")]
    [OnValueChanged("CreateHeightTexture")]
    [OnValueChanged("RunHeightShader")]
    [SerializeField]
    private float _terrainSize = 1024.0f;

    [OnValueChanged("RunHeightShader")] [SerializeField]
    private int _seed = 12345;

    [OnValueChanged("RunHeightShader")] [SerializeField] 
    private float _height = 256f;
    private RenderTexture _heightMap;
    private RenderTexture _colorMap;

    /* ***************************************************************************************** */
    /* *                                     Perlin noise                                      * */
    /* ***************************************************************************************** */
    [Foldout(name: "Perlin Noise Settings")]
    [OnValueChanged("CreateHeightTexture")]
    [OnValueChanged("RunHeightShader")]
    [SerializeField]
    private bool _perlinNoise = true;

    [Foldout(name: "Perlin Noise Settings")] [OnValueChanged("RunHeightShader")] [SerializeField]
    private int _octaves = 4;

    [Foldout(name: "Perlin Noise Settings")] [OnValueChanged("RunHeightShader")] [SerializeField] [Range(0.0f, 1.0f)]
    private float _persistence = 0.5f;

    [Foldout(name: "Perlin Noise Settings")] [OnValueChanged("RunHeightShader")] [SerializeField]
    private float _lacunarity = 2.0f;

    [Foldout(name: "Perlin Noise Settings")] [OnValueChanged("RunHeightShader")] [SerializeField]
    private float _scale = 10.0f;

    [Foldout(name: "Perlin Noise Settings")] [OnValueChanged("RunHeightShader")] [SerializeField]
    private Vector2 _offset = Vector2.zero;

    [Foldout(name: "Perlin Noise Settings")]
    [SerializeField]
    private AnimationCurve _heightCurve = AnimationCurve.Linear(0, 0, 1, 1);


    /* ***************************************************************************************** */
    /* *                                         Blur                                          * */
    /* ***************************************************************************************** */
    [Foldout(name: "Blur Settings")]
    [OnValueChanged("CreateHeightTexture")]
    [OnValueChanged("RunHeightShader")]
    [SerializeField]
    private bool _blur = true;

    [Foldout(name: "Blur Settings")] [OnValueChanged("RunHeightShader")] [SerializeField]
    private int _iterations = 1;

    /* ***************************************************************************************** */
    /* *                                        Color                                          * */
    /* ***************************************************************************************** */
    [Foldout(name: "Color Settings")] [OnValueChanged("RunColorShader")] [SerializeField]
    private Color _seaColor = new Color(0.43f, 0.73f, 1f);

    [Foldout(name: "Color Settings")] [OnValueChanged("RunColorShader")] [SerializeField]
    private Color _shoreColor = new Color(1f, 0.88f, 0.6f);

    [Foldout(name: "Color Settings")] [OnValueChanged("RunColorShader")] [SerializeField]
    private Color _grassColor = new Color(0.41f, 0.55f, 0.17f);

    [Foldout(name: "Color Settings")] [OnValueChanged("RunColorShader")] [SerializeField]
    private Color _dirtColor = new Color(0.31f, 0.23f, 0.14f);

    [Foldout(name: "Color Settings")] [OnValueChanged("RunColorShader")] [SerializeField]
    private Color _snowColor = new Color(0.97f, 0.98f, 0.91f);

    
    
    private int _heightCorrectionThreads = 0;
    // Start is called before the first frame update
    void OnEnable()
    {
        _heightCorrectionThreads = 0;
        _prng = new Random(_seed);
        _perlinShaderHandle = _perlinShader.FindKernel("Perlin");
        _blurShaderHandle = _blurShader.FindKernel("Blur");
        _colorShaderHandle = _colorShader.FindKernel("Color");
        CreateHeightTexture();
        CreateColorTexture();
        RunHeightShader();
    }

    private void OnDisable()
    {
        if (_heightMap != null)
        {
            _heightMap.Release();
        }

        if (_colorMap != null)
        {
            _colorMap.Release();
        }
    }

    private void CreateHeightTexture()
    {
        if (_heightMap != null)
        {
            _heightMap.Release();
        }
        
        _terrain.terrainData.heightmapResolution = (int)_terrainSize;
        
        
        _heightMap = new RenderTexture((int)_terrainSize, (int)_terrainSize, 16, Terrain.heightmapRenderTextureFormat);
        _heightMap.filterMode = FilterMode.Bilinear;
        _heightMap.enableRandomWrite = true;
        _heightMap.Create();

        _terrainHeightMap = new Texture2D((int)_terrainSize, (int)_terrainSize, TextureFormat.R16, false);
        _terrainHeightMap.filterMode = FilterMode.Bilinear;
        _terrainHeightMap.anisoLevel = 0;

        if (_perlinNoise)
            _perlinShader.SetTexture(_perlinShaderHandle, "Result", _heightMap);

        if (_blur)
            _blurShader.SetTexture(_blurShaderHandle, "Result", _heightMap);
        CreateColorTexture();
    }

    private void CreateColorTexture()
    {
        _colorMap = new RenderTexture((int)_terrainSize, (int)_terrainSize, 24);
        _colorMap.filterMode = FilterMode.Bilinear;
        _colorMap.enableRandomWrite = true;
        _colorMap.Create();
        GetComponent<Renderer>().sharedMaterial.mainTexture = _colorMap;

        _colorShader.SetTexture(_colorShaderHandle, "Result", _colorMap);
    }


    private void RunHeightShader()
    {
        _prng = new Random(_seed);
        if (_perlinNoise)
        {
            _perlinShader.SetFloat("_terrainSize", _terrainSize);
            if (_terrainSize > 8)
            {
                float x = (float)_prng.Next(-10000, 10000);
                _perlinShader.SetFloat("_seed", x);
                _perlinShader.SetFloat("_scale", _scale);
                _perlinShader.SetFloats("_offset", _offset.x, _offset.y);
                _perlinShader.SetInt("_octaves", _octaves);
                _perlinShader.SetFloat("_persistence", _persistence);
                _perlinShader.SetFloat("_lacunarity", _lacunarity);
                _perlinShader.Dispatch(_perlinShaderHandle, (int)_terrainSize / 8, (int)_terrainSize / 8, 1);
            }
        }

        if (_blur)
        {
            _blurShader.SetTexture(_blurShaderHandle, "Result", _heightMap);
            _blurShader.SetFloat("_terrainSize", _terrainSize);
            _blurShader.SetInt("_iterations", _iterations);
            _blurShader.Dispatch(_blurShaderHandle, (int)_terrainSize / 8, (int)_terrainSize / 8, 1);
        }
        
        UpdateTerrain();
        RunColorShader();
    }


    private void RunColorShader()
    {
        _colorShader.SetTexture(_colorShaderHandle, "_heightMap", _heightMap);
        _colorShader.SetFloats("_seaColor", _seaColor.r, _seaColor.g, _seaColor.b);
        _colorShader.SetFloats("_shoreColor", _shoreColor.r, _shoreColor.g, _shoreColor.b);
        _colorShader.SetFloats("_grassColor", _grassColor.r, _grassColor.g, _grassColor.b);
        _colorShader.SetFloats("_dirtColor", _dirtColor.r, _dirtColor.g, _dirtColor.b);
        _colorShader.SetFloats("_snowColor", _snowColor.r, _snowColor.g, _snowColor.b);

        _colorShader.Dispatch(_colorShaderHandle, (int)_terrainSize / 8, (int)_terrainSize / 8, 1);
    }


    [Button("Bake")]
    private async void HeightCorrectionCurve()
    {
            RunHeightShader();
            await HeightCorrectionCurveTask(); 
        
    }

    private async Task HeightCorrectionCurveTask()
    {
        GetRTPixels(_heightMap);
        
        var pixels = _terrainHeightMap.GetPixels();
        //remap heightmap using curve
        for (int x = 0; x < _terrainSize; ++x)
        {
            for (int y = 0; y < _terrainSize; ++y)
            {
                float color =_heightCurve.Evaluate(pixels[x + y * (int)_terrainSize].r * 2.0f) / 2.0f;
                _terrainHeightMap.SetPixel(x, y, new Color(color, color, color));
            }
        }
        
        SetRTPixels(_heightMap);
        UpdateTerrain();
        
        _colorShader.SetTexture(_colorShaderHandle, "_heightMap", _heightMap);
        _colorShader.Dispatch(_colorShaderHandle, (int)_terrainSize / 8, (int)_terrainSize / 8, 1);

        _heightCorrectionThreads--;
    }
    
    private void GetRTPixels(RenderTexture rt)
    {
        // Remember currently active render texture
        RenderTexture currentActiveRT = RenderTexture.active;

        // Set the supplied RenderTexture as the active one
        RenderTexture.active = rt;

        _terrainHeightMap.ReadPixels(new Rect(0, 0, _terrainHeightMap.width, _terrainHeightMap.height), 0, 0);

        // Restorie previously active render texture
        RenderTexture.active = currentActiveRT;
    }

    private void SetRTPixels(RenderTexture rt)
    {
        // Remember currently active render texture
        RenderTexture currentActiveRT = RenderTexture.active;
        
        // Set the supplied RenderTexture as the active one
        RenderTexture.active = rt;
        
        _terrainHeightMap.Apply();
        Graphics.CopyTexture(_terrainHeightMap, _heightMap);

        // Restorie previously active render texture
        RenderTexture.active = currentActiveRT;
    }
    
    private void UpdateTerrain()
    {
        var active = RenderTexture.active;
        RenderTexture.active = _heightMap;
        var terrainData = _terrain.terrainData;
        terrainData.heightmapResolution = (int)_terrainSize;
        terrainData.baseMapResolution = (int)_terrainSize;
        terrainData.size = new Vector3((int)_terrainSize, _height, (int)_terrainSize);
        terrainData.CopyActiveRenderTextureToHeightmap(new RectInt(0, 0, _heightMap.width, _heightMap.height),
            new Vector2Int(0, 0), TerrainHeightmapSyncControl.HeightAndLod);
        _terrain.terrainData = terrainData;
        RenderTexture.active = active;
    }

    private void SaveTextureAsPNG(Texture2D _texture, string _fullPath)
    {
        byte[] _bytes = _texture.EncodeToPNG();
        System.IO.File.WriteAllBytes(_fullPath, _bytes);
        Debug.Log(_bytes.Length / 1024 + "Kb was saved as: " + _fullPath);
    }
}