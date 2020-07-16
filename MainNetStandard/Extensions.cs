using System;

namespace MainNetStandard
{
    public static class Extensions
    {
        public static TTo[] ConvertParallel<TFrom, TTo>(this TFrom[] toConvert, Func<TFrom, TTo> convert)
        {
            var converted = new TTo[toConvert.Length];
            ParallelFor(i => converted[i] = convert(toConvert[i]), 0, toConvert.Length);
            return converted;
        }

        public static System.Drawing.Bitmap Ensure32bppArgb(this System.Drawing.Bitmap bitmap)
        {
            if (bitmap.PixelFormat == System.Drawing.Imaging.PixelFormat.Format32bppArgb)
            {
                return bitmap;
            }
            var bitmap32bppArgb = bitmap.To32bppArgb();
            bitmap.Dispose();
            return bitmap32bppArgb;
        }

        public static void ExecutePinned<T>(this T[] array, Action<IntPtr, int> executePinnedDelegate)
            where T : unmanaged
        {
            // validate
            if (array is null)
            {
                throw new ArgumentNullException(nameof(array));
            }
            if (executePinnedDelegate is null)
            {
                throw new ArgumentNullException(nameof(executePinnedDelegate));
            }

            var gcHandle = default(System.Runtime.InteropServices.GCHandle);
            try
            {
                // pin memory
                gcHandle = System.Runtime.InteropServices.GCHandle.Alloc(array, System.Runtime.InteropServices.GCHandleType.Pinned);

                // execute
                executePinnedDelegate(gcHandle.AddrOfPinnedObject(), array.GetSizeInBytes());
            }
            finally
            {
                // un-pin memory
                if (gcHandle.IsAllocated)
                {
                    gcHandle.Free();
                }
            }
        }

        public static System.IO.Stream GetEmbeddedResourceStream<TType>(this TType relativeType, string name)
            where TType : Type =>
            relativeType.Assembly.GetManifestResourceStream($"{relativeType.Namespace}.{name}"
                .Replace(' ', '_').Replace('\\', '.').Replace('/', '.'))
            ?? throw new NullReferenceException($"Invalid resource name: {name}.");

        public static unsafe int GetSizeInBytes<T>(this T[] array) where T : unmanaged => sizeof(T) * array.Length;

        public static void ParallelFor(this Action<int> action, int from, int to)
        {
            var parallelOptions = new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };
            System.Threading.Tasks.Parallel.For(from, to, parallelOptions, action);
        }

        public static unsafe int[] ReadImageArgb(this System.IO.Stream stream, out int width, out int height)
        {
            using (var bitmap = new System.Drawing.Bitmap(stream).Ensure32bppArgb())
            {
                var lockBits = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
                    System.Drawing.Imaging.ImageLockMode.ReadOnly, bitmap.PixelFormat);
                try
                {
                    var argb = new int[bitmap.Width * bitmap.Height];
                    ExecutePinned(argb, (argbPtr, totalBytes) => Buffer.MemoryCopy((void*)lockBits.Scan0, (void*)argbPtr, totalBytes, totalBytes));
                    width = bitmap.Width;
                    height = bitmap.Height;
                    return argb;
                }
                finally
                {
                    bitmap.UnlockBits(lockBits);
                }
            }
        }

        public static string ReadString(this System.IO.Stream stream)
        {
            using (var streamReader = new System.IO.StreamReader(stream))
            {
                return streamReader.ReadToEnd();
            }
        }

        public static System.Drawing.Bitmap To32bppArgb(this System.Drawing.Bitmap bitmap)
        {
            var bitmap32bppArgb = new System.Drawing.Bitmap(bitmap.Width, bitmap.Height,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap32bppArgb))
            {
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                graphics.DrawImage
                (
                    bitmap,
                    new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
                    new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
                    System.Drawing.GraphicsUnit.Pixel
                );
            }
            return bitmap32bppArgb;
        }

        public static unsafe int ToArgb(this System.Numerics.Vector4 color)
        {
            int drawingColorArgb;
            var ptr = (byte*)&drawingColorArgb;
            *(ptr + 2) = (byte)(color.X * byte.MaxValue);   // r
            *(ptr + 1) = (byte)(color.Y * byte.MaxValue);   // g
            *ptr = (byte)(color.Z * byte.MaxValue);         // b
            *(ptr + 3) = (byte)(color.W * byte.MaxValue);   // a
            return drawingColorArgb;
        }

        public static unsafe int ToArgb(this System.Numerics.Vector3 color, byte alpha)
        {
            int drawingColorArgb;
            var ptr = (byte*)&drawingColorArgb;
            *(ptr + 2) = (byte)(color.X * byte.MaxValue);   // r
            *(ptr + 1) = (byte)(color.Y * byte.MaxValue);   // g
            *ptr = (byte)(color.Z * byte.MaxValue);         // b
            *(ptr + 3) = alpha;                             // a
            return drawingColorArgb;
        }

        public static int[] ToArgb(this System.Numerics.Vector4[] color) => ConvertParallel(color, ToArgb);

        public static int[] ToArgb(this System.Numerics.Vector3[] color, byte alpha) => ConvertParallel(color, v => ToArgb(v, alpha));

        public static unsafe System.Numerics.Vector4 ToVector4Rgba(this int argb)
        {
            var ptr = (byte*)&argb;
            return new System.Numerics.Vector4
            (
                *(ptr + 2) / 255f,  // r
                *(ptr + 1) / 255f,  // g
                *ptr / 255f,        // b
                *(ptr + 3) / 255f   // a
            );
        }

        public static System.Numerics.Vector4[] ToVector4Rgba(this int[] argb) => ConvertParallel(argb, ToVector4Rgba);

        public static unsafe System.Drawing.Bitmap ToBitmap(this int[] argb, int width, int height)
        {
            var bitmap = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var lockBits = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
                System.Drawing.Imaging.ImageLockMode.WriteOnly, bitmap.PixelFormat);
            try
            {
                ExecutePinned(argb, (argbPtr, totalBytes) => Buffer.MemoryCopy((void*)argbPtr, (void*)lockBits.Scan0, totalBytes, totalBytes));
            }
            finally
            {
                bitmap.UnlockBits(lockBits);
            }
            return bitmap;
        }
    }
}
