using Brutal.GlfwApi;
using Brutal.Numerics;
using System.Diagnostics;

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

    var stopwatch = Stopwatch.StartNew();

    while (!window.ShouldClose)
    {
        Glfw.PollEvents();

        // Time elapsed since application startup.
        float elapsedSeconds =
            (float)stopwatch.Elapsed.TotalSeconds;

        // Rotate at 45 degrees per second.
        float angle =
            elapsedSeconds * MathF.PI / 4.0f;

        renderer.DrawFrame(angle);
    }
    // Resource cleanup will be added below.
}
finally
{
    Glfw.Terminate();
}