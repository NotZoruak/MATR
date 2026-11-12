using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaaFramework.Binding;
using MaaFramework.Binding.Buffers;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using MFAAvalonia.Models;
using MFAAvalonia.Services;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MFAAvalonia.ViewModels.Pages;

public partial class SilhouetteViewModel : ViewModelBase
{
    private readonly SilhouetteService _service;

    [ObservableProperty] private bool _isRecognizing;
    [ObservableProperty] private string _statusMessage = "点击「识别当前画面」开始";
    [ObservableProperty] private ObservableCollection<RecognitionResult> _results = new();
    [ObservableProperty] private Bitmap? _preview1;
    [ObservableProperty] private Bitmap? _preview2;

    public SilhouetteViewModel() : this(App.Services.GetRequiredService<SilhouetteService>()) { }

    public SilhouetteViewModel(SilhouetteService service)
    {
        _service = service;
        // 必须在构造函数体内调用而非 Initialize()，
        // 因为 ViewModelBase 构造时会先调用 Initialize()，此时 _service 尚未赋值
        LoadTemplatesIfNeeded();
    }

    private void LoadTemplatesIfNeeded()
    {
        var silhouetteDir = Path.Combine(AppPaths.ResourceDirectory, "silhouette");
        if (Directory.Exists(silhouetteDir))
        {
            _service.LoadTemplates(silhouetteDir);
            StatusMessage = "模板已加载，就绪";
        }
        else
        {
            LoggerHelper.Warn($"[Silhouette] 模板目录不存在: {silhouetteDir}");
            StatusMessage = "模板目录未找到，请检查 resource/silhouette/";
        }
    }

    private void LoadPreviewImages(List<RecognitionResult> results)
    {
        Preview1 = null;
        Preview2 = null;
        if (results.Count == 0) return;

        var templates = _service.Templates;
        var top1 = templates.FirstOrDefault(t => t.Id == results[0].Id && t.IsHead == results[0].IsHead);
        if (top1 != null && File.Exists(top1.FilePath))
            Preview1 = new Bitmap(top1.FilePath);

        if (results.Count >= 2)
        {
            var top2 = templates.FirstOrDefault(t => t.Id == results[1].Id && t.IsHead == results[1].IsHead);
            if (top2 != null && File.Exists(top2.FilePath))
                Preview2 = new Bitmap(top2.FilePath);
        }
    }

    [RelayCommand]
    public async Task RecognizeAsync()
    {
        if (IsRecognizing) return;
        IsRecognizing = true;
        StatusMessage = "正在截图...";

        try
        {
            var processor = MaaProcessor.Processors.FirstOrDefault(p => p.MaaTasker?.Controller?.IsConnected == true);
            if (processor == null)
            {
                ToastHelper.Error("剪影识别", "未检测到已连接的模拟器，请先在主页连接设备。");
                return;
            }

            var controller = processor.MaaTasker!.Controller!;
            var capStatus = controller.Screencap().Wait();
            if (capStatus != MaaJobStatus.Succeeded)
            {
                ToastHelper.Error("剪影识别", "截图失败，请检查模拟器连接。");
                return;
            }

            // 获取截图 → SKBitmap
            var imageBuffer = new MaaImageBuffer();
            if (!controller.GetCachedImage(imageBuffer))
            {
                ToastHelper.Error("剪影识别", "获取截图数据失败。");
                return;
            }

            SKBitmap? skBitmap = null;
            if (imageBuffer.TryGetEncodedData(out Stream encodedStream))
            {
                encodedStream.Seek(0, SeekOrigin.Begin);
                skBitmap = SKBitmap.Decode(encodedStream);
                await encodedStream.DisposeAsync();
            }

            if (skBitmap == null)
            {
                ToastHelper.Error("剪影识别", "解码截图失败。");
                return;
            }

            StatusMessage = "正在识别...";
            var results = await Task.Run(() => _service.Recognize(skBitmap));
            skBitmap.Dispose();

            Results = new ObservableCollection<RecognitionResult>(results);

            // 加载 Top 模板图片用于预览
            LoadPreviewImages(results);

            if (results.Count == 0)
                StatusMessage = "尚未露出剪影，请先刮开涂层";
            // 不同刀剑的显示得分并列时，不能把排序第一项当成已确认身份
            else if (results.Skip(1).Any(result => result.Id != results[0].Id
                && result.ScoreText == results[0].ScoreText))
                StatusMessage = "多个候选并列，需继续揭露剪影";
            else
                StatusMessage = $"识别完成：{results[0].Name} ({results[0].ScoreText})";
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"[Silhouette] 识别异常：{ex}", ex);
            ToastHelper.Error("剪影识别", $"识别失败：{ex.Message}");
            StatusMessage = "识别出错，请重试";
        }
        finally
        {
            IsRecognizing = false;
        }
    }
}
