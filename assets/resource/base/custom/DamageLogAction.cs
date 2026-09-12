using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

public class DamageLogAction : IMaaCustomAction
{
    public string Name { get; set; } = nameof(DamageLogAction);

    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        var json = ActionParamHelper.Parse(args.ActionParam);
        var message = (string)json["message"];
        if (!string.IsNullOrWhiteSpace(message))
        {
            LoggerHelper.Info(message);
            try
            {
                ActionParamHelper.ResolveOwnerProcessor(context)?.AddLog(message);
            }
            catch
            {
                // 静默忽略，确保不影响流水线执行
            }
        }
        return true;
    }
}
