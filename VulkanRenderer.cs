
using Brutal;
using Brutal.Numerics;
using Brutal.GlfwApi;
using Brutal.ShaderCApi;
using Brutal.Pointers.Extensions;
using Brutal.Strings;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;


public sealed unsafe class VulkanRenderer : IDisposable
{
    private readonly GlfwWindow _window;

    private VkFramebuffer[] _framebuffers = [];

    private VkSwapchainKHR _swapchain;

    private VkFormat _swapchainFormat;
    private VkExtent2D _swapchainExtent;

    private VkImage[] _swapchainImages = [];
    private VkImageView[] _swapchainImageViews = [];

    private VkRenderPass _renderPass;

    private VkPipelineLayout _pipelineLayout;
    private VkPipeline _pipeline;

    private VkBuffer _vertexBuffer;
    private VkDeviceMemory _vertexMemory;

    private VkBuffer _indexBuffer;
    private VkDeviceMemory _indexMemory;

    private int _indexCount;
    private readonly Instance _instance;
    private readonly VkSurfaceKHR _surface;
    private readonly PhysicalDevice _physicalDevice;
    private readonly DeviceEx _device;
    private readonly Queue _graphicsQueue;

    private readonly int _queueFamily;

    private const int MaxFramesInFlight = 1;

    private readonly Frame[] _frames;
    private int _currentFrame;

    
    private static readonly Vertex[] RectangleVertices =
    [
        new Vertex
        {
            Position = new float3(-0.5f, -0.5f, 0.0f)
        },
        new Vertex
        {
            Position = new float3(0.5f, -0.5f, 0.0f)
        },
        new Vertex
        {
            Position = new float3(0.5f, 0.5f, 0.0f)
        },
        new Vertex
        {
            Position = new float3(-0.5f, 0.5f, 0.0f)
        }
    ];


    private static readonly uint[] RectangleIndices =
    [
        0, 1, 2,
        2, 3, 0
    ];

    public VulkanRenderer(GlfwWindow window)
    {
        _window = window;

        // 1. Initialize Vulkan.
        _instance = CreateInstance();

        // 2. Connect Vulkan to the GLFW window.
        _surface = CreateSurface();

        // 3. Select a GPU.
        (_physicalDevice, _queueFamily) =
            PickPhysicalDevice();

        // 4. Create a logical device.
        _device = CreateDevice();

        // 5. Retrieve the graphics queue.
        _graphicsQueue =
            _device.GetQueue(_queueFamily, 0);

        _frames = new Frame[MaxFramesInFlight];

        for (var i = 0; i < _frames.Length; i++)
        {
            _frames[i] = CreateFrame();
        }

        CreateSwapchain();
        CreateImageViews();

        _renderPass = CreateRenderPass();
        CreateFramebuffers();

        CreatePipeline();

        CreateMeshBuffers();

        Console.WriteLine(
            "Vulkan initialized successfully!"
        );
    }

    private sealed class Frame
    {
        public VkCommandPool Pool;
        public CommandBuffer CommandBuffer;

        public VkFence Fence;

        public VkSemaphore ImageAvailable;
        public VkSemaphore RenderFinished;
    }

    private Instance CreateInstance()
    {
        var extensions = new HashSet<string>();

        var required =
            Glfw.GetRequiredInstanceExtensions(out var count);

        if (required == null)
        {
            throw new Exception(
                "GLFW could not provide Vulkan extensions."
            );
        }

        for (var i = 0u; i < count; i++)
        {
            extensions.Add(Utf8.GetString(required[i]));
        }

        VulkanHelpers.AddPlatformRequiredExtensions(
            extensions
        );

        return Vulkan.CreateInstance(
            new Instance.CreateInfo
            {
                AppInfo = new VkApplicationInfo
                {
                    ApplicationName = null,
                    ApiVersion =
                        (int)VulkanHelpers.Api.VERSION_1_3
                },

                Flags =
                    VulkanHelpers
                        .GetPlatformRequiredInstanceCreateFlags(),

                EnabledExtensions = extensions,
                EnabledLayers = new HashSet<string>()
            }
        );
    }

