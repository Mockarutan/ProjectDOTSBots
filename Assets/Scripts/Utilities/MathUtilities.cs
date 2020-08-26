using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Unity.Mathematics;

public static class MathUtilities
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float3 ProjectVectorOnPlane(float3 planeNormal, float3 vector)
    {
        return vector - (math.dot(vector, planeNormal) * planeNormal);
    }
}
