using System;
using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Extensions.MaaFW;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

public class GuiLogAction : IMaaCustomAction
{
    public string Name { get; set; } = nameof(GuiLogAction);

    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        var json = ActionParamHelper.Parse(args.ActionParam);
        var message = (string?)json["message"] ?? string.Empty;
        if (string.IsNullOrWhiteSpace(message))
            return true;

        try
        {
            var isWarning = message.StartsWith("warn:", StringComparison.OrdinalIgnoreCase)
                || message.StartsWith("warning:", StringComparison.OrdinalIgnoreCase);
            ActionParamHelper.ResolveOwnerProcessor(context)?.AddLog(message, recordAsWarning: isWarning);
        }
        catch
        {
            // 静默忽略，确保不影响流水线执行
        }

        return true;
    }
}
