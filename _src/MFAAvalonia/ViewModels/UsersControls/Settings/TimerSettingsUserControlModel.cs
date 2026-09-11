using CommunityToolkit.Mvvm.ComponentModel;
using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Services;
using MFAAvalonia.ViewModels.Other;
using System.Collections.ObjectModel;

namespace MFAAvalonia.ViewModels.UsersControls.Settings;

public partial class TimerSettingsUserControlModel : ViewModelBase
{
    /// <summary>
    /// 全局定时器模型（所有多开实例共享）
    /// </summary>
    public TimerModel TimerModels => TimerModel.Instance;

    /// <summary>
    /// 实例列表，供 UI ComboBox 绑定（UI 上仍显示为"配置"）
    /// </summary>
    public ObservableCollection<TimerModel.InstanceEntry> InstanceList => TimerModel.Instance.InstanceList;

    /// <summary>
    /// 当前平台是否由 Windows 计划任务补足程序未运行时的定时启动
    /// </summary>
    public bool IsWindowsScheduledTaskSupported => WindowsScheduledTaskSyncService.IsSupported;

    /// <summary>
    /// 是否已经产生过 Windows 计划任务同步结果
    /// </summary>
    public bool HasWindowsScheduledTaskStatus =>
        IsWindowsScheduledTaskSupported && WindowsScheduledTaskSyncService.Status.HasRun;

    /// <summary>
    /// Windows 计划任务同步状态提示
    /// </summary>
    public string WindowsScheduledTaskStatusText => WindowsScheduledTaskSyncService.Status.Message;

    protected override void Initialize()
    {
        RefreshInstances();
        WindowsScheduledTaskSyncService.StatusChanged += OnWindowsScheduledTaskStatusChanged;
        // 打开设置界面时重新同步，使上一次失败的计划任务能够恢复
        WindowsScheduledTaskSyncService.RequestSync();
        base.Initialize();
    }

    public void RefreshInstances()
    {
        TimerModel.Instance.RefreshInstanceList();
        OnPropertyChanged(nameof(InstanceList));
    }

    private void OnWindowsScheduledTaskStatusChanged()
    {
        OnPropertyChanged(nameof(HasWindowsScheduledTaskStatus));
        OnPropertyChanged(nameof(WindowsScheduledTaskStatusText));
    }
}
