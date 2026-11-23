
internal static class Shaders
{
    
    public const string Vertex = """
        #version 450

        layout(location = 0) in vec3 inPosition;
        layout(location = 1) in vec3 inNormal;

        layout(push_constant) uniform PushConstants
        {
            mat4 mvp;
        } pc;

        layout(location = 0) out vec3 vNormal;

        void main()
        {
            vec4 clip = pc.mvp * vec4(inPosition, 1.0);

            // Convert the projection convention
            // used by the sample to Vulkan clip space.
            clip.y = -clip.y;
            clip.z = (clip.z + clip.w) * 0.5;

            gl_Position = clip;

            vNormal = inNormal;
        }
        """;

        
    
public const string Fragment = """
        #version 450

        layout(location = 0) in vec3 vNormal;
        layout(location = 0) out vec4 outColor;

        void main()
        {
            // Normalize the surface normal.
            vec3 n = normalize(vNormal);

            // Fixed direction toward the light.
            vec3 lightDir =
                normalize(vec3(0.35, 0.75, 0.55));

            // Lambertian diffuse lighting.
            float lambert =
                max(dot(n, lightDir), 0.0);

            // Small ambient contribution.
            float ambient = 0.25;

            // Base material color.
            vec3 base = vec3(0.85, 0.80, 0.72);

            // Calculate final color.
            vec3 color =
                base * (ambient + lambert);

            outColor = vec4(color, 1.0);
        }
        """;

}