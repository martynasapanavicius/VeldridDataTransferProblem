namespace MainNetStandard
{
    public abstract class GpuBufferDescription
    {
        #region // storage

        public string Name { get; }
        public int LayoutSet { get; }
        public int LayoutBinding { get; }
        public int TotalBytes { get; }
        public int BytesPerItem { get; }
        public bool IsUniform { get; }

        #endregion

        #region // ctor

        protected GpuBufferDescription(string name, int layoutSet, int layoutBinding,
            int totalBytes, int bytesPerItem, bool isUniform)
        {
            Name = name;
            LayoutSet = layoutSet;
            LayoutBinding = layoutBinding;
            TotalBytes = totalBytes;
            BytesPerItem = bytesPerItem;
            IsUniform = isUniform;
        }

        #endregion
    }

    public class GpuStructuredBufferDescription<T> :
        GpuBufferDescription
        where T : unmanaged
    {
        #region // ctor

        public unsafe GpuStructuredBufferDescription(string name, int layoutSet, int layoutBinding, int itemCount) :
            base(name, layoutSet, layoutBinding, sizeof(T) * itemCount, sizeof(T), false)
        {
        }

        #endregion
    }

    public class GpuUniformBufferDescription<T> :
        GpuBufferDescription
        where T : unmanaged
    {
        #region // ctor

        public unsafe GpuUniformBufferDescription(string name, int layoutSet, int layoutBinding) :
            base(name, layoutSet, layoutBinding, sizeof(T), sizeof(T), true)
        {
        }

        #endregion
    }
}
