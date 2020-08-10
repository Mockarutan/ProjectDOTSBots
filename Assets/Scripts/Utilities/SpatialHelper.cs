using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Unity.Mathematics;

public static class SpatialHelper
{
    public static uint PositionToCellIndex(float2 position, uint2 spaceSize, float cellSize, float2 globalOffset)
    {
        position -= globalOffset;

        var yValue = (uint)(math.floor(position.y / cellSize) * spaceSize.x);

        return (uint)math.floor(position.x / cellSize) + yValue;
    }

    public static float2 CellIndexToPosition(uint index, uint2 spaceSize, float cellSize, float2 globalOffset)
    {
        var yValue = math.floor(index / spaceSize.x);
        var xValue = math.floor(index % spaceSize.x);

        return (new float2(xValue, yValue) * cellSize) + globalOffset;
    }
}