using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaaFramework.Binding;
using MaaFramework.Binding.Buffers;
using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using MFAAvalonia.Models;
using MFAAvalonia.Services;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MFAAvalonia.ViewModels.Pages;

public partial class ForgeCalculatorViewModel : ViewModelBase
{
    [ObservableProperty] private int _recipeCharcoal = 700;
    [ObservableProperty] private int _recipeSteel = 700;
    [ObservableProperty] private int _recipeCoolant = 700;
    [ObservableProperty] private int _recipeWhetstone = 700;

    [ObservableProperty] private int _currentScore;

    [ObservableProperty] private int _currentCharcoal;
    [ObservableProperty] private int _currentSteel;
    [ObservableProperty] private int _currentCoolant;
    [ObservableProperty] private int _currentWhetstone;

    [ObservableProperty] private int _currentPlum;
    [ObservableProperty] private int _currentBamboo;
    [ObservableProperty] private int _currentPine;
    [ObservableProperty] private int _currentFuji;

    [ObservableProperty] private int _currentPermits;
    [ObservableProperty] private int _currentSpeedups;

    [ObservableProperty] private int _needCharcoal;
    [ObservableProperty] private int _needSteel;
    [ObservableProperty] private int _needCoolant;
    [ObservableProperty] private int _needWhetstone;
    [ObservableProperty] private int _needPermits;
    [ObservableProperty] private int _needSpeedups;

    [ObservableProperty] private bool _isRecognizing;

    [RelayCommand]
    private void Calculate()
    {
        var remainingScore = Math.Max(0, 5000 - CurrentScore);
        if (remainingScore == 0)
        {
            ClearResults();
            return;
        }

        var talismanQueue = new Queue<(int count, int score)>();
        talismanQueue.Enqueue((CurrentFuji, 60));
        talismanQueue.Enqueue((CurrentPine, 20));
        talismanQueue.Enqueue((CurrentBamboo, 15));
        talismanQueue.Enqueue((CurrentPlum, 10));

        int totalForgeCount = 0;
        int accumulatedScore = 0;

        while (talismanQueue.Count > 0 && accumulatedScore < remainingScore)
        {
            var (count, score) = talismanQueue.Dequeue();
            var used = Math.Min(count, (int)Math.Ceiling((double)(remainingScore - accumulatedScore) / score));
            totalForgeCount += used;
            accumulatedScore += used * score;
        }

        if (accumulatedScore < remainingScore)
        {
            var extra = (int)Math.Ceiling((double)(remainingScore - accumulatedScore) / 5);
            totalForgeCount += extra;
        }

        var totalCharcoal = totalForgeCount * RecipeCharcoal;
        var totalSteel = totalForgeCount * RecipeSteel;
        var totalCoolant = totalForgeCount * RecipeCoolant;
        var totalWhetstone = totalForgeCount * RecipeWhetstone;
        var totalSpeedups = totalForgeCount;
        var totalPermits = totalForgeCount - (totalForgeCount / 10);

        NeedCharcoal = Math.Max(0, totalCharcoal - CurrentCharcoal);
        NeedSteel = Math.Max(0, totalSteel - CurrentSteel);
        NeedCoolant = Math.Max(0, totalCoolant - CurrentCoolant);
        NeedWhetstone = Math.Max(0, totalWhetstone - CurrentWhetstone);
        NeedPermits = Math.Max(0, totalPermits - CurrentPermits);
        NeedSpeedups = Math.Max(0, totalSpeedups - CurrentSpeedups);
    }

    [RelayCommand]
    private void Reset()
    {
        RecipeCharcoal = 700;
        RecipeSteel = 700;
        RecipeCoolant = 700;
        RecipeWhetstone = 700;
        CurrentScore = 0;
        CurrentCharcoal = 0;
        CurrentSteel = 0;
        CurrentCoolant = 0;
        CurrentWhetstone = 0;
        CurrentPlum = 0;
        CurrentBamboo = 0;
        CurrentPine = 0;
        CurrentFuji = 0;
        CurrentPermits = 0;
        CurrentSpeedups = 0;
        ClearResults();
    }

