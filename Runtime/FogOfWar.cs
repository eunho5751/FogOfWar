using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace EunoLab.FogOfWar
{
	public class FogOfWar : MonoBehaviour
	{
		public static string MainTag => "MainFogOfWar";
		private static FogOfWar _main = null;

		[Header("Basics")]
		[SerializeField, Tooltip("If enabled, Activate() is called automatically in Start. Otherwise, you must call Activate() manually from your own code.")]
		private bool _activateOnStart = true;
		[SerializeField, Range(1, 60), Tooltip("How many times per second the visibility (line of sight) grid is recomputed. Higher values react faster but cost more CPU.")]
		private int _visibilityUpdateRate = 20;
		[SerializeField, TeamMask, Tooltip("The team mask this FogOfWar renders from. Fog is cleared by units on these teams.")]
		private int _teamMask;

		[Header("Grid")]
		[SerializeField, Min(1), Tooltip("Size of the fog grid in tiles (width x height). Overwritten by the Grid Data Asset if one is assigned.")]
		private Vector2Int _gridDimensions = new Vector2Int(128, 128);
		[SerializeField, Min(0.01f), Tooltip("World-space size of a single tile. Overwritten by the Grid Data Asset if one is assigned.")]
		private float _gridUnitScale = 0.5f;
		[SerializeField, Tooltip("Pre-scanned grid data (.bytes). When assigned, the runtime obstacle scanning is skipped and obstacle (blocking) info is loaded from this asset instead.")]
		private TextAsset _gridDataAsset;

		[Header("Fog")]
		[SerializeField, Tooltip("Color of the fog.")]
		private Color _fogColor = Color.black;
		[SerializeField, Range(0f, 1f), Tooltip("Fog alpha for areas that have never been seen. Closer to 1 means fully opaque.")]
		private float _fogUnexploredAreaAlpha = 0.9f;
		[SerializeField, Range(0f, 1f), Tooltip("Fog alpha for areas that have been explored before but are not currently in sight. Typically set a bit lower than the unexplored alpha.")]
		private float _fogExploredAreaAlpha = 0.8f;
		[SerializeField, Min(0f), Tooltip("Speed at which fog alpha blends toward its target when a tile's visibility state changes.")]
		private float _fogLerpSpeed = 3f;
		[SerializeField, Min(0), Tooltip("Number of Gaussian blur passes applied to the fog texture. More iterations produce softer edges but cost more GPU time.")]
		private int _fogBlurIterations = 9;
		[SerializeField, Min(0), Tooltip("Gaussian blur kernel radius in pixels. Larger values spread the blur wider.")]
		private int _fogBlurRadius = 5;
		[SerializeField, Min(0.1f), Tooltip("Standard deviation (sigma) of the Gaussian blur. Larger values flatten the weight distribution for a softer, wider falloff.")]
		private float _fogBlurSigma = 1.5f;

		[Header("Obstacle Scanning")]
		[SerializeField, Tooltip("Layer mask used to detect colliders that block vision.")]
		private LayerMask _obstacleScanMask;
		[SerializeField, Min(0f), Tooltip("Inward padding applied to each tile's obstacle-scan box. Prevents colliders that barely spill over into an adjacent tile from marking it as blocking.")]
		private float _obstacleScanPadding = 0.05f;
		[SerializeField, Min(0f), Tooltip("Height (world units) of the obstacle-scan box. Only colliders inside this vertical extent count as blocking, so set it tall enough to cover any obstacles above the map.")]
		private float _obstacleScanHeight = 5f;

		[Header("Debug")]
		[SerializeField, Tooltip("Draws a wireframe gizmo for each tile. Useful for verifying tile size and alignment with the scene.")]
		private bool _drawTileGizmos;
		[SerializeField, Tooltip("Draws the obstacle-scan box used per tile to detect blocking colliders. Useful for tuning obstacle scan padding and height.")]
		private bool _drawObstacleScanGizmos;

		private Tile[,] _grid;
		private readonly List<Vector2Int> _columns = new();
		private readonly Stack<Row> _rows = new();

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

		private Coroutine _fowUpdateCoroutine;
		private readonly List<FogOfWarUnit> _fowUnits = new();

		public void Activate(int? teamMask = null)
		{
			if (teamMask.HasValue)
				_teamMask = teamMask.Value;

			UpdateVisibility(true);
			_fowUpdateCoroutine = StartCoroutine(UpdateFogOfWar());

			IsActivated = true;
		}

		public void Deactivate()
		{
			foreach (var unit in _fowUnits)
			{
				unit.SetVisible(false);
			}

			if (_fowUpdateCoroutine != null)
			{
				StopCoroutine(_fowUpdateCoroutine);
				_fowUpdateCoroutine = null;
			}
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
			if (_gridDataAsset == null)
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
			else
			{
				LoadGrid(_gridDataAsset);
			}
		}

		private void InitializeFOWTexture()
		{
			_fowTexture = new RenderTexture(_gridDimensions.x * 4, _gridDimensions.y * 4, 0, RenderTextureFormat.R8, RenderTextureReadWrite.Linear);
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

			var initialLerpData = new Vector2[fowLerpElementCount];
			Array.Fill(initialLerpData, new Vector2(_fogUnexploredAreaAlpha, _fogUnexploredAreaAlpha));
			_fowLerpBuffer.SetData(initialLerpData);
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
			fowPlaneMaterial.SetColor("_FOWColor", _fogColor);

			var fowPlaneRenderer = _fowPlane.GetComponent<Renderer>();
			fowPlaneRenderer.material = fowPlaneMaterial;
			_fowPlane.GetComponent<Collider>().enabled = false;
		}

		private void Start()
		{
			if (_gridDataAsset == null)
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

		private void LoadGrid(TextAsset gridAsset)
		{
			var gridData = FogOfWarGridData.Load(gridAsset.bytes);
			_gridDimensions = gridData.Dimensions;
			_gridUnitScale = gridData.UnitScale;
			_grid = new Tile[_gridDimensions.y, _gridDimensions.x];
			for (int y = 0; y < _gridDimensions.y; y++)
			{
				for (int x = 0; x < _gridDimensions.x; x++)
				{
					int idx = y * _gridDimensions.x + x;
                    _grid[y, x] = new Tile
                    {
                        IsBlocking = gridData.Tiles[idx].IsBlocking
                    };
                }
			}
		}

		private void ScanGrid(Action<Vector2Int, bool> onTileScanned)
		{
			Vector3 scanBoxExtents = Vector3.one * 0.5f * _gridUnitScale - Vector3.one * _obstacleScanPadding;
			scanBoxExtents.y = _obstacleScanHeight * 0.5f;
			for (int y = 0; y < _gridDimensions.y; y++)
			{
				for (int x = 0; x < _gridDimensions.x; x++)
				{
					Vector3 worldPos = TransformTileToWorldPosition(new Vector2Int(x, y), transform.position.y);
					worldPos.y += scanBoxExtents.y;
					bool isBlocking = Physics.CheckBox(worldPos, scanBoxExtents, Quaternion.identity, _obstacleScanMask, QueryTriggerInteraction.Ignore);
					onTileScanned(new Vector2Int(x, y), isBlocking);
				}
			}
		}

		private IEnumerator UpdateFogOfWar()
		{
			float time = 0f;
			while (true)
			{
				yield return null;

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

			var blurWeights = GetGaussianWeights(_fogBlurRadius, _fogBlurSigma);
			_blurWeightsBuffer?.Dispose();
			_blurWeightsBuffer = new ComputeBuffer(blurWeights.Length, sizeof(float));
			_blurWeightsBuffer.SetData(blurWeights);
			_textureProcessorInstance.SetBuffer(1, "_BlurWeights", _blurWeightsBuffer);
			_textureProcessorInstance.SetBuffer(2, "_BlurWeights", _blurWeightsBuffer);
			_textureProcessorInstance.SetInt("_BlurRadius", _fogBlurRadius);
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
				int rowStart = y * (rawWidth + 1);
				for (int x = 0; x < rawWidth; x++)
				{
					int index = rowStart + x;
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

			float lerpAmount = Mathf.Clamp01(Time.deltaTime * _fogLerpSpeed);
			_rawFOWBuffer.SetData(_rawFOWArray);
			_textureProcessorInstance.SetFloat("_LerpAmount", lerpAmount);
			_textureProcessorInstance.SetFloat("_UnexploredAreaAlpha", _fogUnexploredAreaAlpha);
			_textureProcessorInstance.SetFloat("_ExploredAreaAlpha", _fogExploredAreaAlpha);
			_textureProcessorInstance.Dispatch(0, _numMainKernelThreadGroups.x, _numMainKernelThreadGroups.y, 1);

			// You don't need to insert fences between dispatches.
			// Unity handles resource dependencies automatically.
			for (int i = 0; i < _fogBlurIterations; i++)
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

			bool ignoreObstacles = unit.IgnoreObstacles;

			var originTile = _grid[origin.y, origin.x];
			originTile.SetVisible(unit.TeamLayer, true);

			for (int i = 0; i < 4; i++)
			{
				Cardinal cardinal = (Cardinal)i;
				Quadrant quadrant = new(cardinal, _gridDimensions);

				_rows.Push(new Row(1, -1f, 1f));
				while (_rows.Count > 0)
				{
					var row = _rows.Pop();
					Tile prevTile = null;
					bool prevTileBlocks = false;

					row.GetColumns(_columns);
					foreach (var column in _columns)
					{
						Vector2Int tilePos = quadrant.TransformColumnToTilePosition(origin, column);
						if (!IsTilePositionInGridRange(tilePos))
							continue;

						var tile = _grid[tilePos.y, tilePos.x];
						bool tileBlocks = !ignoreObstacles && tile.IsBlocking;
						if (IsColumnInVisionRadius(column, unit.VisionRadius) && (tileBlocks || IsColumnSymmetric(row, column)))
						{
							tile.SetVisible(unit.TeamLayer, true);
						}
						if (prevTile != null && prevTileBlocks && !tileBlocks)
						{
							row.StartSlope = CalculateSlope(column);
						}
						if (prevTile != null && !prevTileBlocks && tileBlocks)
						{
							if (row.Depth < unit.VisionRadius / _gridUnitScale && !quadrant.IsReachedGridBoundary(origin, row.Depth))
							{
								Row nextRow = new(row.Depth + 1, row.StartSlope, CalculateSlope(column));
								_rows.Push(nextRow);
							}
						}
						prevTile = tile;
						prevTileBlocks = tileBlocks;
					}

					if (prevTile != null && !prevTileBlocks)
					{
						if (row.Depth < unit.VisionRadius / _gridUnitScale && !quadrant.IsReachedGridBoundary(origin, row.Depth))
						{
							_rows.Push(new Row(row.Depth + 1, row.StartSlope, row.EndSlope));
						}
					}

					_columns.Clear();
				}
				_rows.Clear();
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

			FogOfWarGridData gridData = new(_gridDimensions, _gridUnitScale);
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

			if (_drawTileGizmos && _grid != null)
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

			if (_drawObstacleScanGizmos && _obstacleScanHeight > 0f)
			{
				Gizmos.color = Color.skyBlue;

				Vector3 scanBoxSize = Vector3.one * _gridUnitScale - Vector3.one * (_obstacleScanPadding * 2f);
				scanBoxSize.y = 0f;
				for (int y = 0; y < _gridDimensions.y; y++)
				{
					for (int x = 0; x < _gridDimensions.x; x++)
					{
						Vector3 worldPos = TransformTileToWorldPosition(new(x, y), transform.position.y);
						Gizmos.DrawWireCube(worldPos, scanBoxSize);
					}	
				}

				Vector3 pos = transform.position + Vector3.up * (_obstacleScanHeight * 0.5f);
				Vector3 size = new(_gridDimensions.x * _gridUnitScale, _obstacleScanHeight, _gridDimensions.y * _gridUnitScale);
				Gizmos.DrawWireCube(pos, size);
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
}
