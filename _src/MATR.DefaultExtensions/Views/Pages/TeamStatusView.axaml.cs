using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.VisualTree;
using MFAAvalonia.Helper;
using MFAAvalonia.Models;
using MFAAvalonia.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MFAAvalonia.Views.Pages;

/// <summary>五支部队的状态卡片及固定位置成员表。</summary>
public partial class TeamStatusView : UserControl
{
    private sealed record Column(string Key, string Title, double Width, bool DefaultVisible = true);
    private sealed record ColumnLayout(bool Visible, int Order, double Width);
    private sealed record Member(string Serial, HeldSwordRow Sword, int Position);
    private static readonly Column[] Definitions =
    [
        new("Name", "刀剑名", 150), new("Level", "等级", 58), new("Health", "生存", 74),
        new("Fatigue", "疲劳", 58), new("mobile", "机动", 58), new("Troops", "刀装", 255),
        new("Horse", "马匹", 100), new("Charm", "御守", 100), new("Treasure", "宝物", 130),
        new("Book", "刀帐", 58, false), new("Protect", "保护", 64, false),
        new("Ranbu", "乱舞", 58, false), new("hp", "生存", 68, false),
        new("atk", "打击", 58, false), new("def", "防御", 58, false), new("back", "冲力", 58, false),
        new("scout", "侦察", 68, false), new("hide", "隐蔽", 58, false),
        new("loyalties", "必杀", 58, false), new("Serial", "唯一 ID", 120, false), new("Type", "刀种", 72, false)
    ];
    private readonly Dictionary<string, DataGridColumn> _columns = new();
    private readonly string _layoutPath;
    private List<HeldSwordRow> _rows = [];
    private GameOverviewLogParser.Snapshot? _snapshot;
    private string? _expandedTeam;
    private string _signature = "";
    private bool _loadingLayout;
    private ContextMenu? _headerMenu;
    private DateTimeOffset _now = DateTimeOffset.UtcNow;
    private bool _experienceMode;

