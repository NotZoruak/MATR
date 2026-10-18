using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace MFAAvalonia.ViewModels.Pages;

public partial class DataLookupViewModel : ViewModelBase
{
    // ===== 标签页切换 =====

    [ObservableProperty] private int _selectedTabIndex;

    partial void OnSelectedTabIndexChanged(int value)
    {
        ForgeSearchName = "";
        ForgeSearchCategoryIndex = -1;
        CurrentExp = 0;
        TargetLevel = 1;
        ResultText = "";
    }

    // ===== 锻刀公式 =====

    [ObservableProperty] private string _forgeSearchName = "";
    [ObservableProperty] private int _forgeSearchCategoryIndex = -1;

    public static string[] SwordCategories =>
        ["全部", "短刀", "胁差", "打刀", "太刀", "大太刀", "枪", "薙"];

    public ObservableCollection<ForgingRecipeData> AllForgingRecipes { get; } = [];
    public ObservableCollection<ForgingRecipeData> FilteredForgingRecipes { get; } = [];

    partial void OnForgeSearchNameChanged(string value) => FilterForgingRecipes();
    partial void OnForgeSearchCategoryIndexChanged(int value) => FilterForgingRecipes();

    private void FilterForgingRecipes()
    {
        var filtered = AllForgingRecipes.AsEnumerable();
        if (ForgeSearchCategoryIndex > 0)
            filtered = filtered.Where(r => r.Category == SwordCategories[ForgeSearchCategoryIndex]);
        if (!string.IsNullOrWhiteSpace(ForgeSearchName))
            filtered = filtered.Where(r => r.Name.Contains(ForgeSearchName, StringComparison.OrdinalIgnoreCase));

        FilteredForgingRecipes.Clear();
        foreach (var item in filtered)
            FilteredForgingRecipes.Add(item);
    }

    // ===== 经验表计算器 =====

    [ObservableProperty] private int _selectedSwordTypeIndex;
    [ObservableProperty] private long _currentExp;
    [ObservableProperty] private int _targetLevel = 1;
    [ObservableProperty] private string _resultText = "";

    public ObservableCollection<ExpTableData> ExpTableItems { get; } = [];

    partial void OnSelectedSwordTypeIndexChanged(int value) => RefreshExpTable();

    private void RefreshExpTable()
    {
        var swordType = SwordExpCurves.SwordTypes[SelectedSwordTypeIndex];
        var curve = SwordExpCurves.GetCurve(swordType);
        var maxLv = 199;

        ExpTableItems.Clear();
        for (int lv = 1; lv <= maxLv; lv++)
        {
            var cum = curve.GetValueOrDefault(lv, 0);
            var prev = lv > 1 ? curve.GetValueOrDefault(lv - 1, 0) : 0;
            ExpTableItems.Add(new ExpTableData(lv, cum, cum - prev));
        }
        CalculateExp();
    }

    [RelayCommand]
    private void CalculateExp()
    {
        var target = ExpTableItems.FirstOrDefault(e => e.Level == TargetLevel);
        if (target == null)
        {
            ResultText = "请输入有效的目标等级";
            return;
        }
        var need = target.CumulativeExp - CurrentExp;
        if (need <= 0)
        {
            ResultText = $"当前经验已达到 Lv.{TargetLevel} 的累计经验要求，无需额外经验";
            return;
        }
        ResultText = $"升至 Lv.{TargetLevel} 还需 {need:N0} 经验";
    }

    // ===== 地图收益（静态） =====

    public ObservableCollection<MapRewardData> MapRewardItems { get; } = [];

    // ===== 远征收益 =====

    public ObservableCollection<ExpeditionRewardDisplayItem> ExpeditionRewardDisplayItems { get; } = [];

    public string ExpeditionEfficiencyNote =>
        "收益值下方灰色数字为该资源每小时效率，底色越深表示效率越高";

    // ===== 行选中 =====

    [ObservableProperty] private ExpeditionRewardDisplayItem? _selectedExpeditionItem;

    // ===== 初始化 =====

    protected override void Initialize()
    {
        foreach (var item in ForgingRecipeData.DefaultData)
            AllForgingRecipes.Add(item);
        FilterForgingRecipes();

        RefreshExpTable();

        foreach (var item in MapRewardData.DefaultData)
            MapRewardItems.Add(item);

        BuildExpeditionDisplayItems();
    }

    private void BuildExpeditionDisplayItems()
    {
        var rawItems = new List<(ExpeditionRewardData Data, double Hours, Dictionary<string, double> Eff)>();

        foreach (var d in ExpeditionRewardData.DefaultData)
        {
            var hours = ParseHours(d.Duration);
            var eff = new Dictionary<string, double>
            {
                ["Charcoal"] = hours > 0 ? d.Charcoal / hours : 0,
                ["Steel"] = hours > 0 ? d.Steel / hours : 0,
                ["Coolant"] = hours > 0 ? d.Coolant / hours : 0,
                ["Whetstone"] = hours > 0 ? d.Whetstone / hours : 0,
                ["Total"] = hours > 0 ? (d.Charcoal + d.Steel + d.Coolant + d.Whetstone) / hours : 0,
                ["Gold"] = hours > 0 ? d.SmallGoldChest / hours : 0,
                ["Speed"] = hours > 0 ? d.SpeedTokens / hours : 0,
                ["Ticket"] = hours > 0 ? d.CommissionTickets / hours : 0,
            };
            rawItems.Add((d, hours, eff));
        }

        // 计算每列最大效率
        var columns = new[] { "Charcoal", "Steel", "Coolant", "Whetstone", "Total", "Gold", "Speed", "Ticket" };
        var maxEff = new Dictionary<string, double>();
        foreach (var col in columns)
            maxEff[col] = rawItems.Max(r => r.Eff[col]);

        foreach (var (data, hours, eff) in rawItems)
        {
            ExpeditionRewardDisplayItems.Add(new ExpeditionRewardDisplayItem(
                data.Name, data.Duration, data.Charcoal, data.Steel, data.Coolant, data.Whetstone,
                data.SmallGoldChest, data.SpeedTokens, data.CommissionTickets,
                eff, maxEff));
        }
    }

