using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace EunoLab.FogOfWar
{
	[Serializable]
	public class FogOfWarGridData
	{
		public class TileData
		{
			public bool IsBlocking;
		}

		[SerializeField]
		private Vector2Int _dimensions;
		[SerializeField]
		private float _unitScale;
		[SerializeField]
		private TileData[] _tiles;

		public FogOfWarGridData(Vector2Int dimensions, float unitScale)
		{
			_dimensions = dimensions;
			_unitScale = unitScale;
			_tiles = new TileData[dimensions.x * dimensions.y];
			for (int i = 0; i < _tiles.Length; i++)
			{
				_tiles[i] = new TileData();
			}
		}

		public void Save(string path)
		{
			using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
			using var writer = new BinaryWriter(stream);

			writer.Write(_dimensions.x);
			writer.Write(_dimensions.y);
			writer.Write(_unitScale);

			int byteCount = (_tiles.Length + 7) / 8;
			byte[] packed = new byte[byteCount];
			for (int i = 0; i < _tiles.Length; i++)
			{
				if (_tiles[i].IsBlocking)
					packed[i >> 3] |= (byte)(1 << (i & 7));
			}
			writer.Write(packed);
		}

		public static FogOfWarGridData Load(byte[] gridBytes)
		{
			using var stream = new MemoryStream(gridBytes);
			using var reader = new BinaryReader(stream);

			int width = reader.ReadInt32();
			int height = reader.ReadInt32();
			float unitScale = reader.ReadSingle();
			Vector2Int dimensions = new(width, height);
			FogOfWarGridData gridData = new(dimensions, unitScale);
			var tiles = gridData._tiles;

			int byteCount = (tiles.Length + 7) / 8;
			byte[] packed = reader.ReadBytes(byteCount);
			for (int i = 0; i < tiles.Length; i++)
			{
				tiles[i].IsBlocking = (packed[i >> 3] & (1 << (i & 7))) != 0;
			}

			return gridData;
		}

		public Vector2Int Dimensions => _dimensions;
		public float UnitScale => _unitScale;
		public IReadOnlyList<TileData> Tiles => _tiles;
	}
}
