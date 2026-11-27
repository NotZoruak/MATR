using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using MFAAvalonia.Helper;
using MFAAvalonia.Services;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Layout;

namespace MFAAvalonia.Views.Pages;

/// <summary>本丸状态总览界面。</summary>
public partial class DefaultOverviewView : UserControl
{
    private static readonly string[] OverviewKeys = ["PlayerName", "Country", "Level", "Charcoal", "Steel", "Coolant", "File", "Money", "Bill", "Accelerator", "Team1", "Team2", "Team3", "Team4", "Team5", "Duty", "Forge", "Repair"];
    private static readonly JObject SwordCatalog = HeldSwordTableData.LoadCatalog();

    private string _paletteKey = "";
    private GameOverviewSnapshot? _snapshot;
    private readonly Func<Task> _syncWarehouse;
    private DateTimeOffset _now = DateTimeOffset.UtcNow;
    private bool _syncingWarehouse;
    public DefaultOverviewView() : this(() => Task.CompletedTask) { }

    public DefaultOverviewView(Func<Task> syncWarehouse)
    {
        _syncWarehouse = syncWarehouse;
        InitializeComponent();
        AttachedToVisualTree += (_, _) => UpdatePalette();
        ActualThemeVariantChanged += (_, _) => UpdatePalette();
    }
    public void Update(GameOverviewSnapshot? snapshot, string status)
    {
        if (snapshot == null) ClearValues(); else ApplySnapshot(snapshot);
        AccountReadStatus.Text = status;
    }
    public void Tick(DateTimeOffset now)
    {
        _now = now;
        UpdatePalette();
        RenderWorkStatus();
        RenderTeamStatus();
        RenderTrainingStatus();
    }
    private async void SyncWarehouse(object? sender, RoutedEventArgs e)
    {
        if (_snapshot == null || _syncingWarehouse) return;
        _syncingWarehouse = true;
        WarehouseSyncButton.IsEnabled = false;
        try { await _syncWarehouse(); }
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
        DutyHorseMembers.Children.Clear();
        DutyFieldMembers.Children.Clear();
        DutyBoutMembers.Children.Clear();
        ValueTrainingSummary.Text = "未读取";
        ToolTip.SetTip(ValueTrainingSummary, null);
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

    private void ApplySnapshot(GameOverviewSnapshot snapshot)
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
        RenderDutyMembers(snapshot);
        RenderWorkStatus();
        RenderTeamStatus();
        RenderTrainingStatus();
    }

