using Brutal.Numerics;

public sealed class OrbitCamera
{
    public float Yaw = 0.7f;
    public float Pitch = 0.45f;
    public float Distance = 3.0f;

    public float3 Target = new(0f, 0f, 0f);

    public float4x4 ViewProjection(float aspectRatio)
    {
        float cosPitch = MathF.Cos(Pitch);

        var eye = new float3(
            Target.X + Distance * cosPitch * MathF.Sin(Yaw),
            Target.Y + Distance * MathF.Sin(Pitch),
            Target.Z + Distance * cosPitch * MathF.Cos(Yaw)
        );

        var view = float4x4.CreateLookAt(
            eye,
            Target,
            new float3(0f, 1f, 0f)
        );

        var projection =
            float4x4.CreatePerspectiveFieldOfView(
                MathF.PI / 4f,
                aspectRatio,
                0.05f,
                100f
            );

        return view * projection;
    }
}