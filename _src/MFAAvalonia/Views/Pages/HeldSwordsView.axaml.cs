using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.Markup.Xaml.MarkupExtensions;
using MFAAvalonia.Helper;
using MFAAvalonia.Models;
using MFAAvalonia.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MFAAvalonia.Views.Pages;

/// <summary>只读所持刀剑表，管理列显示、顺序、筛选和数值排序。</summary>
public partial class HeldSwordsView : UserControl
{
    private sealed record Column(string Key, string Title, bool DefaultVisible = true, string? Filter = null);
    private static readonly Column[] Definitions =
    [
        new("Protect", "保护", true, "保护"), new("Book", "刀帐"), new("Name", "刀名"),
        new("Level", "等级"), new("Ranbu", "乱舞"), new("Health", "生存"), new("Fatigue", "疲劳"),
        new("hp", "生存"), new("atk", "打击"), new("def", "防御"), new("mobile", "机动"), new("back", "冲力"),
        new("scout", "侦察"), new("hide", "隐蔽"), new("loyalties", "必杀"),
        new("Team", "部队", false, "部队"), new("Serial", "唯一 ID", false), new("Type", "刀种", false, "刀种"),
        new("Troops", "刀装", false), new("Horse", "马匹", false), new("Charm", "御守", false),
        new("Treasure", "宝物", false), new("Appeared", "显现", false)
    ];
    private static readonly string[] TypeOrder = ["短刀", "胁差", "打刀", "太刀", "大太刀", "枪", "薙刀", "剑"];
    private static readonly string[] TeamFilterOrder = ["部队一", "部队二", "部队三", "部队四", "部队五", "未编队"];
    private sealed record ColumnLayout(bool Visible, int Order, double Width);
    private readonly Dictionary<string, DataGridColumn> _columns = new();
    private readonly Dictionary<string, HashSet<string>> _filters = new();
    private List<HeldSwordRow> _rows = [];
    private DataGridCollectionView? _view;
    private bool _experienceMode;
    private bool _loadingLayout;
    private string _signature = "";
    private string _observedAt = "";
    private (Column Column, Control Target, Point Position)? _filterPress;
    private ContextMenu? _headerMenu;
    private readonly string _dataDirectory;
    private string LayoutPath => Path.Combine(_dataDirectory, "held-swords-view.json");

    public HeldSwordsView() : this(Path.Combine(AppPaths.ConfigDirectory, "honmaru", "ui")) { }