    private VkSurfaceKHR CreateSurface()
    {
        ulong handle;

        var result = Glfw.PInvoke.CreateWindowSurface(
            _instance.VkHandle,
            _window,
            null,
            &handle
        );

        if (result != 0)
        {
            throw new Exception(
                "Failed to create Vulkan window surface."
            );
        }

        return new VkSurfaceKHR((nint)handle);
    }

    private VkSurfaceFormatKHR SelectSurfaceFormat()
    {
        var formatCount =
            _physicalDevice.GetSurfaceFormatsKHR_Count(_surface);

        Span<VkSurfaceFormatKHR> formats =
            stackalloc VkSurfaceFormatKHR[formatCount];

        _physicalDevice.GetSurfaceFormatsKHR_Write(
            _surface,
            formats
        );

        if (formats.Length == 0)
            throw new Exception("No supported surface formats.");

        // Handle the special case where the surface
        // does not specify a preferred format.
        if (formats.Length == 1 &&
            formats[0].Format == VkFormat.Undefined)
        {
            return new VkSurfaceFormatKHR
            {
                Format = VkFormat.B8G8R8A8UNorm,
                ColorSpace = VkColorSpaceKHR.SRGBNonlinearKHR
            };
        }

        foreach (var format in formats)
        {
            if (format.Format is
                VkFormat.B8G8R8A8UNorm or
                VkFormat.R8G8B8A8UNorm)
            {
                return format;
            }
        }

        return formats[0];
    }

    private VkPresentModeKHR SelectPresentMode()
    {
        var modeCount =
            _physicalDevice.GetSurfacePresentModesKHR_Count(
                _surface
            );

        Span<VkPresentModeKHR> modes =
            stackalloc VkPresentModeKHR[modeCount];

        _physicalDevice.GetSurfacePresentModesKHR_Write(
            _surface,
            modes
        );

        foreach (var mode in modes)
        {
            if (mode == VkPresentModeKHR.MailboxKHR)
                return VkPresentModeKHR.MailboxKHR;
        }

        return VkPresentModeKHR.FifoKHR;
    }

    private (VkBuffer, VkDeviceMemory) CreateHostVisibleBuffer<T>(
        T[] data,
        VkBufferUsageFlags usage
    )
    where T : unmanaged
    {
        // 1. Calculate the required data size.
        var size = ByteSize.Of<T>(data.Length);

        // 2. Create the buffer resource.
        var buffer = _device.CreateBuffer(
            new VkBufferCreateInfo
            {
                Size = size,
                Usage = usage,
                SharingMode = VkSharingMode.Exclusive
            },
            null
        );

        // 3. Query the buffer's memory requirements.
        var requirements =
            _device.GetBufferMemoryRequirements(buffer);

        // 4. Find compatible, CPU-accessible memory.
        var memoryType = _physicalDevice.FindMemoryType(
            (int)requirements.MemoryTypeBits,

            VkMemoryPropertyFlags.HostVisibleBit |
            VkMemoryPropertyFlags.HostCoherentBit
        );

        // 5. Allocate memory.
        var memory = _device.AllocateMemory(
            new VkMemoryAllocateInfo
            {
                AllocationSize = requirements.Size,
                MemoryTypeIndex = memoryType
            },
            null
        );

        // 6. Bind the memory to the buffer.
        _device.BindBufferMemory(
            buffer,
            memory,
            ByteSize.Zero
        );

        // 7. Copy the C# array into the mapped memory.
        _device.WriteMemory(
            data.AsSpan(),
            memory
        );

        return (buffer, memory);
    }
    
    private void CreateMeshBuffers()
    {
        // Upload the four rectangle vertices.
        (_vertexBuffer, _vertexMemory) =
            CreateHostVisibleBuffer(
                RectangleVertices,
                VkBufferUsageFlags.VertexBufferBit
            );

        // Upload the six indices.
        (_indexBuffer, _indexMemory) =
            CreateHostVisibleBuffer(
                RectangleIndices,
                VkBufferUsageFlags.IndexBufferBit
            );

        _indexCount = RectangleIndices.Length;

        Console.WriteLine(
            $"Uploaded {RectangleVertices.Length} vertices."
        );

        Console.WriteLine(
            $"Uploaded {_indexCount} indices."
        );
    }


