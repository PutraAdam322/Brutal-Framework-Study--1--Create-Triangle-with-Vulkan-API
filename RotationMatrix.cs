using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential)]
public struct RotationMatrix
{
    public float M00, M10, M20, M30;
    public float M01, M11, M21, M31;
    public float M02, M12, M22, M32;
    public float M03, M13, M23, M33;

    public static RotationMatrix AroundZ(float angle)
    {
        float c = MathF.Cos(angle);
        float s = MathF.Sin(angle);

        return new RotationMatrix
        {
            M00 = c,
            M10 = s,

            M01 = -s,
            M11 = c,

            M22 = 1.0f,
            M33 = 1.0f
        };
    }
}