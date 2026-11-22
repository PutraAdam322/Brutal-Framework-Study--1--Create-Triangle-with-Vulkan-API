
internal static class Shaders
{
    
    public const string Vertex = """
        #version 450

        layout(location = 0) in vec3 inPosition;

        layout(push_constant) uniform PushConstants
        {
            mat4 model;
        } pc;

        void main()
        {
            vec4 position = vec4(inPosition, 1.0);

            gl_Position = pc.model * position;
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