    private (PhysicalDevice, int) PickPhysicalDevice()
    {
        foreach (
            var gpu in _instance.PhysicalDevices
        )
        {
            var family =
                FindGraphicsPresentFamily(gpu);

            if (family >= 0)
            {
                Console.WriteLine(
                    "Compatible Vulkan GPU found."
                );

                return (gpu, family);
            }
        }

        throw new Exception(
            "No compatible Vulkan GPU found."
        );
    }


    private void CreateSwapchain()
    {
        // 1. Query what the window surface supports.
        var capabilities =
            _physicalDevice.GetSurfaceCapabilitiesKHR(_surface);

        // 2. Choose format and presentation mode.
        var surfaceFormat = SelectSurfaceFormat();
        var presentMode = SelectPresentMode();

        // 3. Request one more image than the minimum.
        var imageCount = capabilities.MinImageCount + 1;

        if (capabilities.MaxImageCount != 0 &&
            imageCount > capabilities.MaxImageCount)
        {
            imageCount = capabilities.MaxImageCount;
        }

        // 4. Determine the image dimensions.
        var extent = capabilities.CurrentExtent;

        if (extent.Width == -1)
        {
            var size = _window.FramebufferSize;

            extent.Width = Math.Clamp(
                size.X,
                capabilities.MinImageExtent.Width,
                capabilities.MaxImageExtent.Width
            );

            extent.Height = Math.Clamp(
                size.Y,
                capabilities.MinImageExtent.Height,
                capabilities.MaxImageExtent.Height
            );
        }

        // 5. Create the swapchain.
        _swapchain = _device.CreateSwapchainKHR(
            new VkSwapchainCreateInfoKHR
            {
                Surface = _surface,

                MinImageCount = imageCount,

                ImageFormat = surfaceFormat.Format,
                ImageColorSpace = surfaceFormat.ColorSpace,

                ImageExtent = extent,
                ImageArrayLayers = 1,

                ImageUsage =
                    VkImageUsageFlags.ColorAttachmentBit,

                ImageSharingMode = VkSharingMode.Exclusive,

                PreTransform =
                    capabilities.SupportedTransforms.HasFlag(
                        VkSurfaceTransformFlagsKHR.IdentityBitKHR)
                    ? VkSurfaceTransformFlagsKHR.IdentityBitKHR
                    : capabilities.CurrentTransform,

                CompositeAlpha =
                    VkCompositeAlphaFlagsKHR.OpaqueBitKHR,

                PresentMode = presentMode,
                Clipped = true
            },
            null
        );

        // 6. Store the chosen format and dimensions.
        _swapchainFormat = surfaceFormat.Format;
        _swapchainExtent = extent;

        // 7. Retrieve the swapchain images.
        var actualCount =
            _device.GetSwapchainImagesKHR_Count(_swapchain);

        _swapchainImages = new VkImage[actualCount];

        _device.GetSwapchainImagesKHR_Write(
            _swapchain,
            _swapchainImages
        );

        Console.WriteLine("Swapchain created!");
        Console.WriteLine($"Images: {actualCount}");
        Console.WriteLine(
            $"Resolution: {extent.Width} x {extent.Height}"
        );
    }

    private VkShaderModule CompileShader(string source, ShaderKind kind, string name)
    {
        var compiler = ShaderC.CreateCompiler();

        using var result = compiler.Compile(
            source,
            kind,
            name
        );

        var length = (int)(ByteSize)result.GetLength();

        var bytes = new ReadOnlySpan<byte>(result.GetBytes(), length);

        return ShaderModuleExtensions.FromSpirv(_device, bytes, null);

    }

    private void CreateImageViews()
    {
        _swapchainImageViews =
            new VkImageView[_swapchainImages.Length];

        for (var i = 0; i < _swapchainImages.Length; i++)
        {
            _swapchainImageViews[i] =
                _device.CreateImageView(
                    new VkImageViewCreateInfo
                    {
                        Image = _swapchainImages[i],

                        ViewType = VkImageViewType._2D,

                        Format = _swapchainFormat,

                        Components = new VkComponentMapping
                        {
                            R = VkComponentSwizzle.R,
                            G = VkComponentSwizzle.G,
                            B = VkComponentSwizzle.B,
                            A = VkComponentSwizzle.A
                        },

                        SubresourceRange =
                            new VkImageSubresourceRange
                            {
                                AspectMask =
                                    VkImageAspectFlags.ColorBit,

                                BaseMipLevel = 0,
                                LevelCount = 1,

                                BaseArrayLayer = 0,
                                LayerCount = 1
                            }
                    },
                    null
                );
        }

        Console.WriteLine("Swapchain image views created!");
    }

