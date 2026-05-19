using System;
using System.IO;
using System.Xml.Serialization;

namespace NoFences.Model
{
    /// <summary>
    /// 栅栏管理器（单例）。负责从磁盘加载/保存栅栏元数据，
    /// 以及创建和删除栅栏窗口。
    /// 
    /// 数据存储路径：%LocalAppData%/NoFences/<guid>/__fence_metadata.xml
    /// </summary>
    public class FenceManager
    {
        /// <summary>全局单例</summary>
        public static FenceManager Instance { get; } = new FenceManager();

        private const string MetaFileName = "__fence_metadata.xml";

        /// <summary>栅栏数据根目录：%LocalAppData%/NoFences/</summary>
        private readonly string basePath;

        public FenceManager()
        {
            basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NoFences");
            EnsureDirectoryExists(basePath);
        }

        /// <summary>
        /// 从磁盘加载所有栅栏并创建对应的 FenceWindow。
        /// 遍历 basePath 下的每个子目录，反序列化其中的 __fence_metadata.xml。
        /// </summary>
        public void LoadFences()
        {
            foreach (var dir in Directory.EnumerateDirectories(basePath))
            {
                var metaFile = Path.Combine(dir, MetaFileName);
                var serializer = new XmlSerializer(typeof(FenceInfo));
                var reader = new StreamReader(metaFile);
                var fence = serializer.Deserialize(reader) as FenceInfo;
                reader.Close();

                new FenceWindow(fence).Show();
            }
        }

        /// <summary>
        /// 创建新栅栏并显示。
        /// </summary>
        public void CreateFence(string name)
        {
            var fenceInfo = new FenceInfo(Guid.NewGuid())
            {
                Name = name,
                PosX = 100,
                PosY = 250,
                Height = 300,
                Width = 300
            };

            UpdateFence(fenceInfo);
            new FenceWindow(fenceInfo).Show();
        }

        /// <summary>
        /// 删除栅栏及其数据目录。
        /// </summary>
        public void RemoveFence(FenceInfo info)
        {
            Directory.Delete(GetFolderPath(info), true);
        }

        /// <summary>
        /// 将栅栏元数据序列化到磁盘（XmlSerializer）。
        /// </summary>
        public void UpdateFence(FenceInfo fenceInfo)
        {
            var path = GetFolderPath(fenceInfo);
            EnsureDirectoryExists(path);

            var metaFile = Path.Combine(path, MetaFileName);
            var serializer = new XmlSerializer(typeof(FenceInfo));
            var writer = new StreamWriter(metaFile);
            serializer.Serialize(writer, fenceInfo);
            writer.Close();
        }

        /// <summary>确保目录存在，不存在则创建。</summary>
        private void EnsureDirectoryExists(string dir)
        {
            var di = new DirectoryInfo(dir);
            if (!di.Exists)
                di.Create();
        }

        /// <summary>获取栅栏对应的存储目录路径。</summary>
        private string GetFolderPath(FenceInfo fenceInfo)
        {
            return Path.Combine(basePath, fenceInfo.Id.ToString());
        }
    }
}
