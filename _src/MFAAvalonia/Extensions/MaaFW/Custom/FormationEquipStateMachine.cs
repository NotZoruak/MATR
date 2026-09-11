using MaaFramework.Binding;
using MaaFramework.Binding.Buffers;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Helper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>装备流程状态机（全包方案）：判断当前位 → 导航 → 刀装/马匹配置 → 循环，出口后返回</summary>
public class FormationEquipStateMachine : IMaaCustomAction
{
    public string Name { get; set; } = nameof(FormationEquipStateMachine);

    /// <summary>前一位编号 OCR 区域</summary>
    private static readonly int[] FrontRoi = [433, 10, 34, 31];

    /// <summary>后一位编号 OCR 区域</summary>
    private static readonly int[] BackRoi = [875, 10, 34, 30];

    /// <summary>下一个刀剑槽位导航按钮</summary>
    private static readonly int[] NextButton = [825, 15, 77, 22];

    /// <summary>刀装槽位点击位置（槽 1-3）</summary>
    private static readonly int[][] EquipSlotCoords =
    [
        [356, 145, 111, 44],
        [361, 245, 111, 44],
        [364, 346, 111, 44],
    ];

    /// <summary>刀装区空槽位检测区域</summary>
    private static readonly int[] EquipAreaRoi = [322, 136, 57, 256];

    /// <summary>马匹区空槽位检测区域</summary>
    private static readonly int[] HorseAreaRoi = [607, 137, 55, 55];

    /// <summary>刀装列表确认 OCR 区域</summary>
    private static readonly int[] EquipListConfirmRoi = [844, 88, 83, 39];

    /// <summary>马匹槽位点击位置</summary>
    private static readonly int[] HorseSlotCoords = [648, 154, 57, 32];

    /// <summary>马匹列表确认 OCR 区域</summary>
    private static readonly int[] HorseListConfirmRoi = [855, 96, 36, 27];

    /// <summary>当前位识别的最大尝试次数（进入装备页与翻页瞬间可能读不到前后位编号）</summary>
    private const int SlotDetectAttempts = 3;