    private void CreateFramebuffers()
    {
        _framebuffers = new VkFramebuffer[_swapchainImageViews.Length];

        for (var i = 0; i < _swapchainImageViews.Length; i++)
        {
            Span<VkImageView> attachments = [_swapchainImageViews[i]];

            _framebuffers[i] = _device.CreateFramebuffer(
                new VkFramebufferCreateInfo
                {
                    RenderPass = _renderPass,

                    AttachmentCount = attachments.Length,
                    Attachments = attachments.AsPointer(),

                    Width = _swapchainExtent.Width,
                    Height = _swapchainExtent.Height,

                    Layers = 1
                },
                null
            );
        }

        Console.WriteLine($"Created {_framebuffers.Length} framebuffers.");
    }


    private void CreatePipeline()
    {
        var vertexShader = CompileShader(
            Shaders.Vertex,
            ShaderKind.VertexShader,
            "triangle.vert"
        );

        var fragmentShader = CompileShader(
            Shaders.Fragment,
            ShaderKind.FragmentShader,
            "triangle.frag"
        );

        try
        {
            // No descriptors or push constants needed.
            _pipelineLayout = _device.CreatePipelineLayout(
                [],
                [],
                null
            );

            // Define the shader stages.
            Span<VkPipelineShaderStageCreateInfo> stages =
            [
                Presets.ShaderStage.Vertex(vertexShader),
                Presets.ShaderStage.Fragment(fragmentShader)
            ];

            // No vertex buffers: positions come from the shader.
           var bindingDescription =
            new VkVertexInputBindingDescription
            {
                Binding = 0,

                Stride = ByteSize.Of<Vertex>(),

                InputRate = VkVertexInputRate.Vertex
            };

        // Describe the position attribute.
            var positionAttribute =
                new VkVertexInputAttributeDescription
                {
                    Location = 0,
                    Binding = 0,

                    Format = VkFormat.R32G32B32SFloat,

                    Offset = ByteSize.OffsetOf<Vertex>(
                        nameof(Vertex.Position)
                    )
                };

            // Connect the descriptions to the pipeline.
            var vertexInput =
                new VkPipelineVertexInputStateCreateInfo
                {
                    VertexBindingDescriptionCount = 1,
                    VertexBindingDescriptions = &bindingDescription,

                    VertexAttributeDescriptionCount = 1,
                    VertexAttributeDescriptions = &positionAttribute
                };

            // Every three vertices form one triangle.
            var inputAssembly =
                new VkPipelineInputAssemblyStateCreateInfo
                {
                    Topology = VkPrimitiveTopology.TriangleList
                };

            // Viewport and scissor will be set dynamically.
            var viewportState =
                new VkPipelineViewportStateCreateInfo
                {
                    ViewportCount = 1,
                    ScissorCount = 1
                };

            // Configure triangle rasterization.
            var rasterization =
                new VkPipelineRasterizationStateCreateInfo
                {
                    PolygonMode = VkPolygonMode.Fill,
                    CullMode = VkCullModeFlags.None,
                    FrontFace = VkFrontFace.CounterClockwise,
                    LineWidth = 1.0f
                };

            // Disable multisampling for now.
            var multisample =
                new VkPipelineMultisampleStateCreateInfo
                {
                    RasterizationSamples =
                        VkSampleCountFlags._1Bit
                };

            // No depth buffer for our 2D triangle.
            var depthStencil =
                new VkPipelineDepthStencilStateCreateInfo
                {
                    DepthTestEnable = false,
                    DepthWriteEnable = false
                };

            // Write all four color channels.
            var colorAttachment =
                new VkPipelineColorBlendAttachmentState
                {
                    BlendEnable = false,

                    ColorWriteMask =
                        VkColorComponentFlags.RBit |
                        VkColorComponentFlags.GBit |
                        VkColorComponentFlags.BBit |
                        VkColorComponentFlags.ABit
                };

            var colorBlend =
                new VkPipelineColorBlendStateCreateInfo
                {
                    AttachmentCount = 1,
                    Attachments = &colorAttachment
                };

            // Enable dynamic viewport and scissor.
            Span<VkDynamicState> dynamicStates =
            [
                VkDynamicState.Viewport,
                VkDynamicState.Scissor
            ];

            var dynamicState =
                new VkPipelineDynamicStateCreateInfo
                {
                    DynamicStateCount = dynamicStates.Length,
                    DynamicStates = dynamicStates.AsPointer()
                };

            // Finally create the graphics pipeline.
            _pipeline = _device.CreateGraphicsPipeline(
                default,
                new VkGraphicsPipelineCreateInfo
                {
                    StageCount = stages.Length,
                    Stages = stages.AsPointer(),

                    VertexInputState = &vertexInput,
                    InputAssemblyState = &inputAssembly,
                    ViewportState = &viewportState,
                    RasterizationState = &rasterization,
                    MultisampleState = &multisample,
                    DepthStencilState = &depthStencil,
                    ColorBlendState = &colorBlend,
                    DynamicState = &dynamicState,

                    Layout = _pipelineLayout,
                    RenderPass = _renderPass,
                    Subpass = 0
                },
                null
            );

            Console.WriteLine(
                "Graphics pipeline created successfully!"
            );
        }
        finally
        {
            // Shader modules are no longer needed
            // after pipeline creation.
            _device.DestroyShaderModule(vertexShader, null);
            _device.DestroyShaderModule(fragmentShader, null);
        }
    }


