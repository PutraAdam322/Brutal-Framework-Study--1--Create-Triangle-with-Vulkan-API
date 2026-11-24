using Brutal.GltfApi;
using Brutal.Numerics;
using Brutal.NativeFileDialogApi;

public static class MeshImport
{

    private static float3 TransformPoint(
        float3 p,
        float4x4 m)
    {
        return new float3(
            p.X * m.X.X +
            p.Y * m.Y.X +
            p.Z * m.Z.X +
            m.W.X,

            p.X * m.X.Y +
            p.Y * m.Y.Y +
            p.Z * m.Z.Y +
            m.W.Y,

            p.X * m.X.Z +
            p.Y * m.Y.Z +
            p.Z * m.Z.Z +
            m.W.Z
        );
    }

    private static float3 TransformDirection(
        float3 v,
        float4x4 m)
    {
        return new float3(
            v.X * m.X.X +
            v.Y * m.Y.X +
            v.Z * m.Z.X,

            v.X * m.X.Y +
            v.Y * m.Y.Y +
            v.Z * m.Z.Y,

            v.X * m.X.Z +
            v.Y * m.Y.Z +
            v.Z * m.Z.Z
        );
    }


    public static unsafe MeshData Load(string path)
    {
        using var loader = new GltfLoader(path);

        var gltf = loader.GltfJson;

        var vertices = new List<Vertex>();
        var indices = new List<uint>();

        bool needsGeneratedNormals = false;

        foreach(var root in GetSceneRoots(gltf))
        {
            AppendNode(
                gltf,
                loader,
                root,
                float4x4.Identity,
                vertices,
                indices,
                ref needsGeneratedNormals
            );
        }

         if (needsGeneratedNormals)
        {
            GenerateNormals(vertices, indices);
        }

        return new MeshData
        {
            Vertices = vertices.ToArray(),
            Indices = indices.ToArray(),
            Name = Path.GetFileName(path)
        };
    }

    private static int[] GetSceneRoots(Gltf gltf)
    {
        if (gltf.Scenes is { Length: > 0 } scenes)
        {
            var sceneIndex = gltf.Scene ?? 0;

            if (sceneIndex < scenes.Length &&
                scenes[sceneIndex].Nodes is { } sceneNodes)
            {
                return sceneNodes;
            }
        }

        // Fallback when no scene is declared.
        var isChild = new bool[gltf.Nodes.Length];

        foreach (var node in gltf.Nodes)
        {
            if (node.Children is { } children)
            {
                foreach (var child in children)
                {
                    isChild[child] = true;
                }
            }
        }

        var roots = new List<int>();

        for (int i = 0; i < isChild.Length; i++)
        {
            if (!isChild[i])
            {
                roots.Add(i);
            }
        }

        return roots.ToArray();
    }


    private static float4x4 LocalMatrix(Node node)
    {
        if (node.Matrix != float4x4.Identity)
        {
            return node.Matrix;
        }

        return float4x4.CreateScale(node.Scale)
            * float4x4.CreateFromQuaternion(node.Rotation)
            * float4x4.CreateTranslation(node.Translation);
    }


    private static unsafe void AppendNode(
        Gltf gltf,
        IGltfLoader loader,
        int nodeIndex,
        float4x4 parentWorld,
        List<Vertex> vertices,
        List<uint> indices,
        ref bool needsGeneratedNormals)
    {
        var node = gltf.Nodes[nodeIndex];

        var world = LocalMatrix(node) * parentWorld;

        if (node.Mesh is int meshIndex)
        {
            AppendMesh(
                gltf,
                loader,
                meshIndex,
                world,
                vertices,
                indices,
                ref needsGeneratedNormals
            );
        }

        if (node.Children is { } children)
        {
            foreach (var child in children)
            {
                AppendNode(
                    gltf,
                    loader,
                    child,
                    world,
                    vertices,
                    indices,
                    ref needsGeneratedNormals
                );
            }
        }
    }

    private static void GenerateNormals(
        List<Vertex> vertices,
        List<uint> indices)
    {
        // Reset existing normals.
        for (int i = 0; i < vertices.Count; i++)
        {
            var vertex = vertices[i];

            vertex.Normal = default;

            vertices[i] = vertex;
        }

        // Calculate each triangle's surface normal.
        for (int i = 0; i + 2 < indices.Count; i += 3)
        {
            int a = (int)indices[i];
            int b = (int)indices[i + 1];
            int c = (int)indices[i + 2];

            var edge1 =
                vertices[b].Position -
                vertices[a].Position;

            var edge2 =
                vertices[c].Position -
                vertices[a].Position;

            var faceNormal = Cross(edge1, edge2);

            AccumulateNormal(vertices, a, faceNormal);
            AccumulateNormal(vertices, b, faceNormal);
            AccumulateNormal(vertices, c, faceNormal);
        }

        // Normalize accumulated vertex normals.
        for (int i = 0; i < vertices.Count; i++)
        {
            var vertex = vertices[i];

            vertex.Normal = Normalize(vertex.Normal);

            vertices[i] = vertex;
        }
    }

