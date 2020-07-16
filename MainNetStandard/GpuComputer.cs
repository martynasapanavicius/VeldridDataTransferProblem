using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.SPIRV;
using Veldrid.StartupUtilities;

namespace MainNetStandard
{
    public class GpuComputer :
        IDisposable
    {
        #region // storage

        private GraphicsBackend GraphicsBackend { get; set; }
        private Sdl2Window Window { get; set; }
        private GraphicsDevice GraphicsDevice { get; set; }
        private Shader Shader { get; set; }
        private CommandList CommandList { get; set; }
        private Pipeline Pipeline { get; set; }
        private List<GpuBuffer> GpuBuffers { get; set; }
        private List<ResourceLayout> ResourceLayouts { get; set; }
        private List<(int slot, ResourceSet rs)> ResourceSets { get; set; }

        #endregion

        #region // ctor

        public GpuComputer(GraphicsBackend graphicsBackend, string computeShaderSource, string computeShaderEntryPoint)
        {
            if (!GraphicsDevice.IsBackendSupported(graphicsBackend))
            {
                throw new NotSupportedException($"{nameof(GpuComputer)} does not support '{graphicsBackend}' graphics backend.");
            }

            GraphicsBackend = graphicsBackend;
            GpuBuffers = new List<GpuBuffer>();

            // create hidden window
            Window = VeldridStartup.CreateWindow(new WindowCreateInfo(0, 0, 256, 256, WindowState.Hidden, string.Empty));

            // create gpu device
            GraphicsDevice = VeldridStartup.CreateGraphicsDevice(Window, GraphicsBackend);
            if (!GraphicsDevice.Features.StructuredBuffer)
            {
                GraphicsDevice.WaitForIdle();
                GraphicsDevice.Dispose();
                Window.Close();
                throw new NotSupportedException($"'{graphicsBackend}' graphics backend does not support structured buffers.");
            }

            // create shader
            Shader = GraphicsDevice.ResourceFactory.CreateFromSpirv(new ShaderDescription(
                ShaderStages.Compute, Encoding.UTF8.GetBytes(computeShaderSource), computeShaderEntryPoint));

            // create command list
            CommandList = GraphicsDevice.ResourceFactory.CreateCommandList();
        }

        public void Dispose()
        {
            GraphicsDevice.WaitForIdle();

            DisposePipeline();

            CommandList.Dispose();
            CommandList = default;

            Shader.Dispose();
            Shader = default;

            GpuBuffers.ForEach(d => d.Dispose());
            GpuBuffers = default;

            GraphicsDevice.WaitForIdle();
            GraphicsDevice = default;

            Window.Close();
            Window = default;

            GraphicsBackend = default;
        }

        private void DisposePipeline()
        {
            ResourceLayouts?.ForEach(d => d.Dispose());
            ResourceLayouts = default;

            ResourceSets?.ForEach(tuple => tuple.rs.Dispose());
            ResourceSets = default;

            Pipeline?.Dispose();
            Pipeline = default;
        }

        #endregion

        #region // routines

        private GpuBuffer StoreResource(GpuBuffer gpuBuffer)
        {
            if (GpuBuffers.Any(b => string.Equals(b.Description.Name, gpuBuffer.Description.Name)))
            {
                throw new ArgumentException(@"Resource name is already taken.", nameof(gpuBuffer));
            }
            if (GpuBuffers.Any(b => b.Description.LayoutSet == gpuBuffer.Description.LayoutSet &&
                                     b.Description.LayoutBinding == gpuBuffer.Description.LayoutBinding))
            {
                throw new ArgumentException(@"Resource layout set and binding is already taken.", nameof(gpuBuffer));
            }
            GpuBuffers.Add(gpuBuffer);

            // invalidate pipeline, because new buffers won't be bound to it
            // pipeline will be reconstructed just before launch if needed
            DisposePipeline();

            return gpuBuffer;
        }

        public GpuBuffer CreateBuffer(GpuBufferDescription description) =>
            StoreResource(new GpuBuffer(description, GraphicsDevice, CommandList));

        private void CreatePipeline()
        {
            DisposePipeline();

            var groupedBySet = GpuBuffers
                .GroupBy(r => r.Description.LayoutSet, r => r)
                .OrderBy(group => group.Key)
                .ToArray();

            var resourceLayouts = new List<ResourceLayout>();
            var resourceSets = new List<(int slot, ResourceSet rs)>();
            foreach (var group in groupedBySet)
            {
                // sort by binding
                var sortedByBinding = group.OrderBy(r => r.Description.LayoutBinding).ToArray();

                // create resource layout
                var resourceLayoutElementDescriptions = sortedByBinding.SelectMany(r => r.GetResourceLayoutElementDescriptions()).ToArray();
                var resourceLayoutDescription = new ResourceLayoutDescription(resourceLayoutElementDescriptions);
                var resourceLayout = GraphicsDevice.ResourceFactory.CreateResourceLayout(resourceLayoutDescription);
                resourceLayouts.Add(resourceLayout);

                // create resource set
                var bindableResources = sortedByBinding.SelectMany(r => r.GetBindableResources()).ToArray();
                var resourceSetDescription = new ResourceSetDescription(resourceLayout, bindableResources);
                var resourceSet = GraphicsDevice.ResourceFactory.CreateResourceSet(resourceSetDescription);
                resourceSets.Add((group.Key, resourceSet));
            }
            ResourceLayouts = resourceLayouts;
            ResourceSets = resourceSets;

            // create pipeline
            Pipeline = GraphicsDevice.ResourceFactory.CreateComputePipeline(
                new ComputePipelineDescription(Shader, resourceLayouts.ToArray(), 1, 1, 1));
        }

        private static void Launch(int groupCountX, int groupCountY, int groupCountZ, GraphicsDevice graphicsDevice,
            CommandList commandList, Pipeline pipeline, IEnumerable<(int slot, ResourceSet rs)> resourceSets)
        {
            commandList.Begin();

            // set pipeline
            commandList.SetPipeline(pipeline);

            // set resource sets (bind gpu buffers)
            foreach (var (slot, rs) in resourceSets)
            {
                commandList.SetComputeResourceSet((uint)slot, rs);
            }

            // launch gpu computation
            commandList.Dispatch((uint)groupCountX, (uint)groupCountY, (uint)groupCountZ);

            commandList.End();

            // submit and wait for gpu to finish
            graphicsDevice.SubmitCommands(commandList);
            graphicsDevice.WaitForIdle();
        }

        public void Launch(int groupCountX, int groupCountY, int groupCountZ)
        {
            // ensure pipeline is created
            if (ResourceLayouts is null || ResourceSets is null || Pipeline is null)
            {
                CreatePipeline();
            }

            // launch
            Launch(groupCountX, groupCountY, groupCountZ, GraphicsDevice, CommandList, Pipeline, ResourceSets);
        }

        #endregion
    }
}
