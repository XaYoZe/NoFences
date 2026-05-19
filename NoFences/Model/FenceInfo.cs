using System;
using System.Collections.Generic;

namespace NoFences.Model
{
    /// <summary>
    /// 栅栏元数据实体类。通过 XmlSerializer 序列化到
    /// %LocalAppData%/NoFences/<guid>/__fence_metadata.xml。
    /// 
    /// 【警告】所有属性名称不得重命名 — 它们直接对应 XML 节点名，
    /// 重命名会破坏已有用户的栅栏数据文件。
    /// </summary>
    public class FenceInfo
    {
        /*
         * 请勿重命名任何属性。它们通过 XmlSerializer 直接序列化到磁盘，
         * 没有 [XmlElement] 等自定义映射属性。
         */

        /// <summary>栅栏唯一标识符（对应存储目录名）</summary>
        public Guid Id { get; set; }

        /// <summary>栅栏显示名称</summary>
        public string Name { get; set; }

        /// <summary>窗口 X 坐标（屏幕像素）</summary>
        public int PosX { get; set; }

        /// <summary>窗口 Y 坐标（屏幕像素）</summary>
        public int PosY { get; set; }

        /// <summary>DPI 缩放后的窗口宽度</summary>
        public int Width { get; set; }

        /// <summary>DPI 缩放后的窗口高度</summary>
        public int Height { get; set; }

        /// <summary>是否锁定（锁定后不可拖动/调整大小）</summary>
        public bool Locked { get; set; }

        /// <summary>是否允许最小化（鼠标离开时收缩为标题栏）</summary>
        public bool CanMinify { get; set; }

        /// <summary>标题栏高度（逻辑像素，默认 35）</summary>
        public int TitleHeight { get; set; } = 35;

        /// <summary>栅栏内包含的文件/文件夹路径列表</summary>
        public List<string> Files { get; set; } = new List<string>();

        public FenceInfo()
        {

        }

        public FenceInfo(Guid id)
        {
            Id = id;
        }
    }
}
