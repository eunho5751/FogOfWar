using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Cysharp.Threading.Tasks;

namespace Prototype
{
    public class FogOfWar : MonoBehaviour
    {
        public enum Cardinal
        {
            North   = 0,
            East    = 1,
            South   = 2,
            West    = 3
        }

        public class Tile
        {
            public bool IsBlocking;
            public bool IsVisible;
        }

        public class Row
        {
            public int Depth;
            public float StartSlope;
            public float EndSlope;

            public Row(int depth, float startSlope, float endSlope)
            {
                Depth = depth;
                StartSlope = startSlope;
                EndSlope = endSlope;
            }

            public List<Vector2Int> GetColumns()
            {
                List<Vector2Int> columns = new();
                int min = Mathf.RoundToInt(Depth * StartSlope);
                int max = Mathf.RoundToInt(Depth * EndSlope);
                for (int col = min; col <= max; col++)
                {
                    columns.Add(new Vector2Int(Depth, col));
                }
                return columns;
            }
        }
        
        public class Quadrant
        {
            public Quadrant(Cardinal cardinal, Vector2Int dimension)
            {
                Cardinal = cardinal;
                Dimension = dimension;
            }

            public Vector2Int TransformColumnToTilePosition(Vector2Int origin, Vector2Int column)
            {
                int rowDepth = column.x;
                int col = column.y;
                return Cardinal switch
                {
                    Cardinal.North => new(origin.x + col, origin.y + rowDepth),
                    Cardinal.South => new(origin.x + col, origin.y - rowDepth),
                    Cardinal.East => new(origin.x + rowDepth, origin.y + col),
                    Cardinal.West => new(origin.x - rowDepth, origin.y + col),
                    _ => default
                };
            }

            public bool IsReachedGridBoundary(Vector2Int origin, int rowDepth)
            {
                return Cardinal switch
                {
                    Cardinal.North => origin.y + rowDepth >= Dimension.y - 1,
                    Cardinal.South => origin.y - rowDepth <= 0,
                    Cardinal.East => origin.x + rowDepth >= Dimension.x - 1,
                    Cardinal.West => origin.x - rowDepth <= 0,
                    _ => true
                };
            }

            public Cardinal Cardinal { get; }
            public Vector2Int Dimension { get; }
        }

        [SerializeField]
        private bool _activateOnStart = true;
        [SerializeField, Range(1, 60)]
        private int _visibilityUpdateRate = 20;
        [SerializeField]
        private int _teamMask;
        [SerializeField]
        private FogOfWarUnit[] _initialUnits;

        [Space(20)]

        [SerializeField]
        private Vector2Int _gridDimensions = new Vector2Int(128, 128);
        [SerializeField]
        private float _gridUnitScale = 0.5f;

        [Space(20)]

        [SerializeField] 
        private ComputeShader _fowTextureProcessor;
        [SerializeField]
        private float _fowLerpSpeed = 3f;
        [SerializeField]
        private Color _fowColor = Color.black;
        [SerializeField]
        private int _blurIterations = 9;
        [SerializeField]
        private int _blurRadius = 5;
        [SerializeField]
        private float _blurSigma = 1.5f;

        [Space(20)]

        [SerializeField]
        private Material _fowPlaneMaterial;
        [SerializeField]
        private float _fowPlaneHeightOffset;

        [Space(20)]

        [SerializeField]
        private LayerMask _scanMask;
        [SerializeField]
        private float _scanBoxPadding = 0.05f;
        [SerializeField]
        private float _scanBoxHeight = 5f;

        private Tile[,] _grid;

        private ComputeShader _textureProcessorInstance;
        private byte[] _rawFOWArray;
        private float[] _fowLerpArray;
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
            
            Scan();

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
                bool visible = unit.IsTeammate(_teamMask) || IsVisibleTile(unit.transform.position);
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

        public bool IsVisibleTile(Vector3 worldPos)
        {
            Vector2Int tilePos = TransformWorldToTilePosition(worldPos);
            return IsVisibleTile(tilePos);
        }

        public bool IsVisibleTile(Vector2Int tilePos)
        {
            return _grid[tilePos.y, tilePos.x].IsVisible;
        }
        
