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
    public void RecognizeScreen()
    {
        LoggerHelper.Info("[ForgeCalculator] 识别屏幕开始");
        if (IsRecognizing) return;
        IsRecognizing = true;

        try
        {
            var processor = MaaProcessor.Processors.FirstOrDefault(p => p.MaaTasker?.Controller?.IsConnected == true);
            if (processor == null)
            {
                ShowError("未检测到已连接的模拟器，请先在主页连接设备。");
                IsRecognizing = false;
                return;
            }

            var tasker = processor.MaaTasker!;

            var controller = tasker.Controller!;
            var capStatus = controller.Screencap().Wait();
            LoggerHelper.Info($"[ForgeCalculator] 截图结果: {capStatus}");
            if (capStatus != MaaJobStatus.Succeeded)
            {
                ShowError("截图失败，请检查模拟器连接。");
                IsRecognizing = false;
                return;
            }

            var rois = new Dictionary<string, int[]>
            {
                ["charcoal"]  = new[] { 354, 7, 138, 38 },
                ["steel"]     = new[] { 490, 7, 138, 38 },
                ["coolant"]   = new[] { 628, 7, 138, 38 },
                ["whetstone"] = new[] { 770, 7, 138, 38 },
                ["plum"]      = new[] { 1042, 232, 99, 69 },
                ["bamboo"]    = new[] { 1042, 309, 99, 69 },
                ["pine"]      = new[] { 1042, 386, 99, 69 },
                ["fuji"]      = new[] { 1042, 462, 99, 69 },
                ["permits"]   = new[] { 1167, 283, 97, 26 },
                ["speedups"]  = new[] { 1167, 448, 92, 32 },
            };

            // 获取缓存的截图作为 OCR 图像源
            var imageBuffer = new MaaImageBuffer();
            if (!controller.GetCachedImage(imageBuffer))
            {
                LoggerHelper.Info("[ForgeCalculator] 获取缓存图像失败");
                ShowError("获取截图数据失败。");
                IsRecognizing = false;
                return;
            }

            int ParseOcr(string key)
            {
                var roi = rois[key];
                var recoParam = JsonConvert.SerializeObject(new { roi = roi });
                var job = tasker.AppendRecognition("OCR", recoParam, imageBuffer);
                LoggerHelper.Info($"[ForgeCalculator] OCR {key} 开始...");
                if (job.WaitFor(MaaJobStatus.Succeeded) == null)
                {
                    LoggerHelper.Info($"[ForgeCalculator] OCR {key} 失败");
                    return 0;
                }
                var detailObj = job.QueryRecognitionDetail();
                if (detailObj == null || string.IsNullOrWhiteSpace(detailObj.Detail))
                {
                    LoggerHelper.Info($"[ForgeCalculator] OCR {key} 无结果");
                    return 0;
                }
                var query = JsonConvert.DeserializeObject<MaaExtensions.RecognitionQuery>(detailObj.Detail);
                var text = query?.Best?.Text ?? "";
                LoggerHelper.Info($"[ForgeCalculator] OCR {key} 识别到: [{text}]");
                text = text.Replace(",", "").Replace("，", "").Replace(".", "").Trim();
                return int.TryParse(text, out var val) ? val : 0;
            }

            CurrentCharcoal  = ParseOcr("charcoal");
            CurrentSteel     = ParseOcr("steel");
            CurrentCoolant   = ParseOcr("coolant");
            CurrentWhetstone = ParseOcr("whetstone");
            CurrentPlum      = ParseOcr("plum");
            CurrentBamboo    = ParseOcr("bamboo");
            CurrentPine      = ParseOcr("pine");
            CurrentFuji      = ParseOcr("fuji");
            CurrentPermits   = ParseOcr("permits");
            CurrentSpeedups  = ParseOcr("speedups");
            LoggerHelper.Info("[ForgeCalculator] 识别完成");
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"[ForgeCalculator] OCR 异常：{ex}", ex);
            ShowError($"识别失败：{ex.Message}");
        }
        finally
        {
            IsRecognizing = false;
        }
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
