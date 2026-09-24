using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Configuration;
using MFAAvalonia.Helper;
using MFAAvalonia.Models;
using MFAAvalonia.ViewModels.UsersControls;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>自定编队任务入口：读取任务参数中的预设编号，校验并固化本次执行上下文</summary>
public class FormationConfigAction : IMaaCustomAction
{
    public string Name { get; set; } = nameof(FormationConfigAction);

    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        try
        {
            var json = ActionParamHelper.Parse(args.ActionParam);
            int presetId = json["preset_id"]?.Value<int>() ?? 0;
            if (presetId <= 0)
            {
                LoggerHelper.Error("[FormationConfig] 未选择编队预设（preset_id=0），任务失败");
                return false;
            }

            var presets = ConfigurationManager.CurrentInstance.GetValue<List<FormationPreset>>(ConfigurationKeys.FormationPresets, []);
            var preset = presets?.FirstOrDefault(p => p.Id == presetId);
            if (preset == null)
            {
                LoggerHelper.Error($"[FormationConfig] 预设不存在: id={presetId}，任务失败");
                return false;
            }

            preset.EnsureSlots();

            // 普通编成模式要求填写 1 号位；部队记录模式只操作已有部队记录，不读取预设成员。
            if (!preset.UseGameFormationRecordOnly && !preset.SaveGameFormationRecordOnly
                && string.IsNullOrWhiteSpace(preset.Slots[0].Sword))
            {
                LoggerHelper.Error("[FormationConfig] 预设 1 号位未配置刀剑，任务失败");
                return false;
            }

            // 固化本次执行上下文
            FormationContext.Reset();
            FormationContext.Team = preset.Team;
            FormationContext.ClearEquipment = preset.ClearEquipmentBeforeFormation;
            FormationContext.SaveRecord = preset.SaveGameFormationRecordAfterFormation;
            for (int i = 0; i < 6; i++)
            {
                FormationContext.Swords[i] = preset.Slots[i].Sword?.Trim() ?? "";
                FormationContext.Equips[i] = (preset.Slots[i].Equip ?? "").Replace(" ", "");
                FormationContext.Horses[i] = string.IsNullOrWhiteSpace(preset.Slots[i].Horse) ? "无" : preset.Slots[i].Horse.Trim();
                FormationContext.Treasures[i] = FormationOptions.ToOcrTreasureName(preset.Slots[i].Treasure?.Trim());
            }
            FormationContext.MemberSlots = Enumerable.Range(1, 6)
                .Where(i => !string.IsNullOrEmpty(FormationContext.Swords[i - 1]))
                .ToList();

            LoggerHelper.Info(
                $"[FormationConfig] 预设「{preset.Name}」Team={FormationContext.Team} " +
                $"成员位={string.Join(",", FormationContext.MemberSlots)} " +
                $"卸装备={FormationContext.ClearEquipment} 保存记录={FormationContext.SaveRecord}");
            return true;
        }
        catch (MaaStopException)
        {
            LoggerHelper.Info("[FormationConfig] 手动停止");
            return false;
        }
        catch (Exception e)
        {
            LoggerHelper.Error($"[FormationConfig] Error: {e.Message}");
            return false;
        }
    }
}
