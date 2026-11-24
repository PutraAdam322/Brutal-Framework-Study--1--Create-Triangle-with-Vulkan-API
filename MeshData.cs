public sealed class MeshData
{
    public Vertex[] Vertices = [];

    public uint[] Indices = [];

    public string Name = string.Empty;

    public int TriangleCount => Indices.Length / 3;
}