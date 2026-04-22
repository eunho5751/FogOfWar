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
		private int _width;
		[SerializeField]
		private int _height;
		[SerializeField]
		private TileData[] _tiles;

		public FogOfWarGridData(int width, int height)
		{
			_width = width;
			_height = height;
			_tiles = new TileData[width * height];
			for (int i = 0; i < _tiles.Length; i++)
			{
				_tiles[i] = new TileData();
			}
		}

		public void Save(string path)
		{
			using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
			using var writer = new BinaryWriter(stream);

			writer.Write(_width);
			writer.Write(_height);

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
			FogOfWarGridData gridData = new(width, height);
			var tiles = gridData._tiles;

			int byteCount = (tiles.Length + 7) / 8;
			byte[] packed = reader.ReadBytes(byteCount);
			for (int i = 0; i < tiles.Length; i++)
			{
				tiles[i].IsBlocking = (packed[i >> 3] & (1 << (i & 7))) != 0;
			}

			return gridData;
		}

		public int Width => _width;
		public int Height => _height;
		public IReadOnlyList<TileData> Tiles => _tiles;
	}
}