    private VkRenderPass CreateRenderPass()
    {
        var colorAttachment = new VkAttachmentDescription
        {
            Format = _swapchainFormat,
            Samples = VkSampleCountFlags._1Bit,

            LoadOp = VkAttachmentLoadOp.Clear,
            StoreOp = VkAttachmentStoreOp.Store,

            StencilLoadOp = VkAttachmentLoadOp.DontCare,
            StencilStoreOp = VkAttachmentStoreOp.DontCare,

            InitialLayout = VkImageLayout.Undefined,
            FinalLayout = VkImageLayout.PresentSrcKHR
        };

        var colorReference = new VkAttachmentReference
        {
            Attachment = 0,
            Layout = VkImageLayout.ColorAttachmentOptimal
        };

        var subpass = new VkSubpassDescription
        {
            PipelineBindPoint = VkPipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            ColorAttachments = &colorReference
        };

        var dependency = new VkSubpassDependency
        {
            SrcSubpass = VK.SUBPASS_EXTERNAL,
            DstSubpass = 0,

            SrcStageMask =
                VkPipelineStageFlags.ColorAttachmentOutputBit,

            DstStageMask =
                VkPipelineStageFlags.ColorAttachmentOutputBit,

            SrcAccessMask = 0,

            DstAccessMask =
                VkAccessFlags.ColorAttachmentWriteBit
                
        };

        return _device.CreateRenderPass(
            new VkRenderPassCreateInfo
            {
                AttachmentCount = 1,
                Attachments = &colorAttachment,

                SubpassCount = 1,
                Subpasses = &subpass,

                DependencyCount = 1,
                Dependencies = &dependency
            },
            null
        );
    }

    private Frame CreateFrame()
    {
        var pool = _device.CreateCommandPool(
            new VkCommandPoolCreateInfo
            {
                QueueFamilyIndex = _queueFamily
            },
            null
        );

        var commandBuffer = _device.AllocateCommandBuffer(
            new VkCommandBufferAllocateInfo
            {
                CommandPool = pool,
                Level = VkCommandBufferLevel.Primary
            }
        );

        return new Frame
        {
            Pool = pool,
            CommandBuffer = commandBuffer,

            Fence = _device.CreateFence(
                new VkFenceCreateInfo
                {
                    Flags = VkFenceCreateFlags.SignaledBit
                },
                null
            ),

            ImageAvailable = _device.CreateSemaphore(
                new VkSemaphoreCreateInfo(),
                null
            ),

            RenderFinished = _device.CreateSemaphore(
                new VkSemaphoreCreateInfo(),
                null
            )
        };
    }


