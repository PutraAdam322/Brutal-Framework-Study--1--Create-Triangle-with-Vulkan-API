using Brutal.GlfwApi;
using Brutal.Numerics;

if (!Glfw.Init())
    throw new Exception("Failed to initialize GLFW");

try
{
    if (!Glfw.VulkanSupported())
        throw new Exception("Vulkan is not available");

    Glfw.WindowHint(
        GlfwWindowHint.ClientApi,
        Glfw.Constants.GLFW_NO_API
    );

    using var window = Glfw.CreateWindow(
        new GlfwWindow.CreateInfo
        {
            Title = "BRUTAL Vulkan Triangle",
            Size = new int2(800, 600)
        }
    );

    // Initialize Vulkan using our renderer.
    using var renderer = new VulkanRenderer(window);

    Console.WriteLine("Application is running.");

    while (!window.ShouldClose)
    {
        Glfw.PollEvents();

        renderer.DrawFrame();
    }

    // Resource cleanup will be added below.
}
finally
{
    Glfw.Terminate();
}