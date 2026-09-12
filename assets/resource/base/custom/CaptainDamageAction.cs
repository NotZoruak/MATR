using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

public class CaptainDamageAction : IMaaCustomAction
{
    public string Name { get; set; } = nameof(CaptainDamageAction);

    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        Log(context, "检测到队长重伤，撤退撤退");
        return true;
    }

    private static void Log<T>(T context, string message) where T : IMaaContext
    {
        LoggerHelper.Info(message);
        try
        {
            ActionParamHelper.ResolveOwnerProcessor(context)?.AddLog(message);
        }
        catch { }
    }
}