    /// <summary>装备页入口固定打开的部队位置（FC_OpenEquip 点击 1 号位装备按钮）</summary>
    private const int EntrySlot = 1;

    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        try
        {
            if (FormationContext.MemberSlots.Count == 0)
            {
                LoggerHelper.Error("[FormationEquipStateMachine] 无成员槽位，装备流程无法执行");
                return false;
            }

            int expectIndex = 0; // MemberSlots 下标
            bool navigated = false; // 是否已点过翻页按钮（入口页面固定为 1 号位）

            while (true)
            {
                ActionParamHelper.ThrowIfStopping(context);

                int cur = DetectCurrentSlotWithRetry(context);
                if (cur < 0)
                {
                    // 队伍只有一名成员时页面前后位栏为空，读不到编号；入口固定是 1 号位的装备按钮，
                    // 因此首次进入且首位成员就在 1 号位时按该位继续配置。
                    if (!navigated && expectIndex == 0 && FormationContext.MemberSlots[0] == EntrySlot)
                    {
                        LoggerHelper.Warning(
                            $"[FormationEquipStateMachine] 连续 {SlotDetectAttempts} 次读不到前后位编号，按入口 {EntrySlot} 号位继续配置");
                        cur = EntrySlot;
                    }
                    else
                    {
                        // 其余情况无法确认当前位：跳过剩余装备配置，由 FC_BackFromEquip 统一返回编成页，
                        // 避免落到 on_error 后反复识别同一画面而卡死。
                        LoggerHelper.Warning(
                            $"[FormationEquipStateMachine] 连续 {SlotDetectAttempts} 次无法判断当前刀剑槽位，跳过剩余装备配置（已配置 {expectIndex} 个成员）");
                        return true;
                    }
                }

                if (cur != FormationContext.MemberSlots[expectIndex])
                {
                    // 未到位：导航到下一个槽位继续，并等待画面切换完成
                    ClickRect(context, NextButton);
                    navigated = true;
                    WaitNavAway(context, cur);
                    continue;
                }

                // 到位：配置该位装备，并确认刀装/马匹已装上（空则重装，最多 3 轮；因刀本身无此槽位而未装上的不重装）
                if (!ConfigureCurrent(context, cur, out bool slotMissing))
                    return false;
                for (int retry = 0; !slotMissing && retry < 3 && IsEquipEmpty(context, cur); retry++)
                {
                    LoggerHelper.Warning($"[FormationEquipStateMachine] 槽位{cur} 装备确认失败（槽位为空），重新装备（第 {retry + 1} 轮）");
                    if (!ConfigureCurrent(context, cur, out slotMissing))
                        return false;
                }

                expectIndex++;
                if (expectIndex >= FormationContext.MemberSlots.Count)
                {
                    // 全部成员配置完成，由 FC_BackFromEquip 统一处理返回
                    LoggerHelper.Info("[FormationEquipStateMachine] 全部成员装备配置完成");
                    return true;
                }

                // 点导航前检查出口：后位为「一」表示当前已是最后一位，由 FC_BackFromEquip 统一处理返回
                if (IsBackOne(context))
                {
                    LoggerHelper.Info("[FormationEquipStateMachine] 后位为「一」，装备流程结束");
                    return true;
                }

                // 导航到下一个刀剑槽位，并等待画面切换完成
                ClickRect(context, NextButton);
                navigated = true;
                WaitNavAway(context, cur);
            }
        }
        catch (MaaStopException)
        {
            LoggerHelper.Info("[FormationEquipStateMachine] 手动停止");
            return false;
        }
        catch (Exception e)
        {
            LoggerHelper.Error($"[FormationEquipStateMachine] Error: {e.Message}");
            return false;
        }
    }

    /// <summary>推算当前页为第几位：后位编号在成员列表中的前一个（环回）</summary>
    private int DetectCurrentSlot<T>(T context) where T : IMaaContext
    {
        using var image = context.GetImage();
        if (image == null)
            return -1;

        int front = FormationContext.ChineseNumToInt(context.GetText(FrontRoi[0], FrontRoi[1], FrontRoi[2], FrontRoi[3], image));
        int back = FormationContext.ChineseNumToInt(context.GetText(BackRoi[0], BackRoi[1], BackRoi[2], BackRoi[3], image));
        LoggerHelper.Info($"[FormationEquipStateMachine] 前位={front} 后位={back}");

        int idx = FormationContext.MemberSlots.IndexOf(back);
        if (idx < 0)
            return -1;
        return FormationContext.MemberSlots[(idx - 1 + FormationContext.MemberSlots.Count) % FormationContext.MemberSlots.Count];
    }

    /// <summary>带重试的当前位识别；连续读不到前后位编号时返回 -1，由调用方跳过装备配置</summary>
    private int DetectCurrentSlotWithRetry<T>(T context) where T : IMaaContext
    {
        for (int attempt = 0; attempt < SlotDetectAttempts; attempt++)
        {
            if (attempt > 0)
                ActionParamHelper.SleepWithStopCheck(context, 400);

            int cur = DetectCurrentSlot(context);
            if (cur >= 0)
                return cur;
        }
        return -1;
    }

    /// <summary>等待导航后的画面切换：OCR 前后位推算的当前位与导航前不同即视为切换完成（最多 3 秒，超时继续）</summary>
    private void WaitNavAway<T>(T context, int prevSlot) where T : IMaaContext
    {
        for (int i = 0; i < 10; i++)
        {
            ActionParamHelper.ThrowIfStopping(context);
            int cur = DetectCurrentSlot(context);
            if (cur >= 0 && cur != prevSlot)
            {
                LoggerHelper.Info($"[FormationEquipStateMachine] 导航完成：槽位 {prevSlot} → {cur}");
                return;
            }
            ActionParamHelper.SleepWithStopCheck(context, 300);
        }
        LoggerHelper.Warning("[FormationEquipStateMachine] 等待导航切换超时，继续流程");
    }

    /// <summary>OCR 后位编号是否为「一」</summary>
    private bool IsBackOne<T>(T context) where T : IMaaContext
    {
        using var image = context.GetImage();
        if (image == null)
            return false;
        string text = context.GetText(BackRoi[0], BackRoi[1], BackRoi[2], BackRoi[3], image);
        return text?.Trim() == "一";
    }

    /// <summary>检查当前位装备是否为空：刀装区 [322,136,57,256] 匹配 装备为空.png；马匹非「无」时马匹区 [607,137,55,55] 也匹配</summary>
    private bool IsEquipEmpty<T>(T context, int pos) where T : IMaaContext
    {
        using var image = context.GetImage();
        if (image == null)
            return true;

        if (MatchEquipEmpty(context, image, EquipAreaRoi))
            return true;
        if (FormationContext.Horses[pos - 1] != "无" && MatchEquipEmpty(context, image, HorseAreaRoi))
            return true;
        return false;
    }

    /// <summary>指定区域匹配 装备为空.png 模板</summary>
    private bool MatchEquipEmpty<T>(T context, IMaaImageBuffer image, int[] roi) where T : IMaaContext
    {
        var taskModel = new MaaNode
        {
            Name = "FormationEquipEmptyCheck",
            Recognition = "TemplateMatch",
            Template = ["Common/装备为空.png"],
            Roi = new List<int>(roi),
            Threshold = new List<double> { 0.9 },
            GreenMask = true,
        };
        var detail = context.RunRecognition(taskModel, image);
        return detail?.IsHit() == true;
    }

    /// <summary>配置当前位的刀装（0-3 槽）与马匹（非「无」）；slotMissing=true 表示存在无法完成的项（槽位不存在或目标不存在），调用方据此跳过重装</summary>
    private bool ConfigureCurrent<T>(T context, int pos, out bool slotMissing) where T : IMaaContext
    {
        slotMissing = false;
        FormationContext.CurrentSlot = pos;
        LoggerHelper.Info($"[FormationEquipStateMachine] 配置槽位{pos} 装备");

        // 刀装：按切分数量配置 0-3 个槽位
        var equipList = FormationContext.SplitEquip(FormationContext.Equips[pos - 1]);
        for (int slot = 0; slot < equipList.Count && slot < 3; slot++)
        {
            if (!ClickEquipSlot(context, slot))
            {
                // 该刀无此刀装槽（如短刀 1 槽、胁差 2 槽）：停止配置后续槽位，继续马匹流程
                slotMissing = true;
                LoggerHelper.Warning($"[FormationEquipStateMachine] 槽位{pos} 刀装槽{slot + 1} 无法打开刀装列表，视为无此槽位，停止后续刀装配置");
                break;
            }
            if (!FormationEquipSelectAction.SelectEquip(context, pos, slot + 1))
            {
                // 目标刀装不存在（列表已到底）：停止配置后续槽位，继续马匹流程
                slotMissing = true;
                LoggerHelper.Error($"[FormationEquipStateMachine] 槽位{pos} 刀装槽{slot + 1} 未找到目标刀装，停止后续刀装配置");
                break;
            }
        }

        // 马匹：非「无」时配置
        if (!string.IsNullOrEmpty(FormationContext.Horses[pos - 1]) && FormationContext.Horses[pos - 1] != "无")
        {
            if (!ClickHorseSlot(context))
            {
                // 马匹列表打不开：跳过马匹配置，继续下一成员，避免流程卡死
                slotMissing = true;
                LoggerHelper.Error($"[FormationEquipStateMachine] 槽位{pos} 马匹列表未确认，跳过马匹配置");
            }
            else if (!FormationHorseSelectAction.SelectHorse(context, pos))
            {
                // 目标马匹不存在（列表已到底）：跳过马匹配置，继续下一成员，避免流程卡死
                slotMissing = true;
                LoggerHelper.Error($"[FormationEquipStateMachine] 槽位{pos} 未找到目标马匹「{FormationContext.Horses[pos - 1]}」，跳过马匹配置");
            }
        }

        return true;
    }

    /// <summary>点击指定刀装槽位并确认刀装列表打开（未确认则重试 3 次，避免对不存在的槽位反复点击）</summary>
    private bool ClickEquipSlot<T>(T context, int index) where T : IMaaContext
    {
        var coords = EquipSlotCoords[index];
        for (int attempt = 0; attempt < 3; attempt++)
        {
            ActionParamHelper.ThrowIfStopping(context);
            ClickRect(context, coords);
            ActionParamHelper.SleepWithStopCheck(context, 500);

            if (IsTextAt(context, EquipListConfirmRoi, "刀装"))
                return true;
        }
        LoggerHelper.Warning($"[FormationEquipStateMachine] 刀装列表未确认（槽 {index + 1}），视为无此刀装槽");
        return false;
    }

    /// <summary>点击马匹槽位并确认马匹列表打开（未确认则重试）</summary>
    private bool ClickHorseSlot<T>(T context) where T : IMaaContext
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            ActionParamHelper.ThrowIfStopping(context);
            ClickRect(context, HorseSlotCoords);
            ActionParamHelper.SleepWithStopCheck(context, 500);

            if (IsTextAt(context, HorseListConfirmRoi, "马"))
                return true;
        }
        LoggerHelper.Error("[FormationEquipStateMachine] 马匹列表未确认");
        return false;
    }

    /// <summary>指定区域 OCR 是否包含目标文本</summary>
    private bool IsTextAt<T>(T context, int[] roi, string expected) where T : IMaaContext
    {
        using var image = context.GetImage();
        if (image == null)
            return false;
        string text = context.GetText(roi[0], roi[1], roi[2], roi[3], image);
        return text?.Contains(expected, StringComparison.Ordinal) == true;
    }

    /// <summary>计算区域中心并点击</summary>
    private static void ClickRect<T>(T context, int[] coords) where T : IMaaContext
    {
        int cx = coords[0] + coords[2] / 2;
        int cy = coords[1] + coords[3] / 2;
        context.Click(cx, cy);
    }
}