    private void RenderDutyMembers(GameOverviewSnapshot snapshot)
    {
        foreach (var (job, panel, jobCard) in new[] { ("horse", DutyHorseMembers, DutyHorseCard), ("field", DutyFieldMembers, DutyFieldCard), ("bout", DutyBoutMembers, DutyBoutCard) })
        {
            panel.Children.Clear();
            jobCard.IsVisible = true;
            if (!snapshot.DutyMembers.TryGetValue(job, out var members))
            {
                panel.Children.Add(new TextBlock { Text = "未读取", Classes = { "overview-detail" } });
                continue;
            }
            if (members.All(id => id == null))
            {
                jobCard.IsVisible = false;
                continue;
            }

            var attributes = job switch
            {
                "horse" => new[] { ("def", "防御"), ("mobile", "机动") },
                "field" => new[] { ("hp", "生存"), ("scout", "侦察") },
                _ => new[] { ("atk", "打击"), ("back", "冲力") }
            };
            snapshot.DutyMemberSerials.TryGetValue(job, out var serials);
            for (var index = 0; index < members.Count; index++)
            {
                var swordId = members[index];
                if (swordId == null)
                {
                    continue;
                }
                if (swordId.StartsWith("个体：", StringComparison.Ordinal))
                {
                    panel.Children.Add(new TextBlock { Text = "未识别刀剑", Classes = { "overview-detail" } });
                    continue;
                }
                var serial = serials != null && index < serials.Count ? serials[index] : null;
                var swordData = serial != null && snapshot.HeldSwords.TryGetValue(serial, out var raw) ? raw : null;
                var definition = SwordCatalog["swords"]?[swordId] as JObject;
                var member = new StackPanel { Spacing = 8 };
                var name = definition?["name"]?.ToString() ?? "刀剑 ID：" + swordId;
                if (definition?["symbol"]?.ToString() == "2") name += "·极";
                var nameText = new TextBlock { Text = name, FontWeight = Avalonia.Media.FontWeight.SemiBold, Classes = { "overview-detail" } };
                member.Children.Add(nameText);
                var stats = new Avalonia.Controls.Primitives.UniformGrid { Columns = 1 };
                // 按成员卡片的实际可用宽度切换，不依赖窗口或侧栏宽度。
                stats.SizeChanged += (_, args) => stats.Columns = args.NewSize.Width >= 260 ? 2 : 1;
                for (var attributeIndex = 0; attributeIndex < attributes.Length; attributeIndex++)
                {
                    var attribute = attributes[attributeIndex];
                    var stat = CreateDutyProgress(attribute.Item1, attribute.Item2, swordData, definition);
                    stats.Children.Add(new Border { Child = stat, Padding = new Avalonia.Thickness(4), ClipToBounds = true });
                }
                member.Children.Add(stats);
                var card = new Border { Child = member, Padding = new Avalonia.Thickness(10), CornerRadius = new Avalonia.CornerRadius(6), BorderThickness = new Avalonia.Thickness(1), ClipToBounds = true };
                card.Bind(Border.BackgroundProperty, new DynamicResourceExtension("SukiCardBackground"));
                card.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("SukiControlBorderBrush"));
                panel.Children.Add(card);
            }
        }
        DutyJobs.Columns = Math.Max(1, DutyJobs.Children.Count(card => card.IsVisible));
    }

    private Control CreateDutyProgress(string key, string label, JObject? data, JObject? definition)
    {
        long? remaining = long.TryParse(data?[key + "_up"]?.ToString(), out var left) ? left : null;
        long? maximum = long.TryParse(definition?[key + "_up"]?.ToString(), out var total) ? total : null;
        // 静态表记录可提升总量，个体记录剩余量，两者之差为已提升值。
        long? current = maximum.HasValue && remaining.HasValue && remaining >= 0 && maximum >= remaining
            ? maximum - remaining : null;
        var box = new StackPanel { Spacing = 4 };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions(remaining == 0 ? "Auto,*,Auto" : "Auto,*"), ColumnSpacing = 6 };
        header.Children.Add(new TextBlock { Text = label, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Classes = { "caption" } });
        var number = new TextBlock { Text = current.HasValue && maximum.HasValue ? $"{current}/{maximum}" : "未读取", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(number, 1);
        header.Children.Add(number);
        if (remaining == 0)
        {
            var footer = new TextBlock { Text = "MAX", HorizontalAlignment = HorizontalAlignment.Right, FontSize = 11 };
            footer.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("OverviewSuccessBrush"));
            Grid.SetColumn(footer, 2);
            header.Children.Add(footer);
        }
        box.Children.Add(header);
        if (current.HasValue && maximum.HasValue)
        {
            var ratio = maximum > 0 ? Math.Clamp((double)current.Value / maximum.Value, 0, 1) : 1;
            var bar = new Grid { Height = 3, ColumnDefinitions = new ColumnDefinitions() };
            bar.ColumnDefinitions.Add(new ColumnDefinition(ratio, GridUnitType.Star));
            bar.ColumnDefinitions.Add(new ColumnDefinition(1 - ratio, GridUnitType.Star));
            var fill = new Border();
            fill.Bind(Border.BackgroundProperty, new DynamicResourceExtension(remaining == 0 ? "OverviewSuccessBrush" : "OverviewAccentBrush"));
            bar.Children.Add(fill);
            var track = new Border { Child = bar, Height = 3, CornerRadius = new Avalonia.CornerRadius(1.5), ClipToBounds = true };
            track.Bind(Border.BackgroundProperty, new DynamicResourceExtension("SukiControlBorderBrush"));
            box.Children.Add(track);
        }
        return box;
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
            var displayText = display.Text;
            if (team.Value.Status == "3" && team.Value.SortieMap is { } rawMap)
                displayText = displayText.Replace(rawMap, GameEventNames.FormatMap(rawMap), StringComparison.Ordinal);
            if (team.Value.Status == "2" && !display.Completed)
            {
                displayText = "远征中，" + (team.Value.ExpeditionMap ?? "地图未知");
                displayText += "，" + GameOverviewWorkStatus.FormatSchedule(team.Value.FinishedAt, _now).Text;
            }
            text.Text = null;
            text.Inlines ??= new InlineCollection();
            text.Inlines.Clear();
            if (display.Completed)
            {
                var returned = new Run("远征归来");
                returned.Bind(Run.ForegroundProperty, new DynamicResourceExtension("OverviewSuccessBrush"));
                text.Inlines.Add(returned);
                if (!string.IsNullOrWhiteSpace(team.Value.ExpeditionMap))
                    text.Inlines.Add(new Run("，" + team.Value.ExpeditionMap));
            }
            else text.Inlines.Add(new Run(displayText));
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
                    var status = GameOverviewWorkStatus.FormatSchedule(entry.FinishedAt, _now);
                    var statusText = new TextBlock { Text = status.Text == "未使用" ? "空闲" : status.Text.Replace("，剩余：", "\n剩余：", StringComparison.Ordinal), TextWrapping = TextWrapping.Wrap };
                    statusText.Classes.Add("overview-detail");
                    statusText.Classes.Add(status.Completed ? "overview-complete" :
                        status.Text.Contains("剩余：", StringComparison.Ordinal) ? "overview-running" : "overview-idle");
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
                text.Inlines.Add(new Run(group.Key == "Duty" ? "待机中" : "未使用"));
            for (var index = 0; index < group.Value.Count; index++)
            {
                if (index > 0) text.Inlines.Add(new LineBreak());
                var entry = group.Value[index];
                if (entry.Label.Length > 0) text.Inlines.Add(new Run(entry.Label + "　"));
                var status = GameOverviewWorkStatus.FormatSchedule(entry.FinishedAt, _now);
                var statusText = group.Key == "Duty" && status.Text == "未使用" ? "待机中" : status.Text;
                var run = new Run(statusText);
                run.Bind(Run.ForegroundProperty, new DynamicResourceExtension(status.Completed ? "OverviewSuccessBrush" :
                    status.Text.Contains("剩余：", StringComparison.Ordinal) ? "OverviewAccentBrush" : "OverviewMutedBrush"));
                text.Inlines.Add(run);
            }
        }
    }

    private void RenderTrainingStatus()
    {
        if (_snapshot == null)
        {
            ValueTrainingSummary.Text = "未读取";
            ToolTip.SetTip(ValueTrainingSummary, null);
            return;
        }
        if (_snapshot.TrainingItems.Count == 0)
        {
            ValueTrainingSummary.Text = "当前没有修行中的刀剑";
            ToolTip.SetTip(ValueTrainingSummary, null);
            return;
        }

        var summaries = _snapshot.TrainingItems.Select(entry =>
        {
            var swordId = _snapshot.HeldSwords.TryGetValue(entry.SwordSerial, out var swordData)
                ? swordData["sword_id"]?.ToString()
                : null;
            var definition = swordId == null ? null : SwordCatalog["swords"]?[swordId] as JObject;
            var name = definition?["name"]?.ToString() ?? "未知刀剑";
            if (definition?["symbol"]?.ToString() == "2") name += "·极";

            var schedule = GameOverviewWorkStatus.FormatSchedule(entry.FinishedAt, _now);
            if (schedule.Completed)
            {
                var end = entry.FinishedAt is { Length: >= 5 } finishedAt ? finishedAt[5..] : "未读取";
                return $"{name}，结束时间：{end}，剩余：已完成";
            }
            if (schedule.Text == "未使用") return $"{name}，结束时间未读取";
            var split = schedule.Text.Split("，剩余：", 2, StringSplitOptions.None);
            var finish = split[0].Replace("结束：", "结束时间：", StringComparison.Ordinal);
            return split.Length > 1 ? $"{name}，{finish}，剩余：{split[1]}" : $"{name}，{finish}";
        }).ToArray();
        ValueTrainingSummary.Text = string.Join("；", summaries);
        ToolTip.SetTip(ValueTrainingSummary, summaries.Length > 1 ? string.Join(Environment.NewLine, summaries) : null);
    }

    private static readonly JArray SwordBook = JArray.Parse(CatalogResources.Read("GameSwordBookCatalog.json"));
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
