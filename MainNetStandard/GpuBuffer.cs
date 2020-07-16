using System;
using System.Collections.Generic;
using Veldrid;

namespace MainNetStandard
{
    public class GpuBuffer :
        IDisposable
    {
        #region // storage

        public GpuBufferDescription Description { get; private set; }
        private GraphicsDevice GraphicsDevice { get; set; }
        private CommandList CommandList { get; set; }
        private DeviceBuffer DeviceBuffer { get; set; }
        private DeviceBuffer DeviceBufferStaging { get; set; }
        private ResourceLayoutElementDescription ResourceLayoutElementDescription { get; set; }

        #endregion

        #region // ctor

        public GpuBuffer(GpuBufferDescription description, GraphicsDevice graphicsDevice, CommandList commandList)
        {
            Description = description;
            GraphicsDevice = graphicsDevice;
            CommandList = commandList;

            // create gpu buffer
            var bufferDescription = new BufferDescription
            (
                (uint)Description.TotalBytes,
                Description.IsUniform ? BufferUsage.UniformBuffer : BufferUsage.StructuredBufferReadWrite,
                Description.IsUniform ? 0 : (uint)Description.BytesPerItem
            );
            DeviceBuffer = GraphicsDevice.ResourceFactory.CreateBuffer(bufferDescription);

            // create gpu buffer for staging (for non-uniforms)
            if (!Description.IsUniform)
            {
                var bufferStagingDescription = new BufferDescription(DeviceBuffer.SizeInBytes, BufferUsage.Staging);
                DeviceBufferStaging = GraphicsDevice.ResourceFactory.CreateBuffer(bufferStagingDescription);
            }

            // create resource layout element description
            ResourceLayoutElementDescription = new ResourceLayoutElementDescription
            (
                Description.Name,
                Description.IsUniform ? ResourceKind.UniformBuffer : ResourceKind.StructuredBufferReadWrite,
                ShaderStages.Compute
            );
        }

        public void Dispose()
        {
            ResourceLayoutElementDescription = default;

            DeviceBufferStaging?.Dispose();
            DeviceBufferStaging = default;

            DeviceBuffer.Dispose();
            DeviceBuffer = default;

            CommandList = default;
            GraphicsDevice = default;

            Description = default;
        }

        #endregion

        #region // routines

        private static void Write(GraphicsDevice graphicsDevice, CommandList commandList,
            DeviceBuffer deviceBuffer, IntPtr source, int bytesToWrite)
        {
            // validate
            if (deviceBuffer.SizeInBytes < bytesToWrite)
            {
                throw new ArgumentOutOfRangeException(nameof(bytesToWrite), @"Too many bytes to write.");
            }

            // copy cpu -> gpu
            commandList.Begin();
            commandList.UpdateBuffer(deviceBuffer, 0, source, (uint)bytesToWrite);
            commandList.End();
            graphicsDevice.SubmitCommands(commandList);
            graphicsDevice.WaitForIdle();
        }

        private static void Read(GraphicsDevice graphicsDevice, CommandList commandList, DeviceBuffer deviceBuffer,
            DeviceBuffer deviceBufferStaging, IntPtr destination, int bytesToRead)
        {
            // validate
            if ((deviceBuffer.Usage & BufferUsage.UniformBuffer) != 0)
            {
                throw new InvalidOperationException("Cannot read from uniform buffer.");
            }
            if (deviceBuffer.SizeInBytes < bytesToRead || deviceBufferStaging.SizeInBytes < bytesToRead)
            {
                throw new ArgumentOutOfRangeException(nameof(bytesToRead), @"Too many bytes to read.");
            }

            // copy gpu -> gpu staging
            commandList.Begin();
            commandList.CopyBuffer(deviceBuffer, 0, deviceBufferStaging, 0, deviceBuffer.SizeInBytes);
            commandList.End();
            graphicsDevice.SubmitCommands(commandList);
            graphicsDevice.WaitForIdle();

            // copy gpu staging -> cpu
            var map = graphicsDevice.Map(deviceBufferStaging, MapMode.Read);
            unsafe
            {
                Buffer.MemoryCopy((void*)map.Data, (void*)destination, bytesToRead, bytesToRead);
            }
            graphicsDevice.Unmap(deviceBufferStaging);
        }

        public void Write(IntPtr source, int bytesToWrite) =>
            Write(GraphicsDevice, CommandList, DeviceBuffer, source, bytesToWrite);

        public void Read(IntPtr destination, int bytesToRead) =>
            Read(GraphicsDevice, CommandList, DeviceBuffer, DeviceBufferStaging, destination, bytesToRead);

        public void Write<T>(T[] data)
            where T : unmanaged =>
            data.ExecutePinned(Write);

        public unsafe void Write<T>(T uniform)
            where T : unmanaged =>
            Write((IntPtr)(&uniform), sizeof(T));

        public void Read<T>(T[] data)
            where T : unmanaged =>
            data.ExecutePinned(Read);

        public unsafe T[] Read<T>()
            where T : unmanaged
        {
            if (Description.TotalBytes % sizeof(T) != 0)
            {
                throw new InvalidOperationException($"Cannot align size of {typeof(T)} with buffer.");
            }
            var data = new T[Description.TotalBytes / sizeof(T)];
            data.ExecutePinned(Read);
            return data;
        }

        public IEnumerable<ResourceLayoutElementDescription> GetResourceLayoutElementDescriptions()
        {
            yield return ResourceLayoutElementDescription;
        }

        public IEnumerable<BindableResource> GetBindableResources()
        {
            yield return DeviceBuffer;
        }

        #endregion
    }
}