        private void Awake()
        {
            _grid = new Tile[_gridDimensions.y, _gridDimensions.x];
            for (int y = 0; y < _gridDimensions.y; y++)
            {
                for (int x = 0; x < _gridDimensions.x; x++)
                {
                    _grid[y, x] = new();
                }
            }
            
            _fowTexture = new RenderTexture(_gridDimensions.x * 4, _gridDimensions.y * 4, 0, GraphicsFormat.R8_UNorm);
            _fowTexture.wrapMode = TextureWrapMode.Clamp;
            _fowTexture.filterMode = FilterMode.Bilinear;
            _fowTexture.enableRandomWrite = true;
            _fowTexture.Create();

            // Dimension+1 for upscale edge-case handling
            _rawFOWArray = new byte[((_gridDimensions.x + 1) * (_gridDimensions.y + 1) + 3) / 4 * 4];
            _rawFOWBuffer = new ComputeBuffer(_rawFOWArray.Length, sizeof(int), ComputeBufferType.Raw);

            _fowLerpArray = new float[(_gridDimensions.x * 4) * (_gridDimensions.y * 4)];
            _fowLerpBuffer = new ComputeBuffer(_fowLerpArray.Length, sizeof(float));

            _textureProcessorInstance = Instantiate(_fowTextureProcessor);
            _textureProcessorInstance.SetInt("_RawWidth", _gridDimensions.x);
            _textureProcessorInstance.SetBuffer(0, "_RawFOWBuffer", _rawFOWBuffer);
            _textureProcessorInstance.SetBuffer(0, "_FOWLerpBuffer", _fowLerpBuffer);
            _textureProcessorInstance.SetTexture(0, "_FOWTexture", _fowTexture);
            _textureProcessorInstance.GetKernelThreadGroupSizes(0, out uint numMainThreadsX, out uint numMainThreadsY, out _);
            _numMainKernelThreadGroups.x = Mathf.CeilToInt((float)_gridDimensions.x / numMainThreadsX);
            _numMainKernelThreadGroups.y = Mathf.CeilToInt((float)_gridDimensions.y / numMainThreadsY);
            
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

            _fowPlane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            _fowPlane.name = "FOW Plane";
            _fowPlane.transform.SetParent(transform, false);
            _fowPlane.transform.localPosition = Vector3.up * _fowPlaneHeightOffset;
            _fowPlane.transform.localScale = new Vector3((_gridDimensions.x * _gridUnitScale) / 10f, 1f, (_gridDimensions.y * _gridUnitScale) / 10f);

            Material fowPlaneMaterial = new(_fowPlaneMaterial);
            fowPlaneMaterial.mainTexture = _fowTexture;
            fowPlaneMaterial.SetColor("_FOWColor", _fowColor);

            var fowPlaneRenderer = _fowPlane.GetComponent<Renderer>();
            fowPlaneRenderer.material = fowPlaneMaterial;
            _fowPlane.GetComponent<Collider>().enabled = false;

            foreach (var unit in _initialUnits)
            {
                AddUnit(unit);
            }
        }

        private void Start()
        {
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
        }

        private void Scan()
        {
            Vector3 scanBoxExtents = Vector3.one * 0.5f * _gridUnitScale - Vector3.one * _scanBoxPadding;
            scanBoxExtents.y = _scanBoxHeight * 0.5f;
            for (int y = 0; y < _gridDimensions.y; y++)
            {
                for (int x = 0; x < _gridDimensions.x; x++)
                {
                    Vector3 worldPos = TransformTileToWorldPosition(new Vector2Int(x, y), transform.position.y);
                    worldPos.y += scanBoxExtents.y;
                    if (Physics.CheckBox(worldPos, scanBoxExtents, Quaternion.identity, _scanMask, QueryTriggerInteraction.Ignore))
                    {
                        _grid[y, x].IsBlocking = true;
                    }
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
                    _rawFOWArray[index] = _grid[y, x].IsVisible ? (byte)0 : (byte)255;
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
            List<FogOfWarUnit> units = new(_fowUnits);
            IEnumerable<FogOfWarUnit> teammates = units.Where(x => x.IsTeammate(_teamMask)).ToArray();
            
            ResetGrid();
            foreach (var teammate in teammates)
            {
                UpdateGrid(teammate);

                if (force || !teammate.IsVisible)
                {
                    teammate.SetVisible(true);
                }
                units.Remove(teammate);
            }

            foreach (var unit in units)
            {
                bool visible = IsVisibleTile(unit.transform.position);
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
                    _grid[y, x].IsVisible = false;
                }
            }
        }

        private void UpdateGrid(FogOfWarUnit unit)
        {
            if (!unit.HasVision || unit.VisionRadius <= 0f)
                return;

            Vector2Int origin = TransformWorldToTilePosition(unit.transform.position);
            if (!IsTilePositionInGridRange(origin))
                return;
            _grid[origin.y, origin.x].IsVisible = true;

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
                            tile.IsVisible = true;
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

        private void OnValidate()
        {
            UpdateFOWBlurProperties();
        }

        private void OnDrawGizmos()
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
                        Gizmos.color = _grid[y, x].IsVisible ? Color.green : (_grid[y, x].IsBlocking ? Color.red : Color.gray);
                        Gizmos.DrawWireSphere(worldPos, 0.1f);
                    }
                }
            }
            
            Gizmos.color = c;
        }

        public bool IsActivated { get; private set; }
    }
}