using System;
using System.Runtime.InteropServices;

namespace NoFences.Win32
{
    /// <summary>亚克力模糊效果的状态枚举。</summary>
    public enum AccentState
    {
        ACCENT_DISABLED = 0,
        ACCENT_ENABLE_GRADIENT = 1,
        ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
        ACCENT_ENABLE_BLURBEHIND = 3,    // 启用背景模糊（Acrylic 效果）
        ACCENT_INVALID_STATE = 4
    }

    /// <summary>亚克力模糊策略参数结构体。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct AccentPolicy
    {
        public AccentState AccentState;
        public int AccentFlags;
        public int GradientColor;   // 渐变颜色（ARGB）
        public int AnimationId;
    }

    /// <summary>窗口合成属性数据。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct WindowCompositionAttributeData
    {
        public WindowCompositionAttribute Attribute;
        public IntPtr Data;          // 指向 AccentPolicy 结构体的指针
        public int SizeOfData;
    }

    /// <summary>窗口合成属性枚举。</summary>
    public enum WindowCompositionAttribute
    {
        WCA_ACCENT_POLICY = 19  // 设置亚克力/模糊策略
    }

    /// <summary>
    /// Windows 10 亚克力模糊效果工具类。
    /// 通过 SetWindowCompositionAttribute 为窗口启用背景模糊。
    /// </summary>
    public class BlurUtil
    {
        [DllImport("user32.dll")]
        public static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        /// <summary>
        /// 为指定窗口启用背景模糊（Acrylic BlurBehind）。
        /// </summary>
        public static void EnableBlur(IntPtr hwnd)
        {
            var accent = new AccentPolicy();
            accent.AccentState = AccentState.ACCENT_ENABLE_BLURBEHIND;

            var accentStructSize = Marshal.SizeOf(accent);

            // 将 AccentPolicy 结构体封送到非托管内存
            var accentPtr = Marshal.AllocHGlobal(accentStructSize);
            Marshal.StructureToPtr(accent, accentPtr, false);

            var data = new WindowCompositionAttributeData();
            data.Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY;
            data.SizeOfData = accentStructSize;
            data.Data = accentPtr;

            SetWindowCompositionAttribute(hwnd, ref data);

            Marshal.FreeHGlobal(accentPtr);
        }
    }
}