    [RelayCommand]
    private void LoadWarehouseData()
    {
        try
        {
            var data = ConfigurationManager.Current.GetValue(ConfigurationKeys.WarehouseData, new WarehouseData());
            var values = ForgeCalculatorWarehouseDataMapper.Read(data);

            CurrentCharcoal = values.Charcoal;
            CurrentSteel = values.Steel;
            CurrentCoolant = values.Coolant;
            CurrentWhetstone = values.Whetstone;
            CurrentPlum = values.Plum;
            CurrentBamboo = values.Bamboo;
            CurrentPine = values.Pine;
            CurrentFuji = values.Fuji;
            CurrentPermits = values.Permits;
            CurrentSpeedups = values.Speedups;

            ToastHelper.Success("限锻计算", "已读取仓库数据并填入现有资源、御札和道具。");
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"[ForgeCalculator] 读取仓库数据异常：{ex}", ex);
            ShowError($"读取仓库数据失败：{ex.Message}");
        }
    }

    [RelayCommand]
    private async Task RecognizeScreenAsync()
    {
        if (IsRecognizing) return;
        IsRecognizing = true;

        try
        {
            var processor = MaaProcessor.Processors.FirstOrDefault(p => p.MaaTasker?.Controller?.IsConnected == true);
            if (processor == null)
            {
                ShowError("未检测到已连接的模拟器，请先在主页连接设备。");
                return;
            }

            // 截图与 OCR 全程在后台线程执行，界面不会被识别耗时阻塞
            var result = await Task.Run(() => RecognizeScreenCore(processor));
            if (!result.Success || result.Values == null)
            {
                ShowError(result.Message);
                return;
            }

            ApplyRecognizedValues(result.Values);
            ToastHelper.Success("限锻计算", "已根据屏幕识别结果填入现有资源、御札和道具。");
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"[ForgeCalculator] 识别屏幕异常：{ex}", ex);
            ShowError($"识别失败：{ex.Message}");
        }
        finally
        {
            IsRecognizing = false;
        }
    }

    /// <summary>
    /// 屏幕识别的取样区域，基于 1280×720 的锻刀资源投入页面。
    /// </summary>
    private static readonly (string Key, string Label, int[] Roi)[] ScreenRois =
    [
        ("charcoal", "木炭", [354, 7, 138, 38]),
        ("steel", "玉钢", [490, 7, 138, 38]),
        ("coolant", "冷却材", [628, 7, 138, 38]),
        ("whetstone", "砥石", [770, 7, 138, 38]),
        ("plum", "梅", [1042, 232, 99, 69]),
        ("bamboo", "竹", [1042, 309, 99, 69]),
        ("pine", "松", [1042, 386, 99, 69]),
        ("fuji", "富士", [1042, 462, 99, 69]),
        ("permits", "依頼札", [1167, 283, 97, 26]),
        ("speedups", "手伝い札", [1167, 448, 92, 32]),
    ];

    /// <summary>单次识别的等待上限，避免异常时无限期占用识别线程。</summary>
    private static readonly TimeSpan RecognitionTimeout = TimeSpan.FromSeconds(5);

    /// <summary>回退到主 tasker 且主任务正在运行时的等待上限，此时识别会被流水线阻塞。</summary>
    private static readonly TimeSpan BusyRecognitionTimeout = TimeSpan.FromSeconds(3);

    /// <summary>截图等待上限。</summary>
    private static readonly TimeSpan ScreencapTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 执行一次屏幕识别。
    /// 主 tasker 空闲时直接使用它；正在跑流水线时改用独立识别执行器，避免识别排队等待；
    /// 独立执行器不可用时回退到主 tasker，并用更短的超时给出明确提示。
    /// </summary>
    private static (bool Success, string Message, Dictionary<string, int>? Values) RecognizeScreenCore(MaaProcessor processor)
    {
        var mainTasker = processor.MaaTasker;
        if (mainTasker?.Controller == null)
            return (false, "未检测到已连接的模拟器，请先在主页连接设备。", null);

        // 主 tasker 空闲时不必额外开一条设备连接，只有流水线运行期间才需要独立执行器
        if (mainTasker.IsRunning || mainTasker.IsStopping)
        {
            var auxTasker = processor.AcquireAuxRecognitionTasker();
            if (auxTasker != null)
            {
                var auxResult = RecognizeWithTasker(auxTasker, isAuxTasker: true);
                if (auxResult.Success)
                    return auxResult;

                LoggerHelper.Warning($"[ForgeCalculator] 独立识别执行器识别失败，回退到主任务：{auxResult.Message}");
                processor.DisposeAuxRecognitionTasker();
            }
        }

        return RecognizeWithTasker(mainTasker, isAuxTasker: false);
    }

    /// <summary>
    /// 用指定执行器完成截图与 OCR。
    /// 主 tasker 正在运行流水线时识别会一直排队到该轮结束，此时使用更短的等待上限并给出提示。
    /// </summary>
    private static (bool Success, string Message, Dictionary<string, int>? Values) RecognizeWithTasker(MaaTasker tasker, bool isAuxTasker)
    {
        var controller = tasker.Controller;
        if (controller == null)
            return (false, "未检测到已连接的模拟器，请先在主页连接设备。", null);

        var taskerBusy = !isAuxTasker && (tasker.IsRunning || tasker.IsStopping);
        var timeout = taskerBusy ? BusyRecognitionTimeout : RecognitionTimeout;

        LoggerHelper.Info($"[ForgeCalculator] 识别屏幕开始（执行器：{(isAuxTasker ? "独立" : "主")}）");

        var capStatus = WaitJob(controller.Screencap(), ScreencapTimeout);
        LoggerHelper.Info($"[ForgeCalculator] 截图结果：{capStatus}");
        if (capStatus != MaaJobStatus.Succeeded)
            return (false, "截图失败，请检查模拟器连接。", null);

        // 获取缓存的截图作为 OCR 图像源
        using var imageBuffer = new MaaImageBuffer();
        if (!controller.GetCachedImage(imageBuffer))
            return (false, "获取截图数据失败。", null);

        var values = new Dictionary<string, int>();
        foreach (var (key, label, roi) in ScreenRois)
        {
            var recoParam = JsonConvert.SerializeObject(new { roi });
            var job = tasker.AppendRecognition("OCR", recoParam, imageBuffer);
            var status = WaitJob(job, timeout);
            if (status != MaaJobStatus.Succeeded)
            {
                LoggerHelper.Warning($"[ForgeCalculator] OCR {key} 未完成：{status}");
                return (false, taskerBusy
                    ? "识别被正在运行的任务阻塞，请先停止任务后再识别屏幕。"
                    : $"识别超时（{label}），请确认游戏位于锻刀资源投入页面后重试。", null);
            }

            var detailObj = job.QueryRecognitionDetail();
            if (detailObj == null || string.IsNullOrWhiteSpace(detailObj.Detail))
            {
                LoggerHelper.Info($"[ForgeCalculator] OCR {key} 无结果");
                values[key] = 0;
                continue;
            }

            var query = JsonConvert.DeserializeObject<MaaExtensions.RecognitionQuery>(detailObj.Detail);
            var text = query?.Best?.Text ?? "";
            LoggerHelper.Info($"[ForgeCalculator] OCR {key} 识别到：[{text}]");
            var normalized = text.Replace(",", "").Replace("，", "").Replace(".", "").Trim();
            values[key] = int.TryParse(normalized, out var parsed) ? parsed : 0;
        }

        LoggerHelper.Info("[ForgeCalculator] 识别完成");
        return (true, string.Empty, values);
    }

    /// <summary>
    /// 带超时地等待任务结束，替代无超时的 WaitFor，避免识别线程被无限期阻塞。
    /// </summary>
    private static MaaJobStatus WaitJob(MaaJob job, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var status = job.Status;
        while ((status.IsPending() || status.IsRunning()) && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(30);
            status = job.Status;
        }

        return status;
    }

    private void ApplyRecognizedValues(Dictionary<string, int> values)
    {
        CurrentCharcoal = values.GetValueOrDefault("charcoal");
        CurrentSteel = values.GetValueOrDefault("steel");
        CurrentCoolant = values.GetValueOrDefault("coolant");
        CurrentWhetstone = values.GetValueOrDefault("whetstone");
        CurrentPlum = values.GetValueOrDefault("plum");
        CurrentBamboo = values.GetValueOrDefault("bamboo");
        CurrentPine = values.GetValueOrDefault("pine");
        CurrentFuji = values.GetValueOrDefault("fuji");
        CurrentPermits = values.GetValueOrDefault("permits");
        CurrentSpeedups = values.GetValueOrDefault("speedups");
    }

    private void ClearResults()
    {
        NeedCharcoal = 0;
        NeedSteel = 0;
        NeedCoolant = 0;
        NeedWhetstone = 0;
        NeedPermits = 0;
        NeedSpeedups = 0;
    }

    private static void ShowError(string message)
    {
        ToastHelper.Error("限锻计算", message);
    }
}