    public void DrawFrame()
    {
        var frame = _frames[_currentFrame];

        // 1. Wait until this frame's previous GPU work is done.
        _device.WaitForFences(
            [frame.Fence],
            true,
            -1
        );

        var imageAvailable = frame.ImageAvailable;
        var renderFinished = frame.RenderFinished;
        var swapchain = _swapchain;

        // 2. Acquire an image from the swapchain.
        var acquire = _device.AcquireNextImageKHR(
            swapchain,
            -1,
            imageAvailable,
            default,
            out var imageIndex
        );

        if (acquire == VkResult.ErrorOutOfDateKHR)
        {
            // Resize handling will be added later.
            return;
        }

        if (acquire is not VkResult.Success
            and not VkResult.SuboptimalKHR)
        {
            throw new Exception(
                $"Failed to acquire image: {acquire}"
            );
        }

        // 3. Prepare the command buffer for recording.
        _device.ResetFences([frame.Fence]);

        _device.ResetCommandPool(
            frame.Pool,
            VkCommandPoolResetFlags.None
        );

        var commandBuffer = frame.CommandBuffer;

        commandBuffer.Begin(
            new VkCommandBufferBeginInfo
            {
                Flags = VkCommandBufferUsageFlags.OneTimeSubmitBit
            }
        );

        // 4. Set the background clear color.
        var clearColor = new VkClearValue
        {
            Color = new VkClearColorValue
            {
                Float32 = new float4(
                    0.05f,
                    0.06f,
                    0.08f,
                    1.0f
                )
            }
        };

        Span<VkClearValue> clearValues = [clearColor];

        // 5. Begin rendering to the acquired framebuffer.
        commandBuffer.BeginRenderPass(
            new VkRenderPassBeginInfo
            {
                RenderPass = _renderPass,

                Framebuffer = _framebuffers[imageIndex],

                RenderArea = new VkRect2D
                {
                    Extent = _swapchainExtent
                },

                ClearValueCount = clearValues.Length,
                ClearValues = clearValues.AsPointer()
            },
            VkSubpassContents.Inline
        );

        // 6. Bind our triangle graphics pipeline.
        commandBuffer.BindPipeline(
            VkPipelineBindPoint.Graphics,
            _pipeline
        );

        // 7. Configure viewport and scissor.
        commandBuffer.SetViewport(
            0,
            [
                new VkViewport
                {
                    Width = _swapchainExtent.Width,
                    Height = _swapchainExtent.Height,
                    MaxDepth = 1.0f
                }
            ]
        );

        commandBuffer.SetScissor(
            0,
            [
                new VkRect2D
                {
                    Extent = _swapchainExtent
                }
            ]
        );
        commandBuffer.BindVertexBuffer(
            0,
            _vertexBuffer
        );
  
        commandBuffer.BindIndexBuffer(
            _indexBuffer,
            ByteSize.Zero,
            VkIndexType.UInt32
        );

        // 8. Draw three vertices!
        commandBuffer.DrawIndexed(_indexCount, 1, 0, 0, 0);

        // 9. Finish recording.
        commandBuffer.EndRenderPass();
        commandBuffer.End();

        // 10. Submit commands to the graphics queue.
        var waitStage =
            VkPipelineStageFlags.ColorAttachmentOutputBit;

        var commandBufferHandle = commandBuffer.Handle;

        _graphicsQueue.Submit(
            [
                new VkSubmitInfo
                {
                    WaitSemaphoreCount = 1,
                    WaitSemaphores = &imageAvailable,

                    WaitDstStageMask = &waitStage,

                    CommandBufferCount = 1,
                    CommandBuffers = &commandBufferHandle,

                    SignalSemaphoreCount = 1,
                    SignalSemaphores = &renderFinished
                }
            ],
            frame.Fence
        );

        // 11. Present the completed image.
        var presentIndex = imageIndex;

        var present = _graphicsQueue.PresentKHR(
            new VkPresentInfoKHR
            {
                WaitSemaphoreCount = 1,
                WaitSemaphores = &renderFinished,

                SwapchainCount = 1,
                Swapchains = &swapchain,

                ImageIndices = &presentIndex
            }
        );

        if (present is not VkResult.Success
            and not VkResult.SuboptimalKHR
            and not VkResult.ErrorOutOfDateKHR)
        {
            throw new Exception(
                $"Failed to present image: {present}"
            );
        }

        // Simplified synchronization for our tutorial.
        // Wait before reusing the presentation semaphore.
        _graphicsQueue.WaitIdle();

        _currentFrame =
            (_currentFrame + 1) % MaxFramesInFlight;
    }


