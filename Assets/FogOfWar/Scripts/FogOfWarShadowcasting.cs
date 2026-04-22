using System.Collections.Generic;
using UnityEngine;

public enum Cardinal
{
    North   = 0,
    East    = 1,
    South   = 2,
    West    = 3
}

public class Tile
{
    private int _visitFlags;
    private int _visibleFlags;

    public void SetVisible(int teamLayer, bool visible)
    {
        int teamMask = 1 << teamLayer;
        if (visible)
            _visitFlags |= teamMask;
        _visibleFlags = visible ? (_visibleFlags | teamMask) : (_visibleFlags & ~teamMask);
    }

    public void Reset(bool visibleOnly)
    {
        if (!visibleOnly)
            _visitFlags = 0;
        _visibleFlags = 0;
    }

    public bool IsVisited(int teamMask) => (_visitFlags & teamMask) != 0;
    public bool IsVisible(int teamMask) => (_visibleFlags & teamMask) != 0;

    public bool IsBlocking { get; set; }
}

public struct Row
{
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

    public int Depth { get; set; }
    public float StartSlope { get; set; }
    public float EndSlope { get; set; }
}

public struct Quadrant
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