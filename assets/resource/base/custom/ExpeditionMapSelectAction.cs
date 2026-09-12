using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using Newtonsoft.Json.Linq;
using System;
using System.IO;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>
/// 同步后勤的选图动作：按「后勤」任务中该支部队的目的地选择时代与地域。
///
/// 目的地必须取执行任务那个实例的配置，与同步后勤的管线合并保持同一数据源：
/// 多实例下若按「当前激活实例」读取，会拿到别的实例的后勤设置，
/// 出现管线要求派遣、动作却判定休息的冲突，流程会反复回到本丸形成死循环。
/// </summary>
public class ExpeditionMapSelectAction : IMaaCustomAction
{
    public string Name { get; set; } = nameof(ExpeditionMapSelectAction);

    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        try
        {
            var json = ActionParamHelper.Parse(args.ActionParam);
            int team = (int?)json["team"] ?? 1;
            string teamLabel = TeamToLabel(team);

            var processor = MaaProcessor.ResolveByTasker(context.Tasker);
            int? mapIndex = processor?.GetLogisticsTeamMapIndex(teamLabel);

            if (processor == null)
            {
                // 定位不到执行实例时回退到旧的按文件读取方式，尽量保持可用。
                LoggerHelper.Warning("[ExpeditionMapSelect] 无法定位执行任务的实例，回退按当前激活实例读取后勤配置");
                var fallbackIndex = ReadMapIndexFromActiveInstanceConfig(team);
                mapIndex = fallbackIndex < 0 ? null : fallbackIndex;
            }

            if (mapIndex is null or <= 0)
            {
                // 该部队本次不派遣。读取不到配置同样按跳过处理，避免把休息的部队派出去；
                // 该 node 失败后由 on_error 回到本丸重新检查队伍，届时
                // ExpeditionTeamRestRecognition 会跳过该部队，流程继续检查下一支部队。
                LoggerHelper.Warning(mapIndex == null
                    ? $"[ExpeditionMapSelect] {teamLabel} 在后勤配置中没有目的地（未选择），跳过派遣"
                    : $"[ExpeditionMapSelect] {teamLabel} 设置为休息，跳过派遣");
                return false;
            }

            // index 映射: 1=1-1, 2=1-2, 3=1-3, 4=1-4, 5=2-1, ..., 20=5-4
            int index = mapIndex.Value;
            int era = (index - 1) / 4 + 1;
            int region = (index - 1) % 4 + 1;
            string mapLabel = $"{era}-{region}";

            // 点击时代标签
            int[] eraTarget = GetEraTarget(era);
            int eraX = eraTarget[0] + eraTarget[2] / 2;
            int eraY = eraTarget[1] + eraTarget[3] / 2;
            LoggerHelper.Info($"[ExpeditionMapSelect] {teamLabel} → {mapLabel}，点击时代{era} ({eraX}, {eraY})");
            context.Click(eraX, eraY);
            ActionParamHelper.SleepWithStopCheck(context, 300);

            // 点击小地图区域
            int[] regionTarget = GetRegionTarget(region);
            int regionX = regionTarget[0] + regionTarget[2] / 2;
            int regionY = regionTarget[1] + regionTarget[3] / 2;
            LoggerHelper.Info($"[ExpeditionMapSelect] {teamLabel} → {mapLabel}，点击地域{region} ({regionX}, {regionY})");
            context.Click(regionX, regionY);

            LoggerHelper.Info($"[ExpeditionMapSelect] {teamLabel} 地图选择完成: {mapLabel}");
            return true;
        }
        catch (MaaStopException)
        {
            LoggerHelper.Info("[ExpeditionMapSelect] 手动停止");
            return false;
        }
        catch (Exception e)
        {
            LoggerHelper.Error($"[ExpeditionMapSelect] 错误: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// 兜底路径：按当前激活实例读取「后勤」任务的部队地图序号，找不到时返回 -1。
    /// 仅在无法定位执行实例时使用，正常运行不会走到这里。
    /// </summary>
    private static int ReadMapIndexFromActiveInstanceConfig(int team)
    {
        string instancesDir = AppPaths.InstancesDirectory;
        if (!Directory.Exists(instancesDir))
        {
            LoggerHelper.Error($"[ExpeditionMapSelect] 实例目录不存在: {instancesDir}");
            return -1;
        }

        // 使用当前激活实例的 UUID 定位配置文件（config/instances/{uuid}.json），
        // 与 InstanceConfiguration.GetConfigFilePath() 保持一致。
        // 注意：不能使用 appsettings.json 的 Instances.LastActiveName（实例显示名），
        // 显示名与实例文件名无关；且回退 default.json 会读错其他实例的远征配置。
        string instanceId = MaaProcessorManager.Instance?.Current?.InstanceId ?? string.Empty;
        string configPath = string.IsNullOrWhiteSpace(instanceId)
            ? Path.Combine(instancesDir, "default.json")
            : Path.Combine(instancesDir, $"{instanceId}.json");
        if (!File.Exists(configPath))
        {
            configPath = Path.Combine(instancesDir, "default.json");
        }
        if (!File.Exists(configPath))
        {
            LoggerHelper.Error("[ExpeditionMapSelect] 找不到实例配置文件");
            return -1;
        }

        var config = JObject.Parse(File.ReadAllText(configPath));
        if (config["TaskItems"] is not JArray taskItems)
        {
            LoggerHelper.Error("[ExpeditionMapSelect] TaskItems 不存在");
            return -1;
        }

        // 从「后勤」任务配置中找到该部队的地图选择（按 entry 匹配，兼容任务改名）
        string teamName = TeamToConfigName(team);
        foreach (var item in taskItems)
        {
            if ((string?)item["entry"] != "Expedition")
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

    private static string TeamToLabel(int team) => team switch
    {
        1 => "部队一",
        2 => "部队二",
        3 => "部队三",
        4 => "部队四",
        5 => "部队五",
        _ => $"部队{team}"
    };

    /// <summary>「后勤」任务中部队选项的名称与展示名称一致。</summary>
    private static string TeamToConfigName(int team) => TeamToLabel(team);

    /// <summary>
    /// 获取时代标签的点击坐标，与 interface.json 中部队一的各时代 target 保持一致
    /// </summary>
    private static int[] GetEraTarget(int era) => era switch
    {
        1 => new[] { 276, 188, 37, 39 },
        2 => new[] { 469, 178, 50, 40 },
        3 => new[] { 661, 183, 50, 35 },
        4 => new[] { 891, 190, 50, 36 },
        5 => new[] { 1078, 191, 45, 34 },
        _ => new[] { 276, 188, 37, 39 }
    };

    /// <summary>
    /// 获取小地图区域的点击坐标，与 interface.json 中各地域的 target 保持一致
    /// </summary>
    private static int[] GetRegionTarget(int region) => region switch
    {
        1 => new[] { 173, 365, 97, 96 },
        2 => new[] { 487, 373, 84, 113 },
        3 => new[] { 767, 370, 81, 115 },
        4 => new[] { 1098, 368, 67, 79 },
        _ => new[] { 173, 365, 97, 96 }
    };
}
