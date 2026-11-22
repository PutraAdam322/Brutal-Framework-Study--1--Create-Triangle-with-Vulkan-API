
internal static class Shaders
{
    
    public const string Vertex = """
        #version 450

        layout(location = 0) in vec3 inPosition;

        layout(push_constant) uniform PushConstants
        {
            mat4 mvp;
        } pc;

        void main()
        {
            vec4 clip = pc.mvp * vec4(inPosition, 1.0);

            // Convert the projection convention
            // used by the sample to Vulkan clip space.
            clip.y = -clip.y;
            clip.z = (clip.z + clip.w) * 0.5;

            gl_Position = clip;
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