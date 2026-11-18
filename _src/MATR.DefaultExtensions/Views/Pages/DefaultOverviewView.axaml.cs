using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using MFAAvalonia.Helper;
using MFAAvalonia.Services;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Markup.Xaml.MarkupExtensions;

namespace MFAAvalonia.Views.Pages;

/// <summary>默认扩展状态总览界面，仅显示宿主提供的游戏状态。</summary>
public partial class DefaultOverviewView : UserControl
{
    private static readonly string[] OverviewKeys = ["PlayerName", "Country", "Level", "Charcoal", "Steel", "Coolant", "File", "Money", "Bill", "Accelerator", "Team1", "Team2", "Team3", "Team4", "Team5", "Duty", "Forge", "Repair"];

    private string _paletteKey = "";
    private GameOverviewLogParser.Snapshot? _snapshot;
    private readonly MATR.ExtensionSdk.IExtensionContext _context;
    private DateTimeOffset _now = DateTimeOffset.UtcNow;
    private bool _syncingWarehouse;
    public DefaultOverviewView(MATR.ExtensionSdk.IExtensionContext context)
    {
        _context = context;
        InitializeComponent();
        AttachedToVisualTree += (_, _) => UpdatePalette();
        ActualThemeVariantChanged += (_, _) => UpdatePalette();
    }
    public void Update(MATR.ExtensionSdk.ExtensionState state)
    {
        var snapshot = string.IsNullOrWhiteSpace(state.SnapshotJson) ? null : Newtonsoft.Json.JsonConvert.DeserializeObject<GameOverviewLogParser.Snapshot>(state.SnapshotJson);
        if (snapshot == null) ClearValues(); else ApplySnapshot(snapshot);
        AccountReadStatus.Text = state.Status;
    }
    public void Tick(DateTimeOffset now)
    {
        _now = now;
        UpdatePalette();
        RenderWorkStatus();
        RenderTeamStatus();
    }
    private async void SyncWarehouse(object? sender, RoutedEventArgs e)
    {
        if (_snapshot == null || _syncingWarehouse) return;
        _syncingWarehouse = true;
        WarehouseSyncButton.IsEnabled = false;
        try { await _context.SyncWarehouseAsync(); }
        catch (Exception exception)
        {
            ToastHelper.Warn("仓库同步失败", "未能保存，请检查本丸数据目录权限。");
            LoggerHelper.Warning("扩展仓库同步失败，类型：" + exception.GetType().Name);
        }
        finally
        {
            _syncingWarehouse = false;
            WarehouseSyncButton.IsEnabled = _snapshot != null;
        }
    }
    private void UpdatePalette()
    {
        var primary = SukiUI.SukiTheme.GetInstance()?.ActiveColorTheme?.Primary ?? Color.Parse("#567FA0");
        var dark = ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
        var key = primary.ToString() + dark;
        if (_paletteKey == key) return;
        _paletteKey = key;
        // 主体使用中性色，仅混入少量主题色，避免高饱和文字铺满页面。
        var heading = Color.Parse(dark ? "#D6DCE5" : "#303A48");
        var detail = Color.Parse(dark ? "#BEC9D7" : "#465264");
        Resources["OverviewHeadingBrush"] = new SolidColorBrush(Blend(heading, primary, 0.12));
        Resources["OverviewAccentBrush"] = new SolidColorBrush(Blend(detail, primary, 0.23));
    }

    private static Color Blend(Color neutral, Color accent, double weight) => Color.FromRgb(
        (byte)Math.Round(neutral.R * (1 - weight) + accent.R * weight),
        (byte)Math.Round(neutral.G * (1 - weight) + accent.G * weight),
        (byte)Math.Round(neutral.B * (1 - weight) + accent.B * weight));

