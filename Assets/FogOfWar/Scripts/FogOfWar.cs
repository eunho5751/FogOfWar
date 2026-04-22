using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Cysharp.Threading.Tasks;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class FogOfWar : MonoBehaviour
{
    public static string MainTag => "MainFogOfWar";
    private static FogOfWar _main = null;

    [Header("Basics")]
    [SerializeField]
    private bool _activateOnStart = true;
    [SerializeField, Range(1, 60)]
    private int _visibilityUpdateRate = 20;
    [SerializeField, TeamMask]
    private int _teamMask;
    [SerializeField]
    private Vector2Int _gridDimensions = new Vector2Int(128, 128);
    [SerializeField]
    private float _gridUnitScale = 0.5f;
    [SerializeField]
    private TextAsset _gridDataAsset;

    [Header("Fog")]
    [SerializeField]
    private Color _fowColor = Color.black;
    [SerializeField, Range(0f, 1f)]
    private float _unexploredAreaAlpha = 0.9f;
    [SerializeField, Range(0f, 1f)]
    private float _exploredAreaAlpha = 0.8f;
    [SerializeField]
    private float _fowLerpSpeed = 3f;
    [SerializeField]
    private int _blurIterations = 9;
    [SerializeField]
    private int _blurRadius = 5;
    [SerializeField]
    private float _blurSigma = 1.5f;

    [Header("Obstacle Scanning")]
    [SerializeField]
    private LayerMask _scanMask;
    [SerializeField]
    private float _scanBoxPadding = 0.05f;
    [SerializeField]
    private float _scanBoxHeight = 5f;

    private Tile[,] _grid;

    private ComputeShader _textureProcessorInstance;
    private byte[] _rawFOWArray;
    private ComputeBuffer _rawFOWBuffer;
    private ComputeBuffer _fowLerpBuffer;
    private ComputeBuffer _blurWeightsBuffer;
    private RenderTexture _fowTexture;
    private RenderTexture _blurTempTexture;
    private Vector2Int _numMainKernelThreadGroups;
    private Vector2Int _numBlurKernelThreadGroups;

    private GameObject _fowPlane;

    private CancellationTokenSource _fowUpdateCTS;
    private readonly List<FogOfWarUnit> _fowUnits = new();

    public void Activate(int? teamMask = null)
    {
        if (teamMask.HasValue)
            _teamMask = teamMask.Value;
        
        _fowUpdateCTS = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
        UpdateVisibility(true);
        UpdateFogOfWar(_fowUpdateCTS.Token).Forget();

        IsActivated = true;
    }

    public void Deactivate()
    {
        foreach (var unit in _fowUnits)
        {
            unit.SetVisible(false);
        }

        _fowUpdateCTS.Cancel();
        IsActivated = false;
    }

    public void ScanGrid()
    {
        ScanGrid((pos, isBlocking) =>
        {
            _grid[pos.y, pos.x].IsBlocking = isBlocking;
        });
    }

    public void AddUnit(FogOfWarUnit unit)
    {
        if (unit.FogOfWar != null)
        {
            return;
        }

        unit.SetFogOfWar(this);
        _fowUnits.Add(unit);
        if (IsActivated)
        {
            bool visible = unit.IsTeammate(_teamMask) || IsVisible(unit.transform.position);
            unit.SetVisible(visible);
        }
    }

    public void RemoveUnit(FogOfWarUnit unit)
    {
        if (unit.FogOfWar != null)
        {
            return;
        }

        unit.SetFogOfWar(null);
        _fowUnits.Remove(unit);
    }

    public bool ContainsUnit(FogOfWarUnit unit)
    {
        return _fowUnits.Contains(unit);
    }

    public bool IsVisible(Vector3 worldPos, int? teamMask = null)
    {
        Vector2Int tilePos = TransformWorldToTilePosition(worldPos);
        return IsVisibleTile(tilePos, teamMask ?? _teamMask);
    }
    
    private void Awake()
    {
        InitializeGrid();
        InitializeFOWTexture();
        InitializeFOWBuffers();
        InitializeTextureProcessor();
        InitializeBlurKernels();
        InitializePlane();
    }

    private void InitializeGrid()
    {
        _grid = new Tile[_gridDimensions.y, _gridDimensions.x];
        for (int y = 0; y < _gridDimensions.y; y++)
        {
            for (int x = 0; x < _gridDimensions.x; x++)
            {
                _grid[y, x] = new();
            }
        }
    }

    private void InitializeFOWTexture()
    {
        _fowTexture = new RenderTexture(_gridDimensions.x * 4, _gridDimensions.y * 4, 0, GraphicsFormat.R8_UNorm);
        _fowTexture.wrapMode = TextureWrapMode.Clamp;
        _fowTexture.filterMode = FilterMode.Bilinear;
        _fowTexture.enableRandomWrite = true;
        _fowTexture.Create();
    }

    private void InitializeFOWBuffers()
    {
        // Dimension+1 for upscale edge-case handling
        _rawFOWArray = new byte[((_gridDimensions.x + 1) * (_gridDimensions.y + 1) + 3) / 4 * 4];
        _rawFOWBuffer = new ComputeBuffer(_rawFOWArray.Length, sizeof(int), ComputeBufferType.Raw);

        int fowLerpElementCount = (_gridDimensions.x * 4) * (_gridDimensions.y * 4);
        _fowLerpBuffer = new ComputeBuffer(fowLerpElementCount, sizeof(float) * 2);
    }

    private void InitializeTextureProcessor()
    {
        var fowTextureProcessor = Resources.Load<ComputeShader>("FogOfWarTextureProcessor");
        _textureProcessorInstance = Instantiate(fowTextureProcessor);
        _textureProcessorInstance.SetInt("_RawWidth", _gridDimensions.x);
        _textureProcessorInstance.SetBuffer(0, "_RawFOWBuffer", _rawFOWBuffer);
        _textureProcessorInstance.SetBuffer(0, "_FOWLerpBuffer", _fowLerpBuffer);
        _textureProcessorInstance.SetTexture(0, "_FOWTexture", _fowTexture);
        _textureProcessorInstance.GetKernelThreadGroupSizes(0, out uint numMainThreadsX, out uint numMainThreadsY, out _);
        _numMainKernelThreadGroups.x = Mathf.CeilToInt((float)_gridDimensions.x / numMainThreadsX);
        _numMainKernelThreadGroups.y = Mathf.CeilToInt((float)_gridDimensions.y / numMainThreadsY);
    }

    private void InitializeBlurKernels()
    {
        _blurTempTexture = new RenderTexture(_fowTexture);
        _blurTempTexture.enableRandomWrite = true;
        _textureProcessorInstance.SetTexture(1, "_BlurInputTexture", _fowTexture);
        _textureProcessorInstance.SetTexture(1, "_BlurOutputTexture", _blurTempTexture);
        _textureProcessorInstance.SetTexture(2, "_BlurInputTexture", _blurTempTexture);
        _textureProcessorInstance.SetTexture(2, "_BlurOutputTexture", _fowTexture);

        UpdateFOWBlurProperties();
        _textureProcessorInstance.GetKernelThreadGroupSizes(1, out uint numBlurThreadsX, out _, out _);
        _textureProcessorInstance.GetKernelThreadGroupSizes(2, out _, out uint numBlurThreadsY, out _);
        _numBlurKernelThreadGroups.x = Mathf.CeilToInt((float)_fowTexture.width / numBlurThreadsX);
        _numBlurKernelThreadGroups.y = Mathf.CeilToInt((float)_fowTexture.height / numBlurThreadsY);
    }

    private void InitializePlane()
    {
        _fowPlane = GameObject.CreatePrimitive(PrimitiveType.Plane);
        _fowPlane.name = "FOW Plane";
        _fowPlane.transform.SetParent(transform, false);
        _fowPlane.transform.localScale = new Vector3((_gridDimensions.x * _gridUnitScale) / 10f, 1f, (_gridDimensions.y * _gridUnitScale) / 10f);

        var fowPlaneShader = Resources.Load<Shader>("FogOfWarPlaneShader");
        Material fowPlaneMaterial = new(fowPlaneShader);
        fowPlaneMaterial.mainTexture = _fowTexture;
        fowPlaneMaterial.SetColor("_FOWColor", _fowColor);

        var fowPlaneRenderer = _fowPlane.GetComponent<Renderer>();
        fowPlaneRenderer.material = fowPlaneMaterial;
        _fowPlane.GetComponent<Collider>().enabled = false;
    }

    private void Start()
    {
        bool isGridLoaded = false;
        if (_gridDataAsset != null)
        {
            isGridLoaded = LoadGrid(_gridDataAsset);
        }

        if (!isGridLoaded)
        {
            ScanGrid();
        }

        if (_activateOnStart)
        {
            Activate();
        }
    }

    private void OnDestroy()
    {
        _rawFOWBuffer.Dispose();
        _fowLerpBuffer.Dispose();
        _blurWeightsBuffer.Dispose();
        _fowTexture.Release();
        _blurTempTexture.Release();
        Destroy(_textureProcessorInstance);
    }

    private bool LoadGrid(TextAsset gridAsset)
    {
        var gridData = FogOfWarGridData.Load(gridAsset.bytes);
        if (gridData.Width != _gridDimensions.x || gridData.Height != _gridDimensions.y)
        {
            Debug.LogError($"Grid data size ({gridData.Width}x{gridData.Height}) does not match configured grid dimensions ({_gridDimensions.x}x{_gridDimensions.y}).");
            return false;
        }

        for (int y = 0; y < _gridDimensions.y; y++)
        {
            for (int x = 0; x < _gridDimensions.x; x++)
            {
                int idx = y * _gridDimensions.x + x;
                _grid[y, x].IsBlocking = gridData.Tiles[idx].IsBlocking;
            }
        }

        return true;
    }

    private void ScanGrid(Action<Vector2Int, bool> onTileScanned)
    {
        Vector3 scanBoxExtents = Vector3.one * 0.5f * _gridUnitScale - Vector3.one * _scanBoxPadding;
        scanBoxExtents.y = _scanBoxHeight * 0.5f;
        for (int y = 0; y < _gridDimensions.y; y++)
        {
            for (int x = 0; x < _gridDimensions.x; x++)
            {
                Vector3 worldPos = TransformTileToWorldPosition(new Vector2Int(x, y), transform.position.y);
                worldPos.y += scanBoxExtents.y;
                bool isBlocking = Physics.CheckBox(worldPos, scanBoxExtents, Quaternion.identity, _scanMask, QueryTriggerInteraction.Ignore);
                onTileScanned(new Vector2Int(x, y), isBlocking);
            }
        }
    }

    private async UniTaskVoid UpdateFogOfWar(CancellationToken token)
    {
        float time = 0f;
        while (!token.IsCancellationRequested)
        {
            await UniTask.Yield(PlayerLoopTiming.LastUpdate, token);

            float interval = 1f / _visibilityUpdateRate;
            time += Time.deltaTime;
            if (time >= interval)
            {
                UpdateVisibility(false);
                time -= interval;
            }
            UpdateFog();
        }
    }

    private void UpdateFOWBlurProperties()
    {
        if (!Application.isPlaying || _textureProcessorInstance == null)
            return;

        var blurWeights = GetGaussianWeights(_blurRadius, _blurSigma);
        _blurWeightsBuffer?.Dispose();
        _blurWeightsBuffer = new ComputeBuffer(blurWeights.Length, sizeof(float));
        _blurWeightsBuffer.SetData(blurWeights);
        _textureProcessorInstance.SetBuffer(1, "_BlurWeights", _blurWeightsBuffer);
        _textureProcessorInstance.SetBuffer(2, "_BlurWeights", _blurWeightsBuffer);
        _textureProcessorInstance.SetInt("_BlurRadius", _blurRadius);
    }

    private float[] GetGaussianWeights(int radius, float sigma)
    {
        float sum = 0f;
        float[] weights = new float[radius * 2 + 1];
        for (int i = -radius; i <= radius; i++)
        {
            float weight = CalculateGaussianWeight(i, sigma);
            weights[i + radius] = weight;
            sum += weight;
        }

        for (int i = 0; i < weights.Length; i++)
        {
            weights[i] /= sum;
        }
        return weights;
    }

    private float CalculateGaussianWeight(int x, float sigma)
    {
        return 0.39894f * Mathf.Exp(-0.5f * x * x / (sigma * sigma)) / sigma;
    }

    private void UpdateFog()
    {
        int rawWidth = _gridDimensions.x;
        int rawHeight = _gridDimensions.y;
        for (int y = 0; y < rawHeight; y++)
        {
            for (int x = 0; x < rawWidth; x++)
            {
                int index = y * (rawWidth + 1) + x;
                var tile = _grid[y, x];
                byte visibleBit = tile.IsVisible(_teamMask) ? (byte)0 : (byte)1;
                byte visitedBit = tile.IsVisited(_teamMask) ? (byte)0 : (byte)1;
                _rawFOWArray[index] = (byte)((visitedBit << 1) | visibleBit);
            }
        }

        for (int x = 0; x <= rawWidth; x++)
        {
            _rawFOWArray[rawHeight * (rawWidth + 1) + x] = _rawFOWArray[(rawHeight - 1) * (rawWidth + 1) + x];
        }
        for (int y = 0; y <= rawHeight; y++)
        {
            _rawFOWArray[y * (rawWidth + 1) + rawWidth] = _rawFOWArray[y * (rawWidth + 1) + rawWidth - 1];
        }

        float lerpAmount = Mathf.Clamp01(Time.deltaTime * _fowLerpSpeed);
        _rawFOWBuffer.SetData(_rawFOWArray);
        _textureProcessorInstance.SetFloat("_LerpAmount", lerpAmount);
        _textureProcessorInstance.SetFloat("_UnexploredAreaAlpha", _unexploredAreaAlpha);
        _textureProcessorInstance.SetFloat("_ExploredAreaAlpha", _exploredAreaAlpha);
        _textureProcessorInstance.Dispatch(0, _numMainKernelThreadGroups.x, _numMainKernelThreadGroups.y, 1);

        // You don't need to insert fences between dispatches.
        // Unity handles resource dependencies automatically.
        for (int i = 0; i < _blurIterations; i++)
        {
            _textureProcessorInstance.Dispatch(1, _numBlurKernelThreadGroups.x, _fowTexture.height, 1);
            _textureProcessorInstance.Dispatch(2, _fowTexture.width, _numBlurKernelThreadGroups.y, 1);
        }
    }

    private void UpdateVisibility(bool force)
    {
        ResetGrid();
        foreach (var unit in _fowUnits)
        {
            UpdateGrid(unit);
        }

        foreach (var unit in _fowUnits)
        {
            bool visible = IsVisible(unit.transform.position);
            if (force || unit.IsVisible != visible)
            {
                unit.SetVisible(visible);
            }
        }
    }

    private void ResetGrid()
    {
        for (int y = 0; y < _gridDimensions.y; y++)
        {
            for (int x = 0; x < _gridDimensions.x; x++)
            {
                _grid[y, x].Reset(visibleOnly: true);
            }
        }
    }

    private void UpdateGrid(FogOfWarUnit unit)
    {
        if (!unit.enabled || !unit.HasVision || unit.VisionRadius <= 0f)
            return;

        Vector2Int origin = TransformWorldToTilePosition(unit.transform.position);
        if (!IsTilePositionInGridRange(origin))
            return;

        var originTile = _grid[origin.y, origin.x];
        originTile.SetVisible(unit.TeamLayer, true);

        for (int i = 0; i < 4; i++)
        {
            Cardinal cardinal = (Cardinal)i;
            Quadrant quadrant = new(cardinal, _gridDimensions);
            Stack<Row> rows = new();

            rows.Push(new Row(1, -1f, 1f));
            while (rows.Count > 0)
            {
                var row = rows.Pop();
                Tile prevTile = null;
                foreach (var column in row.GetColumns())
                {
                    Vector2Int tilePos = quadrant.TransformColumnToTilePosition(origin, column);
                    if (!IsTilePositionInGridRange(tilePos))
                        continue;

                    var tile = _grid[tilePos.y, tilePos.x];
                    if (IsColumnInVisionRadius(column, unit.VisionRadius) && (tile.IsBlocking || IsColumnSymmetric(row, column)))
                    {
                        tile.SetVisible(unit.TeamLayer, true);
                    }
                    if (prevTile != null && prevTile.IsBlocking && !tile.IsBlocking)
                    {
                        row.StartSlope = CalculateSlope(column);
                    }
                    if (prevTile != null && !prevTile.IsBlocking && tile.IsBlocking)
                    {
                        if (row.Depth < unit.VisionRadius / _gridUnitScale && !quadrant.IsReachedGridBoundary(origin, row.Depth))
                        {
                            Row nextRow = new(row.Depth + 1, row.StartSlope, CalculateSlope(column));
                            rows.Push(nextRow);
                        }
                    }
                    prevTile = tile;
                }

                if (prevTile != null && !prevTile.IsBlocking)
                {
                    if (row.Depth < unit.VisionRadius / _gridUnitScale && !quadrant.IsReachedGridBoundary(origin, row.Depth))
                    {
                        rows.Push(new Row(row.Depth + 1, row.StartSlope, row.EndSlope));
                    }
                }
            }
        }
    }

    private Vector2Int TransformWorldToTilePosition(Vector3 worldPos)
    {
        int x = Mathf.RoundToInt((worldPos.x - transform.position.x) / _gridUnitScale) + _gridDimensions.x / 2;
        int y = Mathf.RoundToInt((worldPos.z - transform.position.z) / _gridUnitScale) + _gridDimensions.y / 2;
        return new(x, y);
    }

    private Vector3 TransformTileToWorldPosition(Vector2Int tilePos, float y)
    {
        float x = (tilePos.x - _gridDimensions.x / 2) * _gridUnitScale + transform.position.x;
        float z = (tilePos.y - _gridDimensions.y / 2) * _gridUnitScale + transform.position.z;
        return new(x, y, z);
    }

    private float CalculateSlope(Vector2Int column)
    {
        return (2 * column.y - 1) / (2f * column.x);
    }

    private bool IsColumnSymmetric(Row row, Vector2Int column)
    {
        return column.y >= row.Depth * row.StartSlope && column.y <= row.Depth * row.EndSlope;
    }

    private bool IsColumnInVisionRadius(Vector2Int column, float visionRadius)
    {
        return column.sqrMagnitude <= (visionRadius * visionRadius) / (_gridUnitScale * _gridUnitScale);
    }

    private bool IsTilePositionInGridRange(Vector2Int tilePos)
    {
        return tilePos.x >= 0 && tilePos.x < _gridDimensions.x && tilePos.y >= 0 && tilePos.y < _gridDimensions.y;
    }

    private bool IsVisibleTile(Vector2Int tilePos, int teamMask)
    {
        return IsTilePositionInGridRange(tilePos) ? _grid[tilePos.y, tilePos.x].IsVisible(teamMask) : false;
    }

#if UNITY_EDITOR
    [ContextMenu("Save Grid")]
    private void SaveGrid()
    {
        string path = EditorUtility.SaveFilePanelInProject("Save FogOfWar Grid", "GridData", "bytes", "");
        if (string.IsNullOrEmpty(path))
            return;

        FogOfWarGridData gridData = new(_gridDimensions.x, _gridDimensions.y);
        ScanGrid((pos, isBlocking) =>
        {
            int idx = pos.y * _gridDimensions.x + pos.x;
            gridData.Tiles[idx].IsBlocking = isBlocking;
        });

        gridData.Save(path);
        AssetDatabase.Refresh();
        Debug.Log($"Saved to '{path}'");
    }

    private void OnValidate()
    {
        UpdateFOWBlurProperties();
    }

    private void OnDrawGizmosSelected()
    {
        Color c = Gizmos.color;

        Gizmos.color = Color.yellow;
        Vector3 fowPlaneScale = new(_gridDimensions.x * _gridUnitScale, 0.1f, _gridDimensions.y * _gridUnitScale);
        Gizmos.DrawWireCube(transform.position, fowPlaneScale);

        if (_grid != null)
        {
            for (int y = 0; y < _gridDimensions.y; y++)
            {
                for (int x = 0; x < _gridDimensions.x; x++)
                {
                    Vector3 worldPos = TransformTileToWorldPosition(new(x, y), transform.position.y);
                    Gizmos.color = _grid[y, x].IsVisible(_teamMask) ? Color.green : (_grid[y, x].IsBlocking ? Color.red : Color.gray);
                    Gizmos.DrawWireSphere(worldPos, 0.1f);
                }
            }
        }
        
        Gizmos.color = c;
    }
#endif

    public bool IsActivated { get; private set; }

    public static FogOfWar Main
    {
        get
        {
            if (_main == null)
            {
                var fowGO = GameObject.FindWithTag(MainTag);
                if (fowGO != null)
                {
                    fowGO.TryGetComponent(out _main);
                }

                if (_main == null)
                {
                    Debug.LogError($"FogOfWar instance with {MainTag} tag not found.");
                }
            }

            return _main;
        }
    }
}