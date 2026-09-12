using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using Newtonsoft.Json.Linq;
using System;
using System.IO;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>
/// 远征派遣打点：写入 GUI 日志与工作记录词表。
///
/// 日志与地图名称都取执行任务那个实例的配置，与同步后勤的管线合并保持同一数据源；
/// 多实例下按「当前激活实例」记录会把派遣记录写到别的实例。
/// </summary>
public class DispatchLogAction : IMaaCustomAction
{
    public string Name { get; set; } = nameof(DispatchLogAction);

    private static int? _markedTeam;

    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        var json = ActionParamHelper.Parse(args.ActionParam);
        var mode = (string?)json["mode"];

        if (mode == "mark")
        {
            int team = (int?)json["team"] ?? 0;
            _markedTeam = team;
            return true;
        }

        if (mode == "log")
        {
            int team = _markedTeam ?? 0;
            _markedTeam = null;
            string message;

            if (team == 0)
            {
                message = "[远征派遣] 派出远征队伍";
                Log(context, message, "[后勤] 派遣远征 派出远征队伍");
            }
            else
            {
                string teamLabel = TeamToLabel(team);
                string mapLabel = ReadMapLabel(context, team);
                message = $"[远征派遣] {teamLabel} → {mapLabel}";
                // 文件日志保留词表格式，供工作记录解析器识别。
                Log(context, message, $"[后勤] 派遣远征 {teamLabel}已派遣至 {mapLabel}");
            }

            return true;
        }

        return true;
    }

    /// <summary>把记录写到执行任务那个实例的日志里；定位不到时退回当前激活实例。</summary>
    private static void Log<T>(T context, string message, string fileMessage) where T : IMaaContext
    {
        LoggerHelper.Info(fileMessage);
        try
        {
            var processor = MaaProcessor.ResolveByTasker(context.Tasker)
                ?? MaaProcessorManager.Instance.Current;
            processor?.AddLog(message);
        }
        catch
        {
            // 静默忽略，确保不影响流水线执行
        }
    }

    private static string TeamToLabel(int team) => $"部队{team}";

    private static string TeamToConfigName(int team) => team switch
    {
        1 => "部队一",
        2 => "部队二",
        3 => "部队三",
        4 => "部队四",
        5 => "部队五",
        _ => $"部队{team}"
    };

    /// <summary>读取该部队的目的地文本；休息显示为「休息」，读不到时显示为「??」。</summary>
    private static string ReadMapLabel<T>(T context, int team) where T : IMaaContext
    {
        var processor = MaaProcessor.ResolveByTasker(context.Tasker);
        int? mapIndex = processor?.GetLogisticsTeamMapIndex(TeamToConfigName(team));

        if (processor == null)
        {
            var fallbackIndex = ReadMapIndexFromActiveInstanceConfig(team);
            mapIndex = fallbackIndex < 0 ? null : fallbackIndex;
        }

        if (mapIndex == null)
            return "??";
        if (mapIndex <= 0)
            return "休息";

        int index = mapIndex.Value;
        int era = (index - 1) / 4 + 1;
        int region = (index - 1) % 4 + 1;
        return $"{era}-{region}";
    }

    /// <summary>
    /// 兜底路径：按当前激活实例读取「后勤」任务的部队地图序号，找不到时返回 -1。
    /// 仅在无法定位执行实例时使用，正常运行不会走到这里。
    /// </summary>
    private static int ReadMapIndexFromActiveInstanceConfig(int team)
    {
        try
        {
            string instancesDir = AppPaths.InstancesDirectory;
            if (!Directory.Exists(instancesDir))
                return -1;

            // 使用当前激活实例的 UUID 定位配置文件（config/instances/{uuid}.json），
            // 与 InstanceConfiguration.GetConfigFilePath() 保持一致。
            // 注意：不能使用 appsettings.json 的 Instances.LastActiveName（实例显示名），
            // 显示名与实例文件名无关；且回退 default.json 会读错其他实例的远征配置。
            string instanceId = MaaProcessorManager.Instance?.Current?.InstanceId ?? string.Empty;
            string configPath = string.IsNullOrWhiteSpace(instanceId)
                ? Path.Combine(instancesDir, "default.json")
                : Path.Combine(instancesDir, $"{instanceId}.json");
            if (!File.Exists(configPath))
                configPath = Path.Combine(instancesDir, "default.json");
            if (!File.Exists(configPath))
                return -1;

            var config = JObject.Parse(File.ReadAllText(configPath));
            if (config["TaskItems"] is not JArray taskItems)
                return -1;

            string teamName = TeamToConfigName(team);
            foreach (var item in taskItems)
            {
                // 按 entry 匹配后勤任务（任务由「远征」改名而来，兼容旧名）
                if ((string?)item["entry"] != "Expedition" && (string?)item["name"] != "远征")
                    continue;

                if (item["option"] is not JArray options)
                    return -1;

                foreach (var opt in options)
                {
                    if ((string?)opt["name"] == teamName)
                        return (int?)opt["index"] ?? -1;
                }

                return -1;
            }

            return -1;
        }
        catch (Exception e)
        {
            LoggerHelper.Warning($"[DispatchLog] 读取后勤配置失败：{e.Message}");
            return -1;
        }
    }
}
