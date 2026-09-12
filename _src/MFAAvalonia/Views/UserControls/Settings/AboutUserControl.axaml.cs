using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using AvaloniaEdit.Highlighting;
using MFAAvalonia.Extensions;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using MFAAvalonia.ViewModels.Windows;
using MFAAvalonia.Views.Mobile;
using MFAAvalonia.Views.Windows;
using System;
using System.IO;
using System.Linq;

namespace MFAAvalonia.Views.UserControls.Settings;

public partial class AboutUserControl : UserControl
{
    public AboutUserControl()
    {
        InitializeComponent();
        TutorialCard.IsVisible = !OperatingSystem.IsAndroid();
    }
    private async void Button_OnClick(object? sender, RoutedEventArgs e)
    {
        var storageProvider = Instances.StorageProvider;
        if (storageProvider == null)
        {
            ToastHelper.Warn(LangKeys.Warning.ToLocalization(), LangKeys.PlatformNotSupportedOperation.ToLocalization());
            return;
        }

        await FileLogExporter.CompressRecentLogs(storageProvider);
    }
    
    private void DisplayAnnouncement(object? sender, RoutedEventArgs e)
    {
       AnnouncementViewModel.CheckAnnouncement(true);
    }
    
    private void ClearCache_Click(object? sender, RoutedEventArgs e)
    {
        if (!Instances.RootViewModel.Idle)
        {
            ToastHelper.Warn(
                LangKeys.Warning.ToLocalization(),
                LangKeys.StopTaskBeforeClearCache.ToLocalization());
            return;
        }

        var processors = MaaProcessor.Processors.ToList();
        foreach (var processor in processors)
        {
            try
            {
                processor.SetTasker();
            }
            catch (Exception ex)
            {
                LoggerHelper.Error($"清理缓存前停止实例 {processor.InstanceId} 的 Tasker 失败: {ex}");
                ToastHelper.Error(
                    LangKeys.ClearCacheFailed.ToLocalization(),
                    LangKeys.ClearCacheStopInstanceFailed.ToLocalizationFormatted(false, processor.InstanceId));
                return;
            }
        }

        var remainingTasker = processors.FirstOrDefault(p => p.MaaTasker != null || p.ScreenshotTasker != null);
        if (remainingTasker != null)
        {
            LoggerHelper.Warning($"清理缓存中止：实例 {remainingTasker.InstanceId} 仍存在未释放 Tasker。");
            ToastHelper.Error(
                LangKeys.ClearCacheFailed.ToLocalization(),
                LangKeys.ClearCacheInstanceStillUsingResource.ToLocalizationFormatted(false, remainingTasker.InstanceId));
            return;
        }

        var baseDirectory = AppPaths.DataRoot;
        var debugDirectory = Path.Combine(baseDirectory, "debug");
        var logsDirectory = AppPaths.LogsDirectory;

        try
        {
            ClearDirectory(debugDirectory);
            ClearDirectory(logsDirectory);
            Directory.CreateDirectory(debugDirectory);
            Directory.CreateDirectory(logsDirectory);
            ToastHelper.Success(LangKeys.ClearCacheSuccess.ToLocalization());
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"清理缓存失败: {ex.Message}");
            ToastHelper.Error(LangKeys.ClearCacheFailed.ToLocalization(), ex.Message);
        }
    }
    
    private static void ClearDirectory(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            return;
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(directoryPath))
        {
            try
            {
                if (Directory.Exists(entry))
                {
                    Directory.Delete(entry, true);
                }
                else
                {
                    File.Delete(entry);
                }
            }
            catch (Exception ex)
            {
                LoggerHelper.Warning($"清理缓存项失败: {entry}, {ex.Message}");
            }
        }
    }
    
    private void ShowLicense_Click(object? sender, RoutedEventArgs e)
    {
        var viewModel = DataContext as ViewModels.Pages.SettingsViewModel;
        if (viewModel != null && !string.IsNullOrEmpty(viewModel.ResourceLicense))
        {
            LicenseView.ShowLicense(viewModel.ResourceLicense);
        }
    }

    private void StartTutorial_Click(object? sender, RoutedEventArgs e)
    {
        // 教程实现在桌面与移动共用的根壳里（RootViewContent 的 TutorialOverlay），
        // 关于页只负责触发重播：上游桌面端把这里留成了纯提示占位，没有接入口。
        var rootContent = this.FindAncestorOfType<RootViewContent>()
            ?? (this.GetVisualRoot() as Visual)?.FindDescendantOfType<RootViewContent>();
        if (rootContent == null)
        {
            LoggerHelper.Warning("未找到根视图，无法启动使用教程");
            ToastHelper.Warn("暂时无法启动教程，请回到任务页后重试。");
            return;
        }

        rootContent.TryStartTutorial();
    }
}

