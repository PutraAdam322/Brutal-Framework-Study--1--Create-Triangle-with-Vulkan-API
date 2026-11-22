
internal static class Shaders
{
    public const string Vertex = """
        #version 450

        layout(location = 0) in vec3 inPosition;

        void main()
        {
            gl_Position = vec4(inPosition, 1.0);
        }
        """;
        
    public const string Fragment = """
        #version 450
        layout(location = 0) out vec4 outColor;

        void main() {
            outColor = vec4(1.0, 0.4, 0.1, 1.0);
        }
    """;
}