    private void ToggleCard(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string name || this.FindControl<Control>(name) is not { } body) return;
        body.IsVisible = !body.IsVisible;
        if (button.Content is FluentIcons.Avalonia.Fluent.FluentIcon icon)
            icon.Icon = body.IsVisible ? FluentIcons.Common.Icon.ChevronUp : FluentIcons.Common.Icon.ChevronDown;
        ToolTip.SetTip(button, body.IsVisible ? "收起卡片" : "展开卡片");
        Avalonia.Automation.AutomationProperties.SetName(button, body.IsVisible ? "收起卡片" : "展开卡片");
    }

    private void ClearValues()
    {
        _snapshot = null;
        WarehouseSyncButton.IsEnabled = false;
        DutyHorseMembers.Text = DutyFieldMembers.Text = DutyBoutMembers.Text = "未读取";
        ForgeSlots.Children.Clear();
        RepairSlots.Children.Clear();
        foreach (var key in OverviewKeys)
            if (this.FindControl<TextBlock>("Value" + key) is { } text)
            {
                text.Classes.Remove("overview-running");
                text.Inlines?.Clear();
                text.Text = "未读取";
                ToolTip.SetTip(text, null);
            }
    }

    private void ApplySnapshot(GameOverviewLogParser.Snapshot snapshot)
    {
        ClearValues();
        foreach (var entry in snapshot.Values)
            if (this.FindControl<TextBlock>("Value" + entry.Key) is { } text)
            {
                text.Text = entry.Key == "Secretary" ? SecretaryName(entry.Value.Text) : entry.Value.Text;
                ToolTip.SetTip(text, "数据时间：" + entry.Value.ObservedAt);
            }
        _snapshot = snapshot;
        WarehouseSyncButton.IsEnabled = !_syncingWarehouse;
        foreach (var job in new[] { ("horse", DutyHorseMembers), ("field", DutyFieldMembers), ("bout", DutyBoutMembers) })
            if (snapshot.DutyMembers.TryGetValue(job.Item1, out var members))
                job.Item2.Text = members.All(id => id == null) ? "未指派" : string.Join("\n", members.Select(id => id == null ? "未指派" :
                    id.StartsWith("个体：", StringComparison.Ordinal) ? "未识别刀剑" : SecretaryName("刀剑 ID：" + id)));
        RenderWorkStatus();
        RenderTeamStatus();
    }

    private void RenderTeamStatus()
    {
        if (_snapshot == null) return;
        foreach (var team in _snapshot.Teams)
        {
            var key = "Team" + team.Key;
            if (!_snapshot.Values.TryGetValue(key, out var original)
                || this.FindControl<TextBlock>("Value" + key) is not { } text) continue;
            var display = GameOverviewWorkStatus.FormatExpedition(team.Value, original.Text, _now);
            text.Classes.Set("overview-running", team.Value.Status == "2" && !display.Completed);
            text.Text = null;
            text.Inlines ??= new InlineCollection();
            text.Inlines.Clear();
            if (display.Completed)
            {
                var returned = new Run("远征已归来");
                returned.Bind(Run.ForegroundProperty, new DynamicResourceExtension("OverviewSuccessBrush"));
                text.Inlines.Add(returned);
                text.Inlines.Add(new Run(display.Text[5..]));
            }
            else text.Inlines.Add(new Run(display.Text));
        }
    }

    private void RenderWorkStatus()
    {
        if (_snapshot == null) return;
        foreach (var group in _snapshot.WorkItems)
        {
            if (group.Key is "Forge" or "Repair")
            {
                var panel = group.Key == "Forge" ? ForgeSlots : RepairSlots;
                panel.Children.Clear();
                panel.Columns = Math.Max(1, group.Value.Count);
                foreach (var entry in group.Value)
                {
                    var status = GameOverviewWorkStatus.Format(entry.FinishedAt, _now);
                    var statusText = new TextBlock { Text = status.Text == "未使用" ? "空闲" : status.Text.Replace("结束时间：", "", StringComparison.Ordinal), TextWrapping = TextWrapping.Wrap };
                    statusText.Classes.Add("overview-detail");
                    statusText.Classes.Add(status.Completed ? "overview-complete" :
                        status.Text.StartsWith("结束时间：", StringComparison.Ordinal) ? "overview-running" : "overview-idle");
                    var item = new StackPanel { Spacing = 8, Margin = new Avalonia.Thickness(0, 0, 12, 0) };
                    var slotLabel = new TextBlock { Text = entry.Label };
                    slotLabel.Classes.Add("overview-label");
                    item.Children.Add(slotLabel);
                    item.Children.Add(statusText);
                    if (_snapshot.Values.TryGetValue(group.Key, out var value))
                        ToolTip.SetTip(item, "数据时间：" + value.ObservedAt);
                    panel.Children.Add(item);
                }
                if (group.Value.Count == 0)
                {
                    var placeholder = new TextBlock { Text = "暂无已开启工坊信息" };
                    placeholder.Classes.Add("caption");
                    panel.Children.Add(placeholder);
                }
                continue;
            }
            if (this.FindControl<TextBlock>("Value" + group.Key) is not { } text) continue;
            text.Text = null;
            text.Inlines ??= new InlineCollection();
            text.Inlines.Clear();
            if (group.Value.Count == 0)
                text.Inlines.Add(new Run("未使用"));
            for (var index = 0; index < group.Value.Count; index++)
            {
                if (index > 0) text.Inlines.Add(new LineBreak());
                var entry = group.Value[index];
                if (entry.Label.Length > 0) text.Inlines.Add(new Run(entry.Label + "　"));
                var status = GameOverviewWorkStatus.Format(entry.FinishedAt, _now);
                var run = new Run(status.Text);
                run.Bind(Run.ForegroundProperty, new DynamicResourceExtension(status.Completed ? "OverviewSuccessBrush" :
                    status.Text.StartsWith("结束时间：", StringComparison.Ordinal) ? "OverviewAccentBrush" : "OverviewMutedBrush"));
                text.Inlines.Add(run);
            }
        }
    }

    private static readonly JArray SwordBook = JArray.Parse(MATR.DefaultExtensions.CatalogResources.Read("SwordBookCatalog.json"));
    private static string SecretaryName(string fallback)
    {
        var id = fallback.Replace("刀剑 ID：", "", StringComparison.Ordinal);
        return SwordBook.OfType<JObject>().FirstOrDefault(x => x["number"]?.ToString() == id)?["name"]?.ToString() ?? fallback;
    }
    private void UpdateContentLayout(object? sender, SizeChangedEventArgs e)
    {
        CoreResources.Columns = e.NewSize.Width < 420 ? 2 : 4;
    }
}