    private static unsafe List<float3> ReadVec3(
        IGltfLoader loader,
        int accessorIndex)
    {
        var accessor =
            loader.GltfJson.Accessors[accessorIndex];

        if (accessor.Type != Accessor.TypeEnum.Vec3 ||
            accessor.ComponentType !=
                Accessor.ComponentTypeEnum.Float)
        {
            throw new NotSupportedException(
                "Only FLOAT VEC3 attributes are supported."
            );
        }

        var bytes = loader
            .GetAccessorBufferSpan(accessorIndex)
            .ToArray();

        var stride = bytes.Length / accessor.Count;

        var result = new List<float3>(accessor.Count);

        fixed (byte* basePtr = bytes)
        {
            for (int i = 0; i < accessor.Count; i++)
            {
                var component =
                    (float*)(basePtr + i * stride);

                result.Add(
                    new float3(
                        component[0],
                        component[1],
                        component[2]
                    )
                );
            }
        }

        return result;
    }


    private static unsafe List<int> ReadIndices(
        IGltfLoader loader,
        int accessorIndex)
    {
        var accessor =
            loader.GltfJson.Accessors[accessorIndex];

        var bytes = loader
            .GetAccessorBufferSpan(accessorIndex)
            .ToArray();

        var stride = bytes.Length / accessor.Count;

        var result = new List<int>(accessor.Count);

        fixed (byte* basePtr = bytes)
        {
            switch (accessor.ComponentType)
            {
                case Accessor.ComponentTypeEnum.UnsignedByte:

                    for (int i = 0; i < accessor.Count; i++)
                    {
                        result.Add(
                            *(basePtr + i * stride)
                        );
                    }

                    break;

                case Accessor.ComponentTypeEnum.UnsignedShort:

                    for (int i = 0; i < accessor.Count; i++)
                    {
                        result.Add(
                            *(ushort*)(basePtr + i * stride)
                        );
                    }

                    break;

                case Accessor.ComponentTypeEnum.UnsignedInt:

                    for (int i = 0; i < accessor.Count; i++)
                    {
                        result.Add(
                            (int)*(uint*)(basePtr + i * stride)
                        );
                    }

                    break;

                default:

                    throw new NotSupportedException(
                        $"Unsupported index type: " +
                        $"{accessor.ComponentType}"
                    );
            }
        }

        return result;
    }

    private static unsafe void AppendMesh(
        Gltf gltf,
        IGltfLoader loader,
        int meshIndex,
        float4x4 transform,
        List<Vertex> vertices,
        List<uint> indices,
        ref bool needsGeneratedNormals)
    {
        foreach (var primitive in
            gltf.Meshes[meshIndex].Primitives)
        {
            if (primitive.Mode !=
                MeshPrimitive.ModeEnum.Triangles)
            {
                continue;
            }

            if (!primitive.Attributes.TryGetValue(
                "POSITION", out var positionAccessor))
            {
                continue;
            }

            if (primitive.Indices is not int indexAccessor)
            {
                continue;
            }

            var positions =
                ReadVec3(loader, positionAccessor);

            var hasNormals =
                primitive.Attributes.TryGetValue(
                    "NORMAL",
                    out var normalAccessor
                );

            var normals = hasNormals
                ? ReadVec3(loader, normalAccessor)
                : null;

            if (!hasNormals)
            {
                needsGeneratedNormals = true;
            }

            var baseVertex = vertices.Count;

            for (int i = 0; i < positions.Count; i++)
            {
                vertices.Add(new Vertex
                {
                    Position = TransformPoint(
                        positions[i],
                        transform
                    ),

                    Normal = normals is null
                        ? default
                        : TransformDirection(
                            normals[i],
                            transform
                        )
                });
            }

            foreach (var index in
                ReadIndices(loader, indexAccessor))
            {
                indices.Add(
                    (uint)(baseVertex + index)
                );
            }
        }
    }

    private static float3 Cross(float3 a, float3 b)
    {
        return new float3(
            a.Y * b.Z - a.Z * b.Y,
            a.Z * b.X - a.X * b.Z,
            a.X * b.Y - a.Y * b.X
        );
    }

    private static float3 Normalize(float3 v)
    {
        var length = MathF.Sqrt(
            v.X * v.X +
            v.Y * v.Y +
            v.Z * v.Z
        );

        return length > 1e-8f
            ? new float3(
                v.X / length,
                v.Y / length,
                v.Z / length
            )
            : new float3(0f, 1f, 0f);
    }

    private static void AccumulateNormal(
        List<Vertex> vertices,
        int index,
        float3 normal)
    {
        var vertex = vertices[index];

        vertex.Normal += normal;

        vertices[index] = vertex;
    }


    public static MeshData? OpenDialogAndLoad()
    {
        var path = NativeFileDialog.OpenDialog(
            new FileDialogInfo
            {
                FilterList =
                [
                    new FilterItem
                    {
                        Name = "glTF models",
                        Spec = "gltf,glb"
                    }
                ]
            },
            out var result
        );

        if (result != NativeFileDialog.Result.Okay ||
            string.IsNullOrEmpty(path))
        {
            return null;
        }

        return Load(path);
    }



}