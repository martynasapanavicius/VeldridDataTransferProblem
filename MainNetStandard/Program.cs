using System;
using System.Drawing.Imaging;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using Veldrid;

namespace MainNetStandard
{
    public class Program
    {
        public static void Main()
        {
            var input4 = typeof(Program).GetEmbeddedResourceStream("image.png").ReadImageArgb(out var width, out var height).ToVector4Rgba();
            var input3 = input4.ConvertParallel(v => new Vector3(v.X, v.Y, v.Z));

            var computeShaderSource4 = typeof(Program).GetEmbeddedResourceStream("computeInvertColors4.glsl").ReadString();
            var computeShaderSource3 = typeof(Program).GetEmbeddedResourceStream("computeInvertColors3.glsl").ReadString();

            foreach (var graphicsBackend in Enum.GetValues(typeof(GraphicsBackend)).Cast<GraphicsBackend>())
            {
                try
                {
                    var output4 = LaunchComputeInvertColors(graphicsBackend, computeShaderSource4, "main", input4, width, height);
                    var output3 = LaunchComputeInvertColors(graphicsBackend, computeShaderSource3, "main", input3, width, height);

                    using (var bitmap = output4.ToArgb().ToBitmap(width, height))
                    {
                        bitmap.Save($"vec4_{graphicsBackend}.png", ImageFormat.Png);
                    }
                    using (var bitmap = output3.ToArgb(255).ToBitmap(width, height))
                    {
                        bitmap.Save($"vec3_{graphicsBackend}.png", ImageFormat.Png);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"{graphicsBackend} failed: {e.Message}");
                }
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct Info
        {
            public int Width { get; }
            public int Height { get; }
            private Vector2 _Padding { get; }

            public Info(int width, int height) => (Width, Height, _Padding) = (width, height, default);
        }

        private static T[] LaunchComputeInvertColors<T>(GraphicsBackend graphicsBackend, string computeShaderSource,
            string computeShaderEntryPoint, T[] input, int width, int height)
            where T : unmanaged
        {
            using (var gpuComputer = new GpuComputer(graphicsBackend, computeShaderSource, computeShaderEntryPoint))
            {
                // allocate cpu
                var info = new Info(width, height);

                // allocate gpu
                var infoBuffer = gpuComputer.CreateBuffer(new GpuUniformBufferDescription<Info>("gInfo", 0, 0));
                var inputBuffer = gpuComputer.CreateBuffer(new GpuStructuredBufferDescription<T>("gInput", 1, 0, info.Width * info.Height));
                var outputBuffer = gpuComputer.CreateBuffer(new GpuStructuredBufferDescription<T>("gOutput", 2, 0, info.Width * info.Height));

                // cpu -> gpu
                infoBuffer.Write(info);
                inputBuffer.Write(input);
                System.Diagnostics.Debug.Assert(inputBuffer.Read<T>().SequenceEqual(input));

                // launch computation
                gpuComputer.Launch(info.Width, info.Height, 1);

                // gpu -> cpu
                return outputBuffer.Read<T>();
            }
        }
    }
}