    public HeldSwordsView(string dataDirectory)
    {
        _dataDirectory = dataDirectory;
        InitializeComponent();
        BuildColumns();
        SwordGrid.AddHandler(PointerPressedEvent, (_, e) =>
        {
            _filterPress = null;
            if (!e.GetCurrentPoint(SwordGrid).Properties.IsLeftButtonPressed || e.Source is not Visual source) return;
            var header = source as DataGridColumnHeader
                ?? source.GetVisualAncestors().OfType<DataGridColumnHeader>().FirstOrDefault();
            if (header == null) return;
            var definition = Definitions.FirstOrDefault(x => x.Filter != null && ReferenceEquals(_columns[x.Key].Header, header.Content));
            if (definition != null) _filterPress = (definition, header, e.GetPosition(SwordGrid));
        }, RoutingStrategies.Tunnel, true);
        SwordGrid.AddHandler(PointerReleasedEvent, (_, e) =>
        {
            var press = _filterPress;
            _filterPress = null;
            if (press is not { } click || e.InitialPressMouseButton != MouseButton.Left) return;
            var position = e.GetPosition(SwordGrid);
            if (Math.Abs(position.X - click.Position.X) >= 4 || Math.Abs(position.Y - click.Position.Y) >= 4) return;
            Dispatcher.UIThread.Post(() =>
            {
                if (click.Target.IsAttachedToVisualTree()) OpenHeaderMenu(click.Target, () => FilterMenu(click.Column));
            });
        }, RoutingStrategies.Tunnel, true);
        LoadLayout();
        SwordGrid.ColumnReordered += (_, _) => SaveLayout();
        DetachedFromVisualTree += (_, _) => CloseHeaderMenu();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty && !IsVisible) CloseHeaderMenu();
        };
        SwordGrid.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (e.ClickCount != 2 || !e.GetCurrentPoint(SwordGrid).Properties.IsLeftButtonPressed) return;
            var levelCell = (e.Source as Visual)?.GetVisualAncestors().OfType<Border>().FirstOrDefault(x => Equals(x.Tag, "Level"));
            if (levelCell == null) return;
            _experienceMode = !_experienceMode;
            var levelDefinition = Definitions.First(x => x.Key == "Level");
            _columns["Level"].Header = BuildHeader(levelDefinition, _experienceMode ? "累计经验" : "等级");
            if (_experienceMode && _columns["Level"].ActualWidth < 130) _columns["Level"].Width = new DataGridLength(130);
            else if (!_experienceMode && _columns["Level"].Width.Value == 130) _columns["Level"].Width = new DataGridLength(DefaultWidth("Level"));
            RefreshCells();
            e.Handled = true;
        }, RoutingStrategies.Tunnel, true);
    }

    public void ClearSnapshot()
    {
        _signature = "";
        _rows = [];
        _view = null;
        _observedAt = "";
        ToolTip.SetTip(CountText, null);
        SwordGrid.ItemsSource = null;
        CountText.Text = "尚未同步刀剑列表";
    }

    public void SetSnapshot(GameOverviewSnapshot snapshot)
    {
        var signature = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            snapshot.Account + snapshot.HasFullSwordPool + JsonConvert.SerializeObject(snapshot.HeldSwords) + JsonConvert.SerializeObject(snapshot.SwordTeams) + JsonConvert.SerializeObject(snapshot.SwordTeamPositions) + JsonConvert.SerializeObject(snapshot.EquipmentKinds))));
        if (signature == _signature) return;
        _signature = signature;
        _observedAt = snapshot.SwordObservedAt;
        try
        {
            var sort = _view?.SortDescriptions.ToArray();
            var selected = (SwordGrid.SelectedItem as HeldSwordRow)?.Serial;
            _rows = HeldSwordTableData.Build(snapshot, HeldSwordTableData.LoadCatalog());
            _view = new DataGridCollectionView(_rows) { Filter = Match };
            if (sort is { Length: > 0 }) foreach (var item in sort) _view.SortDescriptions.Add(item);
            else _view.SortDescriptions.Add(DataGridSortDescription.FromComparer(_columns["Level"].CustomSortComparer, ListSortDirection.Descending));
            SwordGrid.ItemsSource = _view;
            SwordGrid.SelectedItem = _rows.FirstOrDefault(x => x.Serial == selected);
            UpdateCount();
            ToolTip.SetTip(CountText, "刀剑数据时间：" + _observedAt);
            if (!snapshot.HasFullSwordPool) CountText.Text = "尚无完整刀剑列表，请进入游戏编队页后同步";
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            CountText.Text = "刀剑资料读取失败";
            LoggerHelper.Warning("所持刀剑资料加载失败：" + exception.GetType().Name);
        }
    }

    private void BuildColumns()
    {
        foreach (var definition in Definitions)
        {
            var def = definition;
            var column = new DataGridTemplateColumn
            {
                Header = BuildHeader(def),
                IsVisible = def.DefaultVisible, MinWidth = def.Key == "Protect" ? 72 : 56,
                Width = new DataGridLength(DefaultWidth(def.Key)),
                CanUserSort = def.Filter == null,
                SortMemberPath = def.Key,
                CustomSortComparer = new RowComparer(def.Key),
                CellTemplate = new FuncDataTemplate<HeldSwordRow>((row, _) => BuildCell(row!, def))
            };
            column.HeaderPointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(SwordGrid).Properties.IsRightButtonPressed && e.Source is Control control)
                {
                    OpenHeaderMenu(control, ColumnsMenu);
                    e.Handled = true;
                }
            };
            column.HeaderPointerReleased += (_, e) =>
            {
                SaveLayout();
            };
            _columns[def.Key] = column;
            SwordGrid.Columns.Add(column);
        }
    }

    private Control BuildCell(HeldSwordRow row, Column column)
    {
        if (column.Key == "Troops")
        {
            var slots = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*") };
            for (var i = 0; i < row.Troops.Length; i++)
            {
                var troop = row.Troops[i];
                var label = new TextBlock { TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0), TextTrimming = TextTrimming.CharacterEllipsis };
                label.Inlines ??= new InlineCollection();
                label.Inlines.Add(new Run(troop?.Kind ?? "—"));
                if (troop != null)
                    label.Inlines.Add(BuildRatio(troop.Current, troop.Maximum));
                var slot = new Border { Child = label, Padding = new Thickness(1, 0), BorderThickness = new Thickness(0, 0, i < 2 ? 1 : 0, 0) };
                slot.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("SukiBorderBrush"));
                Grid.SetColumn(slot, i);
                slots.Children.Add(slot);
            }
            return slots;
        }
        if (column.Key == "Protect")
        {
            var protection = row.Number("protect");
            var indicator = new Border
            {
                Background = Brushes.Transparent,
                Padding = new Thickness(1, 2),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            if (protection is 1 or 2)
                indicator.Child = new FluentIcons.Avalonia.Fluent.FluentIcon
                {
                    Icon = protection == 2 ? FluentIcons.Common.Icon.Star : FluentIcons.Common.Icon.LockClosed,
                    IconVariant = FluentIcons.Common.IconVariant.Filled,
                    IconSize = FluentIcons.Common.IconSize.Size20,
                    Foreground = new SolidColorBrush(Color.Parse("#C49A32")),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            ToolTip.SetTip(indicator, row.Protection);
            Avalonia.Automation.AutomationProperties.SetName(indicator, row.Protection);
            return indicator;
        }
        var value = row.SortValue(column.Key, _experienceMode);
        var content = new TextBlock { FontSize = 14, Margin = new Thickness(0), TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        object? toolTip = null;
        content.Text = column.Key switch
        {
            "Protect" => row.Protection,
            "Health" => HeldSwordRow.Text(row.Number("hp")) + "/" + HeldSwordRow.Text(row.Number("hp_max")),
            "Appeared" => row.Data["created_at"]?.ToString() ?? "—",
            _ => value is long number ? HeldSwordRow.Text(number) : value?.ToString() ?? "—"
        };
        if (column.Key is "hp" or "scout" && row.Bonus(column.Key) is { } bonus)
        {
            var main = content.Text;
            content.Text = null;
            content.Inlines ??= new InlineCollection();
            content.Inlines.Add(new Run(main));
            content.Inlines.Add(BuildRatio(bonus.Gained, bonus.Total));
            toolTip = $"内番已提升：{bonus.Gained}\n可提升总量：{bonus.Total}";
        }
        if (column.Key == "Name") toolTip = row.NameTip;
        if (column.Key == "Treasure" && row.Treasure != "—") toolTip = row.Treasure;

        if (column.Key == "Level")
        {
            toolTip = BuildLevelTip(row);
        }
        var border = new Border { Tag = column.Key, Background = Brushes.Transparent, Padding = new Thickness(1, 2), Child = content, HorizontalAlignment = HorizontalAlignment.Stretch };
        ToolTip.SetTip(border, toolTip);
        if (column.Key is "hp" or "atk" or "def" or "mobile" or "back" or "scout" or "hide" or "loyalties" && row.IsMaximum(column.Key))
            border.Bind(Border.BackgroundProperty, new DynamicResourceExtension("SwordMaximumBrush"));
        return border;
    }

    private static Run BuildRatio(object current, object maximum) =>
        new($" ({current}⁄{maximum})") { FontSize = 10, FontStyle = FontStyle.Italic };

    private static StackPanel BuildLevelTip(HeldSwordRow row)
    {
        var tip = new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var line in row.LevelTip.Split('\n'))
            tip.Children.Add(new TextBlock { Text = line, TextAlignment = TextAlignment.Left, HorizontalAlignment = HorizontalAlignment.Left });
        return tip;
    }

    private static Control BuildHeader(Column column, bool isFiltered = false) => BuildHeader(column, column.Title, isFiltered);

    private static Control BuildHeader(Column column, string title, bool isFiltered = false)
    {
        var content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center
        };
        var caption = new TextBlock
        {
            Text = title,
            Margin = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = isFiltered ? FontWeight.SemiBold : FontWeight.Normal
        };
        caption.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension(isFiltered ? "SukiPrimaryColor" : "SukiText"));
        content.Children.Add(caption);
        if (column.Filter != null)
        {
            var filterIcon = new Avalonia.Controls.Shapes.Path
            {
                Width = 8,
                Height = 6,
                Margin = new Thickness(0, 0, 2, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Stretch = Stretch.Fill,
                Data = Geometry.Parse("M 0,0 L 8,0 L 4,5 Z")
            };
            filterIcon.Bind(Avalonia.Controls.Shapes.Path.FillProperty, new DynamicResourceExtension(isFiltered ? "SukiPrimaryColor" : "SukiText"));
            content.Children.Add(filterIcon);
        }
        return content;
    }

    private void RefreshCells()
    {
        if (_view == null) return;
        var selected = (SwordGrid.SelectedItem as HeldSwordRow)?.Serial;
        _view.Refresh();
        SwordGrid.ItemsSource = null;
        SwordGrid.ItemsSource = _view;
        SwordGrid.SelectedItem = _rows.FirstOrDefault(x => x.Serial == selected);
        UpdateCount();
    }

    private bool Match(object item)
    {
        if (item is not HeldSwordRow row) return false;
        var query = SearchBox.Text?.Trim() ?? "";
        if (query.Length > 0 && !row.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            && !row.Serial.Contains(query, StringComparison.Ordinal) && !(row.Number("sword_id")?.ToString().Contains(query, StringComparison.Ordinal) ?? false)) return false;
        foreach (var filter in _filters.Where(x => x.Value.Count > 0))
        {
            if (filter.Key == "Protect")
            {
                var state = row.Number("protect");
                if (!((filter.Value.Contains("无锁") && state == 0) || (filter.Value.Contains("上锁") && state >= 1) || (filter.Value.Contains("喜爱") && state == 2))) return false;
            }
            else if (!filter.Value.Contains(filter.Key == "Team" ? row.TeamFilter : row.Type)) return false;
        }
        return true;
    }

    private void CloseHeaderMenu()
    {
        var previous = _headerMenu;
        _headerMenu = null;
        previous?.Close();
    }

    private void OpenHeaderMenu(Control target, Func<ContextMenu> create)
    {
        // 列设置和筛选共用一个活动菜单，连续点击时先关闭旧弹层。
        CloseHeaderMenu();
        var menu = create();
        _headerMenu = menu;
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_headerMenu, menu)) _headerMenu = null;
        };
        menu.Open(target);
    }

    private ContextMenu FilterMenu(Column column)
    {
        if (!_filters.TryGetValue(column.Key, out var selected)) _filters[column.Key] = selected = [];
        var menu = new ContextMenu();
        var clear = new MenuItem { Header = "清空筛选（显示全部）" };
        clear.Click += (_, _) => { selected.Clear(); RefreshFilter(column); };
        menu.Items.Add(clear);
        var values = column.Key == "Protect" ? new[] { "无锁", "上锁", "喜爱" }
            : column.Key == "Team"
                ? _rows.Select(x => x.TeamFilter).Distinct().OrderBy(x => OrderIndex(TeamFilterOrder, x)).ToArray()
                : _rows.Select(x => x.Type).Distinct().OrderBy(x => Array.IndexOf(TypeOrder, x)).ToArray();
        foreach (var value in values)
        {
            var item = new MenuItem { Header = value, ToggleType = MenuItemToggleType.CheckBox, IsChecked = selected.Contains(value) };
            item.PropertyChanged += (_, e) =>
            {
                if (e.Property != MenuItem.IsCheckedProperty) return;
                if (item.IsChecked) selected.Add(value); else selected.Remove(value);
                RefreshFilter(column);
            };
            menu.Items.Add(item);
        }
        return menu;
    }

    private static int OrderIndex(string[] order, string value)
    {
        var index = Array.IndexOf(order, value);
        return index < 0 ? int.MaxValue : index;
    }

    private void RefreshFilter(Column column)
    {
        _columns[column.Key].Header = BuildHeader(column, _filters[column.Key].Count > 0);
        _view?.Refresh();
        UpdateCount();
    }

    private ContextMenu ColumnsMenu()
    {
        var menu = new ContextMenu();
        var attributes = new MenuItem { Header = "属性数值" };
        foreach (var definition in Definitions)
        {
            var column = _columns[definition.Key];
            var label = definition.Key == "Health" ? "生存（当前／最大）" : definition.Title;
            var item = new MenuItem { Header = label, ToggleType = MenuItemToggleType.CheckBox, IsChecked = column.IsVisible, IsEnabled = definition.Key != "Name" };
            item.PropertyChanged += (_, e) =>
            {
                if (e.Property != MenuItem.IsCheckedProperty) return;
                column.IsVisible = item.IsChecked;
                SaveLayout();
            };
            if (definition.Key is "hp" or "atk" or "def" or "mobile" or "back" or "scout" or "hide" or "loyalties") attributes.Items.Add(item);
            else menu.Items.Add(item);
        }
        menu.Items.Add(attributes);
        var reset = new MenuItem { Header = "恢复默认列和顺序" };
        reset.Click += (_, _) =>
        {
            _loadingLayout = true;
            for (var index = 0; index < Definitions.Length; index++)
            {
                var def = Definitions[index];
                _columns[def.Key].IsVisible = def.DefaultVisible;
                _columns[def.Key].DisplayIndex = index;
            }
            _loadingLayout = false;
            SaveLayout();
        };
        menu.Items.Add(reset);
        return menu;
    }

    private void SearchChanged(object? sender, TextChangedEventArgs e) { _view?.Refresh(); UpdateCount(); }
    private void UpdateCount() => CountText.Text = _view == null ? "尚未同步刀剑列表" : $"{_view.Count} / {_rows.Count} 振";

    private void SaveLayout()
    {
        if (_loadingLayout) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LayoutPath)!);
            var data = _columns.ToDictionary(x => x.Key, x => new ColumnLayout(x.Value.IsVisible, x.Value.DisplayIndex, x.Value.ActualWidth >= 62 ? x.Value.ActualWidth : x.Value.Width.Value));
            var temporary = LayoutPath + ".tmp";
            File.WriteAllText(temporary, JsonConvert.SerializeObject(new { Version = 2, Columns = data }, Formatting.Indented), new UTF8Encoding(false));
            File.Move(temporary, LayoutPath, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { LoggerHelper.Warning("所持刀剑列设置保存失败：" + exception.GetType().Name); }
    }

    private void LoadLayout()
    {
        _loadingLayout = true;
        try
        {
            if (!File.Exists(LayoutPath)) return;
            var document = JObject.Parse(File.ReadAllText(LayoutPath));
            var legacy = document["Columns"] == null;
            var settings = (legacy ? document : document["Columns"])?.ToObject<Dictionary<string, ColumnLayout>>();
            if (settings == null) return;
            foreach (var item in settings.OrderBy(x => x.Value.Order))
                if (_columns.TryGetValue(item.Key, out var column))
                {
                    column.IsVisible = item.Key == "Name" || item.Value.Visible;
                    if (!legacy && double.IsFinite(item.Value.Width) && item.Value.Width >= 56)
                    {
                        column.Width = new DataGridLength(Math.Clamp(item.Value.Width, column.MinWidth, 800));
                    }
                    column.DisplayIndex = Math.Clamp(item.Value.Order, 0, Definitions.Length - 1);
                }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        { LoggerHelper.Warning("所持刀剑列设置加载失败：" + exception.GetType().Name); }
        finally { _loadingLayout = false; }
    }

    private static double DefaultWidth(string key) => key switch
    {
        "Protect" => 72, "Book" => 56, "Name" => 160, "Level" => 60, "Ranbu" => 56,
        "Health" => 68, "Fatigue" => 56, "hp" or "scout" => 92,
        "Troops" => 330, "Horse" or "Charm" => 105, "Treasure" => 130, "Serial" => 130, "Appeared" => 155, "Team" => 80, "Type" => 72,
        _ => 56
    };

    private sealed class RowComparer(string key) : IComparer
    {
        public int Compare(object? x, object? y)
        {
            if (x is not HeldSwordRow a || y is not HeldSwordRow b) return 0;
            var av = a.SortingValue(key);
            var bv = b.SortingValue(key);
            var result = av is long an && bv is long bn ? an.CompareTo(bn) : av == null ? (bv == null ? 0 : -1)
                : bv == null ? 1 : string.Compare(av.ToString(), bv.ToString(), StringComparison.CurrentCulture);
            if (result != 0) return result;
            return string.Compare(a.Serial, b.Serial, StringComparison.Ordinal);
        }
    }
}
