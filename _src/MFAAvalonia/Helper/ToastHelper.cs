using Avalonia.Controls.Notifications;
using SukiUI.Toasts;
using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using System.Diagnostics;

namespace MFAAvalonia.Helper;

public static class ToastHelper
{
    private const string IssueUrl = "https://github.com/NotZoruak/MATR/issues";

    public static SukiToastBuilder CreateToastByType(NotificationType toastType, string title = "", object? content = null, int duration = 3)
    {
        if (duration <= 0)
        {
            return Instances.ToastManager.CreateToast()
           .WithTitle(title)
           .WithContent(
               content)
           .OfType(toastType).Dismiss().ByClicking();
        }
        return Instances.ToastManager.CreateToast()
            .WithTitle(title)
            .WithContent(
                content)
            .OfType(toastType).Dismiss().After(TimeSpan.FromSeconds(duration))
            .Dismiss().ByClicking();
    }

    public static void Success(string title = "", object? content = null, int duration = 3)
    {
        DispatcherHelper.RunOnMainThread(() => CreateToastByType(NotificationType.Success, title, content, duration).Queue());
    }

    public static void SuccessWithIssue(string title, string message, int duration = 0)
    {
        DispatcherHelper.RunOnMainThread(() =>
        {
            var content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 0,
                MaxWidth = 360,
                VerticalAlignment = VerticalAlignment.Center,
            };
            content.Children.Add(new TextBlock { Text = message, VerticalAlignment = VerticalAlignment.Center });

            var issueButton = new Button
            {
                Content = "前往反馈。",
                Foreground = Brushes.DodgerBlue,
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                VerticalContentAlignment = VerticalAlignment.Center,
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            };
            issueButton.Click += (_, _) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo(IssueUrl) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    LoggerHelper.Warning($"打开 GitHub Issue 链接失败：{ex.Message}");
                }
            };
            content.Children.Add(issueButton);
            CreateToastByType(NotificationType.Success, title, content, duration).Queue();
        });
    }

    public static void Info(string title = "", object? content = null, int duration = 3)
    {
        DispatcherHelper.RunOnMainThread(() => CreateToastByType(NotificationType.Information, title, content, duration).Queue());
    }

    public static void Warn(string title = "", object? content = null, int duration = 3)
    {
        DispatcherHelper.RunOnMainThread(() => CreateToastByType(NotificationType.Warning, title, content, duration).Queue());
    }

    public static void Error(string title = "", object? content = null, int duration = 3)
    {
        DispatcherHelper.RunOnMainThread(() => CreateToastByType(NotificationType.Error, title, content, duration).Queue());
    }
}