    private int FindGraphicsPresentFamily(
        PhysicalDevice gpu
    )
    {
        var count =
            gpu.GetQueueFamilyProperties_Count();

        Span<VkQueueFamilyProperties> families =
            stackalloc VkQueueFamilyProperties[count];

        gpu.GetQueueFamilyProperties_Write(families);

        for (var i = 0; i < families.Length; i++)
        {
            if (!families[i].QueueFlags.HasFlag(
                    VkQueueFlags.GraphicsBit))
            {
                continue;
            }

            if (!gpu.GetSurfaceSupportKHR(
                    i, _surface))
            {
                continue;
            }

            return i;
        }

        return -1;
    }

    private DeviceEx CreateDevice()
    {
        float* priority = stackalloc float[1];
        priority[0] = 1.0f;

        var queueInfo =
            new VkDeviceQueueCreateInfo
            {
                QueueFamilyIndex = _queueFamily,
                QueueCount = 1,
                QueuePriorities = priority
            };

        var extensions = new HashSet<string>();

        VulkanHelpers.AddSwapchainExtensions(
            extensions
        );

        VulkanHelpers
            .AddMandatoryIfSupportedDeviceExtensions(
                extensions,
                _physicalDevice
            );

        return _physicalDevice.CreateDevice(
            new DeviceEx.CreateInfo
            {
                Extensions = extensions,
                QueueCreateInfos = [queueInfo]
            }
        );
    }

    private void DestroySwapchain()
    {
        foreach (var imageView in _swapchainImageViews)
        {
            if (!imageView.IsNull())
            {
                _device.DestroyImageView(imageView, null);
            }
        }

        _swapchainImageViews = [];

        if (!_swapchain.IsNull())
        {
            _device.DestroySwapchainKHR(
                _swapchain,
                null
            );
        }

        _swapchain = default;
        _swapchainImages = [];
    }

    void DestroyPipeline()
    {
        if (!_pipeline.IsNull())
        {
            _device.DestroyPipeline(_pipeline, null);
            _pipeline = default;
        }

        if (!_pipelineLayout.IsNull())
        {
            _device.DestroyPipelineLayout(_pipelineLayout, null);
            _pipelineLayout = default;

        }
    }

    private void DestroyFramebuffers()
    {
        foreach (var framebuffer in _framebuffers)
        {
            if (!framebuffer.IsNull())
            {
                _device.DestroyFramebuffer(framebuffer, null);
            }
        }

        _framebuffers = [];
    }

    private void DestroyMeshBuffer()
    {
        if (!_vertexBuffer.IsNull())
        {
            _device.DestroyBuffer(
                _vertexBuffer,
                null
            );

            _device.FreeMemory(
                _vertexMemory,
                null
            );
        }

        if (!_indexBuffer.IsNull())
        {
            _device.DestroyBuffer(_indexBuffer, null);
            _device.FreeMemory(_indexMemory, null);
        }

        _vertexBuffer = default;
        _vertexMemory = default;

        _indexBuffer = default;
        _indexMemory = default;

        _indexCount = 0;

    }

    public void Dispose()
    {
        _device.WaitIdle();

        DestroyMeshBuffer();
        DestroyPipeline();
        DestroyFramebuffers();

        if(!_renderPass.IsNull())
        {
            _device.DestroyRenderPass(_renderPass, null);
        }

        DestroySwapchain();

        foreach (var frame in _frames)
        {
            _device.DestroyFence(
                frame.Fence,
                null
            );

            _device.DestroySemaphore(
                frame.ImageAvailable,
                null
            );

            _device.DestroySemaphore(
                frame.RenderFinished,
                null
            );

            _device.DestroyCommandPool(
                frame.Pool,
                null
            );
        }

        _device.Dispose();

        _instance.DestroySurfaceKHR(
            _surface,
            null
        );

        _instance.Dispose();
    }
}
