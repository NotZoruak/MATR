using MaaFramework.Binding;

namespace MFAAvalonia.Extensions.MaaFW;

/// <summary>
/// ADB 截图方式连接失败时的降级策略。
/// </summary>
public static class MaaAdbConnectionFallback
{
    /// <summary>
    /// MuMu 专用截图方式不可用时回退到通用 ADB 截图方式。
    /// </summary>
    public static AdbScreencapMethods GetFallbackScreenCap(AdbScreencapMethods screenCap) =>
        screenCap.HasFlag(AdbScreencapMethods.EmulatorExtras)
            ? AdbScreencapMethods.Default
            : screenCap;
}