    public TeamStatusView(string dataDirectory)
    {
        _layoutPath = Path.Combine(dataDirectory, "team-status-view.json");
        InitializeComponent();
        BuildColumns();
        BuildCards();
        LoadLayout();
        TeamGrid.ColumnReordered += (_, _) => SaveLayout();
        TeamGrid.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (e.ClickCount != 2 || !e.GetCurrentPoint(TeamGrid).Properties.IsLeftButtonPressed) return;
            var levelCell = (e.Source as Visual)?.GetVisualAncestors().OfType<Border>().FirstOrDefault(x => Equals(x.Tag, "Level"));
            if (levelCell == null) return;
            _experienceMode = !_experienceMode;
            _columns["Level"].Header = Header(_experienceMode ? "累计经验" : "等级");
            if (_experienceMode && _columns["Level"].ActualWidth < 130) _columns["Level"].Width = new DataGridLength(130);
            else if (!_experienceMode && _columns["Level"].Width.Value == 130) _columns["Level"].Width = new DataGridLength(58);
            RefreshMembers();
            e.Handled = true;
        }, RoutingStrategies.Tunnel, true);
        DetachedFromVisualTree += (_, _) => CloseHeaderMenu();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty && !IsVisible) CloseHeaderMenu();
        };
    }

    public void SetSnapshot(GameOverviewLogParser.Snapshot snapshot)
    {
        var signature = snapshot.Account + JsonConvert.SerializeObject(snapshot.Teams)
            + JsonConvert.SerializeObject(snapshot.HeldSwords) + JsonConvert.SerializeObject(snapshot.SwordTeams)
            + JsonConvert.SerializeObject(snapshot.SwordTeamPositions) + JsonConvert.SerializeObject(snapshot.EquipmentKinds);
        if (signature == _signature) return;
        _signature = signature;
        _snapshot = snapshot;
        _rows = HeldSwordTableData.Build(snapshot, HeldSwordTableData.LoadCatalog());
        BuildCards();
        RefreshMembers();
    }

    public void ClearSnapshot()
    {
        _signature = "";
        _snapshot = null;
        _rows = [];
        BuildCards();
        RefreshMembers();
    }

    public void Tick(DateTimeOffset now)
    {
        var previous = _now;
        _now = now;
        if (_snapshot?.Teams.Values.Any(team => team.Status == "2"
            && GameOverviewWorkStatus.Format(team.FinishedAt, previous).Completed
                != GameOverviewWorkStatus.Format(team.FinishedAt, now).Completed) == true)
            BuildCards();
    }

    private void BuildCards()
    {
        TeamCards.Children.Clear();
        for (var number = 1; number <= 5; number++)
        {
            var team = number.ToString(CultureInfo.InvariantCulture);
            var entry = _snapshot?.Teams.GetValueOrDefault(team);
            var members = _snapshot?.SwordTeams.Count(x => x.Value == team) ?? 0;
            var status = entry?.Status switch
            {
                "0" => "未知", "1" => "待命", "2" => "远征中", "3" => "出阵中",
                null => "状态未知", _ => "未知状态"
            };
            var expedition = entry?.Status == "2"
                ? GameOverviewWorkStatus.FormatExpedition(entry, status, _now)
                : null;
            var map = entry?.Status switch
            {
                "3" => (entry.SortieMap ?? "地图未知") + (entry.SortiePoint == null ? "" : " · 点位 " + entry.SortiePoint),
                _ => ""
            };
            var details = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
            details.Children.Add(new TextBlock
            {
                Text = $"部队{ChineseNumber(number)}", FontSize = 16, FontWeight = FontWeight.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            var statusText = new TextBlock
            {
                Text = entry?.Status == "2"
                    ? (expedition?.Text ?? status) + " · " + (entry.ExpeditionMap ?? "地图未知")
                    : status,
                FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 174
            };
            if (expedition?.Completed == true)
                statusText.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("OverviewSuccessBrush"));
            details.Children.Add(statusText);
            if (entry?.Status == "2")
                details.Children.Add(new TextBlock
                {
                    Text = FormatReturnTime(entry.FinishedAt), FontSize = 11, Foreground = Brushes.Gray,
                    HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap, MaxWidth = 174
                });
            details.Children.Add(new TextBlock { Text = map, FontSize = 12, Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center });
            details.Children.Add(new TextBlock { Text = $"{members}/6", FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center });
            var button = new Button
            {
                Content = details, Padding = new Thickness(8), MinHeight = 92,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = Brushes.Transparent, BorderThickness = new Thickness(1),
                Tag = team
            };
            button.Click += (_, _) =>
            {
                _expandedTeam = _expandedTeam == team ? null : team;
                BuildCards();
                RefreshMembers();
            };
            if (_expandedTeam == team)
            {
                button.Background = new SolidColorBrush(Color.Parse("#DCE8FF"));
                button.BorderBrush = new SolidColorBrush(Color.Parse("#4B79FF"));
            }
            else
            {
                button.Bind(Button.BackgroundProperty, new DynamicResourceExtension("SukiCardBackground"));
                button.Bind(Button.BorderBrushProperty, new DynamicResourceExtension("SukiBorderBrush"));
            }
            Grid.SetColumn(button, number - 1);
            TeamCards.Children.Add(button);
        }
    }

    private static string ChineseNumber(int number) => number switch { 1 => "一", 2 => "二", 3 => "三", 4 => "四", _ => "五" };

    private static string FormatReturnTime(string? finishedAt) =>
        DateTime.TryParseExact(finishedAt, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time.ToString("MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            : "归来时间未知";

    private void BuildColumns()
    {
        foreach (var definition in Definitions)
        {
            var column = new DataGridTemplateColumn
            {
                Header = Header(definition.Title), IsVisible = definition.DefaultVisible,
                Width = new DataGridLength(definition.Width), MinWidth = 48,
                CellTemplate = new FuncDataTemplate<Member>((member, _) => member == null ? null : BuildCell(member, definition))
            };
            column.HeaderPointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(TeamGrid).Properties.IsRightButtonPressed || e.Source is not Control control) return;
                OpenHeaderMenu(control);
                e.Handled = true;
            };
            column.HeaderPointerReleased += (_, _) => SaveLayout();
            _columns[definition.Key] = column;
            TeamGrid.Columns.Add(column);
        }
    }

    private static Control Header(string title) => new TextBlock
    {
        Text = title, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
    };

    private Control BuildCell(Member member, Column column)
    {
        var row = member.Sword;
        if (column.Key == "Troops")
        {
            var slots = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*") };
            for (var i = 0; i < row.Troops.Length; i++)
            {
                var troop = row.Troops[i];
                var text = new TextBlock { TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 13 };
                text.Inlines ??= new InlineCollection();
                text.Inlines.Add(new Run(troop?.Kind ?? "—"));
                if (troop != null) text.Inlines.Add(new Run($" ({troop.Current}/{troop.Maximum})") { FontSize = 10, FontStyle = FontStyle.Italic });
                var border = new Border { Child = text, BorderThickness = new Thickness(0, 0, i < 2 ? 1 : 0, 0) };
                border.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("SukiBorderBrush"));
                Grid.SetColumn(border, i);
                slots.Children.Add(border);
            }
            return slots;
        }
        var textValue = column.Key switch
        {
            "Name" => row.Name,
            "Level" => HeldSwordRow.Text(_experienceMode ? row.Number("exp") : row.Number("level")),
            "Fatigue" or "mobile" or "Book" or "Protect" or "Ranbu" or "hp" or "atk" or "def" or "back" or "scout" or "hide" or "loyalties" => row.SortValue(column.Key, false)?.ToString() ?? "—",
            "Health" => HeldSwordRow.Text(row.Number("hp")) + "/" + HeldSwordRow.Text(row.Number("hp_max")),
            "Horse" => row.Horse,
            "Charm" => row.Charm,
            "Treasure" => row.Treasure,
            "Serial" => row.Serial,
            "Type" => row.Type,
            _ => "—"
        };
        if (column.Key == "Level") textValue = HeldSwordRow.Text(row.Number("level"));
        var content = new TextBlock
        {
            Text = textValue, FontSize = 14, TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
        };
        if (column.Key == "Health"
            && row.Number("hp") is { } health
            && row.Number("hp_max") is { } maximumHealth
            && maximumHealth > 0)
        {
            if (health * 100 <= maximumHealth * 30)
                content.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("OverviewCriticalBrush"));
            else if (health * 100 <= maximumHealth * 65)
                content.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("OverviewWarningBrush"));
        }
        var cell = new Border { Child = content, Background = Brushes.Transparent, Padding = new Thickness(1, 2) };
        if (column.Key == "Name") ToolTip.SetTip(cell, row.NameTip);
        if (column.Key == "Level") ToolTip.SetTip(cell, row.LevelTip);
        if (column.Key == "Treasure" && row.Treasure != "—") ToolTip.SetTip(cell, row.Treasure);
        return cell;
    }

    private void RefreshMembers()
    {
        if (_snapshot == null || _expandedTeam == null)
        {
            TeamGrid.ItemsSource = null;
            return;
        }
        TeamGrid.ItemsSource = _rows
            .Where(row => _snapshot.SwordTeams.GetValueOrDefault(row.Serial) == _expandedTeam)
            .Select(row => new Member(row.Serial, row,
                int.TryParse(_snapshot.SwordTeamPositions.GetValueOrDefault(row.Serial), out var position) ? position : int.MaxValue))
            .OrderBy(member => member.Position).ThenBy(member => member.Serial, StringComparer.Ordinal)
            .ToArray();
    }

    private void OpenHeaderMenu(Control target)
    {
        CloseHeaderMenu();
        var menu = new ContextMenu();
        foreach (var definition in Definitions)
        {
            var column = _columns[definition.Key];
            var item = new MenuItem { Header = definition.Title, ToggleType = MenuItemToggleType.CheckBox, IsChecked = column.IsVisible };
            item.PropertyChanged += (_, e) =>
            {
                if (e.Property != MenuItem.IsCheckedProperty) return;
                column.IsVisible = item.IsChecked;
                SaveLayout();
            };
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var reset = new MenuItem { Header = "恢复默认列和顺序" };
        reset.Click += (_, _) =>
        {
            _loadingLayout = true;
            for (var index = 0; index < Definitions.Length; index++)
            {
                var definition = Definitions[index];
                _columns[definition.Key].IsVisible = definition.DefaultVisible;
                _columns[definition.Key].Width = new DataGridLength(definition.Width);
                _columns[definition.Key].DisplayIndex = index;
            }
            _loadingLayout = false;
            SaveLayout();
        };
        menu.Items.Add(reset);
        _headerMenu = menu;
        menu.Closed += (_, _) => { if (ReferenceEquals(_headerMenu, menu)) _headerMenu = null; };
        menu.Open(target);
    }

    private void CloseHeaderMenu()
    {
        var menu = _headerMenu;
        _headerMenu = null;
        menu?.Close();
    }

    private void SaveLayout()
    {
        if (_loadingLayout) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_layoutPath)!);
            var layout = _columns.ToDictionary(pair => pair.Key, pair => new ColumnLayout(pair.Value.IsVisible,
                pair.Value.DisplayIndex, pair.Value.ActualWidth >= 48 ? pair.Value.ActualWidth : pair.Value.Width.Value));
            var temporary = _layoutPath + ".tmp";
            File.WriteAllText(temporary, JsonConvert.SerializeObject(layout, Formatting.Indented));
            File.Move(temporary, _layoutPath, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LoggerHelper.Warning("部队状况列设置保存失败：" + exception.GetType().Name);
        }
    }

    private void LoadLayout()
    {
        _loadingLayout = true;
        try
        {
            if (!File.Exists(_layoutPath)) return;
            var settings = JObject.Parse(File.ReadAllText(_layoutPath)).ToObject<Dictionary<string, ColumnLayout>>();
            if (settings == null) return;
            foreach (var item in settings.OrderBy(pair => pair.Value.Order))
                if (_columns.TryGetValue(item.Key, out var column))
                {
                    column.IsVisible = item.Value.Visible;
                    if (double.IsFinite(item.Value.Width) && item.Value.Width >= 48)
                        column.Width = new DataGridLength(Math.Clamp(item.Value.Width, column.MinWidth, 600));
                    column.DisplayIndex = Math.Clamp(item.Value.Order, 0, Definitions.Length - 1);
                }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            LoggerHelper.Warning("部队状况列设置读取失败：" + exception.GetType().Name);
        }
        finally { _loadingLayout = false; }
    }
}
