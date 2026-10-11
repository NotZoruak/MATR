using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Helper;
using Newtonsoft.Json.Linq;
using System;
using System.Text.RegularExpressions;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

public class DungeonFloorSelectAction : IMaaCustomAction
{
    public string Name { get; set; } = nameof(DungeonFloorSelectAction);

    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        try
        {
            var json = ActionParamHelper.Parse(args.ActionParam);
            int targetFloor = (int?)json["target_floor"] ?? 51;
            targetFloor = Math.Clamp(targetFloor, 1, 99);

            int targetTens = targetFloor / 10;
            int targetOnes = targetFloor % 10;

            var floorRoi = ParseRoi(json, "floor_roi");
            var tensUp = ParseRoi(json, "tens_up");
            var tensDown = ParseRoi(json, "tens_down");
            var onesUp = ParseRoi(json, "ones_up");
            var onesDown = ParseRoi(json, "ones_down");
            LoggerHelper.Info($"[DungeonFloorSelect] 目标层数: {targetFloor} (十位={targetTens}, 个位={targetOnes})");

            int currentFloor = ReadFloorStable(context, floorRoi, "当前层数");
            int currentTens = currentFloor / 10;
            int currentOnes = currentFloor % 10;
            LoggerHelper.Info($"[DungeonFloorSelect] 当前层数: {currentFloor}");

            // 调整十位（不循环，只能单向）
            if (currentTens != targetTens)
            {
                int realClicks = currentTens > targetTens ? currentTens - targetTens : targetTens - currentTens;
                bool goDown = currentTens > targetTens;
                int btnX = goDown ? tensDown[0] + tensDown[2] / 2 : tensUp[0] + tensUp[2] / 2;
                int btnY = goDown ? tensDown[1] + tensDown[3] / 2 : tensUp[1] + tensUp[3] / 2;
                LoggerHelper.Info($"[DungeonFloorSelect] 十位 {currentTens}→{targetTens}，{(goDown ? "减" : "加")}{realClicks}次");
                for (int i = 0; i < realClicks; i++)
                {
                    context.ClickWithProgress(btnX, btnY);
                    ActionParamHelper.SleepWithStopCheck(context, 500);
                }
                ActionParamHelper.SleepWithStopCheck(context, 300);
            }

            // 调整个位（不循环，只能单向）
            if (currentOnes != targetOnes)
            {
                int realClicks = currentOnes > targetOnes ? currentOnes - targetOnes : targetOnes - currentOnes;
                bool goDown = currentOnes > targetOnes;
                int btnX = goDown ? onesDown[0] + onesDown[2] / 2 : onesUp[0] + onesUp[2] / 2;
                int btnY = goDown ? onesDown[1] + onesDown[3] / 2 : onesUp[1] + onesUp[3] / 2;
                LoggerHelper.Info($"[DungeonFloorSelect] 个位 {currentOnes}→{targetOnes}，{(goDown ? "减" : "加")}{realClicks}次");
                for (int i = 0; i < realClicks; i++)
                {
                    context.ClickWithProgress(btnX, btnY);
                    ActionParamHelper.SleepWithStopCheck(context, 500);
                }
                ActionParamHelper.SleepWithStopCheck(context, 300);
            }

            // 验证
            int finalFloor = ReadFloorStable(context, floorRoi, "验证层数");
            LoggerHelper.Info($"[DungeonFloorSelect] 验证结果: {finalFloor}");

            if (finalFloor == targetFloor)
            {
                LoggerHelper.Info("[DungeonFloorSelect] 层数选择完成");
                return true;
            }

            // 验证不通过，用逐次 OCR 方式微调
            LoggerHelper.Info("[DungeonFloorSelect] 盲点未到位，进入逐次微调模式");
            AdjustDigitLoop(context, floorRoi, targetFloor, tensUp, tensDown, onesUp, onesDown);
            LoggerHelper.Info("[DungeonFloorSelect] 层数选择完成");
            return true;
        }
        catch (MaaStopException)
        {
            LoggerHelper.Info("[DungeonFloorSelect] 手动停止");
            return false;
        }
        catch (Exception e)
        {
            LoggerHelper.Error($"[DungeonFloorSelect] Error: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// 稳定读取完整层数，内部处理重试。
    /// </summary>
    private int ReadFloorStable(IMaaContext context, int[] roi, string label)
    {
        const int requiredConsecutiveReads = 3;
        const int maxAttempts = 15;
        int? previousFloor = null;
        int consecutiveReads = 0;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            ActionParamHelper.ThrowIfStopping(context);

            using var image = context.GetImage();
            if (image == null)
            {
                ActionParamHelper.SleepWithStopCheck(context, 400);
                continue;
            }

            var text = context.GetText(roi[0], roi[1], roi[2], roi[3], image);
            int? floor = ParseFloor(text);
            if (floor.HasValue)
            {
                if (previousFloor == floor)
                    consecutiveReads++;
                else
                {
                    previousFloor = floor;
                    consecutiveReads = 1;
                }

                LoggerHelper.Info($"[DungeonFloorSelect] {label} OCR 识别: {floor}，连续一致 {consecutiveReads}/{requiredConsecutiveReads}");
                if (consecutiveReads >= requiredConsecutiveReads)
                    return floor.Value;
            }
            else
            {
                previousFloor = null;
                consecutiveReads = 0;
                LoggerHelper.Info($"[DungeonFloorSelect] {label} OCR 结果无效: '{text}'，连续计数已清零");
            }

            if (attempt < maxAttempts - 1)
                ActionParamHelper.SleepWithStopCheck(context, 200);
        }

        throw new Exception($"{label} OCR 识别失败（未能连续{requiredConsecutiveReads}次得到一致结果，已尝试{maxAttempts}次）");
    }

    /// <summary>
    /// 逐次 OCR 微调模式（盲点未到位时的后备方案）
    /// </summary>
    private void AdjustDigitLoop(IMaaContext context, int[] roi, int target, int[] tensUp, int[] tensDown, int[] onesUp, int[] onesDown)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            ActionParamHelper.ThrowIfStopping(context);

            int current = ReadFloorStable(context, roi, "微调层数");
            if (current == target)
            {
                LoggerHelper.Info($"[DungeonFloorSelect] 层数已匹配: {current} == {target}");
                return;
            }

            int currentTens = current / 10;
            int currentOnes = current % 10;
            int targetTens = target / 10;
            int targetOnes = target % 10;
            bool tensChanged = currentTens != targetTens;
            bool goDown = tensChanged ? currentTens > targetTens : currentOnes > targetOnes;
            int[] button = tensChanged
                ? (goDown ? tensDown : tensUp)
                : (goDown ? onesDown : onesUp);
            int clickX = button[0] + button[2] / 2;
            int clickY = button[1] + button[3] / 2;
            LoggerHelper.Info($"[DungeonFloorSelect] 层数 {current}→{target}，点击 {(goDown ? "减" : "加")} ({clickX}, {clickY})");

                context.ClickWithProgress(clickX, clickY);
            ActionParamHelper.SleepWithStopCheck(context, 500);
        }

        throw new Exception("层数微调失败，已重试10次");
    }

    private static int[] ParseRoi(JObject json, string key)
    {
        var arr = json[key] as JArray;
        if (arr == null || arr.Count != 4)
            throw new Exception($"参数 {key} 格式错误，需要 [x, y, w, h]");
        return new[] { (int)arr[0], (int)arr[1], (int)arr[2], (int)arr[3] };
    }

    private static int? ParseFloor(string? text)
    {
        text = Regex.Replace(text?.Trim().Replace('O', '0').Replace('o', '0') ?? string.Empty, @"\s+", string.Empty);
        var match = Regex.Match(text ?? string.Empty, "[0-9]{1,2}");
        if (match.Success && int.TryParse(match.Value, out int floor) && floor is >= 1 and <= 99)
            return floor;
        return null;
    }
}