    private static double ParseHours(string duration)
    {
        if (duration.EndsWith("m"))
            return double.TryParse(duration.TrimEnd('m'), out var m) ? m / 60.0 : 0;
        if (duration.EndsWith("h"))
            return double.TryParse(duration.TrimEnd('h'), out var h) ? h : 0;
        return 0;
    }
}

// ===== 远征收益展示行 =====

public class ExpeditionRewardDisplayItem
{
    private static readonly SolidColorBrush Tier1Bg = new(Color.Parse("#59187000"));
    private static readonly SolidColorBrush Tier2Bg = new(Color.Parse("#5950A000"));
    private static readonly SolidColorBrush Tier3Bg = new(Color.Parse("#59A0D858"));

    public string Name { get; }
    public string Duration { get; }

    public string CharcoalValue { get; }
    public string CharcoalEff { get; }
    public IBrush CharcoalBg { get; }

    public string SteelValue { get; }
    public string SteelEff { get; }
    public IBrush SteelBg { get; }

    public string CoolantValue { get; }
    public string CoolantEff { get; }
    public IBrush CoolantBg { get; }

    public string WhetstoneValue { get; }
    public string WhetstoneEff { get; }
    public IBrush WhetstoneBg { get; }

    public string TotalValue { get; }
    public string TotalEff { get; }
    public IBrush TotalBg { get; }

    public string GoldValue { get; }
    public string GoldEff { get; }
    public IBrush GoldBg { get; }

    public string SpeedValue { get; }
    public string SpeedEff { get; }
    public IBrush SpeedBg { get; }

    public string TicketValue { get; }
    public string TicketEff { get; }
    public IBrush TicketBg { get; }

    public ExpeditionRewardDisplayItem(
        string name, string duration,
        int charcoal, int steel, int coolant, int whetstone,
        int gold, int speed, int ticket,
        Dictionary<string, double> eff, Dictionary<string, double> maxEff)
    {
        Name = name;
        Duration = duration;

        CharcoalValue = charcoal > 0 ? charcoal.ToString() : "—";
        CharcoalEff = charcoal > 0 ? FormatEff(eff["Charcoal"]) : "";
        CharcoalBg = MakeBg(eff["Charcoal"], maxEff["Charcoal"]);

        SteelValue = steel > 0 ? steel.ToString() : "—";
        SteelEff = steel > 0 ? FormatEff(eff["Steel"]) : "";
        SteelBg = MakeBg(eff["Steel"], maxEff["Steel"]);

        CoolantValue = coolant > 0 ? coolant.ToString() : "—";
        CoolantEff = coolant > 0 ? FormatEff(eff["Coolant"]) : "";
        CoolantBg = MakeBg(eff["Coolant"], maxEff["Coolant"]);

        WhetstoneValue = whetstone > 0 ? whetstone.ToString() : "—";
        WhetstoneEff = whetstone > 0 ? FormatEff(eff["Whetstone"]) : "";
        WhetstoneBg = MakeBg(eff["Whetstone"], maxEff["Whetstone"]);

        var total = charcoal + steel + coolant + whetstone;
        TotalValue = total > 0 ? total.ToString() : "—";
        TotalEff = total > 0 ? FormatEff(eff["Total"]) : "";
        TotalBg = MakeBg(eff["Total"], maxEff["Total"]);

        GoldValue = gold > 0 ? gold.ToString() : "—";
        GoldEff = gold > 0 ? FormatEff(eff["Gold"]) : "";
        GoldBg = MakeBg(eff["Gold"], maxEff["Gold"]);

        SpeedValue = speed > 0 ? speed.ToString() : "—";
        SpeedEff = speed > 0 ? FormatEff(eff["Speed"]) : "";
        SpeedBg = MakeBg(eff["Speed"], maxEff["Speed"]);

        TicketValue = ticket > 0 ? ticket.ToString() : "—";
        TicketEff = ticket > 0 ? FormatEff(eff["Ticket"]) : "";
        TicketBg = MakeBg(eff["Ticket"], maxEff["Ticket"]);
    }

    private static string FormatEff(double val)
    {
        if (val < 1 && val > 0) return val.ToString("F2");
        return val.ToString("F0");
    }

    private static IBrush MakeBg(double efficiency, double max)
    {
        if (max <= 0 || efficiency <= 0) return Brushes.Transparent;
        var ratio = efficiency / max;
        if (ratio <= 0.40) return Brushes.Transparent;
        if (ratio <= 0.70) return Tier3Bg;
        if (ratio <= 0.90) return Tier2Bg;
        return Tier1Bg;
    }
}
