using Brutal.Numerics;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential)]
public struct Vertex
{
    public float3 Position;
    public float3 Normal;
}