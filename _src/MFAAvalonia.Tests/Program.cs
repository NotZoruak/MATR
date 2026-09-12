using MFAAvalonia.Models;
using MFAAvalonia.Services;
using MFAAvalonia.Extensions.MaaFW.Custom;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Configuration;
using MFAAvalonia.Helper;
using MFAAvalonia.ViewModels.Pages;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

var actualWindowSize = WindowSizePersistence.GetValidSize(1366, 768);
var earlyCompletionRequest = new TaskEarlyCompletionRequest();
AssertTrue(earlyCompletionRequest.Request("目标 PT 已达成"), "首次提前结束请求必须成功");
AssertTrue(earlyCompletionRequest.TryConsume(out var earlyCompletionReason)
    && earlyCompletionReason == "目标 PT 已达成", "提前结束请求必须返回原始原因");
AssertFalse(earlyCompletionRequest.TryConsume(out _), "同一提前结束请求不得被重复消费");
var normalIteration = TaskIterationDecision.Resolve(new TaskEarlyCompletionRequest());
AssertTrue(normalIteration.ShouldCountIteration && normalIteration.ShouldContinueRepeating,
    "没有提前结束请求时必须计入当前轮并继续重复");
var terminatingRequest = new TaskEarlyCompletionRequest();
terminatingRequest.Request("门票不足");
var terminatingIteration = TaskIterationDecision.Resolve(terminatingRequest);
AssertFalse(terminatingIteration.ShouldCountIteration || terminatingIteration.ShouldContinueRepeating,
    "提前结束请求不得计入当前轮且必须停止重复");
AssertTrue(terminatingIteration.Reason == "门票不足", "提前结束决策必须保留结束原因");

// 计划日以本地时间每日 05:00 为界
AssertTrue(ActivityGoalPtPlanner.GetPlanDay(new DateTime(2026, 9, 11, 4, 59, 0)) == new DateOnly(2026, 9, 10),
    "04:59 必须视为前一个计划日");
AssertTrue(ActivityGoalPtPlanner.GetPlanDay(new DateTime(2026, 9, 11, 5, 0, 0)) == new DateOnly(2026, 9, 11),
    "05:00 起必须视为新的计划日");

// 计划 9 月 10 日至 9 月 19 日，总目标 30000，按日线性推进
var goalStart = new DateOnly(2026, 9, 10);
var goalEnd = new DateOnly(2026, 9, 19);
AssertTrue(ActivityGoalPtPlanner.GetAbsoluteGoal(30000, goalStart, goalEnd, new DateOnly(2026, 9, 10)) == 3000,
    "计划首日的绝对目标应为总目标的十分之一");
AssertTrue(ActivityGoalPtPlanner.GetAbsoluteGoal(30000, goalStart, goalEnd, new DateOnly(2026, 9, 11)) == 6000,
    "计划次日的绝对目标应为总目标的十分之二");
AssertTrue(ActivityGoalPtPlanner.GetAbsoluteGoal(30000, goalStart, goalEnd, new DateOnly(2026, 9, 12)) == 9000,
    "计划第三日的绝对目标应为总目标的十分之三");
AssertTrue(ActivityGoalPtPlanner.GetAbsoluteGoal(30000, goalStart, goalEnd, goalEnd) == 30000,
    "到达截至日时绝对目标必须固定为总目标");
AssertTrue(ActivityGoalPtPlanner.GetAbsoluteGoal(30000, goalStart, goalEnd, new DateOnly(2026, 9, 25)) == 30000,
    "超过截至日时绝对目标仍为总目标");
AssertTrue(ActivityGoalPtPlanner.GetAbsoluteGoal(30000, goalStart, goalEnd, new DateOnly(2026, 9, 9)) == null,
    "计划尚未开始时不得计算绝对目标");
AssertTrue(ActivityGoalPtPlanner.GetAbsoluteGoal(30000, goalStart, goalEnd, new DateOnly(2026, 9, 13)) == 12000,
    "总天数不能整除时绝对目标应按向上取整推进");

// 无法整除时验证向上取整，避免目标被低估
AssertTrue(ActivityGoalPtPlanner.GetAbsoluteGoal(10000, goalStart, goalEnd, new DateOnly(2026, 9, 10)) == 1000,
    "整除场景应精确按比例分配");
AssertTrue(ActivityGoalPtPlanner.GetAbsoluteGoal(7, goalStart, new DateOnly(2026, 9, 13), new DateOnly(2026, 9, 10)) == 2,
    "无法整除时必须向上取整");

// 无效配置不得参与计算
AssertTrue(ActivityGoalPtPlanner.GetAbsoluteGoal(0, goalStart, goalEnd, new DateOnly(2026, 9, 11)) == null,
    "目标总 PT 非正数时必须拒绝计算");
AssertTrue(ActivityGoalPtPlanner.GetAbsoluteGoal(30000, goalEnd, goalStart, new DateOnly(2026, 9, 11)) == null,
    "开始日期晚于截至日期时必须拒绝计算");

// 达标判断：PT 识别失败或目标不可计算时绝不判定达标
AssertTrue(ActivityGoalPtPlanner.IsGoalReached(6000, 6000), "PT 等于绝对目标时必须判定达标");
AssertTrue(ActivityGoalPtPlanner.IsGoalReached(7000, 6000), "PT 超过绝对目标时必须判定达标");
AssertFalse(ActivityGoalPtPlanner.IsGoalReached(5999, 6000), "PT 低于绝对目标时不得判定达标");
AssertFalse(ActivityGoalPtPlanner.IsGoalReached(null, 6000), "PT 识别失败时不得判定达标");
AssertFalse(ActivityGoalPtPlanner.IsGoalReached(6000, null), "绝对目标不可计算时不得判定达标");
AssertFalse(ActivityGoalPtPlanner.IsGoalReached(null, null), "两者都不可用时不得判定达标");

var interfaceDefinition = JObject.Parse(File.ReadAllText(Path.Combine(
    Directory.GetCurrentDirectory(), "assets", "interface.json")));
var sortieDefinition = JObject.Parse(File.ReadAllText(Path.Combine(
    Directory.GetCurrentDirectory(), "assets", "resource", "base", "pipeline", "Sortie.json")));
var isekaiBuyTicketOption = interfaceDefinition["option"]?["异去_购买门票"];
var isekaiBuyTicketOverride = isekaiBuyTicketOption?["cases"]?.FirstOrDefault()?["pipeline_override"]?["S_IsIsekaiNoTicket"];
AssertTrue(isekaiBuyTicketOverride?["next"]?.Values<string>()
        .SequenceEqual(["S_IsIsekaiConfirmPurchase", "S_IsIsekaiNoTicket"]) == true
    && sortieDefinition["S_IsIsekaiNoTicket"]?["next"]?.Values<string>()
        .SequenceEqual(["S_CompleteCurrentTaskAfterIsekaiNoTicket"]) == true,
    "勾选异去购买门票时必须进入补票链路，未勾选时因无票结束");
var pastMode = interfaceDefinition["option"]?["过去/异去"]?["cases"]?
    .FirstOrDefault(item => item?["name"]?.Value<string>() == "过去");
var avoidKebiOption = interfaceDefinition["option"]?["S_避战检非"];
AssertTrue(pastMode?["option"]?.Values<string>().Contains("S_避战检非") == true
    && avoidKebiOption?["type"]?.Value<string>() == "checkbox"
    && avoidKebiOption?["default_case"]?.Values<string>().Any() == false,
    "过去模式必须提供默认关闭的避战检非选项");
var avoidKebiCase = avoidKebiOption?["cases"]?.FirstOrDefault();
var avoidKebiOverrides = avoidKebiCase?["pipeline_override"];
var avoidKebiNodes = new[] { "S_IsKebiishi1", "S_IsKebiishi2", "S_PostKebiishi1", "S_PostKebiishi2" };
AssertTrue(avoidKebiNodes.All(nodeName =>
        avoidKebiOverrides?[nodeName]?["action"]?["custom_action"]?.Value<string>() == "GuiLogAction"
        && avoidKebiOverrides?[nodeName]?["action"]?["custom_action_param"]?["message"]?.Value<string>() == "warn:[重启游戏] 遭遇检非"
        && avoidKebiOverrides?[nodeName]?["next"]?.Values<string>().SequenceEqual(["S_AvoidKebiRestart"]) == true)
    && avoidKebiOverrides?["S_IsKebiishi2"]?["repeat"]?.Value<int>() == 1
    && avoidKebiOverrides?["S_PostKebiishi2"]?["repeat"]?.Value<int>() == 1,
    "避战检非必须覆盖全部四个检非识别，并分别写入警告日志后进入一次性重启链");
AssertTrue(sortieDefinition["S_AvoidKebiRestart"]?["action"]?["custom_action"]?.Value<string>() == "RestartGameAction"
    && sortieDefinition["S_AvoidKebiRestart"]?["action"]?["custom_action_param"]?["log_auto_recovery"]?.Value<bool>() == false
    && sortieDefinition["S_AvoidKebiRestart"]?["next"]?.Values<string>().SequenceEqual(["S_DetectWhereAmI"]) == true,
    "避战检非重启必须不写入卡死恢复日志并回到主枢纽");
var resourcePointLogActionSource = File.ReadAllText(Path.Combine(
    Directory.GetCurrentDirectory(), "assets", "resource", "base", "custom", "ResourcePointLogAction.cs"));
AssertTrue(
    resourcePointLogActionSource.IndexOf("LogGained(prefix, lastVisibleText);", StringComparison.Ordinal)
        > resourcePointLogActionSource.IndexOf("while ((System.DateTime.UtcNow - startTime).TotalMilliseconds < timeout)", StringComparison.Ordinal),
    "资源点奖励必须在提示消失后按最后一次 OCR 结果记录，不能采用动画首帧的截断数量");
var hanapaiDefinitionPath = Path.Combine(Directory.GetCurrentDirectory(), "assets", "resource", "base", "pipeline", "Hanapai.json");
AssertTrue(File.Exists(hanapaiDefinitionPath), "秘宝之里必须提供独立的流程定义");
var hanapaiDefinition = JObject.Parse(File.ReadAllText(hanapaiDefinitionPath));
var hanapaiTask = interfaceDefinition["task"]?.FirstOrDefault(item => item?["entry"]?.Value<string>() == "Hanapai");
AssertTrue(hanapaiTask?["option"]?.Values<string>().SequenceEqual([
        "HP_目标PT", "HP_选择部队", "HP_选择难度", "HP_疲劳处理", "HP_换队长", "HP_购买门票", "HP_同步远征"
    ]) == true,
    "秘宝之里上线后必须注册完整的任务选项");
AssertTrue(hanapaiDefinition["HP_DetectWhereAmI"]?["on_error"]?.Values<string>().SequenceEqual(["HP_RestartGame"]) == true,
    "秘宝之里的状态识别超时必须进入卡死重启链路");
AssertTrue(hanapaiDefinition["HP_ClickMarching"]?["action"]?["custom_action_param"]?["message"]?.Value<string>() == "[秘宝之里] 点击行军",
    "秘宝之里的行军必须写入工作记录词表");
AssertTrue(hanapaiDefinition["HP_IsEventPage"]?["recognition"]?["param"]?["template"]?.Value<string>() == "Activity/秘宝之里.png",
    "秘宝之里活动页必须使用专用活动标识图片");
AssertFalse(hanapaiDefinition.Properties().Any(property => property.Name is "HP_IsActionSelect"
    or "HP_ActionSelectHub" or "HP_IsTeamSwitchNeeded" or "HP_ClickSwitchTeam"
    or "HP_RunTeamSwitch" or "HP_ClickContinueBattle"),
    "秘宝之里不得保留联队战的行动选择或部队交替流程");
AssertTrue(hanapaiDefinition["HP_IsSpecialDrop"]?["recognition"]?["type"]?.Value<string>() == "TemplateMatch"
    && hanapaiDefinition["HP_IsSpecialDrop"]?["recognition"]?["param"]?["roi"]?.Values<int>()
        .SequenceEqual([169, 154, 117, 120]) == true
    && hanapaiDefinition["HP_IsSpecialDrop"]?["recognition"]?["param"]?["template"]?.Value<string>()
        == "Activity/秘宝之里_达成报酬.png",
    "花牌特殊掉落必须按达成报酬模板在指定 ROI 识别");
AssertTrue(hanapaiDefinition["HP_IsSpecialDrop"]?["next"]?.Values<string>()
        .SequenceEqual(["HP_CheckMoreSpecialDrops"]) == true
    && hanapaiDefinition["HP_CheckMoreSpecialDrops"]?["timeout"]?.Value<int>() == 10000
    && hanapaiDefinition["HP_CheckMoreSpecialDrops"]?["on_error"]?.Values<string>()
        .SequenceEqual(["HP_DetectWhereAmI"]) == true,
    "花牌特殊掉落点掉后必须进入复核 hub，超时回到主枢纽");
AssertTrue(hanapaiDefinition["HP_DetectWhereAmI"]?["next"]?.Values<string>().Contains("HP_IsSpecialDrop") == true,
    "花牌特殊掉落识别必须挂在主枢纽的识别顺序中");
AssertTrue(hanapaiDefinition["HP_TerminateRound"]?["action"]?["custom_action_param"]?["message"]?.Value<string>()
        == "[秘宝之里] 完成一圈"
    && hanapaiDefinition["HP_TerminateRound"]?["next"]?.Values<string>().Count() == 0
    && hanapaiDefinition["HP_RoundComplete"] == null,
    "花牌一圈结束时必须由 HP_TerminateRound 写入完成一圈记录，且不保留重复的打点 node");
AssertTrue(hanapaiDefinition["HP_PostSortieHub"]?["timeout"]?.Value<int>() == 30000
    && hanapaiDefinition["HP_PostSortieHub"]?["next"]?.Values<string>()
        .SequenceEqual(["HP_IsPreDamage", "HP_CheckEquipmentPopup", "HP_IsTicketConfirm", "HP_IsNoTicketPopup"]) == true
    && hanapaiDefinition["HP_PostSortieHub"]?["on_error"]?.Values<string>()
        .SequenceEqual(["HP_DetectWhereAmI"]) == true,
    "花牌即刻出阵后必须平级扫描重伤、刀装弹窗、门票确认页与门票不足弹窗");
AssertTrue(hanapaiDefinition["HP_IsTicketConfirm"]?["next"]?.Values<string>()
        .SequenceEqual(["HP_SortieSuccess", "HP_IsTicketConfirm"]) == true,
    "花牌确认使用门票后必须复核是否回到活动页，未离开则再次确认");
AssertTrue(hanapaiDefinition["HP_SortieSuccess"]?["recognition"]?["type"]?.Value<string>() == "TemplateMatch"
    && hanapaiDefinition["HP_SortieSuccess"]?["recognition"]?["param"]?["template"]?.Value<string>()
        == "Activity/秘宝之里.png"
    && hanapaiDefinition["HP_SortieSuccess"]?["action"]?["custom_action_param"]?["message"]?.Value<string>()
        == "[秘宝之里] 出阵",
    "花牌出阵记录必须按活动页模板识别，并在回到活动页时写入出阵");
AssertTrue(hanapaiTask?["option"]?.Values<string>()
        .Any(name => name is "HP_补充刀装" or "HP_刀装保护" or "HP_疲劳撤退") == false,
    "花牌重伤修刀与补充刀装默认开启，不得注册界面选项");
AssertTrue(hanapaiDefinition["HP_IsPreDamage"]?["recognition"]?["param"]?["template"]?.Value<string>()
        == "Common/重伤.png"
    && hanapaiDefinition["HP_IsPreDamage"]?["enabled"] == null
    && hanapaiDefinition["HP_IsPreDamage"]?["next"]?.Values<string>()
        .SequenceEqual(["HP_NavigateToRepair"]) == true,
    "花牌重伤检测必须默认开启并进入修刀导航，不提供界面选项");
AssertTrue(hanapaiDefinition["HP_CheckEquipmentPopup"]?["recognition"]?["param"]?["template"]?.Value<string>()
        == "Common/刀装不足.png"
    && hanapaiDefinition["HP_CheckEquipmentPopup"]?["enabled"] == null
    && hanapaiDefinition["HP_CheckEquipmentPopup"]?["next"]?.Values<string>()
        .SequenceEqual(["HP_PreCheckTroopRecord"]) == true,
    "花牌刀装不足弹窗必须默认开启并进入补充刀装链路，不提供界面选项");
AssertTrue(hanapaiDefinition["HP_PreConfirmSupply"]?["action"]?["custom_action_param"]?["message"]?.Value<string>()
        == "[秘宝之里] 补充刀装"
    && hanapaiDefinition["HP_GuiSupplyLog"]?["next"]?.Values<string>()
        .SequenceEqual(["HP_IsPreSortieConfirm"]) == true,
    "花牌补充刀装必须写入工作记录并回到出阵确认");
AssertTrue(hanapaiDefinition["HP_ClickEnterBattle"] == null,
    "花牌不得保留联队战的进入战斗 node");
AssertTrue(hanapaiDefinition["HP_IsTicketConfirm"]?["recognition"]?["param"]?["expected"]?.Value<string>()
        == "出阵",
    "花牌门票确认页必须按出阵文案识别，与异去保持一致");
AssertTrue(hanapaiDefinition["HP_IsNoTicketPopup"]?["recognition"]?["param"]?["expected"]?.Value<string>()
        == "通行令牌不足"
    && hanapaiDefinition["HP_IsNoTicketPopup"]?["recognition"]?["param"]?["roi"]?.Values<int>()
        .SequenceEqual([526, 202, 231, 56]) == true
    && hanapaiDefinition["HP_IsNoTicketPopup"]?["action"]?["param"]?["target"]?.Values<int>()
        .SequenceEqual([750, 448, 75, 40]) == true
    && hanapaiDefinition["HP_IsNoTicketPopup"]?["next"]?.Values<string>()
        .SequenceEqual(["HP_CompleteCurrentTaskAfterNoTicket", "HP_IsNoTicket"]) == true,
    "花牌门票不足的第一级弹窗必须先尝试结束任务，未命中时进入第二级确认");
AssertTrue(hanapaiDefinition["HP_IsNoTicket"]?["recognition"]?["param"]?["expected"]?.Value<string>()
        == "通行令牌"
    && hanapaiDefinition["HP_IsNoTicket"]?["action"]?["param"]?["target"]?.Values<int>()
        .SequenceEqual([792, 509, 1, 3]) == true
    && hanapaiDefinition["HP_IsNoTicket"]?["next"]?.Values<string>()
        .SequenceEqual(["HP_ClickTicketConfirm"]) == true,
    "花牌门票不足的第二级必须进入购买确认链路");
AssertTrue(hanapaiDefinition["HP_ClickTicketConfirm"]?["action"]?["param"]?["target"]?.Values<int>()
        .SequenceEqual([604, 588, 66, 42]) == true
    && hanapaiDefinition["HP_ClickTicketConfirm"]?["next"]?.Values<string>()
        .SequenceEqual(["HP_IsConfirmPurchase"]) == true,
    "花牌门票不足后必须点击补票入口并进入购买页");
AssertTrue(hanapaiDefinition["HP_IsConfirmPurchase"]?["recognition"]?["param"]?["expected"]?.Value<string>()
        == "确认"
    && hanapaiDefinition["HP_IsConfirmPurchase"]?["action"]?["custom_action_param"]?["message"]?.Value<string>()
        == "[秘宝之里] 购买门票",
    "花牌开启购买门票后必须写入购买记录");
AssertTrue(hanapaiDefinition.Properties().All(property => property.Name is not (
        "HP_DetectTicket" or "HP_HandleNoTicket" or "HP_ReplenishOrTerminate" or "HP_ReplenishTicket"
        or "HP_ClickRestore3" or "HP_ClickReplenishConfirm" or "HP_AfterCaptain"
        or "HP_TerminateNoTicket" or "HP_IsTicketPopup" or "HP_HandleTicketPopup"
        or "HP_IsReplenishDone" or "HP_CloseTicketPopup" or "HP_ConfirmEnterMap")),
    "花牌不得保留联队战式的门票购买链路");
AssertTrue(hanapaiDefinition["HP_DetectWhereAmI"]?["next"]?.Values<string>()
        .Any(nodeName => nodeName is "HP_IsTicketPopup" or "HP_IsReplenishDone") == false,
    "花牌主枢纽不得挂载联队战式的门票弹窗识别");
var hanapaiBuyTicketOverride =
    interfaceDefinition["option"]?["HP_购买门票"]?["cases"]?.Children<JObject>().Single()["pipeline_override"];
AssertTrue(hanapaiBuyTicketOverride?["HP_IsNoTicketPopup"]?["action"]?["param"]?["target"]?.Values<int>()
        .SequenceEqual([452, 452, 88, 34]) == true
    && hanapaiBuyTicketOverride?["HP_IsNoTicketPopup"]?["next"]?.Values<string>()
        .SequenceEqual(["HP_IsNoTicket"]) == true
    && hanapaiBuyTicketOverride?["HP_CompleteCurrentTaskAfterNoTicket"]?["enabled"]?.Value<bool>() == false
    && hanapaiDefinition["HP_IsNoTicket"]?["next"]?.Values<string>()
        .SequenceEqual(["HP_ClickTicketConfirm"]) == true
    && interfaceDefinition["option"]?["HP_购买门票"]?["default_case"]?.Values<string>().Any() == false
    && hanapaiDefinition["HP_CompleteCurrentTaskAfterNoTicket"]?["action"]?["custom_action"]?.Value<string>()
        == "CompleteCurrentTaskAction",
    "勾选花牌购买门票时必须覆盖两级弹窗、关闭无票结束分支并进入购买确认链路");
var hanapaiTeamSelectNext = hanapaiDefinition["HP_IsTeamSelect"]?["next"]?.Values<string>().ToList();
AssertTrue(hanapaiTeamSelectNext?.SequenceEqual([
        "HP_EnableAutoMarch", "HP_ClickTeam"
    ]) == true
    && hanapaiDefinition["HP_EnableAutoMarch"]?["next"]?.Values<string>()
        .SequenceEqual(["HP_ClickAutoMarchNoOrYesDelegate"]) == true,
    "秘宝之里进入部队选择后必须先完成自动行军状态切换，再点击部队");
AssertTrue(hanapaiDefinition["HP_DisableAutoMarch"] == null
    && hanapaiDefinition["HP_EnableAutoMarch"]?["recognition"]?["param"]?["roi"]?.Values<int>()
        .SequenceEqual([1176, 390, 1, 2]) == true
    && hanapaiDefinition["HP_EnableAutoMarch"]?["action"]?["param"]?["target"]?.Values<int>()
        .SequenceEqual([1176, 390, 1, 2]) == true,
    "秘宝之里只前置开启自动行军并进入委托确认，不保留双向切换识别");
AssertTrue(hanapaiDefinition["HP_DisableAutoMarch"]?["enabled"] == null
    && hanapaiDefinition["HP_EnableAutoMarch"]?["enabled"] == null,
    "秘宝之里的自动行军固定开启，不得依赖界面选项覆盖启用状态");
AssertTrue(hanapaiDefinition["HP_ClickAutoMarchNoOrYesDelegate"]?["recognition"]?["param"]?["expected"]?
        .Values<string>().SequenceEqual(["委托", "不委托"]) == true
    && hanapaiDefinition["HP_ReturnFromAutoMarch"]?["next"]?.Values<string>()
        .SequenceEqual(["HP_IsTeamSelect"]) == true,
    "秘宝之里的自动行军确认必须同时识别委托与不委托，确认后回到部队选择复核");
AssertTrue(hanapaiTask?["option"]?.Values<string>().Contains("HP_自动行军") == false,
    "秘宝之里的自动行军固定开启，不得注册界面选项");
AssertTrue(CaptainSettingsDecision.GetDragNodeName("Hanapai") == "HP_DragCaptain"
    && CaptainSettingsDecision.GetSkipOptionName("Hanapai") == "HP_跳过位置",
    "秘宝之里的换队长必须支持任务专属跳过位置");
AssertTrue(hanapaiDefinition["HP_FatigueCheck"]?["action"]?["custom_action"]?.Value<string>() == "FatigueCheckAction"
    && hanapaiDefinition["HP_FatigueCheck"]?["next"]?.Values<string>().SequenceEqual(["HP_IsPreSortieConfirm"]) == true,
    "秘宝之里必须提供出阵前疲劳处理流程");
var hanapaiRecords = WorkRecordBuilder.Build([
    new LogEntry(DateTime.Now, "INF", "开始任务：花牌"),
    new LogEntry(DateTime.Now.AddSeconds(1), "INF", "[秘宝之里] 出阵"),
    new LogEntry(DateTime.Now.AddSeconds(2), "INF", "[秘宝之里] 点击行军"),
    new LogEntry(DateTime.Now.AddSeconds(3), "INF", "[秘宝之里] 完成一圈"),
    new LogEntry(DateTime.Now.AddSeconds(4), "INF", "停止前状态：SUCCEEDED"),
]);
AssertTrue(hanapaiRecords.Count == 1
    && hanapaiRecords[0].SortieCount == 1
    && hanapaiRecords[0].MarchCount == 1
    && hanapaiRecords[0].RoundCount == 1,
    "工作记录必须解析秘宝之里的出阵、行军和完成圈数日志");
var wakeHomeDefinitionPath = Path.Combine(Directory.GetCurrentDirectory(), "assets", "resource", "base", "pipeline", "WakeHome.json");
AssertTrue(File.Exists(wakeHomeDefinitionPath), "唤醒本丸任务必须提供独立的流程定义");
var wakeHomeDefinition = JObject.Parse(File.ReadAllText(wakeHomeDefinitionPath));
AssertTrue(wakeHomeDefinition["WakeHome"]?["next"]?.Values<string>().SequenceEqual(["WH_RestartGame"]) == true,
    "唤醒本丸必须先关闭并重新启动游戏");
AssertTrue(wakeHomeDefinition["WH_RestartGame"]?["action"]?["custom_action_param"]?["log_auto_recovery"]?.Value<bool>() == false,
    "唤醒本丸重启游戏时不得输出卡死重启日志");
var wakeHomeHub = wakeHomeDefinition["WH_MainHub"]?["next"]?.Values<string>().ToList() ?? [];
AssertTrue(wakeHomeHub.Contains("[JumpBack]WH_HandleExperience")
    && wakeHomeHub.Contains("[JumpBack]WH_HandleTrainingApplication")
    && wakeHomeHub.Contains("[JumpBack]WH_HandleConnectionInterrupted")
    && wakeHomeHub.Contains("WH_HandleLoginReward"),
    "主枢纽必须覆盖启动阶段的已知弹窗处理项");
AssertTrue(wakeHomeDefinition["WH_HandleLoginReward"]?["next"]?.Values<string>()
        .SequenceEqual(["WH_LoginRewardClick2"]) == true
    && wakeHomeDefinition["WH_LoginRewardClick3"]?["next"]?.Values<string>()
        .SequenceEqual(["WH_MainHub"]) == true,
    "登录奖励处理完成后必须返回主枢纽");
AssertTrue(wakeHomeDefinition["WH_CheckIsHome"]?["next"] == null
    && wakeHomeDefinition["WH_CheckIsHome"]?["on_error"]?.Values<string>().SequenceEqual(["WH_MainHub"]) == true,
    "本丸识别成功后必须结束任务，未命中时返回主枢纽");
AssertTrue(interfaceDefinition["task"]?.Any(item => item?["name"]?.Value<string>() == "唤醒本丸"
    && item?["entry"]?.Value<string>() == "WakeHome") == true,
    "资源接口必须注册唤醒本丸任务入口");
var restartEnabledCase = interfaceDefinition["option"]?["卡死重启"]?["cases"]?
    .FirstOrDefault(item => item?["name"]?.Value<string>() == "Yes");
AssertFalse(restartEnabledCase?["option"]?.Values<string>().Contains("卡死等待时间") == true,
    "卡死等待时间不应在常规界面中作为卡死重启的附属选项显示");
AssertTrue(interfaceDefinition["option"]?["卡死等待时间"]?["inputs"]?[0]?["default"]?.Value<string>() == "120",
    "隐藏界面入口后仍必须保留卡死等待时间的 120 秒默认值");
AssertTrue(ClientPackageSettings.ResolvePackageName(ClientPackageType.Official, "com.example.other") == "com.youzu.djlw",
    "选择官服时必须始终使用官服包名，不能误用手动输入");
AssertTrue(ClientPackageSettings.ResolvePackageName(ClientPackageType.Other, "  com.example.other  ") == "com.example.other",
    "选择其它客户端时必须读取并去除手动包名两端空白");
AssertTrue(ClientPackageSettings.ResolvePackageName(ClientPackageType.Other, "") == "com.youzu.djlw",
    "其它客户端未填写包名时必须安全回退官服包名");
var migratedLegacyPackage = ClientPackageSettings.MigrateLegacyPackageName("com.example.legacy");
AssertTrue(migratedLegacyPackage.Type == ClientPackageType.Other && migratedLegacyPackage.CustomPackageName == "com.example.legacy",
    "旧目标应用中的自定义包名必须迁移为其它客户端配置");
var migratedOfficialPackage = ClientPackageSettings.MigrateLegacyPackageName("com.youzu.djlw");
AssertTrue(migratedOfficialPackage.Type == ClientPackageType.Official && migratedOfficialPackage.CustomPackageName == null,
    "旧目标应用中的官服默认包名必须迁移为官服配置");
var wakeHomeRecords = WorkRecordBuilder.Build([
    new LogEntry(DateTime.Now, "INF", "开始任务：唤醒本丸"),
    new LogEntry(DateTime.Now.AddSeconds(1), "INF", "停止前状态：SUCCEEDED"),
]);
AssertTrue(wakeHomeRecords.Count == 0, "唤醒本丸不应生成工作记录");
var recoveryMonitor = new TaskRecoveryMonitor();
var recoveryTimeout = TimeSpan.FromSeconds(120);
recoveryMonitor.Start(TimeSpan.Zero);
AssertTrue(recoveryMonitor.GetReason(TimeSpan.FromSeconds(119), recoveryTimeout, true, false) == null,
    "静默未达到阈值不能触发恢复");
AssertTrue(recoveryMonitor.GetReason(recoveryTimeout, recoveryTimeout, true, false) != null,
    "底层一直没有回调时必须独立触发恢复");
AssertTrue(recoveryMonitor.GetReason(TimeSpan.FromSeconds(130), recoveryTimeout, false, false) == null,
    "关闭卡死重启时不能恢复");
AssertTrue(recoveryMonitor.GetReason(TimeSpan.FromSeconds(300), recoveryTimeout, true, true) == null,
    "智能等待和人工弹窗期间不能恢复");
AssertTrue(recoveryMonitor.GetReason(TimeSpan.FromSeconds(301), recoveryTimeout, true, false) == null,
    "合法等待结束后必须重新计算静默时间");
recoveryMonitor.RecordCallback(TimeSpan.FromSeconds(410));
AssertTrue(recoveryMonitor.GetReason(TimeSpan.FromSeconds(420), recoveryTimeout, true, false) == null,
    "收到回调后应重置静默计时");
for (var i = 0; i < 110; i++) recoveryMonitor.FeedAction("测试", "Click", 1, 2);
AssertTrue(recoveryMonitor.GetReason(TimeSpan.FromSeconds(421), recoveryTimeout, true, false) != null,
    "持续收到相同点击也必须识别画面冻结");
recoveryMonitor.Stop();
AssertTrue(recoveryMonitor.GetReason(TimeSpan.FromSeconds(999), recoveryTimeout, true, false) == null,
    "任务结束或停止后不能触发恢复");
recoveryMonitor.Start(TimeSpan.Zero);
for (var i = 0; i < 110; i++) recoveryMonitor.FeedAction("测试", "DoNothing", 1, 2);
AssertTrue(recoveryMonitor.GetReason(TimeSpan.FromSeconds(1), recoveryTimeout, true, false) == null,
    "空动作不能作为画面冻结证据");
for (var i = 0; i < 200; i++) recoveryMonitor.FeedAction("测试", "Click", i % 2, 2);
AssertTrue(recoveryMonitor.GetReason(TimeSpan.FromSeconds(2), recoveryTimeout, true, false) == null,
    "点击位置发生变化时应放弃冻结判定");
var otherRecoveryMonitor = new TaskRecoveryMonitor();
otherRecoveryMonitor.Start(TimeSpan.Zero);
otherRecoveryMonitor.RecordCallback(TimeSpan.FromSeconds(119));
AssertTrue(recoveryMonitor.GetReason(recoveryTimeout, recoveryTimeout, true, false) != null,
    "其他实例的回调不能重置当前实例的静默计时");
AssertTrue(TaskRecoveryMonitor.ShouldStartEmulator(false, true, false), "只开 ADB 重启也应允许按已配置路径启动模拟器");
AssertTrue(TaskRecoveryMonitor.ShouldStartEmulator(false, false, true), "只开强制 ADB 重启也应允许启动模拟器");
AssertFalse(TaskRecoveryMonitor.ShouldStartEmulator(false, false, false), "关闭所有恢复选项时不能自动启动模拟器");
AssertTrue(actualWindowSize is { Width: 1366, Height: 768 },
    "窗口保存应使用用户拖拽后的实际客户区尺寸");
AssertTrue(TaskQueueContinuationPolicy.CanContinue(true, true),
    "失败且启用继续时应继续执行队列");
AssertFalse(TaskQueueContinuationPolicy.CanContinue(true, false),
    "失败但未启用继续时应停止队列");
AssertFalse(TaskQueueContinuationPolicy.CanContinue(false, true),
    "非失败状态不应因继续选项而推进队列");
AssertTrue(TaskQueueContinuationPolicy.ShouldInsertGoHome("Sortie"),
    "普通任务之间应插入回本丸");
AssertFalse(TaskQueueContinuationPolicy.ShouldInsertGoHome("CountdownAction"),
    "MFAA 倒计时特殊任务前不应插入回本丸");
AssertFalse(TaskQueueContinuationPolicy.ShouldInsertGoHome("WebhookAction"),
    "MFAA Webhook 特殊任务前不应插入回本丸");
AssertFalse(TaskQueueContinuationPolicy.ShouldInsertGoHome(null),
    "没有下一个任务时不应插入回本丸");
AssertFalse(FormationNameMatcher.IsExactMatch("次郎太刀", "太郎太刀"),
    "刀剑一字之差不得命中，避免次郎太刀被误选");
AssertTrue(FormationNameMatcher.IsExactMatch("太郎太刀", "太郎太刀"),
    "刀剑同名必须命中");
AssertTrue(FormationNameMatcher.IsExactMatch("05 太郎太刀", "太郎太刀"),
    "刀剑带序号前缀应命中");
AssertTrue(FormationNameMatcher.IsExactMatch("鶴丸国永", "鹤丸国永"),
    "刀剑应容忍鶴/鹤字形差异");
AssertTrue(FormationNameMatcher.IsExactMatch("山姥切国厂", "山姥切国广"),
    "刀剑应容忍厂/广形近误识");
AssertTrue(FormationNameMatcher.IsExactMatch("軽歩兵", "轻步"),
    "刀装应容忍軽歩字形差异并命中完整显示名");
AssertFalse(FormationNameMatcher.IsExactMatch("重歩兵", "轻步"),
    "刀装不得因一字之差命中异种刀装");
AssertFalse(FormationNameMatcher.IsExactMatch("高黑", "高楯黑"),
    "刀装不启用丢字容错");
AssertTrue(FormationNameMatcher.IsExactMatch("统兵·特上", "铳"),
    "OCR 将铳识别为统时仍应命中铳兵刀装");
AssertTrue(FormationNameMatcher.IsExactMatch("銃兵·特上", "铳"),
    "日文字形銃应归一到铳并命中");
AssertTrue(FormationNameMatcher.IsExactMatch("槍兵·特上", "枪"),
    "日文字形槍应归一到枪并命中");
AssertFalse(FormationNameMatcher.IsExactMatch("弓兵·特上", "铳"),
    "单字刀装归一化后仍不得命中异种刀装");
AssertTrue(FormationNameMatcher.IsLegacyFuzzyMatch("高黑", "高楯黑"),
    "马匹保留 OCR 丢字容错");
AssertTrue(FormationNameMatcher.IsLegacyFuzzyMatch("次郎太刀", "太郎太刀"),
    "马匹维持原有一字差容错");
var rootViewSource = File.ReadAllText(Path.Combine(
    Directory.GetCurrentDirectory(), "_src", "MFAAvalonia", "Views", "Windows", "RootView.axaml.cs"));
var rootViewMarkup = File.ReadAllText(Path.Combine(
    Directory.GetCurrentDirectory(), "_src", "MFAAvalonia", "Views", "Windows", "RootView.axaml"));
var settingsViewMarkup = File.ReadAllText(Path.Combine(
    Directory.GetCurrentDirectory(), "_src", "MFAAvalonia", "Views", "Pages", "SettingsView.axaml"));
var guiSettingsMarkup = File.ReadAllText(Path.Combine(
    Directory.GetCurrentDirectory(), "_src", "MFAAvalonia", "Views", "UserControls", "Settings", "GuiSettingsUserControl.axaml"));
var externalNotificationSettingsMarkup = File.ReadAllText(Path.Combine(
    Directory.GetCurrentDirectory(), "_src", "MFAAvalonia", "Views", "UserControls", "Settings", "ExternalNotificationSettingsUserControl.axaml"));
var settingsLayoutMarkup = File.ReadAllText(Path.Combine(
    Directory.GetCurrentDirectory(), "_src", "SukiUI", "Controls", "Settings", "SettingsLayout.axaml"));
var settingsLayoutSource = File.ReadAllText(Path.Combine(
    Directory.GetCurrentDirectory(), "_src", "SukiUI", "Controls", "Settings", "SettingsLayout.axaml.cs"));
var taskQueueViewSource = File.ReadAllText(Path.Combine(
    Directory.GetCurrentDirectory(), "_src", "MFAAvalonia", "Views", "Pages", "TaskQueueView.axaml.cs"));
var taskQueueViewMarkup = File.ReadAllText(Path.Combine(
    Directory.GetCurrentDirectory(), "_src", "MFAAvalonia", "Views", "Pages", "TaskQueueView.axaml"));
var taskQueueViewModelSource = File.ReadAllText(Path.Combine(
    Directory.GetCurrentDirectory(), "_src", "MFAAvalonia", "ViewModels", "Pages", "TaskQueueViewModel.cs"));
var taskStartProcessorSource = File.ReadAllText(Path.Combine(
    Directory.GetCurrentDirectory(), "_src", "MFAAvalonia", "Extensions", "MaaFW", "MaaProcessor.cs"));
var adbInputSelectionSource = ExtractSourceSection(
    taskStartProcessorSource,
    "private AdbInputMethods ConfigureAdbInputTypes()",
    "private AdbScreencapMethods ConfigureAdbScreenCapTypes()");
AssertTrue(adbInputSelectionSource.Contains("IsMuMuDeviceName()", StringComparison.Ordinal)
    && adbInputSelectionSource.Contains("detected | AdbInputMethods.EmulatorExtras", StringComparison.Ordinal),
    "自动模式必须为 MuMu 设备保留已发现输入方式并补充 EmulatorExtras，避免连续上滑退回 AdbShell");
AssertTrue(taskStartProcessorSource.Contains("_recoveryMonitor.RecordCallback", StringComparison.Ordinal)
    && taskStartProcessorSource.Contains("_recoveryMonitor.FeedAction", StringComparison.Ordinal)
    && taskStartProcessorSource.Contains("_recoveryMonitor.GetReason", StringComparison.Ordinal),
    "升级上游后必须保留无回调与动作循环检测的接入及独立检查，避免挂起后只能等待 pipeline");
var mfaTaskSource = File.ReadAllText(Path.Combine(
    Directory.GetCurrentDirectory(), "_src", "MFAAvalonia", "Helper", "ValueType", "MFATask.cs"));
var sortieRepeatSource = ExtractSourceSection(
    taskStartProcessorSource,
    "private NodeAndParam CreateNodeAndParam",
    "private void ApplyFormationPresetOverride");
AssertTrue(sortieRepeatSource.Contains("var repeatCount = task.InterfaceItem?.RepeatCount;", StringComparison.Ordinal)
    && !sortieRepeatSource.Contains("异去_重复次数", StringComparison.Ordinal),
    "常驻作战的轮数必须与其它任务一致，直接取任务级重复次数，不得再读过去/异去的下级选项");
var sortieTaskDefinition = interfaceDefinition["task"]?
    .FirstOrDefault(item => item?["entry"]?.Value<string>() == "Sortie");
var sortieModeOption = interfaceDefinition["option"]?["过去/异去"];
var pastSortieCaseDefinition = sortieModeOption?["cases"]?
    .FirstOrDefault(item => item?["name"]?.Value<string>() == "过去");
var isekaiSortieCaseDefinition = sortieModeOption?["cases"]?
    .FirstOrDefault(item => item?["name"]?.Value<string>() == "异去");
AssertTrue(sortieTaskDefinition?["repeatable"]?.Value<bool>() == true
    && sortieTaskDefinition?["repeat_count"]?.Value<int>() == 3
    && interfaceDefinition["option"]?["异去_重复次数"] == null
    && isekaiSortieCaseDefinition?["option"]?.Values<string>().Contains("异去_重复次数") == false,
    "常驻作战必须改用任务级重复次数，并移除旧的异去下级次数选项");
AssertTrue(pastSortieCaseDefinition?["pipeline_override"]?["S_CheckHomeBrightness"]?["next"]?.Values<string>()
        .SequenceEqual(["S_IsSortieRoundDone", "S_IsHome", "S_DetectWhereAmI"]) == true,
    "过去模式必须在回本丸路径上优先判断这一圈是否已经打完");
AssertTrue(sortieDefinition["S_IsSortieRoundDone"]?["recognition"]?["type"]?.Value<string>() == "Custom"
    && sortieDefinition["S_IsSortieRoundDone"]?["recognition"]?["param"]?["custom_recognition"]?.Value<string>() == "SortieRoundDoneRecognition"
    && sortieDefinition["S_IsSortieRoundDone"]?["action"]?["custom_action"]?.Value<string>() == "LogAction"
    && sortieDefinition["S_IsSortieRoundDone"]?["next"]?.Values<string>()?.Any() == false,
    "过去模式的单圈结算 node 必须用自定义识别判定，并在打点后结束本轮任务运行");
AssertTrue(SortieRepeatCountMigration.TryResolve("Sortie", "5", out var migratedSortieRepeat)
    && migratedSortieRepeat == 5,
    "合战场旧轮数必须迁移到任务级重复次数");
AssertTrue(SortieRepeatCountMigration.TryResolve("Sortie", "-1", out var migratedInfiniteRepeat)
    && migratedInfiniteRepeat == -1,
    "合战场旧轮数为 -1 时必须迁移为无限重复");
AssertFalse(SortieRepeatCountMigration.TryResolve("Sortie", "无法识别", out _),
    "旧轮数非法时不得迁移");
AssertFalse(SortieRepeatCountMigration.TryResolve("Underground", "5", out _),
    "非合战场任务不得参与轮数迁移");
var taskLoaderSource = File.ReadAllText(Path.Combine(
    Directory.GetCurrentDirectory(), "_src", "MFAAvalonia", "Extensions", "MaaFW", "TaskLoader.cs"));
AssertTrue(taskLoaderSource.Contains("MigrateSortieRepeatCount(oldItem.InterfaceItem)", StringComparison.Ordinal),
    "加载存量配置时必须执行合战场轮数迁移");
AssertTrue(mfaTaskSource.Contains("!infinite && Count > 1 && Type == MFATaskType.MAAFW", StringComparison.Ordinal)
    && mfaTaskSource.Contains("LangKeys.TaskRoundComplete", StringComparison.Ordinal),
    "有限重复任务每完成一轮都必须输出任务完成和当前进度");
var taskQueueContinuationPolicySource = File.ReadAllText(Path.Combine(
    Directory.GetCurrentDirectory(), "_src", "MFAAvalonia", "Helper", "TaskQueueContinuationPolicy.cs"));
AssertTrue(taskQueueContinuationPolicySource.Contains("ShouldInsertGoHome", StringComparison.Ordinal),
    "任务队列应提供统一的回本丸插入判定，避免特殊任务前执行回本丸");
AssertTrue(taskStartProcessorSource.Contains("ShouldInsertGoHome(taskAndParams[i + 1].Entry)", StringComparison.Ordinal),
    "任务队列应根据下一个任务的 Entry 判断是否插入回本丸");
var restartGameActionSource = File.ReadAllText(Path.Combine(
    Directory.GetCurrentDirectory(), "_src", "MFAAvalonia", "Extensions", "MaaFW", "Custom", "RestartGameAction.cs"));
var appSource = File.ReadAllText(Path.Combine(
    Directory.GetCurrentDirectory(), "_src", "MFAAvalonia", "App.axaml.cs"));
var appPathsSource = File.ReadAllText(Path.Combine(
    Directory.GetCurrentDirectory(), "_src", "MFAAvalonia", "Helper", "AppPaths.cs"));
var versionCheckerSource = File.ReadAllText(Path.Combine(
    Directory.GetCurrentDirectory(), "_src", "MFAAvalonia", "Helper", "VersionChecker.cs"));
AssertTrue(versionCheckerSource.Contains("GetDataRootRelativePath", StringComparison.Ordinal)
    && versionCheckerSource.Contains("normalized = $\"assets/{normalized}\"", StringComparison.Ordinal)
    && versionCheckerSource.Contains("Path.Combine(candidateRoot, \"assets\", \"interface.json\")", StringComparison.Ordinal)
    && versionCheckerSource.Contains("Path.Combine(candidateRoot, \"assets\", \"resource\")", StringComparison.Ordinal),
    "资源更新必须将资源包内容映射到 assets 目录，并识别包含程序文件的完整资源包根目录");
AssertTrue(versionCheckerSource.Contains("var newInterfacePath = AppPaths.InterfaceJsonPath;", StringComparison.Ordinal),
    "资源更新写入版本元数据时必须使用 assets/interface.json，不能在程序根目录创建 interface.json");
var maaLogRotatorSource = File.ReadAllText(Path.Combine(
    Directory.GetCurrentDirectory(), "_src", "MFAAvalonia", "Helper", "MaaLogRotator.cs"));
var workRecordsViewModelSource = File.ReadAllText(Path.Combine(
    Directory.GetCurrentDirectory(), "_src", "MFAAvalonia", "ViewModels", "Pages", "WorkRecordsViewModel.cs"));
var taskOptionGeneratorSource = File.ReadAllText(Path.Combine(
    Directory.GetCurrentDirectory(), "_src", "MFAAvalonia", "Helper", "TaskOptionGenerator.cs"));
var addTaskDialogSource = File.ReadAllText(Path.Combine(
    Directory.GetCurrentDirectory(), "_src", "MFAAvalonia", "ViewModels", "UsersControls", "AddTaskDialogViewModel.cs"));
var desktopProjectSource = File.ReadAllText(Path.Combine(
    Directory.GetCurrentDirectory(), "_src", "MFAAvalonia.Desktop", "MFAAvalonia.Desktop.csproj"));
var aboutViewMarkup = File.ReadAllText(Path.Combine(
    Directory.GetCurrentDirectory(), "_src", "MFAAvalonia", "Views", "UserControls", "Settings", "AboutUserControl.axaml"));
var fileLogExporterSource = File.ReadAllText(Path.Combine(
    Directory.GetCurrentDirectory(), "_src", "MFAAvalonia", "Helper", "FileLogExporter.cs"));
var toastHelperSource = File.ReadAllText(Path.Combine(
    Directory.GetCurrentDirectory(), "_src", "MFAAvalonia", "Helper", "ToastHelper.cs"));
var swordDropLogActionSource = File.ReadAllText(Path.Combine(
    Directory.GetCurrentDirectory(), "assets", "resource", "base", "custom", "SwordDropLogAction.cs"));
var externalNotificationHelperSource = File.ReadAllText(Path.Combine(
    Directory.GetCurrentDirectory(), "_src", "MFAAvalonia", "Helper", "ExternalNotificationHelper.cs"));
AssertTrue(swordDropLogActionSource.Contains(
        "ExternalNotificationHelper.ExternalNotificationAsync(message)",
        StringComparison.Ordinal),
    "刀剑掉落播报名单命中时应同时发送外部通知");
AssertTrue(externalNotificationHelperSource.Contains(
        "var apiEndpoint = $\"{serverUrl}/v3/send/{apiKey}\";",
        StringComparison.Ordinal),
    "QMsg 通知必须调用当前 v3 推送接口，避免旧接口导致发送测试失败");
AssertTrue(taskStartProcessorSource.Contains(
        "ToastNotification.Show(LangKeys.TaskFailed.ToLocalization());",
        StringComparison.Ordinal),
    "任务失败时应发送与任务完成一致的系统通知");
var packWinScript = File.ReadAllText(Path.Combine(
    Directory.GetCurrentDirectory(), "tools", "pack_win.ps1"));
var packMacScript = File.ReadAllText(Path.Combine(
    Directory.GetCurrentDirectory(), "tools", "pack_mac.ps1"));
var packAllScript = File.ReadAllText(Path.Combine(
    Directory.GetCurrentDirectory(), "tools", "pack_all.ps1"));
AssertTrue(rootViewSource.IndexOf("InitializeComponent();", StringComparison.Ordinal)
    < rootViewSource.IndexOf("LoadWindowSizeAndPosition();", StringComparison.Ordinal),
    "窗口应在加载已保存尺寸前完成XAML初始化，避免默认尺寸覆盖配置");
AssertTrue(rootViewMarkup.Contains("Width=\"900\"", StringComparison.Ordinal)
    && rootViewMarkup.Contains("Height=\"675\"", StringComparison.Ordinal),
    "窗口默认尺寸应为900×675");
AssertTrue(settingsViewMarkup.Contains("MinWidthWhetherStackSummaryShow=\"900\"", StringComparison.Ordinal),
    "默认窗口宽度下设置分类必须改为顶部导航，不能挤占设置内容");
AssertTrue(guiSettingsMarkup.Contains("Classes=\"ResponsiveSettingsField\"", StringComparison.Ordinal)
    && guiSettingsMarkup.Contains("MinWidth=\"160\"", StringComparison.Ordinal)
    && guiSettingsMarkup.Contains("MaxWidth=\"215\"", StringComparison.Ordinal),
    "界面设置的选择控件必须在160至215像素间自适应，优先保留说明文字宽度");
AssertTrue(externalNotificationSettingsMarkup.Contains("AcceptsReturn=\"True\"", StringComparison.Ordinal)
    && externalNotificationSettingsMarkup.Contains("TextWrapping=\"Wrap\"", StringComparison.Ordinal)
    && !externalNotificationSettingsMarkup.Contains("CustomFailureText, UpdateSourceTrigger=PropertyChanged}\"\n                        Watermark", StringComparison.Ordinal),
    "外部通知成功与失败内容必须使用可换行的独立输入框");
AssertTrue(settingsLayoutMarkup.Contains("RadioButton.MenuChipTop  TextBlock", StringComparison.Ordinal)
    && settingsLayoutMarkup.Contains("FontSize\" Value=\"18\"", StringComparison.Ordinal)
    && settingsLayoutMarkup.Contains("HorizontalScrollBarVisibility=\"Hidden\"", StringComparison.Ordinal)
    && !settingsLayoutMarkup.Contains("HorizontalScrollBarVisibility=\"Auto\"", StringComparison.Ordinal),
    "顶部设置导航必须使用18号字号并隐藏横向滚动条");
var topSummaryTextSource = ExtractSourceSection(
    settingsLayoutSource,
    "var contentTop = new TextBlock",
    "header.Bind(");
AssertTrue(topSummaryTextSource.Contains("FontSize = 18", StringComparison.Ordinal)
    && !topSummaryTextSource.Contains("FontSize = 14", StringComparison.Ordinal),
    "顶部设置导航文字必须以18号字号创建，不能再被14号本地值覆盖");
AssertTrue(settingsLayoutSource.Contains("private const double TopSummaryDragThreshold = 4.0;", StringComparison.Ordinal)
    && settingsLayoutSource.Contains("InputElement.PointerPressedEvent, OnTopSummaryPointerPressed", StringComparison.Ordinal)
    && settingsLayoutSource.Contains("InputElement.PointerMovedEvent, OnTopSummaryPointerMoved", StringComparison.Ordinal)
    && settingsLayoutSource.Contains("InputElement.PointerReleasedEvent, OnTopSummaryPointerReleased", StringComparison.Ordinal),
    "顶部设置导航必须独立订阅指针拖拽事件，并把启动阈值设为4个逻辑像素");
var topSummaryDragSource = ExtractSourceSection(
    settingsLayoutSource,
    "private void OnTopSummaryPointerMoved(",
    "private void OnTopSummaryPointerReleased(");
AssertTrue(topSummaryDragSource.Contains("e.Pointer.Capture(scrollTop);", StringComparison.Ordinal)
    && topSummaryDragSource.Contains("Math.Clamp(_topSummaryDragStartOffsetX - horizontalDelta, 0, maxX)", StringComparison.Ordinal)
    && settingsLayoutSource.Contains("if (_suppressTopSummaryClick)", StringComparison.Ordinal),
    "顶部设置导航拖拽必须捕获指针、限制横向偏移，并抑制拖拽后误触发的分类跳转");
AssertTrue(settingsLayoutSource.Contains("topRadio.BringIntoView();", StringComparison.Ordinal),
    "页面滚动切换设置分类时，顶部导航必须自动将当前项带入可视区域");
AssertTrue(taskQueueViewSource.Contains("new WrapPanel", StringComparison.Ordinal)
    && !taskQueueViewSource.Contains("new UniformGrid { Columns = 2", StringComparison.Ordinal),
    "复选框应根据可用宽度自适应换行，不能固定为两列");
AssertTrue(taskQueueViewMarkup.Contains("Command=\"{Binding ToggleSelectAllCommand}\"", StringComparison.Ordinal)
    && !taskQueueViewMarkup.Contains("Command=\"{Binding SelectAllCommand}\"", StringComparison.Ordinal)
    && !taskQueueViewMarkup.Contains("Command=\"{Binding SelectNoneCommand}\"", StringComparison.Ordinal),
    "任务列表必须使用一个按钮在全选与全不选之间切换，不能恢复上游的两个独立按钮");
AssertTrue(taskQueueViewMarkup.Contains("RowDefinitions=\"Auto,Auto\"", StringComparison.Ordinal)
    && taskQueueViewMarkup.Contains("Grid.Row=\"1\"", StringComparison.Ordinal)
    && taskQueueViewMarkup.Contains("ColumnDefinitions=\"Auto,*,24\"", StringComparison.Ordinal)
    && !taskQueueViewMarkup.Contains("ColumnDefinitions=\"Auto,*,36,20,3,18\"", StringComparison.Ordinal)
    && !taskQueueViewMarkup.Contains("ColumnDefinitions=\"Auto,*,24,3,18\"", StringComparison.Ordinal),
    "运行耗时必须显示在任务名下方，不能占用任务名称所在行的固定宽度");
AssertTrue(taskQueueViewMarkup.Contains("<Grid Grid.Column=\"2\" Grid.Row=\"1\" Width=\"24\" Height=\"24\"", StringComparison.Ordinal),
    "任务运行状态图标必须使用与齿轮相同的 24×24 布局边界，确保两者中轴重合");
var runningStatusGridStart = taskQueueViewMarkup.IndexOf(
    "<Grid Grid.Column=\"2\" Grid.Row=\"1\" Width=\"24\" Height=\"24\"",
    StringComparison.Ordinal);
AssertTrue(runningStatusGridStart >= 0
    && taskQueueViewMarkup.Substring(runningStatusGridStart, 720).Contains("<Path Width=\"16\" Height=\"16\"", StringComparison.Ordinal),
    "任务运行状态图标必须在统一布局边界内使用 16×16 图标尺寸，保持与齿轮的视觉中心对齐");
AssertTrue(runningStatusGridStart >= 0
    && taskQueueViewMarkup.Substring(runningStatusGridStart, 980).Contains("<TranslateTransform X=\"3\" />", StringComparison.Ordinal),
    "任务运行状态图标必须补偿图形自身的视觉偏移，与齿轮视觉中轴重合");
AssertTrue(taskQueueViewModelSource.Contains("private void ToggleSelectAll()", StringComparison.Ordinal)
    && !taskQueueViewModelSource.Contains("private void SelectAll()", StringComparison.Ordinal)
    && !taskQueueViewModelSource.Contains("private void SelectNone()", StringComparison.Ordinal),
    "任务列表必须通过 ToggleSelectAll 统一处理全选与全不选");
AssertTrue(appSource.Contains(
        ".AddView<WorkRecordNameDialogView, WorkRecordNameDialogViewModel>(services)",
        StringComparison.Ordinal),
    "工作记录保存时必须注册名称输入对话框视图，避免提示找不到 WorkRecordNameDialogViewModel 对应视图");
AssertTrue(appSource.Contains("AppPaths.CleanupOldDebugLogs(", StringComparison.Ordinal)
    && appSource.Contains("MaaLogRotator.Start();", StringComparison.Ordinal)
    && appSource.Contains("MaaLogRotator.Stop();", StringComparison.Ordinal),
    "应用启动和退出时必须启用并停止磁盘日志维护，避免 debug 目录无限增长");
AssertTrue(appPathsSource.Contains("public static void CleanupOldDebugLogs(int retainDays = 3", StringComparison.Ordinal)
    && appPathsSource.Contains("DateTime.Now.AddDays(-retainDays)", StringComparison.Ordinal)
    && appPathsSource.Contains("debugDirSize > 500 * 1024 * 1024", StringComparison.Ordinal)
    && appPathsSource.Contains("Directory.EnumerateFiles(debugPath, \"maafw.bak.*.log\", SearchOption.TopDirectoryOnly)", StringComparison.Ordinal)
    && appPathsSource.Contains("Directory.EnumerateFiles(onErrorPath, \"*.png\", SearchOption.TopDirectoryOnly)", StringComparison.Ordinal)
    && appPathsSource.Contains("files.OrderByDescending(file => file).Skip(retainCount)", StringComparison.Ordinal),
    "磁盘日志清理必须删除过期备份，并在空间超限时限制备份日志与错误截图数量");
AssertTrue(maaLogRotatorSource.Contains("public static void Stop()", StringComparison.Ordinal),
    "日志切块器必须提供停止入口，避免应用退出后遗留轮询任务");
AssertTrue(workRecordsViewModelSource.Contains("FormatRepairCost(item.Wood)", StringComparison.Ordinal)
    && workRecordsViewModelSource.Contains("cost < 0 ? \"未识别\"", StringComparison.Ordinal),
    "工作记录的修刀明细必须将未识别资源显示为未识别，而不是内部失败标记");
var applyCurrentDeviceSelectionSource = ExtractSourceSection(
    taskQueueViewModelSource,
    "private void ApplyCurrentDeviceSelection",
    "private void SetEmptyDeviceState");
AssertFalse(applyCurrentDeviceSelectionSource.Contains("Dispatcher.UIThread.Post", StringComparison.Ordinal),
    "恢复已保存的 ADB 设备必须同步写入连接配置，不能延后到后台 UI 队列，否则启动连接会读到空序列号");
AssertFalse(taskQueueViewModelSource.Contains("_liveViewNoImageLogged", StringComparison.Ordinal),
    "实时画面首帧尚未完成时不应立即输出无画面警告，必须改为连续失败确认");
AssertTrue(restartGameActionSource.Contains(
        "shell am start -a android.intent.action.MAIN -c android.intent.category.LAUNCHER -p {package}",
        StringComparison.Ordinal)
    && !restartGameActionSource.Contains("shell monkey -p", StringComparison.Ordinal),
    "重启游戏应使用 am start 启动指定包名，不能依赖雷电缺失的 monkey 命令");
AssertTrue(restartGameActionSource.Contains(
        "shell cmd package resolve-activity --brief",
        StringComparison.Ordinal)
    && restartGameActionSource.Contains("shell am start -n {launchActivity}", StringComparison.Ordinal),
    "重启游戏应先解析实际启动 Activity，再使用组件名启动，兼容包名没有可解析默认 Intent 的模拟器");
AssertTrue(restartGameActionSource.Contains(
        "LogRestartEvent(\"重启游戏\", \"模拟器重启完成，但游戏启动失败\", true)",
        StringComparison.Ordinal)
    && restartGameActionSource.Contains(
        "LogRestartEvent(\"重启模拟器\", \"模拟器重启失败，停止任务\", true)",
        StringComparison.Ordinal),
    "游戏启动失败不能直接判定任务失败，必须与模拟器重启失败分开记录并继续主枢纽流程");
AssertTrue(restartGameActionSource.Contains("LogRestartEvent(\"重启游戏\", \"检测到游戏疑似卡死\", true)", StringComparison.Ordinal)
    && restartGameActionSource.Contains("LogRestartEvent(\"重启模拟器\", \"游戏重启失败，重启模拟器\", true)", StringComparison.Ordinal)
    && restartGameActionSource.Contains("LogRestartEvent(\"重启游戏\", \"游戏重启完成\", false)", StringComparison.Ordinal),
    "重启动作必须分别记录游戏重启、模拟器重启和游戏重启完成事件");
var startTaskSource = ExtractSourceSection(
    taskStartProcessorSource,
    "public async Task StartTask(",
    "private readonly record struct TaskQueueResult");
AssertTrue(startTaskSource.Contains("await TaskManager.RunTaskAsync(async () =>", StringComparison.Ordinal)
    && !startTaskSource.Contains("token: token, name: \"启动任务\"", StringComparison.Ordinal),
    "启动任务必须等待异步任务队列完成，不能把异步 lambda 绑定到 Action 重载后提前停止");
var liveViewFrameAvailability = new LiveViewFrameAvailability();
AssertTrue(liveViewFrameAvailability.RecordFrame(false) == LiveViewFrameAvailabilityChange.None
    && liveViewFrameAvailability.RecordFrame(false) == LiveViewFrameAvailabilityChange.None
    && liveViewFrameAvailability.RecordFrame(false) == LiveViewFrameAvailabilityChange.BecameUnavailable,
    "实时画面应在连续三个定时周期无图像后才提示不可用");
AssertTrue(liveViewFrameAvailability.RecordFrame(true) == LiveViewFrameAvailabilityChange.Recovered
    && liveViewFrameAvailability.RecordFrame(false) == LiveViewFrameAvailabilityChange.None,
    "实时画面恢复后应重置故障状态，下一次短暂缺帧不应立即再次提示");
var displayTaskCompletionMessageSource = ExtractSourceSection(
    taskStartProcessorSource,
    "private void DisplayTaskCompletionMessage(",
    "public void HandleAfterTaskOperation()");
AssertTrue(displayTaskCompletionMessageSource.Contains(
        "if (!onlyStart)\n                HandleAfterTaskOperation();",
        StringComparison.Ordinal),
    "任务队列失败时也必须执行任务结束后操作");
var liveViewTimerElapsedSource = ExtractSourceSection(
    taskQueueViewModelSource,
    "private void OnLiveViewTimerElapsed",
    "[ObservableProperty] private bool _enableLiveView = true;");
AssertTrue(liveViewTimerElapsedSource.Contains(
        "Processor.ResetLiveViewTasker();\n                            return;",
        StringComparison.Ordinal),
    "实时画面触发恢复后必须结束当前刷新轮次，避免继续读取已替换的截图执行器");
AssertTrue(packWinScript.Contains("$AgentTarget = \"$TempDir\\runtimes\\libs\\MaaAgentBinary\"", StringComparison.Ordinal)
    && packWinScript.Contains("Remove-Item -Recurse -Force $AgentTarget", StringComparison.Ordinal),
    "Windows 打包脚本在复制运行时库后必须清理已移除的 MaaAgentBinary 目录");
AssertTrue(packMacScript.Contains("$AgentTarget = Join-Path $MacOsDir 'MaaAgentBinary'", StringComparison.Ordinal)
    && packMacScript.Contains("$RuntimeAgentTarget = Join-Path $MacOsDir 'runtimes\\libs\\MaaAgentBinary'", StringComparison.Ordinal)
    && packMacScript.Contains("Remove-Item -LiteralPath $AgentTarget -Recurse -Force", StringComparison.Ordinal)
    && packMacScript.Contains("Remove-Item -LiteralPath $RuntimeAgentTarget -Recurse -Force", StringComparison.Ordinal),
    "macOS 打包脚本在复制发布产物后必须清理根目录和 runtimes/libs 中已移除的 MaaAgentBinary 目录");
AssertTrue(packMacScript.Contains("$BundleExecutable = Join-Path $MacOsDir 'MATR'", StringComparison.Ordinal)
    && !packMacScript.Contains("$SourceExecutable = Join-Path $MacOsDir 'MFAAvalonia'", StringComparison.Ordinal),
    "macOS 打包脚本必须直接使用发布产物中的 MATR 入口，不能再查找旧的 MFAAvalonia 名称");
AssertTrue(packMacScript.Contains("$StagingDir = Join-Path $Root '_temp_macos'", StringComparison.Ordinal)
    && packMacScript.Contains("Remove-Item -LiteralPath $StagingDir -Recurse -Force", StringComparison.Ordinal),
    "macOS 打包前必须完整清理临时目录，避免不完整残留导致程序集重复进入压缩包");
AssertTrue(packAllScript.Contains("Remove-Item -LiteralPath (Join-Path $PublishBase 'osx-arm64\\publish') -Recurse -Force", StringComparison.Ordinal),
    "macOS 发布前必须清理旧发布目录，避免过期程序集与当前 runtimes/libs 重复打入发布包");
AssertTrue(taskOptionGeneratorSource.Contains("var grid = new UniformGrid", StringComparison.Ordinal)
    && taskOptionGeneratorSource.Contains("void UpdateColumns()", StringComparison.Ordinal)
    && taskOptionGeneratorSource.Contains("grid.Columns = columns", StringComparison.Ordinal),
    "实际生成任务选项的复选框布局应根据可用宽度自适应列数，并拉伸填满当前行");
AssertTrue(desktopProjectSource.Contains("<AssemblyName>MATR</AssemblyName>", StringComparison.Ordinal)
    && desktopProjectSource.Contains("<OutputName>MATR</OutputName>", StringComparison.Ordinal),
    "Windows 发布产物必须使用 MATR 名称，才能与打包脚本和品牌入口保持一致");
AssertFalse(aboutViewMarkup.Contains("HelpImproveSoftware", StringComparison.Ordinal),
    "MATR 已禁用遥测，关于页面不得显示没有实际作用的帮助改进软件开关");
var defaultInterfaceSource = ExtractSourceSection(
    taskStartProcessorSource,
    "public static bool CheckInterface(",
    "// 防止 interface 加载失败时 Toast 重复显示");
AssertTrue(fileLogExporterSource.Contains("ToastHelper.SuccessWithSurvey(", StringComparison.Ordinal)
    && toastHelperSource.Contains("public static void SuccessWithSurvey(", StringComparison.Ordinal)
    && toastHelperSource.Contains("去反馈bug", StringComparison.Ordinal),
    "导出日志成功后必须显示带问卷链接的反馈提示");
AssertTrue(defaultInterfaceSource.Contains("{PROJECT_DIR}/resource/base", StringComparison.Ordinal)
    && !defaultInterfaceSource.Contains("{PROJECT_DIR}/assets/resource/base", StringComparison.Ordinal),
    "默认 interface 的资源路径必须相对 interface 文件所在的 assets 目录，不能在根目录生成 resource 样例目录");
AssertTrue(taskStartProcessorSource.Contains(
        "public static string ProjectDir =>",
        StringComparison.Ordinal)
    && taskStartProcessorSource.Contains(
        "Path.GetDirectoryName(GetInterfaceFilePath() ?? AppPaths.InterfaceJsonPath) ?? AppPaths.DataRoot;",
        StringComparison.Ordinal)
    && taskStartProcessorSource.Contains(
        "MaaInterface.ReplacePlaceholder(customResource.Path ?? new(), ProjectDir)",
        StringComparison.Ordinal),
    "资源路径解析必须以 interface 文件所在目录作为 {PROJECT_DIR}，不能以程序根目录解析");

var optionInterfaceJson = JObject.Parse(File.ReadAllText(Path.Combine(
    Directory.GetCurrentDirectory(), "assets", "interface.json")));
var currentMaaProcessorSource = File.ReadAllText(Path.Combine(
    Directory.GetCurrentDirectory(), "_src", "MFAAvalonia", "Extensions", "MaaFW", "MaaProcessor.cs"));
foreach (var syncOptionName in new[] { "S_同步远征", "U_同步远征", "LR_同步远征", "TT_同步远征", "EC_同步后勤" })
{
    var syncOverride = optionInterfaceJson["option"]?[syncOptionName]?["cases"]?
        .Children<JObject>().Single()["pipeline_override"];
    AssertTrue(Enumerable.Range(1, 5).All(team =>
            syncOverride?[$"E_CheckTeam{team}"] == null),
        $"{syncOptionName} 不应固定启用五支队伍，应复用远征任务的队伍配置");
}
AssertTrue(currentMaaProcessorSource.Contains(
        "ProcessOptions(ref taskModels, expTask.Option, teamOptionNames)",
        StringComparison.Ordinal)
    && currentMaaProcessorSource.Contains("EndsWith(\"同步远征\")", StringComparison.Ordinal)
    && currentMaaProcessorSource.Contains("EndsWith(\"同步后勤\")", StringComparison.Ordinal)
    && currentMaaProcessorSource.Contains("\"EdoCastle\"", StringComparison.Ordinal),
    "同步后勤必须复用当前实例的远征队伍配置，并覆盖江户城任务");
AssertTrue(currentMaaProcessorSource.Contains(
        "MergePipelineOverrideDictionary(checkboxOverride, caseItem.PipelineOverride)",
        StringComparison.Ordinal)
    && currentMaaProcessorSource.Contains("MergeArrayHandling = MergeArrayHandling.Replace", StringComparison.Ordinal),
    "多选筛选条件必须合并到同一个 node 覆盖对象，不能只保留最后一个刀种");
var formationPresetControlSource = ExtractSourceSection(
    taskOptionGeneratorSource,
    "private Control CreateFormationPresetControl(",
    "/// <summary>读取任务选项中保存的预设编号列表");
AssertTrue(optionInterfaceJson["task"]?.Children<JObject>().Any(task =>
        task["name"]?.Value<string>() == "自定编队"
        && task["entry"]?.Value<string>() == "FormationConfig"
        && task["option"]?.Values<string>().SequenceEqual(["FC_选择预设"]) == true) == true
    && optionInterfaceJson["option"]?["FC_选择预设"]?["type"]?.Value<string>() == "input"
    && !addTaskDialogSource.Contains("\"FormationConfig\"", StringComparison.Ordinal)
    && taskOptionGeneratorSource.Contains("IsFormationPresetOption", StringComparison.Ordinal)
    && taskStartProcessorSource.Contains("task.InterfaceItem?.Entry == \"FormationConfig\"", StringComparison.Ordinal),
    "自定编队必须作为普通任务注册，使用任务专属预设设置并在运行前注入编队参数，不能作为特殊任务出现");
AssertTrue(formationPresetControlSource.Contains("RenderFormationPresets(", StringComparison.Ordinal)
    && formationPresetControlSource.Contains("var presetList = new StackPanel", StringComparison.Ordinal)
    && !formationPresetControlSource.Contains("selectButton", StringComparison.Ordinal)
    && !formationPresetControlSource.Contains("viewModel.IsSubPageOpen = true", StringComparison.Ordinal),
    "自定编队作为普通任务时，预设管理器必须直接显示在任务设置中，不能退化为打开选择子页的按钮");
AssertTrue(taskOptionGeneratorSource.Contains(
        "option.Data[\"preset_ids\"] = string.Join(\",\", normalized)",
        StringComparison.Ordinal)
    && taskOptionGeneratorSource.Contains("current.Remove(capturedPreset.Id)", StringComparison.Ordinal),
    "自定编队的预设选择必须保存多个编号并支持取消勾选");
AssertTrue(!taskOptionGeneratorSource.Contains("AppendDailyPresetGearIcon", StringComparison.Ordinal)
    && !taskOptionGeneratorSource.Contains("IsDailyPresetOption", StringComparison.Ordinal)
    && optionInterfaceJson["option"]?["D_启用预设部队"] == null
    && optionInterfaceJson["task"]?.Children<JObject>()
        .Single(task => task["entry"]?.Value<string>() == "DailyTask")["option"]?.Values<string>()
        .Contains("D_启用预设部队") == false,
    "一键日课不再提供开始前启用预设部队选项，需要编队时另行添加自定编队任务");
var dailyTaskDefinition = JObject.Parse(File.ReadAllText(Path.Combine(
    Directory.GetCurrentDirectory(), "assets", "resource", "base", "pipeline", "DailyTask.json")));
AssertTrue(dailyTaskDefinition["DT_PrepareHub"]?["next"]?.Values<string>()
        .SequenceEqual(["DT_LoginRewardGate"]) == true
    && dailyTaskDefinition["DT_PresetHub"] == null
    && dailyTaskDefinition["DT_PresetFallbackWait"] == null
    && dailyTaskDefinition["DT_RestartGameReturnPresetHub"] == null
    && !taskStartProcessorSource.Contains("\"DailyTask\" => \"D_启用预设部队\"", StringComparison.Ordinal)
    && !taskStartProcessorSource.Contains("DT_PresetHub", StringComparison.Ordinal),
    "日课 pipeline 与任务装配不得残留预设部队 hub 及其重启恢复入口");
AssertTrue(optionInterfaceJson["option"]?["FC_选择预设"]?["inputs"]?.Children<JObject>()
        .Any(input => input["name"]?.Value<string>() == "preset_ids") == true,
    "自定编队的预设选择必须声明多选编号数据项");
var formationTaskExpansionSource = ExtractSourceSection(
    taskStartProcessorSource,
    "private List<NodeAndParam> BuildTaskAndParams",
    "private NodeAndParam CreateNodeAndParam");
AssertTrue(formationTaskExpansionSource.Contains("GetSelectedFormationPresetIds(task)", StringComparison.Ordinal)
    && formationTaskExpansionSource.Contains("node.IsContinuation = i > 0", StringComparison.Ordinal),
    "自定编队勾选多个预设时必须按顺序展开为多个队列项，并标记后续项为同一任务的延续");
AssertTrue(taskStartProcessorSource.Contains("!taskAndParams[i + 1].IsContinuation", StringComparison.Ordinal),
    "同一自定编队任务展开出的后续项之间不应插入回本丸");
var formationEquipStateMachineSource = File.ReadAllText(Path.Combine(
    Directory.GetCurrentDirectory(), "_src", "MFAAvalonia", "Extensions", "MaaFW", "Custom", "FormationEquipStateMachine.cs"));
AssertTrue(formationEquipStateMachineSource.Contains("DetectCurrentSlotWithRetry", StringComparison.Ordinal)
    && formationEquipStateMachineSource.Contains("按入口 {EntrySlot} 号位继续配置", StringComparison.Ordinal)
    && formationEquipStateMachineSource.Contains("跳过剩余装备配置", StringComparison.Ordinal),
    "装备状态机读不到前后位编号时，首位成员在 1 号位应按入口位继续配置，其余情况跳过剩余装备配置，不能失败退回主枢纽后反复识别同一画面");
var formationPipelineDefinition = JObject.Parse(File.ReadAllText(Path.Combine(
    Directory.GetCurrentDirectory(), "assets", "resource", "base", "pipeline", "FormationConfig.json")));
AssertTrue(formationPipelineDefinition["FC_DetectWhereAmI"]?["next"]?.Values<string>()
        .Contains("FC_RecoverFromEquip") == true
    && formationPipelineDefinition["FC_RecoverFromEquip"]?["recognition"]?["param"]?["expected"]?.Value<string>()
        == "更换装备"
    && formationPipelineDefinition["FC_RecoverFromEquip"]?["next"]?.Values<string>()
        .SequenceEqual(["FC_BackFromEquip"]) == true,
    "编队主枢纽必须能识别更换装备页面并返回编成页，作为装备流程异常时的兜底出口");
AssertTrue(optionInterfaceJson["resource"]?.Children<JObject>().Single(resource =>
        resource["name"]?.Value<string>() == "刀剑乱舞")["path"]?.Values<string>()
        .SequenceEqual(["{PROJECT_DIR}/resource/base"]) == true,
    "资源路径必须相对 interface.json 所在的 assets 目录，不能重复拼接 assets");
var drillAvoidStrongOption = optionInterfaceJson["option"]?["D_演练避战强敌"];
var drillThreatOption = optionInterfaceJson["option"]?["D_演练威胁度"];
AssertTrue(drillAvoidStrongOption?["type"]?.Value<string>() == "switch"
    && drillAvoidStrongOption["inline_sub_options"]?.Value<bool>() == true
    && drillAvoidStrongOption["default_case"]?.Value<string>() == "No"
    && drillAvoidStrongOption["cases"]?.Children<JObject>().Single(caseItem => caseItem["name"]?.Value<string>() == "Yes")["option"]?.Values<string>()
        .SequenceEqual(["D_演练威胁度"]) == true,
    "避战强敌应在同一页面显示威胁度子选项");
AssertTrue(drillThreatOption?["type"]?.Value<string>() == "input"
    && drillThreatOption["label"]?.Value<string>() == "威胁度阈值"
    && drillThreatOption["description"]?.Value<string>() == "对面每有一把丙子或极化刀剑，威胁度加一；威胁度达到阈值时视为强敌。"
    && drillThreatOption["inputs"]?.Children<JObject>().Single()["control"]?.Value<string>() == "slider"
    && drillThreatOption["inputs"]?.Children<JObject>().Single()["minimum"]?.Value<int>() == 1
    && drillThreatOption["inputs"]?.Children<JObject>().Single()["maximum"]?.Value<int>() == 6
    && drillThreatOption["inputs"]?.Children<JObject>().Single()["tick_frequency"]?.Value<int>() == 1
    && drillThreatOption["inputs"]?.Children<JObject>().Single()["default"]?.Value<string>() == "6",
    "威胁度子选项应为1到6的离散滑块且默认值为6");
AssertTrue(taskOptionGeneratorSource.Contains("var isFullWidthSlider", StringComparison.Ordinal)
    && taskOptionGeneratorSource.Contains("Grid.SetColumnSpan(sliderPanel, 2)", StringComparison.Ordinal),
    "带标题说明的单滑块应在标题下方占满可用宽度");
AssertTrue(SwordBookFilterMatcher.Matches(true, SwordBookFilter.Owned)
    && !SwordBookFilterMatcher.Matches(false, SwordBookFilter.Owned)
    && SwordBookFilterMatcher.Matches(false, SwordBookFilter.Unowned)
    && SwordBookFilterMatcher.Matches(true, SwordBookFilter.All),
    "刀帐筛选应正确匹配全部、已拥有和未拥有状态");
AssertTrue(Enumerable.Range(1, 5).All(index =>
        drillThreatOption?["pipeline_override"]?[$"DT_DrillDangerCheck{index}"]?["action"]?["custom_action_param"]?["threshold"]?.Value<string>() == "{threshold}"),
    "威胁度子选项应将阈值传递给全部五个演练判断 node");
AssertTrue(DrillDangerDecision.ShouldEnterTraining(5, 6)
    && !DrillDangerDecision.ShouldEnterTraining(6, 6)
    && DrillDangerDecision.ShouldEnterTraining(3, 4)
    && !DrillDangerDecision.ShouldEnterTraining(4, 4),
    "威胁度阈值应按大于等于阈值跳过强敌");
AssertTrue(CaptainSettingsDecision.GetDragNodeName("Underground") == "U_DragCaptain",
    "地下城应映射到 U_DragCaptain");
AssertTrue(CaptainSettingsDecision.GetDragNodeName("TacticalTraining") == "TT_DragCaptain",
    "战术强化应映射到 TT_DragCaptain");
AssertTrue(CaptainSettingsDecision.GetDragNodeName("FlowerBrush") == null,
    "不支持的任务入口不应映射拖拽 action");
AssertTrue(CaptainSettingsDecision.GetSkipOptionName("Sortie") == "S_跳过位置",
    "合战场应映射到任务专属跳过位置");
AssertTrue(CaptainSettingsDecision.GetSkipOptionName("TacticalTraining") == null,
    "战术强化不应拥有跳过位置配置");
AssertTrue(CaptainSettingsDecision.ParseSkipPositions(["位置二", "位置五", "未知位置"]).SetEquals([1, 4]),
    "位置名称应转换为有效的零基索引");
var equipmentFallbackDecisionType = typeof(CaptainSettingsDecision).Assembly
    .GetType("MFAAvalonia.Extensions.MaaFW.Custom.EquipmentFallbackDecision");
var equipmentFallbackTarget = equipmentFallbackDecisionType?
    .GetMethod("GetOneClickEquipButtonTarget")?
    .Invoke(null, [201]);
AssertTrue(equipmentFallbackTarget?.ToString() == "(966, 201)",
    "一键装备按钮应保持缺装模板命中行的 y 坐标，并使用固定 x=966");
var maaProcessorSource = File.ReadAllText(Path.Combine(
    Directory.GetCurrentDirectory(), "_src", "MFAAvalonia", "Extensions", "MaaFW", "MaaProcessor.cs"));
var createTaskParamsSource = maaProcessorSource.Split("private NodeAndParam CreateNodeAndParam", 2)[1]
    .Split("var taskParams = SerializeTaskParams(taskModels);", 2)[0];
AssertTrue(createTaskParamsSource.Contains("CaptainSettingsHelper.GetSelectedSkipPositions(task.InterfaceItem)", StringComparison.Ordinal)
    && createTaskParamsSource.Contains("CaptainSettingsDecision.GetDragNodeName(task.InterfaceItem?.Entry)", StringComparison.Ordinal)
    && createTaskParamsSource.Contains("[\"skip_positions\"]", StringComparison.Ordinal),
    "任务序列化前必须将当前任务的跳过位置注入对应拖拽动作，不能仅保存界面选项");
AssertTrue(maaProcessorSource.Contains(
        "tasker.Resource.Register(new Custom.EquipmentFallbackAction());",
        StringComparison.Ordinal),
    "刀装不足时的一键装备 action 必须注册到运行时资源");
AssertFalse(optionInterfaceJson["global_option"]!.Values<string>().Contains("换队长方式"),
    "换队长方式不应继续作为全局设置");
AssertTrue(optionInterfaceJson["option"]?["换队长方式"] == null
    && optionInterfaceJson["option"]?["拖拽跳过位置"] == null,
    "已移除的全局换队长配置不应残留定义");
foreach (var (taskEntry, captainOptionName, skipOptionName) in new[]
         {
             ("Sortie", "S_换队长", "S_跳过位置"),
             ("Underground", "U_换队长", "U_跳过位置"),
             ("LRentaisen", "LR_换队长", "LR_跳过位置"),
             ("EdoCastle", "EC_换队长", "EC_跳过位置")
         })
{
    var captainOption = optionInterfaceJson["option"]?[captainOptionName];
    AssertTrue(captainOption?["type"]?.Value<string>() == "checkbox"
        && captainOption["cases"]?.Children<JObject>().Single()["option"]?.Values<string>().SequenceEqual([skipOptionName]) == true,
        $"{taskEntry}的更换队长应通过齿轮进入任务专属跳过位置设置");
    var dragNodeName = CaptainSettingsDecision.GetDragNodeName(taskEntry)!;
    AssertTrue(captainOption["cases"]?.Children<JObject>().Single()["pipeline_override"]?[dragNodeName]?["enabled"]?.Value<bool>() == true,
        $"{taskEntry}启用更换队长时应启用拖拽 node");
    AssertTrue(optionInterfaceJson["option"]?[skipOptionName]?["label"]?.Value<string>() == "跳过位置",
        $"{taskEntry}的跳过位置设置应使用统一名称");
}
AssertTrue(optionInterfaceJson["option"]?["TT_换队长"]?["cases"]?.Children<JObject>().Single()["option"] == null,
    "战术强化的更换队长不应提供跳过位置设置");
AssertFalse(optionInterfaceJson.ToString().Contains("马匹筛选", StringComparison.Ordinal)
    || optionInterfaceJson.ToString().Contains("刀装筛选", StringComparison.Ordinal),
    "资源配置不应保留马匹或刀装筛选换队长内容");
foreach (var (pipelineFileName, prefix) in new[]
         {
             ("Sortie.json", "S_"),
             ("Underground.json", "U_"),
             ("LRentaisen.json", "LR_"),
             ("EdoCastle.json", "EC_")
         })
{
    var pipeline = JObject.Parse(File.ReadAllText(Path.Combine(
        Directory.GetCurrentDirectory(), "assets", "resource", "base", "pipeline", pipelineFileName)));
    var retiredNodeNames = new[]
    {
        "IsSwordSelect", "DetectSortOrder", "ClickDescending", "ConfirmSortAsc",
        "ClickCaptain1", "ClickCaptain2", "ClickCaptain3", "ClickCaptain4", "ClickCaptain5", "ClickCaptainSlot"
    };
    AssertTrue(retiredNodeNames.All(nodeName => pipeline[$"{prefix}{nodeName}"] == null),
        $"{pipelineFileName}不应保留旧筛选换队长 node");
    AssertTrue(pipeline[$"{prefix}CaptainHub"]?["next"]?.Values<string>().SequenceEqual([$"{prefix}IsPreSortieConfirm"]) == true,
        $"{pipelineFileName}的 CaptainHub 默认应直接进入出阵确认");
}
var sortiePipeline = JObject.Parse(File.ReadAllText(Path.Combine(
    Directory.GetCurrentDirectory(), "assets", "resource", "base", "pipeline", "Sortie.json")));
var dailyTaskPipeline = JObject.Parse(File.ReadAllText(Path.Combine(
    Directory.GetCurrentDirectory(), "assets", "resource", "base", "pipeline", "DailyTask.json")));
var updateDataPipeline = JObject.Parse(File.ReadAllText(Path.Combine(
    Directory.GetCurrentDirectory(), "assets", "resource", "base", "pipeline", "UpdateData.json")));
var updateDataRecoveryNodes = new[]
{
    "UD_LeaveTroopRecord",
    "UD_IsAnnouncementPopup",
    "UD_IsAdvertisementPopup",
    "UD_IsTrainingLetter",
    "UD_IsLoginReward",
    "UD_IsGameIcon",
    "UD_IsLoginButton",
    "UD_IsGameUpdatePopup",
    "UD_IsInGameUpdatePopup",
    "UD_IsInternalReport",
    "UD_IsConnectionInterrupted",
    "UD_FallbackWait"
};
var updateDataExpeditionResultNodes = new[]
{
    "UD_ClickExpeditionReturn_Exp",
    "UD_ClickExpeditionReturn_Title"
};
AssertTrue(updateDataRecoveryNodes.All(nodeName => updateDataPipeline[nodeName] != null),
    "更新数据主枢纽应包含完整的闪退恢复和异常弹窗处理 node");
AssertTrue(updateDataRecoveryNodes.All(nodeName =>
        updateDataPipeline[nodeName]?["on_error"]?.Values<string>().SequenceEqual(["UD_DetectWhereAmI"]) == true),
    "更新数据主枢纽的通用恢复 node 失败时应回到更新数据主枢纽");
AssertTrue(updateDataPipeline["UD_DetectWhereAmI"]?["next"]?.Values<string>().Intersect(updateDataRecoveryNodes).Count()
    == updateDataRecoveryNodes.Length,
    "更新数据主枢纽应将完整通用恢复链加入识别顺序");
AssertTrue(updateDataPipeline["UD_IsAdvertisementPopup"]?["recognition"]?["type"]?.Value<string>() == "TemplateMatch"
    && updateDataPipeline["UD_IsAdvertisementPopup"]?["recognition"]?["param"]?["template"]?.Value<string>() == "Common/广告.png",
    "更新数据广告弹窗应使用广告模板识别，不能使用 OCR");
AssertTrue(updateDataExpeditionResultNodes.All(nodeName => updateDataPipeline[nodeName] != null)
    && updateDataPipeline["UD_DetectWhereAmI"]?["next"]?.Values<string>().Take(2).SequenceEqual(updateDataExpeditionResultNodes) == true,
    "更新数据主枢纽应优先识别远征结果画面");
AssertTrue(updateDataExpeditionResultNodes.All(nodeName =>
        updateDataPipeline[nodeName]?["next"]?.Values<string>().SequenceEqual(["UD_DetectWhereAmI"]) == true),
    "更新数据远征结果处理完成后应回到更新数据主枢纽");
AssertTrue(updateDataPipeline["UD_DetectWhereAmI"]?["next"]?.Values<string>().SequenceEqual([
        "UD_ClickExpeditionReturn_Exp", "UD_ClickExpeditionReturn_Title",
        "UD_LeaveTroopRecord", "UD_IsAnnouncementPopup", "UD_IsAdvertisementPopup",
        "UD_IsTrainingLetter", "UD_IsLoginReward", "UD_IsGameIcon", "UD_IsLoginButton",
        "UD_IsGameUpdatePopup", "UD_IsInGameUpdatePopup", "UD_IsInternalReport",
        "UD_IsNetworkRequestTimeout", "UD_IsConnectionInterrupted",
        "UD_CheckHomeBrightness1", "UD_FallbackWait"]) == true,
    "更新数据主枢纽的识别顺序应先处理恢复画面，再确认本丸");
AssertTrue(updateDataPipeline["UD_IsAnnouncementPopup"]?["recognition"]?["type"]?.Value<string>() == "TemplateMatch"
    && updateDataPipeline["UD_IsAnnouncementPopup"]?["recognition"]?["param"]?["template"]?.Value<string>() == "Common/公告弹窗.png",
    "更新数据公告弹窗应使用公告模板识别");
AssertTrue(updateDataPipeline["UD_IsConnectionInterrupted"]?["recognition"]?["param"]?["expected"]?.Value<string>() == "连接中断"
    && updateDataPipeline["UD_IsConnectionInterrupted"]?["action"]?["param"]?["target"]?.Values<int>().SequenceEqual([738, 449, 100, 39]) == true,
    "更新数据连接中断应使用精确文案和确认按钮坐标");
var warehousePipeline = JObject.Parse(File.ReadAllText(Path.Combine(
    Directory.GetCurrentDirectory(), "assets", "resource", "base", "pipeline", "Warehouse.json")));
var warehouseExpeditionResultNodes = new[]
{
    "Warehouse_ClickExpeditionReturn_Exp",
    "Warehouse_ClickExpeditionReturn_Title"
};
var warehouseRecoveryNodes = new[]
{
    "Warehouse_LeaveTroopRecord",
    "Warehouse_IsAnnouncementPopup",
    "Warehouse_IsTrainingLetter",
    "Warehouse_IsLoginReward",
    "Warehouse_LoginRewardClick2",
    "Warehouse_LoginRewardClick3",
    "Warehouse_IsAdvertisementPopup",
    "Warehouse_IsGameIcon",
    "Warehouse_IsLoginButton",
    "Warehouse_IsGameUpdatePopup",
    "Warehouse_IsInGameUpdatePopup",
    "Warehouse_IsInternalReport",
    "Warehouse_IsNetworkRequestTimeout",
    "Warehouse_IsConnectionInterrupted",
    "Warehouse_FallbackWait"
};
AssertTrue(warehouseExpeditionResultNodes.All(nodeName => warehousePipeline[nodeName] != null)
    && warehousePipeline["Warehouse_Start"]?["next"]?.Values<string>().Intersect(warehouseExpeditionResultNodes).Count()
        == warehouseExpeditionResultNodes.Length,
    "仓库入口应优先识别并关闭远征结果画面");
AssertTrue(warehouseExpeditionResultNodes.All(nodeName =>
        warehousePipeline[nodeName]?["next"]?.Values<string>().SequenceEqual(["Warehouse_Start"]) == true),
    "仓库远征结果处理完成后应回到仓库入口");
AssertTrue(warehouseRecoveryNodes.All(nodeName => warehousePipeline[nodeName] != null)
    && warehouseRecoveryNodes.All(nodeName =>
        warehousePipeline[nodeName]?["on_error"]?.Values<string>().SequenceEqual(["Warehouse_Start"]) == true),
    "仓库入口应包含完整闪退恢复链且失败时回到仓库入口");
AssertTrue(warehousePipeline["Warehouse_Start"]?["timeout"]?.Value<int>() == 120000
    && warehousePipeline["Warehouse_Start"]?["on_error"]?.Values<string>().SequenceEqual(["Warehouse_RestartGame"]) == true
    && warehousePipeline["Warehouse_RestartGame"]?["next"]?.Values<string>().SequenceEqual(["Warehouse_Start"]) == true,
    "仓库入口超时后应重启游戏并回到仓库入口");
var dashboardLayout = JObject.Parse(File.ReadAllText(Path.Combine(
    Directory.GetCurrentDirectory(), "assets", "resource", "mfa_layout.json")));
AssertTrue(dashboardLayout["settings"]?["row_span"]?.Value<int>() == 5
    && dashboardLayout["task_desc"]?["row"]?.Value<int>() == 5
    && dashboardLayout["task_desc"]?["row_span"]?.Value<int>() == 3,
    "任务设置和任务说明卡片的默认高度应分别为5行和3行");
var autoMarchOption = optionInterfaceJson["option"]?["S_自动行军"];
var autoMarchOverride = autoMarchOption?["cases"]?.Children<JObject>().Single()["pipeline_override"]?["S_DisableAutoMarch"];
AssertTrue(autoMarchOption?["type"]?.Value<string>() == "checkbox"
    && autoMarchOption["default_case"] is JArray
    && autoMarchOverride?["enabled"]?.Value<bool>() == false,
    "自动行军应为默认关闭且勾选后取消关闭自动行军流程的复选框");
AssertTrue(sortiePipeline["S_DisableAutoMarch"]?["enabled"]?.Value<bool>() == true,
    "关闭自动行军流程默认应启用，以支持自动行军选项反转逻辑");
var sortieTask = optionInterfaceJson["task"]!.Children<JObject>()
    .Single(task => task["name"]?.Value<string>() == "合战场");
var undergroundTask = optionInterfaceJson["task"]!.Children<JObject>()
    .Single(task => task["name"]?.Value<string>() == "地下城");
var defaultTasks = optionInterfaceJson["task"]!.Children<JObject>().ToList();
AssertTrue(defaultTasks[0]["name"]?.Value<string>() == "唤醒本丸"
    && defaultTasks[1]["name"]?.Value<string>() == "更新数据"
    && defaultTasks[2]["name"]?.Value<string>() == "自定编队"
    && defaultTasks[3]["name"]?.Value<string>() == "日课",
    "默认任务排序应为唤醒本丸 → 更新数据 → 自定编队 → 一键日课");
foreach (var task in optionInterfaceJson["task"]!.Children<JObject>())
{
    var taskOptions = task["option"]?.Values<string>().ToList() ?? [];
    var syncOption = taskOptions.FirstOrDefault(optionName =>
        optionName.EndsWith("同步远征", StringComparison.Ordinal)
        || optionName.EndsWith("同步后勤", StringComparison.Ordinal));
    if (syncOption != null)
    {
        // 一键日课按设计把同步后勤放在选项首位，其余任务保持在末尾
        var expectedIndex = task["name"]?.Value<string>() == "日课" ? 0 : taskOptions.Count - 1;
        AssertTrue(taskOptions[expectedIndex] == syncOption,
            $"{task["name"]}的同步后勤选项位置不符合约定（日课位于首位，其余任务位于末尾）");
    }
}

foreach (var (task, prefix) in new[] { (sortieTask, "S_"), (undergroundTask, "U_") })
{
    var orderedTaskOptions = task["option"]!.Values<string>().ToList();
    var taskOptions = orderedTaskOptions.ToHashSet();
    var fatigueProcessingIndex = orderedTaskOptions.IndexOf($"{prefix}疲劳处理");
    AssertTrue(fatigueProcessingIndex >= 0
        && orderedTaskOptions.Skip(fatigueProcessingIndex + 1)
            .All(optionName => optionInterfaceJson["option"]?[optionName!]?["type"]?.Value<string>() == "checkbox"),
        $"{prefix}疲劳处理之后应全部为复选框");
    AssertTrue(fatigueProcessingIndex >= 0
        && orderedTaskOptions.Skip(fatigueProcessingIndex + 1).Take(3).SequenceEqual([
            $"{prefix}补充刀装", $"{prefix}刀装保护", $"{prefix}疲劳撤退"]),
        $"{prefix}疲劳处理之后应先排列三个新增复选框");
    AssertTrue(taskOptions.Contains($"{prefix}补充刀装"), $"{prefix}任务应提供独立的补充刀装选项");
    AssertTrue(taskOptions.Contains($"{prefix}刀装保护"), $"{prefix}任务应提供独立的刀装保护选项");
    AssertTrue(taskOptions.Contains($"{prefix}疲劳撤退"), $"{prefix}任务应提供独立的疲劳撤退选项");
    AssertFalse(taskOptions.Contains($"{prefix}刀装破坏处理"), $"{prefix}任务不应继续使用组合式刀装破坏处理选项");

    var supplementOption = optionInterfaceJson["option"]![$"{prefix}补充刀装"]!;
    var equipmentProtectionOption = optionInterfaceJson["option"]![$"{prefix}刀装保护"]!;
    var fatigueRetreatOption = optionInterfaceJson["option"]![$"{prefix}疲劳撤退"]!;
    AssertTrue(supplementOption["type"]?.Value<string>() == "checkbox" && supplementOption["default_case"] is JArray,
        $"{prefix}补充刀装应为默认关闭的独立勾选项");
    AssertTrue(equipmentProtectionOption["type"]?.Value<string>() == "checkbox" && equipmentProtectionOption["default_case"] is JArray,
        $"{prefix}刀装保护应为默认关闭的独立勾选项");
    AssertTrue(fatigueRetreatOption["type"]?.Value<string>() == "checkbox" && fatigueRetreatOption["default_case"] is JArray,
        $"{prefix}疲劳撤退应为默认关闭的独立勾选项");
    AssertTrue(supplementOption["description"] == null
        && equipmentProtectionOption["description"] == null
        && fatigueRetreatOption["description"] == null,
        $"{prefix}三个复选框不应显示选项描述");

    var fatigueOption = optionInterfaceJson["option"]![$"{prefix}疲劳处理"]!;
    foreach (var fatigueCase in fatigueOption["cases"]!.Children<JObject>())
    {
        AssertFalse(fatigueCase["pipeline_override"]?[ $"{prefix}FatigueDetect"] != null,
            $"{prefix}疲劳处理不应再负责行军中的重疲劳撤退");
    }
}

var undergroundPipeline = JObject.Parse(File.ReadAllText(Path.Combine(
    Directory.GetCurrentDirectory(), "assets", "resource", "base", "pipeline", "Underground.json")));
foreach (var (prefix, pipeline) in new[] { ("S_", sortiePipeline), ("U_", undergroundPipeline) })
{
    string fallbackOptionName = $"{prefix}记录不足时一键装备";
    var supplementOption = optionInterfaceJson["option"]?[$"{prefix}补充刀装"];
    var fallbackOption = optionInterfaceJson["option"]?[fallbackOptionName];
    AssertTrue(supplementOption?["cases"]?.Children<JObject>().Single()["option"]?.Values<string>()
            .SequenceEqual([fallbackOptionName]) == true,
        $"{prefix}补充刀装应提供一键装备子选项");
    AssertTrue(fallbackOption?["type"]?.Value<string>() == "checkbox"
        && fallbackOption["default_case"] is JArray,
        $"{prefix}一键装备子选项应默认关闭");
    AssertTrue(pipeline[$"{prefix}PreConfirmSupply"]?["post_delay"]?.Value<int>() == 500,
        $"{prefix}部队记录确认后应等待500毫秒，确保确认弹窗完全出现");
    var fallbackOverride = fallbackOption?["cases"]?.Children<JObject>().Single()["pipeline_override"];
    var expectedPreConfirmNext = prefix == "S_"
        ? new[] { "S_FallbackConfirmRecord", "S_GuiSupplyLog", "S_PreConfirmSupply" }
        : new[] { "U_FallbackConfirmRecord", "U_GuiSupplyLog" };
    AssertTrue(pipeline[$"{prefix}PreConfirmSupply"]?["next"]?.Values<string>()
            .SequenceEqual(expectedPreConfirmNext) == true,
        $"{prefix}部队记录确认后应检查记录确认页、输出 GUI 日志并重试确认");
    var downstreamFallbackNodes = new[]
    {
        $"{prefix}FallbackVerifyTeamSelect",
        $"{prefix}FallbackFindMissingEquipment",
        $"{prefix}FallbackConfirmOneClickEquip",
        $"{prefix}FallbackReturnFromEquip"
    };
    var expectedFallbackError = prefix == "S_"
        ? new[] { "S_DetectWhereAmI" }
        : new[] { "U_GuiSupplyLog" };
    var supplementOverride = supplementOption?["cases"]?.Children<JObject>().Single()["pipeline_override"];
    AssertTrue(fallbackOverride?[$"{prefix}PreConfirmSupply"] == null
        && fallbackOverride?[$"{prefix}FallbackConfirmRecord"] == null
        && fallbackOverride?[$"{prefix}FallbackConfirmRecordClick"]?["next"]?.Values<string>()
            .SequenceEqual([$"{prefix}FallbackVerifyTeamSelect"]) == true
        && supplementOverride?[$"{prefix}FallbackConfirmRecord"]?["enabled"]?.Value<bool>() == true
        && pipeline[$"{prefix}FallbackConfirmRecord"]?["enabled"]?.Value<bool>() == false
        && downstreamFallbackNodes.All(nodeName => pipeline[nodeName]?["enabled"]?.Value<bool>() == true),
        $"{prefix}补充刀装主选项应启用记录确认弹窗处理链；子选项覆写确认后的去向进入一键装备兜底，其余兜底 node 应默认开启");
    AssertTrue(pipeline[$"{prefix}FallbackConfirmRecord"]?["action"]?["custom_action"]?.Value<string>()
            == "GuiLogAction"
        && pipeline[$"{prefix}FallbackConfirmRecord"]?["action"]?["custom_action_param"]?["message"]?.Value<string>()
            == "记录中刀装不足"
        && pipeline[$"{prefix}FallbackConfirmRecord"]?["next"]?.Values<string>()
            .SequenceEqual([$"{prefix}FallbackConfirmRecordLog"]) == true
        && pipeline[$"{prefix}FallbackConfirmRecord"]?["on_error"]?.Values<string>()
            .SequenceEqual(expectedFallbackError) == true,
        $"{prefix}记录确认后应先向界面输出刀装不足日志；未出现确认页时应保留原补充路径");
    var expectedFallbackCompletionNext = prefix == "S_"
        ? new[] { "S_CompleteCurrentTaskAfterEquipmentRecordShortage" }
        : Array.Empty<string>();
    AssertTrue(pipeline[$"{prefix}FallbackConfirmRecordLog"]?["action"]?["custom_action"]?.Value<string>()
            == "LogAction"
        && pipeline[$"{prefix}FallbackConfirmRecordLog"]?["action"]?["custom_action_param"]?["message"]?.Value<string>()
            == "记录中刀装不足"
        && pipeline[$"{prefix}FallbackConfirmRecordLog"]?["next"]?.Values<string>()
            .SequenceEqual([$"{prefix}FallbackConfirmRecordClick"]) == true
        && pipeline[$"{prefix}FallbackConfirmRecordClick"]?["action"]?["type"]?.Value<string>() == "Click"
        && pipeline[$"{prefix}FallbackConfirmRecordClick"]?["next"]?.Values<string>()
            .SequenceEqual(expectedFallbackCompletionNext) == true,
        $"{prefix}记录确认点击后的默认去向应符合任务结束或一键装备兜底设计");
    AssertTrue(pipeline[$"{prefix}FallbackConfirmRecord"]?["recognition"]?["param"]?["expected"]?.Value<string>()
            == "记录确认"
        && pipeline[$"{prefix}FallbackConfirmRecord"]?["recognition"]?["param"]?["roi"]?.Values<int>()
            .SequenceEqual([567, 37, 138, 35]) == true,
        $"{prefix}记录确认页应按确认弹窗标题识别");
    AssertTrue(pipeline[$"{prefix}FallbackFindMissingEquipment"]?["action"]?["custom_action"]?.Value<string>()
            == "EquipmentFallbackAction"
        && pipeline[$"{prefix}FallbackFindMissingEquipment"]?["on_error"]?.Values<string>().SingleOrDefault()
            == $"{prefix}IsPreSortieConfirm",
        $"{prefix}未发现缺装刀剑时应直接进入即刻出阵");
    AssertTrue(pipeline[$"{prefix}FallbackReturnFromEquip"]?["next"]?.Values<string>()
            .SequenceEqual([$"{prefix}FallbackFindMissingEquipment"]) == true,
        $"{prefix}一键装备返回后应直接重新检查缺装刀剑，不能再次点击一键装备入口");
}
AssertTrue(sortiePipeline["S_GuiSupplyLog"]?["recognition"]?["type"]?.Value<string>() == "OCR"
    && sortiePipeline["S_GuiSupplyLog"]?["recognition"]?["param"]?["roi"]?.Values<int>()
        .SequenceEqual([563, 4, 153, 45]) == true
    && sortiePipeline["S_GuiSupplyLog"]?["recognition"]?["param"]?["expected"]?.Value<string>() == "部队选择"
    && sortiePipeline["S_GuiSupplyLog"]?["next"]?.Values<string>().SequenceEqual(["S_IsPreSortieConfirm"]) == true,
    "合战场补充刀装日志应在部队选择页输出，并直接进入出阵确认");

AssertTrue(MixGreedySelectionDecision.TryGetRarity(90, 90, 90, out var rarity) && rarity == 1,
    "稀有度1的颜色应正确映射");
AssertTrue(MixGreedySelectionDecision.TryGetRarity(101, 70, 23, out rarity) && rarity == 5,
    "稀有度5的颜色应正确映射");
AssertFalse(MixGreedySelectionDecision.TryGetRarity(100, 100, 100, out _),
    "未知颜色不应映射为稀有度");
AssertTrue(MixGreedySelectionDecision.CalculateRequiredMaterialCount(1, 1, 1) == 57,
    "稀有度1、乱舞1级且距下级1振时应还需57把素材");
AssertTrue(MixGreedySelectionDecision.CalculateRequiredMaterialCount(4, 5, 2) == 9,
    "稀有度4、乱舞5级且距下级2振时应还需9把素材");
AssertTrue(MixGreedySelectionDecision.TryParseSelectedCount(" 12／30 ", out var selected) && selected == 12,
    "应解析全角斜杠的已选数量");
AssertTrue(MixGreedySelectionDecision.TryParseSelectedCount("8/3O", out selected) && selected == 8,
    "应兼容容量30中的字母O误识别");
AssertFalse(MixGreedySelectionDecision.TryParseSelectedCount("无法识别", out _),
    "无斜杠的文本不应被识别为已选数量");
AssertTrue(MixGreedySelectionDecision.GetCancelCount(9, 30) == 21,
    "已选数量超额时应计算需取消的素材数");
AssertTrue(MixGreedySelectionDecision.GetCancelCount(30, 9) == -21,
    "已选数量不足时应保留负差值，以便直接习合");
var createPlanMethod = typeof(MixGreedySelectionDecision).GetMethod("CreatePlan");
var insufficientPlan = createPlanMethod?.Invoke(null, [10, 4]);
AssertTrue(insufficientPlan?.GetType().GetProperty("Mode")?.GetValue(insufficientPlan)?.ToString() == "Proceed",
    "素材不足时选材计划应直接进入习合");
var clearPlan = createPlanMethod?.Invoke(null, [6, 30]);
AssertTrue(clearPlan?.GetType().GetProperty("Mode")?.GetValue(clearPlan)?.ToString() == "ClearAndReselect",
    "超额超过15把时选材计划应全部解除后重选");
AssertFalse(MixGreedySelectionDecision.ShouldClearAllSelection(15),
    "超额15把时应逐把取消");
AssertTrue(MixGreedySelectionDecision.ShouldClearAllSelection(16),
    "超额超过15把时应先全部解除");
var manualClickAttemptsProperty = typeof(MixGreedySelectionDecision).GetProperty("ManualClickAttempts");
AssertTrue(manualClickAttemptsProperty?.GetValue(null) is 2,
    "手动选材首次未出现绿色状态时应重试一次");
var clearAllDelayProperty = typeof(MixGreedySelectionDecision).GetProperty("ClearAllDelayMilliseconds");
AssertTrue(clearAllDelayProperty?.GetValue(null) is 500,
    "全部解除后应等待500毫秒再检查首行绿色状态");
var clearAllAttemptsProperty = typeof(MixGreedySelectionDecision).GetProperty("ClearAllAttempts");
AssertTrue(clearAllAttemptsProperty?.GetValue(null) is 2,
    "首行仍为绿色时应最多再执行一次全部解除");
var swipeSettleDelayProperty = typeof(MixGreedySelectionDecision).GetProperty("SwipeSettleDelayMilliseconds");
AssertTrue(swipeSettleDelayProperty?.GetValue(null) is 500,
    "滑动结束后应等待500毫秒再检查素材选择状态");
var fallbackNeedRoi = typeof(MixGreedySelectionDecision).GetProperty("FallbackNeedOcrRoi")?.GetValue(null);
AssertTrue(fallbackNeedRoi?.ToString() == "MixGreedyRectangle { X = 431, Y = 342, Width = 15, Height = 17 }",
    "下一级需求的备用OCR区域应使用指定的小范围坐标");
var restoreAfterSwipeMethod = typeof(MixGreedySelectionDecision).GetMethod("ShouldRestoreLastSelectedMaterialAfterSwipe");
AssertTrue(restoreAfterSwipeMethod?.Invoke(null, [true, false]) is true,
    "滑动前第五行已选而滑动后第一行失去绿色状态时应恢复选择");
AssertTrue(restoreAfterSwipeMethod?.Invoke(null, [false, false]) is false,
    "滑动前第五行未选时不应恢复下一页第一行的选择");
AssertTrue(MixGreedySelectionDecision.CancelPositions.SequenceEqual([
    new MixGreedyPoint(857, 212),
    new MixGreedyPoint(858, 313),
    new MixGreedyPoint(858, 414),
    new MixGreedyPoint(858, 516),
    new MixGreedyPoint(858, 617),
]), "五行手动选材应仅将绿色状态识别点向左偏移242像素");

EdoRoutePlannerTests.Run();

AssertTrue(TeamSwitchDecision.GetTargetTeam("111112222", 9, 1) == 1,
    "第2轮应以选择部队作为当前部队进行比较");
AssertFalse(TeamSwitchDecision.ShouldSwitch("111112222", 8, 1, 1),
    "目标部队未变化时不应重复切换");
AssertTrue(TeamSwitchDecision.ShouldSwitch("111112222", 4, 1, 1),
    "目标部队从1变为2时应执行切换");
AssertFalse(TeamSwitchDecision.ShouldSwitch("111112222", 3, 2, 2),
    "切换到目标部队后不应再次切换");
AssertTrue(TeamSwitchDecision.ShouldSwitch("211112222", 9, 1, 1),
    "第2轮目标与初始部队不同时应执行切换");
AssertFalse(TeamSwitchState.ShouldSwitch("111112222", 9, 1),
    "状态判断应把选择部队1作为第2轮的当前部队");
AssertFalse(TeamSwitchState.ShouldSwitch("111112222", 8, 1),
    "连续目标为部队1时不应重复换队");
AssertTrue(TeamSwitchState.ShouldSwitch("111112222", 4, 1),
    "状态判断应在目标从部队1变为部队2时请求换队");
TeamSwitchState.SetCurrentTeam(2);
AssertFalse(TeamSwitchState.ShouldSwitch("111112222", 3, 1),
    "状态更新为部队2后不应重复换队");

AssertTrue(
    NewMixTargetSelectionDecision.Decide([
        new NewMixTargetSlot(false, false, null),
    ]).Outcome == NewMixTargetSelectionOutcome.NoSword,
    "一号位没有刀时应进入无刀分支");
AssertTrue(
    NewMixTargetSelectionDecision.Decide([
        new NewMixTargetSlot(true, false, 7),
        new NewMixTargetSlot(true, true, 7),
    ]) is { Outcome: NewMixTargetSelectionOutcome.Locked, Position: 2 },
    "带锁刀应无视乱舞等级优先进入专用链路");
AssertTrue(
    NewMixTargetSelectionDecision.Decide([
        new NewMixTargetSlot(true, false, 7),
        new NewMixTargetSlot(true, false, 6),
    ]) is { Outcome: NewMixTargetSelectionOutcome.Normal, Position: 2 },
    "未上锁且乱舞低于7级的刀应进入普通习合链路");
AssertTrue(
    NewMixTargetSelectionDecision.Decide([
        new NewMixTargetSlot(true, false, 7),
        new NewMixTargetSlot(true, false, 8),
    ]).Outcome == NewMixTargetSelectionOutcome.Completed,
    "所有可见未上锁刀均达到7级时应结束任务");
AssertTrue(
    NewMixTargetSelectionDecision.Decide([
        new NewMixTargetSlot(true, false, null),
        new NewMixTargetSlot(true, false, 6),
    ]) is { Outcome: NewMixTargetSelectionOutcome.Normal, Position: 2 },
    "未识别位置应跳过并继续选择后续可习合刀剑");

AssertTrue(EdoActionCountParser.Parse("６回") == 6,
    "行动次数 OCR 识别为全角数字时应仍能解析出次数");
AssertTrue(EdoActionCountParser.Parse("7回") == 7,
    "行动次数为七时应正确解析，不能误判为 OCR 失败");
AssertTrue(EdoActionCountParser.Parse("12回") == 12,
    "行动次数为两位数时应完整解析，不能只读取首位");
AssertTrue(EdoActionCountParser.Parse("１２回") == 12,
    "行动次数为全角两位数时应完整解析");
AssertTrue(EdoActionCountParser.Parse("冏一") == 1,
    "行动次数 OCR 识别为中文数字时应仍能解析出次数");
AssertTrue(EdoActionCountParser.Parse("冏12") == 12,
    "行动次数前缀被误识别时应仍能完整解析两位数字");
AssertTrue(EdoActionCountParser.Parse("一7") == 7,
    "同时出现汉字一与数字时应优先使用数字");
AssertTrue(EdoActionCountParser.Parse("二7") == 7,
    "同时出现其他汉字与数字时应忽略汉字");
AssertTrue(EdoActionCountParser.Parse("行动次数") == -1,
    "未识别到次数数字时应返回失败标记");
AssertTrue(EdoActionCountParser.Resolve(-1, "Start", 0) == -1,
    "开局行动次数无法 OCR 时不能假定固定次数");
AssertTrue(EdoActionCountParser.Resolve(-1, "P01", 6) == 6,
    "后续行动次数无法 OCR 时应使用已保存的剩余次数");

AssertTrue(RepairDetailFormatter.Format("太郎太刀", [632, 185, -1, 474]) == "太郎太刀 632/185/未识别/474",
    "修复资源部分 OCR 失败时应保留已识别的刀剑名和资源消耗");
AssertTrue(RepairDetailFormatter.Format("", [632, 185, -1, 474]) == "632/185/未识别/474",
    "修复刀剑名 OCR 失败时仍应输出已识别的资源消耗");

var emptyFilter = RepairFilterSelection.FromFlags(new Dictionary<string, bool>());
AssertFalse(emptyFilter.HasAnyFilter, "未选择任何筛选条件时不应启用筛选");

AssertTrue(SwordDropNotificationMatcher.ShouldNotify(true, ["今剑"], "今剑"),
    "开启播报且刀名在名单中时应触发通知");
AssertFalse(SwordDropNotificationMatcher.ShouldNotify(false, ["今剑"], "今剑"),
    "关闭播报开关时不应触发通知");
AssertFalse(SwordDropNotificationMatcher.ShouldNotify(true, ["厚藤四郎"], "今剑"),
    "识别到的刀名不在名单中时不应触发通知");
AssertTrue(SwordDropNotificationMatcher.FormatMessage("太刀", "狮子王") == "获得 太刀「狮子王」",
    "刀剑掉落通知文本应只包含获得信息");
AssertTrue(SwordDropNotificationMatcher.GetAnimationKind("特") == SwordDropAnimationKind.Specialization,
    "识别到特时应判定为特化动画");
AssertTrue(SwordDropNotificationMatcher.GetAnimationKind("极") == SwordDropAnimationKind.Kiwame,
    "识别到极时应判定为极化归来");
AssertTrue(SwordDropNotificationMatcher.GetAnimationKind("極") == SwordDropAnimationKind.Kiwame,
    "极化动画 OCR 识别为繁体极字时仍应识别为极化");
AssertTrue(SwordDropNotificationMatcher.IsColorWithinTolerance(195, 13, 24, 195, 13, 24, 1),
    "初掉落颜色命中目标色时应通过校验");
AssertTrue(SwordDropNotificationMatcher.IsColorWithinTolerance(194, 12, 23, 195, 13, 24, 1),
    "初掉落颜色在容差范围内时应通过校验");
AssertFalse(SwordDropNotificationMatcher.IsColorWithinTolerance(193, 13, 24, 195, 13, 24, 1),
    "初掉落颜色超出容差时不应通过校验");

var selectedFilter = RepairFilterSelection.FromFlags(new Dictionary<string, bool>
{
    ["sword_type_短"] = true,
    ["sword_type_太"] = true,
    ["damage_重伤"] = true,
});
AssertTrue(selectedFilter.HasAnyFilter, "选择刀种或伤势后应启用筛选");
AssertTrue(selectedFilter.SwordTypes.SetEquals(["短", "太"]), "刀种筛选应保留全部已选项");
AssertTrue(selectedFilter.DamageStates.SetEquals(["重伤"]), "伤势筛选应保留已选项");
AssertTrue(RepairFilterSelection.IsFilterTitle("师选"), "筛选标题 OCR 误识别为师选时仍应视为筛选标题");
AssertFalse(RepairFilterSelection.IsFilterTitle("刀剑男士"), "无关 OCR 文本不应视为筛选标题");

var cooldownStart = new DateTime(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc);
RepairCooldownState.Start(cooldownStart);
AssertTrue(RepairCooldownState.IsActive(cooldownStart.AddMinutes(29).AddSeconds(59)),
    "无可修刀后 30 分钟内应保持修刀冷却");
AssertFalse(RepairCooldownState.IsActive(cooldownStart.AddMinutes(30)),
    "修刀冷却达到 30 分钟后应自动恢复");

AssertTrue(FatigueCheckDecision.ShouldContinueWhenFirstValueUnreadable(null),
    "首位疲劳值无法识别时应继续出阵，不应进入刷花");
AssertFalse(FatigueCheckDecision.ShouldContinueWhenFirstValueUnreadable(29),
    "首位疲劳值低于阈值时应保留刷花分支");

AssertTrue(RepairListOcrDecision.IsSameValidResult("甲\n乙", "甲乙"),
    "滑动前后有效 OCR 文本相同应判定列表未变化");
AssertFalse(RepairListOcrDecision.IsSameValidResult("甲", "乙"),
    "滑动前后 OCR 文本变化时应继续扫描");
AssertFalse(RepairListOcrDecision.IsSameValidResult(null, null),
    "OCR 无结果时不应误判为列表到底");

var preset = new FormationPreset();

AssertFalse(preset.ClearEquipmentBeforeFormation, "新预设默认不应卸下现有装备");
AssertFalse(preset.SaveGameFormationRecordAfterFormation, "新预设默认不应保存游戏部队记录");

FormationPreset.SetRecordMode(preset, useRecordOnly: true);
AssertTrue(preset.UseGameFormationRecordOnly && !preset.SaveGameFormationRecordOnly,
    "仅使用编队记录与仅记录编队不能同时启用");
FormationPreset.SetRecordMode(preset, saveRecordOnly: true);
AssertTrue(!preset.UseGameFormationRecordOnly && preset.SaveGameFormationRecordOnly,
    "启用仅记录编队时应自动关闭仅使用编队记录");

var entry = new SwordBookEntry("3", "短刀", "今剑");
var editor = new SwordBookEditor(new[] { entry });
editor.SetOwned("3", SwordPortraitType.Wounded, true);
editor.Revert();
AssertFalse(editor.Entries[0].Wounded, "撤销应恢复上次保存的立绘状态");

editor.SetOwned("3", SwordPortraitType.Wounded, true);
editor.Save();
editor.SetOwned("3", SwordPortraitType.Wounded, false);
editor.Revert();
AssertTrue(editor.Entries[0].Wounded, "撤销应恢复已保存的立绘状态");

var logStart = new DateTime(2026, 8, 21, 3, 44, 55);
var dailyPipeline = JObject.Parse(File.ReadAllText(Path.Combine(
    Directory.GetCurrentDirectory(), "assets", "resource", "base", "pipeline", "DailyTask.json")));
var interfaceJson = JObject.Parse(File.ReadAllText(Path.Combine(
    Directory.GetCurrentDirectory(), "assets", "interface.json")));
var dailyDisassembleEnabledOverride = interfaceJson["option"]?["D_刀解"]?["cases"]?
    .FirstOrDefault(item => (string?)item?["name"] == "Yes")?["pipeline_override"] as JObject;
AssertTrue(
    dailyPipeline["DT_ForgeCheckCapacity"]?["next"]?.Values<string>()
        .SequenceEqual(["DT_ForgeClaimCompletedHub"]) == true
    && dailyPipeline["DT_ForgeCheckCapacity"]?["on_error"]?.Values<string>()
        .SequenceEqual(["DT_ForgeSkipDisassemble"]) == true
    && dailyPipeline["DT_ForgeSkipDisassemble"]?["action"]?["custom_action"]?.Value<string>() == "GuiLogAction",
    "未开启刀解时，收刀所需刀位不足必须跳过收刀和锻刀");
AssertTrue(
    dailyDisassembleEnabledOverride?["DT_ForgeCheckCapacity"]?["next"]?.Values<string>()
        .SequenceEqual(["DT_ForgeDailyDisassembleOnceCheck"]) == true
    && dailyDisassembleEnabledOverride?["DT_ForgeCheckCapacity"]?["on_error"]?.Values<string>()
        .SequenceEqual(["DT_ForgeDisassembleHub"]) == true,
    "开启刀解时，刀位不足必须先按缺口刀解腾位，刀位充足才检查当天刀解状态");
AssertTrue(
    dailyDisassembleEnabledOverride?["DT_ForgeDailyDisassembleOnceCheck"]?["enabled"]?.Value<bool>() == true
    && dailyDisassembleEnabledOverride?["DT_ForgeDailyDisassembleAlreadyCompleted"]?["next"]?.Values<string>()
        .SequenceEqual(["DT_ForgeClaimCompletedHub"]) == true,
    "开启刀解时，当天尚未刀解要先刀解一把，当天已刀解则直接进入收刀");
AssertTrue(
    dailyPipeline["DT_ForgeDisassembleSelectAllowed"]?["action"]?["custom_action_param"]?["minimum_count"]?.Value<int>() == 1
    && dailyPipeline["DT_ForgeDisassembleCompleted"]?["action"]?["custom_action_param"]?["item"]?.Value<string>() == "disassemble",
    "首次刀解至少选择一把，收刀腾位刀解同样必须写入当天刀解完成记录");
var forgeEnabledOverride = interfaceJson["option"]?["D_锻刀"]?["cases"]?
    .FirstOrDefault(item => (string?)item?["name"] == "Yes")?["pipeline_override"] as JObject;
AssertTrue(
    dailyPipeline["DT_ForgeAlreadyCompleted"]?["next"]?.Values<string>()
        .SequenceEqual(["DT_ForgeHub"]) == true
    && dailyPipeline["DT_ForgeClaimCompletedHub"]?["on_error"]?.Values<string>()
        .SequenceEqual(["DT_ForgeStartForgeOnceCheck"]) == true
    && dailyPipeline["DT_ForgeStartForgeOnceCheck"]?["action"]?["custom_action"]?.Value<string>() == "DailyTaskCompletionCheckAction"
    && dailyPipeline["DT_ForgeStartForgeOnceCheck"]?["next"]?.Values<string>()
        .SequenceEqual(["DT_ForgeFindFreeSlot1Hub"]) == true
    && dailyPipeline["DT_ForgeStartForgeOnceCheck"]?["on_error"]?.Values<string>()
        .SequenceEqual(["DT_ForgeReturnHomeHub"]) == true
    && forgeEnabledOverride?["DT_ForgeStartForgeOnceCheck"]?["enabled"]?.Value<bool>() == true,
    "当天锻刀已完成时必须先收取已有完成刀剑，再跳过新建锻刀");
var forgeDisassembleSelectActionSource = File.ReadAllText(Path.Combine(
    Directory.GetCurrentDirectory(), "_src", "MFAAvalonia", "Extensions", "MaaFW", "Custom", "ForgeDisassembleSelectAction.cs"));
AssertTrue(
    forgeDisassembleSelectActionSource.Contains("requiredCount - selectedCount", StringComparison.Ordinal)
    && forgeDisassembleSelectActionSource.Contains(".Take(maximumSelectCount)", StringComparison.Ordinal),
    "刀解选择必须只点击当前剩余所需数量，不能将当前页全部许可刀剑都选中");
var freezeRestartOverride = interfaceJson["option"]?["卡死重启"]?["cases"]?
    .FirstOrDefault(item => (string?)item?["name"] == "Yes")?["pipeline_override"] as JObject;
var fallbackWaitNames = Directory.GetFiles(Path.Combine(
        Directory.GetCurrentDirectory(), "assets", "resource", "base", "pipeline"), "*.json")
    .SelectMany(file => JObject.Parse(File.ReadAllText(file)).Properties())
    .Where(property => property.Name.EndsWith("FallbackWait", StringComparison.Ordinal))
    .Select(property => property.Name)
    .Distinct(StringComparer.Ordinal)
    .ToList();
AssertTrue(
    fallbackWaitNames.All(name => freezeRestartOverride?[name]?["enabled"]?.Value<bool>() == false),
    $"卡死重启开关应覆盖全部 FallbackWait：{string.Join(", ", fallbackWaitNames.Where(name => freezeRestartOverride?[name]?["enabled"]?.Value<bool>() != false))}");
AssertTrue(
    freezeRestartOverride?["DT_LoginRewardHub"]?["on_error"]?.Values<string>()
        .SequenceEqual(["DT_RestartGameReturnLoginRewardHub"]) == true
    && dailyPipeline["DT_RestartGameReturnLoginRewardHub"]?["action"]?["custom_action"]?.Value<string>() == "RestartGameAction"
    && dailyPipeline["DT_RestartGameReturnLoginRewardHub"]?["next"]?.Values<string>()
        .SequenceEqual(["DT_LoginRewardHub"]) == true,
    "开启卡死重启后，日课登录奖励 hub 超时必须重启游戏并回到登录奖励 hub");
var freezeTimeoutOverride = interfaceJson["option"]?["卡死等待时间"]?["pipeline_override"] as JObject;
AssertTrue(
    freezeTimeoutOverride?["DT_DetectWhereAmI"]?["timeout"]?.Value<string>() == "{timeout_seconds}000",
    "卡死等待时间应覆盖日课 DT_DetectWhereAmI，不能继续使用固定10秒超时");
for (var index = 1; index <= 5; index++)
{
    var enterTrainingAction = dailyPipeline[$"DT_DrillEnterTraining{index}"]?["action"];
    AssertTrue((string?)enterTrainingAction?["custom_action"] == "LogAction"
        && (string?)enterTrainingAction?["custom_action_param"]?["message"] == "[日课] 出阵",
        $"日课演练位置 {index} 进入战斗后应记录出阵");
}
var drillVictoryActionSource = File.ReadAllText(Path.Combine(
    Directory.GetCurrentDirectory(), "_src", "MFAAvalonia", "Extensions", "MaaFW", "Custom", "DrillVictoryAction.cs"));
AssertTrue(drillVictoryActionSource.Contains("LoggerHelper.Info(\"[日课] 完成一圈\");", StringComparison.Ordinal),
    "日课演练胜利后应记录完成一圈");

var missingInstanceRecord = WorkRecordBuilder.Build([
    new LogEntry(logStart, "INF", "[cfg=Default][inst=配置 1/default] 开始任务：地下城", "Default", "default"),
    new LogEntry(logStart.AddSeconds(1), "INF", "[cfg=Default] [地下城] 出阵", "Default"),
    new LogEntry(logStart.AddSeconds(2), "INF", "[cfg=Default][inst=配置 1/default] 停止前状态：SUCCEEDED", "Default", "default"),
]);
AssertTrue(missingInstanceRecord.Count == 1 && missingInstanceRecord[0].SortieCount == 1,
    "缺少实例标识的业务日志应继承相邻实例归属");
var parsedInstanceEntry = LogParser.ParseLines([
    "[2026-08-21 03:44:55.000][INF] [cfg=Default][inst=配置 1/default] [地下城] 出阵",
]).Single();
AssertTrue(parsedInstanceEntry.InstanceId == "default", "日志解析应提取实例 ID");
AssertTrue(parsedInstanceEntry.Content.Contains("[地下城] 出阵"), "日志解析应保留业务内容");

var instanceRecord = WorkRecordBuilder.Build([
    new LogEntry(logStart, "INF", "[cfg=Default][inst=配置 1/default] 开始任务：地下城", "Default", "default"),
    new LogEntry(logStart.AddSeconds(1), "INF", "[cfg=Default][inst=配置 1/default] [地下城] 出阵", "Default", "default"),
    new LogEntry(logStart.AddSeconds(2), "INF", "[cfg=Default][inst=配置 1/default] 停止前状态：SUCCEEDED", "Default", "default"),
]);
AssertTrue(instanceRecord.Count == 1 && instanceRecord[0].ConfigName == "default",
    "工作记录应使用实例 ID 作为配置归属");

var workRecord = WorkRecordBuilder.Build([
    new LogEntry(logStart, "INF", "开始任务：地下城"),
    new LogEntry(logStart.AddSeconds(0), "INF", "[地下城] 小判箱掉落"),
    new LogEntry(logStart.AddSeconds(1), "INF", "[地下城] 小判箱掉落"),
    new LogEntry(logStart.AddSeconds(5), "INF", "[地下城] 小判箱掉落"),
    new LogEntry(logStart.AddSeconds(6), "INF", "[地下城] 小判箱掉落"),
    new LogEntry(logStart.AddSeconds(7), "INF", "[地下城] 刀剑掉落 短刀 今剑"),
    new LogEntry(logStart.AddSeconds(8), "INF", "[地下城] 小判箱掉落"),
    new LogEntry(logStart.AddSeconds(12), "INF", "[地下城] 小判箱掉落"),
    new LogEntry(logStart.AddSeconds(13), "INF", "停止前状态：SUCCEEDED"),
]);
AssertTrue(workRecord.Count == 1, "测试日志应聚合为一条工作记录");
AssertTrue(workRecord[0].ResourceGains["小判箱"] == 2, "连续小判箱日志应各自只计为一次掉落");
AssertTrue(workRecord[0].Status == "成功", "存在成功停止状态时应显示成功");
var completedDisplayRecord = new WorkRecord { Status = "成功" };
var manuallyStoppedDisplayRecord = new WorkRecord { Status = "手动停止" };
AssertTrue(completedDisplayRecord.DisplayStatus == "结束"
    && manuallyStoppedDisplayRecord.DisplayStatus == "结束"
    && completedDisplayRecord.StatusForeground == "#15803D"
    && manuallyStoppedDisplayRecord.StatusForeground == "#15803D"
    && completedDisplayRecord.StatusBackground == "#F0FDF4"
    && manuallyStoppedDisplayRecord.StatusBackground == "#F0FDF4",
    "成功和手动停止在工作记录中都应显示为绿色的结束状态");

var resourceGainRecord = WorkRecordBuilder.Build([
    new LogEntry(logStart, "INF", "开始任务：地下城"),
    new LogEntry(logStart.AddSeconds(1), "INF", "[地下城] 资源点获取 木炭x20 玉钢x60"),
    new LogEntry(logStart.AddSeconds(2), "INF", "停止前状态：SUCCEEDED"),
]);
AssertTrue(resourceGainRecord.Count == 1
    && resourceGainRecord[0].ResourceGains.GetValueOrDefault("木炭") == 20
    && resourceGainRecord[0].ResourceGains.GetValueOrDefault("玉钢") == 60,
    "资源点获取打点应计入工作记录的资源获取");

var runningRecord = WorkRecordBuilder.Build([
    new LogEntry(logStart, "INF", "开始任务：地下城"),
    new LogEntry(logStart.AddSeconds(1), "INF", "[地下城] 点击行军"),
]);
AssertTrue(runningRecord.Count == 1, "运行中的任务应保留为一条工作记录");
AssertTrue(runningRecord[0].Status == "进行中", "没有停止状态的运行记录应显示进行中");

var swordBookScanRecords = WorkRecordBuilder.Build([
    new LogEntry(logStart, "INF", "开始任务：地下城"),
    new LogEntry(logStart.AddSeconds(1), "INF", "[地下城] 点击行军"),
    new LogEntry(logStart.AddSeconds(2), "INF", "开始任务：刀帐自动识别"),
    new LogEntry(logStart.AddSeconds(3), "WRN", "[刀帐] 自动识别失败"),
    new LogEntry(logStart.AddSeconds(4), "INF", "停止前状态：SUCCEEDED"),
]);
AssertTrue(swordBookScanRecords.Count == 1 && swordBookScanRecords[0].TaskName == "地下城",
    "刀帐自动识别不应单独显示，也不应作为上一任务的业务记录");
AssertTrue(swordBookScanRecords[0].SpecialEvents.Count == 0,
    "刀帐自动识别期间的日志不应附加到上一条业务记录");

var mixRecords = WorkRecordBuilder.Build([
    new LogEntry(logStart, "INF", "开始任务：习合"),
    new LogEntry(logStart.AddSeconds(1), "INF", "[习合] 完成一圈"),
    new LogEntry(logStart.AddSeconds(2), "INF", "停止前状态：SUCCEEDED"),
]);
AssertTrue(mixRecords.Count == 0,
    "习合搓糖任务不应显示在工作记录中");

var labeledMixRecords = WorkRecordBuilder.Build([
    new LogEntry(logStart, "INF", "开始任务：习合搓糖"),
    new LogEntry(logStart.AddSeconds(1), "INF", "[习合] 完成一圈"),
    new LogEntry(logStart.AddSeconds(2), "INF", "停止前状态：SUCCEEDED"),
]);
AssertTrue(labeledMixRecords.Count == 0,
    "以习合搓糖标签启动的任务不应显示在工作记录中");

var adbWarningRecord = WorkRecordBuilder.Build([
    new LogEntry(logStart, "INF", "开始任务：地下城"),
    new LogEntry(logStart.AddSeconds(1), "WRN", "[卡死重启] 模拟器无响应：超过 120 秒无回调"),
    new LogEntry(logStart.AddSeconds(2), "WRN", "[RestartGameAction] ADB 命令返回警告: device offline"),
]);
AssertTrue(
    adbWarningRecord[0].SpecialEvents.Count == 1
        && adbWarningRecord[0].SpecialEvents[0].Description.Contains("模拟器无响应")
        && adbWarningRecord[0].SpecialEvents[0].Description.Contains("120 秒无回调")
        && adbWarningRecord[0].SpecialEvents.All(eventItem => !eventItem.Description.Contains("device offline")),
    "卡死重启期间的 ADB 警告不应与统一卡死事件重复显示");

var autoRecoveryRecords = WorkRecordBuilder.Build([
    new LogEntry(logStart, "INF", "开始任务：合战场"),
    new LogEntry(logStart.AddSeconds(1), "WRN", "[重启游戏] 检测到游戏疑似卡死：动作循环：node=S_IsBattleResult_Exp, action=Click"),
    new LogEntry(logStart.AddSeconds(2), "WRN", "[重启模拟器] 游戏重启失败，重启模拟器"),
]);
AssertTrue(
    autoRecoveryRecords.Count == 1
        && autoRecoveryRecords[0].SpecialEvents.Count == 2
        && autoRecoveryRecords[0].SpecialEvents[0].Description == "检测到游戏疑似卡死：动作循环：node=S_IsBattleResult_Exp, action=Click"
        && autoRecoveryRecords[0].SpecialEvents[1].Description == "游戏重启失败，重启模拟器",
    "重启游戏与重启模拟器必须以不同的具体文案进入工作记录特殊情况");

var earlyEndRecord = WorkRecordBuilder.Build([
    new LogEntry(logStart, "INF", "开始任务：合战场"),
    new LogEntry(logStart.AddSeconds(1), "INF", "[合战场] 出阵"),
    new LogEntry(logStart.AddSeconds(2), "INF", "[合战场] 道中撤退"),
    new LogEntry(logStart.AddSeconds(3), "INF", "停止前状态：STOPPED"),
]);
AssertTrue(earlyEndRecord.Count == 1 && earlyEndRecord[0].ReturnHomeCount == 0,
    "撤退原因本身不应重复计入返回本丸次数");
AssertTrue(earlyEndRecord[0].DisplayStatus == "结束",
    "手动停止或任务失败导致的结束应在工作记录中显示为结束");

var earlyCompletionReasonRecord = WorkRecordBuilder.Build([
    new LogEntry(logStart, "INF", "开始任务：合战场"),
    new LogEntry(logStart.AddSeconds(1), "WRN", "[Record] [合战场] 任务结束 原因：重伤停止"),
    new LogEntry(logStart.AddSeconds(2), "INF", "停止前状态：SUCCEEDED"),
]).Single();
AssertTrue(earlyCompletionReasonRecord.SpecialEvents.Exists(item => item.Description == "任务结束 原因：重伤停止"),
    "提前结束原因必须以 Warning 词表行进入工作记录特殊情况");

var retreatFilterRecord = WorkRecordBuilder.Build([
    new LogEntry(logStart, "INF", "开始任务：合战场"),
    new LogEntry(logStart.AddSeconds(1), "WRN", "[合战场] 重伤撤退"),
    new LogEntry(logStart.AddSeconds(10), "WRN", "[合战场] 刀装破坏撤退"),
    new LogEntry(logStart.AddSeconds(20), "WRN", "[合战场] 重伤撤退"),
    new LogEntry(logStart.AddSeconds(29), "WRN", "[合战场] 重伤撤退"),
    new LogEntry(logStart.AddSeconds(32), "WRN", "[合战场] 重伤撤退"),
    new LogEntry(logStart.AddSeconds(33), "WRN", "[合战场] 刀装破坏撤退"),
    new LogEntry(logStart.AddSeconds(34), "WRN", "[合战场] 疲劳撤退"),
    new LogEntry(logStart.AddSeconds(35), "INF", "[合战场] 命中王点"),
    new LogEntry(logStart.AddSeconds(36), "INF", "停止前状态：STOPPED"),
]);
AssertTrue(
    retreatFilterRecord.Count == 1
    && retreatFilterRecord[0].SpecialEvents.Count == 4
    && retreatFilterRecord[0].SpecialEvents.Count(eventItem => eventItem.Description == "重伤撤退") == 2
    && retreatFilterRecord[0].SpecialEvents.Count(eventItem => eventItem.Description == "刀装破坏撤退") == 1
    && retreatFilterRecord[0].SpecialEvents.Exists(eventItem => eventItem.Description == "疲劳撤退"),
    "同类撤退信息应在30秒内合并，不同撤退原因和王点信息不应互相合并");

var interleavedRepeatRecord = WorkRecordBuilder.Build([
    new LogEntry(logStart, "INF", "开始任务：地下城"),
    new LogEntry(logStart.AddSeconds(1), "INF", "[地下城] 点击行军"),
    new LogEntry(logStart.AddSeconds(2), "INF", "[地下城] 出阵"),
    new LogEntry(logStart.AddSeconds(3), "INF", "[地下城] 点击行军"),
    new LogEntry(logStart.AddSeconds(4), "INF", "停止前状态：SUCCEEDED"),
]);
AssertTrue(
    interleavedRepeatRecord.Count == 1 && interleavedRepeatRecord[0].MarchCount == 1,
    "通用重复过滤应按词条内容独立计时，不应被交错的其他日志覆盖");

var contextualRepeatRecord = WorkRecordBuilder.Build([
    new LogEntry(logStart, "INF", "开始任务：地下城"),
    new LogEntry(logStart.AddSeconds(1), "INF", "[cfg=Default] [地下城] 点击行军"),
    new LogEntry(logStart.AddSeconds(2), "INF", "[cfg=Default][inst=日常/default] [地下城] 点击行军"),
    new LogEntry(logStart.AddSeconds(3), "INF", "停止前状态：SUCCEEDED"),
]);
AssertTrue(
    contextualRepeatRecord.Count == 1 && contextualRepeatRecord[0].MarchCount == 1,
    "重复过滤不应受业务词条前上下文块格式差异影响");

var returnHomeRecord = WorkRecordBuilder.Build([
    new LogEntry(logStart, "INF", "开始任务：地下城"),
    new LogEntry(logStart.AddSeconds(1), "INF", "[地下城] 返回本丸"),
    new LogEntry(logStart.AddSeconds(2), "INF", "停止前状态：SUCCEEDED"),
]);
AssertTrue(returnHomeRecord.Count == 1 && returnHomeRecord[0].ReturnHomeCount == 1,
    "确认返回本丸日志应计入返回本丸次数");

var undergroundEarlyEndRecord = WorkRecordBuilder.Build([
    new LogEntry(logStart, "INF", "开始任务：地下城"),
    new LogEntry(logStart.AddSeconds(1), "WRN", "[地下城] 刀装近破坏撤退"),
    new LogEntry(logStart.AddSeconds(2), "INF", "[远征计时] 倒计时结束"),
    new LogEntry(logStart.AddSeconds(3), "INF", "[地下城] 返回本丸"),
    new LogEntry(logStart.AddSeconds(4), "INF", "停止前状态：STOPPED"),
]);
AssertTrue(undergroundEarlyEndRecord.Count == 1 && undergroundEarlyEndRecord[0].ReturnHomeCount == 1,
    "地下城应只按确认返回本丸日志计数");
AssertTrue(undergroundEarlyEndRecord[0].SpecialEvents.Exists(e => e.Description == "刀装近破坏撤退"),
    "刀装近破坏撤退仍应作为特殊情况记录");
AssertTrue(undergroundEarlyEndRecord[0].LogisticsCounts["倒计时结束"] == 1,
    "远征倒计时结束应计入后勤记录");

var dragCaptainWarningRecord = WorkRecordBuilder.Build([
    new LogEntry(logStart, "INF", "开始任务：江户潜入"),
    new LogEntry(logStart.AddSeconds(1), "INF", "[江户潜入] 出阵"),
    new LogEntry(logStart.AddSeconds(2), "WRN", "[DragCaptain] 无可用位置（空槽位或 OCR 失败），跳过拖拽"),
    new LogEntry(logStart.AddSeconds(3), "INF", "停止前状态：SUCCEEDED"),
]);
AssertTrue(dragCaptainWarningRecord[0].SpecialEvents.Count == 0,
    "换队长拖拽的无可用位置保护日志不应显示为特殊情况");

var switchedTaskRecords = WorkRecordBuilder.Build([
    new LogEntry(logStart, "INF", "开始任务：异去"),
    new LogEntry(logStart.AddSeconds(1), "INF", "[异去] 完成一圈"),
    new LogEntry(logStart.AddSeconds(2), "INF", "开始任务：回本丸"),
    new LogEntry(logStart.AddSeconds(3), "INF", "停止前状态：SUCCEEDED"),
]);
AssertTrue(switchedTaskRecords.Count == 1 && switchedTaskRecords[0].TaskName == "异去",
    "任务切换时应保留上一条有业务数据的记录");
AssertTrue(switchedTaskRecords[0].Status == "成功", "正常切换到下一个任务时上一条记录应显示成功");

var returnHomeTaskRecords = WorkRecordBuilder.Build([
    new LogEntry(logStart, "INF", "开始任务：回本丸"),
    new LogEntry(logStart.AddSeconds(1), "INF", "[地下城] 返回本丸"),
    new LogEntry(logStart.AddSeconds(2), "INF", "停止前状态：SUCCEEDED"),
]);
AssertTrue(returnHomeTaskRecords.All(record => record.TaskName != "回本丸"),
    "回本丸流程不应作为独立工作记录显示");

var parallelTaskRecords = WorkRecordBuilder.Build([
    new LogEntry(logStart, "INF", "[任务管线合并] 任务#1 名称=[地下城] 入口=[Underground]"),
    new LogEntry(logStart, "INF", "[任务管线合并] 任务#2 名称=[后勤] 入口=[Expedition]"),
    new LogEntry(logStart, "INF", "开始任务：地下城"),
    new LogEntry(logStart.AddSeconds(1), "INF", "开始任务：后勤"),
    new LogEntry(logStart.AddSeconds(2), "INF", "[entry=Underground] [地下城] 刀剑掉落 短刀 秋田藤四郎"),
    new LogEntry(logStart.AddSeconds(3), "INF", "[entry=Expedition] [后勤] 派遣远征 部队1已派遣至 2-4"),
    new LogEntry(logStart.AddSeconds(4), "INF", "停止前状态：SUCCEEDED"),
]);
var parallelDungeon = parallelTaskRecords.Single(record => record.TaskName == "地下城");
var parallelLogistics = parallelTaskRecords.Single(record => record.TaskName == "后勤");
AssertTrue(parallelDungeon.SwordDrops.Count == 1, "并行任务中地下城掉落应归入地下城记录");
AssertTrue(parallelLogistics.SwordDrops.Count == 0, "并行任务中后勤记录不应包含地下城掉落");

var renamedParallelTaskRecords = WorkRecordBuilder.Build([
    new LogEntry(logStart, "INF", "[任务管线合并] 任务#1 名称=[大阪挖地] 入口=[Underground]"),
    new LogEntry(logStart, "INF", "[任务管线合并] 任务#2 名称=[本丸后勤] 入口=[Expedition]"),
    new LogEntry(logStart.AddSeconds(1), "INF", "开始任务：大阪挖地"),
    new LogEntry(logStart.AddSeconds(2), "INF", "开始任务：本丸后勤"),
    new LogEntry(logStart.AddSeconds(3), "INF", "[地下城] 刀剑掉落 短刀 秋田藤四郎"),
    new LogEntry(logStart.AddSeconds(4), "INF", "[后勤] 派遣远征 部队1已派遣至 2-4"),
    new LogEntry(logStart.AddSeconds(5), "INF", "停止前状态：SUCCEEDED"),
]);
var renamedDungeon = renamedParallelTaskRecords.Single(record => record.TaskName == "大阪挖地");
var renamedLogistics = renamedParallelTaskRecords.Single(record => record.TaskName == "本丸后勤");
AssertTrue(renamedDungeon.SwordDrops.Count == 1, "大阪挖地的地下城掉落应按 Entry 归属");
AssertTrue(renamedLogistics.LogisticsCounts["派遣远征"] == 1, "改名后的后勤日志应归入本丸后勤记录");

var remarkedParallelTaskRecords = WorkRecordBuilder.Build([
    new LogEntry(logStart, "INF", "[任务管线合并] 任务#1 名称=[我的地下城备注] 入口=[Underground]"),
    new LogEntry(logStart, "INF", "[任务管线合并] 任务#2 名称=[我的后勤备注] 入口=[Expedition]"),
    new LogEntry(logStart.AddSeconds(1), "INF", "开始任务：我的地下城备注"),
    new LogEntry(logStart.AddSeconds(2), "INF", "开始任务：我的后勤备注"),
    new LogEntry(logStart.AddSeconds(3), "INF", "[地下城] 刀剑掉落 短刀 秋田藤四郎"),
    new LogEntry(logStart.AddSeconds(4), "INF", "[后勤] 派遣远征 部队1已派遣至 2-4"),
    new LogEntry(logStart.AddSeconds(5), "INF", "停止前状态：SUCCEEDED"),
]);
var remarkedDungeon = remarkedParallelTaskRecords.FirstOrDefault(record => record.TaskName == "我的地下城备注");
var remarkedLogistics = remarkedParallelTaskRecords.FirstOrDefault(record => record.TaskName == "我的后勤备注");
AssertTrue(remarkedDungeon != null, "自定义备注的地下城任务应保留为工作记录");
AssertTrue(remarkedLogistics != null, "自定义备注的后勤任务应保留为工作记录");
if (remarkedDungeon != null && remarkedLogistics != null)
{
    AssertTrue(remarkedDungeon.SwordDrops.Count == 1, "自定义备注不应影响地下城业务日志归属");
    AssertTrue(remarkedLogistics.LogisticsCounts["派遣远征"] == 1, "自定义备注不应影响后勤业务日志归属");
}

var finishedLogisticsThenDungeonRecords = WorkRecordBuilder.Build([
    new LogEntry(logStart, "INF", "开始任务：后勤"),
    new LogEntry(logStart.AddSeconds(1), "INF", "[后勤] 检查队伍状况"),
    new LogEntry(logStart.AddSeconds(2), "INF", "停止前状态：SUCCEEDED"),
    new LogEntry(logStart.AddSeconds(3), "INF", "开始任务：地下城"),
    new LogEntry(logStart.AddSeconds(4), "INF", "[后勤] 派遣远征 部队1已派遣至 2-4"),
    new LogEntry(logStart.AddSeconds(5), "INF", "[地下城] 点击行军"),
]);
var laterDungeon = finishedLogisticsThenDungeonRecords.Single(record => record.TaskName == "地下城");
AssertTrue(laterDungeon.LogisticsCounts["派遣远征"] == 1,
    "已结束的后勤记录不应接收后续地下城运行期间的后勤词条");

var supplementRecord = WorkRecordBuilder.Build([
    new LogEntry(logStart, "INF", "开始任务：地下城"),
    new LogEntry(logStart.AddSeconds(1), "INF", "[地下城] 补充刀装"),
    new LogEntry(logStart.AddSeconds(2), "INF", "停止前状态：SUCCEEDED"),
]).Single();
AssertTrue(supplementRecord.SpecialEvents.Any(item => item.Description == "补充刀装"),
    "出阵任务产生的补充刀装应显示在特殊情况中");

var logisticsSupplementRecord = WorkRecordBuilder.Build([
    new LogEntry(logStart, "INF", "开始任务：后勤"),
    new LogEntry(logStart.AddSeconds(1), "INF", "[后勤] 补充刀装"),
    new LogEntry(logStart.AddSeconds(2), "INF", "停止前状态：SUCCEEDED"),
]).Single();
AssertTrue(logisticsSupplementRecord.LogisticsCounts["补充刀装"] == 1,
    "后勤任务产生的补充刀装应显示在后勤记录中");

var partialRepairRecords = WorkRecordBuilder.Build([
    new LogEntry(logStart, "INF", "开始任务：后勤"),
    new LogEntry(logStart.AddSeconds(1), "INF", "[后勤] 开始修复 谦信景光 1/22/未识别/11"),
    new LogEntry(logStart.AddSeconds(2), "INF", "停止前状态：SUCCEEDED"),
]);
AssertTrue(partialRepairRecords.Count == 1
    && partialRepairRecords[0].LogisticsCounts.GetValueOrDefault("开始修复") == 1
    && partialRepairRecords[0].LogisticsRepairs.Count == 1
    && partialRepairRecords[0].LogisticsRepairs[0].SwordName == "谦信景光"
    && partialRepairRecords[0].LogisticsRepairs[0].Coolant == -1,
    "修刀资源部分未识别时仍应保留后勤修刀记录与已识别信息");

var sortieRepairRecord = WorkRecordBuilder.Build([
    new LogEntry(logStart, "INF", "开始任务：常驻作战"),
    new LogEntry(logStart.AddSeconds(1), "WRN", "[合战场] 修复 太郎太刀 632/185/未识别/474"),
    new LogEntry(logStart.AddSeconds(2), "INF", "停止前状态：SUCCEEDED"),
]).Single();
AssertTrue(sortieRepairRecord.SpecialEvents.Count == 1
    && sortieRepairRecord.SpecialEvents[0].Description == "修复 太郎太刀 632/185/未识别/474",
    "出阵任务产生的修刀 Warning 词条应显示在特殊情况板块");

var avoidKebiRecord = WorkRecordBuilder.Build([
    new LogEntry(logStart, "INF", "开始任务：常驻作战"),
    new LogEntry(logStart.AddSeconds(1), "WRN", "[cfg=Default][inst=配置 1/default][src=Monitor][op=MonitorLog] [Record] [重启游戏] 遭遇检非"),
    new LogEntry(logStart.AddSeconds(2), "INF", "停止前状态：SUCCEEDED"),
]).Single();
AssertTrue(avoidKebiRecord.SpecialEvents.Count == 1
    && avoidKebiRecord.SpecialEvents[0].Description == "遭遇检非违使，重启游戏",
    "避战检非的重启警告必须显示在工作记录的特殊情况中");

var firstSavedSource = new WorkRecord
{
    TaskName = "地下城",
    StartTime = new DateTime(2026, 8, 20, 10, 0, 0),
    EndTime = new DateTime(2026, 8, 20, 10, 20, 0),
    Status = "成功",
    SortieCount = 1,
    RoundCount = 2,
};
firstSavedSource.ResourceGains["木炭"] = 120;
var secondSavedSource = new WorkRecord
{
    TaskName = "地下城",
    StartTime = new DateTime(2026, 8, 21, 11, 0, 0),
    EndTime = new DateTime(2026, 8, 21, 11, 30, 0),
    Status = "成功",
    SortieCount = 2,
    RoundCount = 3,
};
secondSavedSource.ResourceGains["木炭"] = 80;
var merged = SavedWorkRecordService.Merge([firstSavedSource, secondSavedSource], "地下城周回");
AssertTrue(merged.DisplayName == "地下城周回", "合并记录应保留用户输入的记录名");
AssertTrue(merged.TaskName == "地下城", "合并记录应保留原任务名");
AssertTrue(merged.StartDate == firstSavedSource.StartTime.Date, "合并记录应取最早日期");
AssertTrue(merged.EndDate == secondSavedSource.EndTime.Date, "合并记录应取最晚日期");
AssertTrue(merged.Duration == TimeSpan.FromMinutes(50), "合并记录应累加持续时间");
AssertTrue(merged.SortieCount == 3 && merged.RoundCount == 5, "合并记录应累加出阵统计");
AssertTrue(merged.ResourceGains["木炭"] == 200, "合并记录应累加资源收获");

var renamedMergeSource = new WorkRecord
{
    TaskName = "大阪挖地",
    Entry = "Underground",
    StartTime = new DateTime(2026, 8, 22, 10, 0, 0),
    EndTime = new DateTime(2026, 8, 22, 10, 20, 0),
    Status = "成功",
    SortieCount = 1,
};
var legacyMergeSource = new WorkRecord
{
    TaskName = "地下城",
    Entry = "Underground",
    StartTime = new DateTime(2026, 8, 21, 10, 0, 0),
    EndTime = new DateTime(2026, 8, 21, 10, 20, 0),
    Status = "成功",
    SortieCount = 2,
};
var renamedMerged = SavedWorkRecordService.Merge([legacyMergeSource, renamedMergeSource], "大阪挖地合并");
AssertTrue(renamedMerged.SortieCount == 3, "新旧任务名称应允许合并实时工作记录");
var renamedSavedMerged = SavedWorkRecordService.Merge(
    [SavedWorkRecordService.Save(legacyMergeSource, "旧地下城"),
     SavedWorkRecordService.Save(renamedMergeSource, "新大阪挖地")],
    "大阪挖地保存记录合并");
AssertTrue(renamedSavedMerged.Segments.Count == 2, "新旧任务名称应允许合并已保存工作记录");

var customRemarkOldSource = new WorkRecord
{
    TaskName = "地下城旧备注",
    Entry = "Underground",
    StartTime = new DateTime(2026, 8, 23, 10, 0, 0),
    EndTime = new DateTime(2026, 8, 23, 10, 20, 0),
    Status = "成功",
};
var customRemarkNewSource = new WorkRecord
{
    TaskName = "大阪挖地新备注",
    Entry = "Underground",
    StartTime = new DateTime(2026, 8, 24, 10, 0, 0),
    EndTime = new DateTime(2026, 8, 24, 10, 20, 0),
    Status = "成功",
};
var customRemarkMerged = SavedWorkRecordService.Merge(
    [customRemarkOldSource, customRemarkNewSource], "备注任务合并");
AssertTrue(customRemarkMerged.Duration == TimeSpan.FromMinutes(40),
    "相同 pipeline 入口的不同任务备注应允许合并");
var customRemarkSavedMerged = SavedWorkRecordService.Merge(
    [SavedWorkRecordService.Save(customRemarkOldSource, "旧备注记录"),
     SavedWorkRecordService.Save(customRemarkNewSource, "新备注记录")],
    "备注保存记录合并");
AssertTrue(customRemarkSavedMerged.Entry == "Underground" && customRemarkSavedMerged.Segments.Count == 2,
    "相同 pipeline 入口的不同任务备注应允许合并已保存记录并保留入口");

var duplicateMerged = SavedWorkRecordService.Merge(
    [firstSavedSource, firstSavedSource],
    "地下城重复合并");
AssertTrue(duplicateMerged.Duration == TimeSpan.FromMinutes(20), "相同时间段合并时不应重复累加时长");
AssertTrue(duplicateMerged.SortieCount == 1, "相同时间段合并时不应重复累加出阵次数");
AssertTrue(duplicateMerged.ResourceGains["木炭"] == 120, "相同时间段合并时不应重复累加资源收获");
var savedDuplicateMerged = SavedWorkRecordService.Merge(
    [SavedWorkRecordService.Save(firstSavedSource, "地下城"),
     SavedWorkRecordService.Save(firstSavedSource, "地下城")],
    "地下城保存记录重复合并");
AssertTrue(savedDuplicateMerged.Duration == TimeSpan.FromMinutes(20), "已保存记录按相同时间段合并时不应重复累加时长");
AssertTrue(savedDuplicateMerged.ResourceGains["木炭"] == 120, "已保存记录按相同时间段合并时不应重复累加资源收获");

var name = SavedWorkRecordService.CreateUniqueName("地下城", ["地下城", "地下城（1）"]);
AssertTrue(name == "地下城（2）", "重名保存记录应自动追加递增编号");

var savedPath = Path.Combine(Path.GetTempPath(), $"matr-saved-{Guid.NewGuid():N}.json");
SavedWorkRecordStore.Save(savedPath, [merged]);
var loaded = SavedWorkRecordStore.Load(savedPath);
File.Delete(savedPath);
AssertTrue(loaded.Count == 1 && loaded[0].DisplayName == "地下城周回", "保存记录应能从本地文件恢复");
AssertTrue(loaded[0].Segments.Count == 2, "保存记录应保留每一段记录的精确时间");

var warehouseEditor = new WarehouseDataEditor(new WarehouseData
{
    CoreResources = new Dictionary<string, int> { ["木炭"] = 100 },
});
warehouseEditor.Data.CoreResources["木炭"] = 120;
AssertTrue(warehouseEditor.HasChanges, "修改仓库资源后应标记为未保存");
warehouseEditor.Revert();
AssertTrue(warehouseEditor.Data.CoreResources["木炭"] == 100, "撤销应恢复已保存的仓库资源");
warehouseEditor.Data.CoreResources["木炭"] = 150;
warehouseEditor.Save();
AssertFalse(warehouseEditor.HasChanges, "保存仓库数据后不应继续标记为未保存");
warehouseEditor.Clear();
AssertTrue(warehouseEditor.Data.CoreResources.Count == 0, "清空应移除仓库资源数据");

AssertTrue(WarehouseScanDraftService.TryParseCount("1,234", out var commaValue) && commaValue == 1234,
    "OCR 数值应清除英文逗号");
AssertTrue(WarehouseScanDraftService.TryParseCount("1，234", out var chineseCommaValue) && chineseCommaValue == 1234,
    "OCR 数值应清除中文逗号");
AssertTrue(WarehouseScanDraftService.TryParseCount("1.234", out var dotValue) && dotValue == 1234,
    "OCR 数值应清除分隔点");
AssertTrue(WarehouseScanDraftService.TryParseCount("所持小判 3,750,570 枚", out var kobanValue) && kobanValue == 3750570,
    "OCR 数值应能从资源名称和单位中提取数字");
AssertFalse(WarehouseScanDraftService.TryParseCount("无法识别", out _),
    "非法 OCR 文本不应被解析为资源数量");

var draftPath = Path.Combine(Path.GetTempPath(), $"matr-warehouse-{Guid.NewGuid():N}.json");
WarehouseScanDraftService.UpdateCoreResource(draftPath, "木炭", 1234);
WarehouseScanDraftService.UpdateCoreResource(draftPath, "玉钢", 5678);
var warehouseDraft = WarehouseScanDraftService.Load(draftPath);
File.Delete(draftPath);
AssertTrue(warehouseDraft.CoreResources["木炭"] == 1234 && warehouseDraft.CoreResources["玉钢"] == 5678,
    "仓库识别草稿应保留已识别的核心资源");

var historyPath = Path.Combine(Path.GetTempPath(), $"matr-warehouse-history-{Guid.NewGuid():N}.json");
WarehouseScanDraftService.AppendSnapshot(historyPath, new Dictionary<string, int>
{
    ["木炭"] = 1234,
    ["玉钢"] = 5678,
});
var historyDraft = WarehouseScanDraftService.Load(historyPath);
File.Delete(historyPath);
AssertTrue(historyDraft.ResourceHistory.Count == 1
    && historyDraft.ResourceHistory[0].Values["木炭"] == 1234,
    "完整仓库识别结束后应追加核心资源历史快照");

var filterNow = new DateTime(2026, 9, 5, 12, 0, 0, DateTimeKind.Local);
var filterHistory = new List<WarehouseResourceSnapshot>
{
    new() { RecordedAt = filterNow.AddHours(-24), Values = new Dictionary<string, int> { ["木炭"] = 1 } },
    new() { RecordedAt = filterNow.AddHours(-24).AddTicks(-1), Values = new Dictionary<string, int> { ["木炭"] = 2 } },
    new() { RecordedAt = filterNow.AddDays(-7), Values = new Dictionary<string, int> { ["木炭"] = 3 } },
    new() { RecordedAt = filterNow.AddDays(-30), Values = new Dictionary<string, int> { ["木炭"] = 4 } },
    new() { RecordedAt = filterNow.AddMinutes(1), Values = new Dictionary<string, int> { ["木炭"] = 5 } },
};
AssertTrue(WarehouseResourceHistoryFilter.Filter(filterHistory, WarehouseChartRange.Last24Hours, filterNow)
        .Select(snapshot => snapshot.Values["木炭"]).SequenceEqual([1]),
    "24小时范围应包含边界记录且排除更早和未来记录");
AssertTrue(WarehouseResourceHistoryFilter.Filter(filterHistory, WarehouseChartRange.Last7Days, filterNow)
        .Select(snapshot => snapshot.Values["木炭"]).SequenceEqual([1, 2, 3]),
    "7天范围应包含七天边界记录");
AssertTrue(WarehouseResourceHistoryFilter.Filter(filterHistory, WarehouseChartRange.Last30Days, filterNow)
        .Select(snapshot => snapshot.Values["木炭"]).SequenceEqual([1, 2, 3, 4]),
    "30天范围应包含三十天边界记录");
var indexedHistory = WarehouseResourceHistoryFilter.FilterWithIndices(filterHistory, WarehouseChartRange.Last24Hours, filterNow);
AssertTrue(indexedHistory.Count == 1 && indexedHistory[0].Index == 0,
    "时间范围筛选应保留原始历史索引，确保删除记录点时定位正确");
var axisLabels = WarehouseChartTimeAxis.BuildLabels(
    filterNow.AddHours(-24), filterNow, WarehouseChartRange.Last24Hours, 650);
AssertTrue(axisLabels.Count == 5
    && axisLabels[0].Text == "12:00"
    && axisLabels[^1].Text == "12:00"
    && axisLabels[0].X == 0
    && axisLabels[^1].X == 650,
    "24小时图表的X轴应显示五个小时分钟刻度并覆盖完整时间范围");
var weeklyAxisLabels = WarehouseChartTimeAxis.BuildLabels(
    filterNow.AddDays(-7), filterNow, WarehouseChartRange.Last7Days, 650);
AssertTrue(weeklyAxisLabels.All(label => label.Text.Length == 5),
    "7天图表的X轴应使用月日格式");
AssertTrue(WarehouseChartTooltipFormatter.Format(filterNow, 12345, 240)
        == "2026-09-05 12:00:00\n当前：12,345\n变动：+240",
    "图表数据点悬停提示应显示当前值和正向变动量");
AssertTrue(WarehouseChartTooltipFormatter.Format(filterNow, 12000, -345)
        == "2026-09-05 12:00:00\n当前：12,000\n变动：-345",
    "图表数据点悬停提示应显示负向变动量");

var chartSelection = new WarehouseChartRangeSelection();
var laterPoint = new DateTime(2026, 9, 5, 13, 30, 0, DateTimeKind.Local);
var earlierPoint = new DateTime(2026, 9, 5, 10, 0, 0, DateTimeKind.Local);
AssertTrue(chartSelection.Select(laterPoint, 1200) == null,
    "首次选择图表记录点时不应立即形成区间");
var selectedRange = chartSelection.Select(earlierPoint, 800);
AssertTrue(selectedRange != null
    && selectedRange.Start.RecordedAt == earlierPoint
    && selectedRange.End.RecordedAt == laterPoint
    && selectedRange.Duration == TimeSpan.FromHours(3.5)
    && selectedRange.Change == 400,
    "两点选择应按时间排序，并计算相隔时长和资源变化量");
AssertTrue(selectedRange?.SummaryText
    == "起始：2026-09-05 10:00:00 · 终止：2026-09-05 13:30:00\n相隔：3小时30分钟 · 变化：+400",
    "完成两点选择后应生成可直接展示的区间摘要");
AssertTrue(chartSelection.Select(new DateTime(2026, 9, 5, 15, 0, 0, DateTimeKind.Local), 1300) == null
    && chartSelection.Start?.Value == 1300
    && chartSelection.End == null,
    "完成区间后再次选择应以新点开启下一段区间");

var updateDataConfigRoot = Path.Combine(Path.GetTempPath(), $"matr-update-data-config-{Guid.NewGuid():N}");
Directory.CreateDirectory(updateDataConfigRoot);
AppPaths.InstancesDirectory = updateDataConfigRoot;
try
{
    var sameDay = new DateTime(2026, 8, 26, 9, 30, 0, DateTimeKind.Local);
    var updateDataConfig = new InstanceConfiguration("update-data-first-run");
    AssertTrue(InvokeUpdateDataShouldRun(updateDataConfig, "每天", sameDay),
        "更新数据在没有成功时间时应允许运行");
    AssertTrue(InvokeUpdateDataGetLastSucceeded(updateDataConfig) == null,
        "缺少成功时间时应返回空值");

    InvokeUpdateDataMarkSucceeded(updateDataConfig, sameDay);
    AssertTrue(InvokeUpdateDataShouldRun(updateDataConfig, "每次", sameDay),
        "触发间隔为每次时不应受上次成功时间影响");
    AssertTrue(InvokeUpdateDataGetLastSucceeded(updateDataConfig) == sameDay,
        "记录成功时间后应按原值读回");

    var reloadedConfig = new InstanceConfiguration("update-data-first-run");
    AssertTrue(File.Exists(reloadedConfig.GetConfigFilePath()),
        "记录成功时间后应生成实例配置文件");
    var reloadedSucceededAt = InvokeUpdateDataGetLastSucceeded(reloadedConfig);
    AssertTrue(reloadedSucceededAt == sameDay,
        "重新加载实例配置后应能从真实配置文件读回成功时间");

    var isolatedConfig = new InstanceConfiguration("update-data-second-instance");
    AssertTrue(InvokeUpdateDataGetLastSucceeded(isolatedConfig) == null,
        "不同实例的成功时间记录不应互相影响");
    AssertTrue(InvokeUpdateDataShouldRun(isolatedConfig, "每天", sameDay),
        "其他实例没有成功时间时仍应允许运行");

    var dailyConfig = new InstanceConfiguration("update-data-daily");
    InvokeUpdateDataMarkSucceeded(dailyConfig, sameDay);
    AssertFalse(InvokeUpdateDataShouldRun(dailyConfig, "每天", sameDay.AddHours(2)),
        "每天触发间隔在同一本地日期内应跳过");
    AssertTrue(InvokeUpdateDataShouldRun(dailyConfig, "每天", sameDay.AddDays(1)),
        "每天触发间隔跨天后应重新运行");

    var weeklyConfig = new InstanceConfiguration("update-data-weekly");
    InvokeUpdateDataMarkSucceeded(weeklyConfig, new DateTime(2026, 8, 26, 9, 30, 0, DateTimeKind.Local));
    AssertFalse(InvokeUpdateDataShouldRun(weeklyConfig, "每周", new DateTime(2026, 8, 28, 9, 30, 0, DateTimeKind.Local)),
        "每周触发间隔在同一 ISO 周内应跳过");
    AssertTrue(InvokeUpdateDataShouldRun(weeklyConfig, "每周", new DateTime(2026, 8, 31, 9, 30, 0, DateTimeKind.Local)),
        "每周触发间隔跨 ISO 周后应重新运行");

    var invalidTimeConfig = new InstanceConfiguration("update-data-invalid");
    invalidTimeConfig.SetValue(ConfigurationKeys.UpdateDataLastSucceededAt, "not-a-time");
    AssertTrue(InvokeUpdateDataShouldRun(invalidTimeConfig, "每天", sameDay),
        "损坏的成功时间记录不应阻止更新数据运行");
    AssertTrue(InvokeUpdateDataGetLastSucceeded(invalidTimeConfig) == null,
        "损坏的成功时间记录应按空值处理");
}
finally
{
    DeleteDirectoryIfExists(updateDataConfigRoot);
}

var dailyTaskCompletionLogRoot = Path.Combine(Path.GetTempPath(), $"matr-daily-task-completion-{Guid.NewGuid():N}");
Directory.CreateDirectory(dailyTaskCompletionLogRoot);
AppPaths.LogsDirectory = dailyTaskCompletionLogRoot;
try
{
    var beforeReset = new DateTime(2026, 9, 6, 4, 59, 0, DateTimeKind.Local);
    var afterReset = new DateTime(2026, 9, 6, 5, 0, 0, DateTimeKind.Local);

    AssertTrue(InvokeDailyTaskShouldRun("login_reward", beforeReset),
        "登录奖励首次执行前应允许运行");
    InvokeDailyTaskMarkCompleted("login_reward", beforeReset);
    AssertFalse(InvokeDailyTaskShouldRun("login_reward", beforeReset),
        "同一游戏日内已完成的登录奖励应跳过");
    AssertTrue(InvokeDailyTaskShouldRun("login_reward", afterReset),
        "每日 05:00 后登录奖励应重新允许运行");

    InvokeDailyTaskMarkCompleted("warm_gift", afterReset);
    AssertFalse(InvokeDailyTaskShouldRun("warm_gift", afterReset.AddHours(12)),
        "暖心礼包完成标记应独立于登录奖励");
    AssertTrue(InvokeDailyTaskShouldRun("mix", afterReset.AddHours(12)),
        "未完成合成时不应因其他项目完成而跳过");
    AssertTrue(InvokeDailyTaskShouldRun("forge", afterReset.AddDays(1)),
        "锻刀跨游戏日后应允许运行");

    var completionLogPath = Path.Combine(dailyTaskCompletionLogRoot, "daily-task-completion.log");
    AssertTrue(File.Exists(completionLogPath)
        && File.ReadAllLines(completionLogPath).Length == 2,
        "日课完成状态应写入独立日志文件，且同一项目同一天不重复写入");
}
finally
{
    DeleteDirectoryIfExists(dailyTaskCompletionLogRoot);
}

ConfigurationManager.Current.Reset();
var oldWarehouse = new WarehouseData
{
    CoreResources = new Dictionary<string, int> { ["木炭"] = 10, ["玉钢"] = 20 },
    OtherItems = new Dictionary<string, int> { ["御守·桃"] = 1 },
};
var oldSwordBook = new List<SwordBookPortraitState>
{
    new("1", true, false, false, false, false),
};
ConfigurationManager.Current.SetValue(ConfigurationKeys.WarehouseData, oldWarehouse.Clone());
ConfigurationManager.Current.SetValue(ConfigurationKeys.SwordBookEntries, CloneSwordBookStates(oldSwordBook));

var warehouseDataSavedEventRaised = false;
Action warehouseDataSavedHandler = () => warehouseDataSavedEventRaised = true;
UpdateDataPersistenceService.WarehouseDataSaved += warehouseDataSavedHandler;
var warehouseDraftSavePath = Path.Combine(Path.GetTempPath(), $"matr-update-data-warehouse-{Guid.NewGuid():N}.json");
File.WriteAllText(warehouseDraftSavePath,
    """
    {
      "core_resources": {
        "木炭": 123,
        "小判": 456
      },
      "other_items": {
        "御守桃": 2
      },
      "resource_history": [
        {
          "recorded_at": "2026-08-25T12:00:00+08:00",
          "values": {
            "木炭": 120
          }
        }
      ]
    }
    """);
AssertTrue(InvokeTrySaveWarehouseDraft(warehouseDraftSavePath),
    "有效的仓库识别草稿应能写入正式仓库数据");
UpdateDataPersistenceService.WarehouseDataSaved -= warehouseDataSavedHandler;
AssertTrue(warehouseDataSavedEventRaised,
    "仓库数据保存后应通知仓库页面刷新正式数据");
DeleteIfExists(warehouseDraftSavePath);

var savedWarehouse = ConfigurationManager.Current.GetValue(ConfigurationKeys.WarehouseData, new WarehouseData());
var untouchedSwordBook = ConfigurationManager.Current.GetValue(ConfigurationKeys.SwordBookEntries, new List<SwordBookPortraitState>());
AssertTrue(savedWarehouse.CoreResources["木炭"] == 123 && savedWarehouse.CoreResources["小判"] == 456,
    "保存仓库草稿后应使用草稿中的核心资源覆盖正式数据");
AssertTrue(savedWarehouse.OtherItems.ContainsKey("御守·桃") && savedWarehouse.OtherItems["御守·桃"] == 2,
    "保存仓库草稿时应复用现有名称归一化逻辑");
AssertTrue(savedWarehouse.ResourceHistory.Count == 1 && savedWarehouse.ResourceHistory[0].Values["木炭"] == 120,
    "保存仓库草稿后应保留草稿中的历史快照");
AssertTrue(AreSameSwordBookStates(untouchedSwordBook, oldSwordBook),
    "保存仓库草稿时不应改写刀帐正式数据");

ConfigurationManager.Current.Reset();
ConfigurationManager.Current.SetValue(ConfigurationKeys.WarehouseData, oldWarehouse.Clone());
ConfigurationManager.Current.SetValue(ConfigurationKeys.SwordBookEntries, CloneSwordBookStates(oldSwordBook));
var persistedWarehouseDraftPath = Path.Combine(Path.GetTempPath(), $"matr-update-data-warehouse-persisted-{Guid.NewGuid():N}.json");
File.WriteAllText(persistedWarehouseDraftPath,
    """
    {
      "core_resources": {
        "木炭": 999
      },
      "other_items": {},
      "resource_history": []
    }
    """);
AssertTrue(InvokeTrySaveWarehouseDraft(persistedWarehouseDraftPath),
    "仓库数据应先成功写入磁盘，作为后续刀帐保存的基准");
DeleteIfExists(persistedWarehouseDraftPath);

// 模拟界面仍持有旧仓库数据：后续刀帐保存不能用该旧状态覆盖磁盘中的新仓库数据。
ConfigurationManager.Current.SetStaleValue(ConfigurationKeys.WarehouseData, oldWarehouse.Clone());
var persistedSwordBookDraftPath = Path.Combine(Path.GetTempPath(), $"matr-update-data-swordbook-persisted-{Guid.NewGuid():N}.json");
File.WriteAllText(persistedSwordBookDraftPath,
    """
    [
      {
        "Number": "3",
        "Owned": true,
        "Wounded": false,
        "TrueSword": false,
        "InnerCare": false,
        "Casual": false
      }
    ]
    """);
AssertTrue(InvokeTrySaveSwordBookDraft(persistedSwordBookDraftPath),
    "刀帐保存应成功写入，同时保留磁盘中的最新仓库数据");
DeleteIfExists(persistedSwordBookDraftPath);
var warehouseAfterStaleSwordBookSave = ConfigurationManager.Current.GetValue(ConfigurationKeys.WarehouseData, new WarehouseData());
AssertTrue(warehouseAfterStaleSwordBookSave.CoreResources["木炭"] == 999,
    "刀帐保存不能用内存中的旧仓库数据覆盖磁盘中的最新仓库数据");

ConfigurationManager.Current.Reset();
var warehouseWithHistory = new WarehouseData
{
    CoreResources = new Dictionary<string, int> { ["木炭"] = 10 },
    ResourceHistory =
    [
        new WarehouseResourceSnapshot
        {
            RecordedAt = new DateTime(2026, 8, 29, 12, 0, 0, DateTimeKind.Local),
            Values = new Dictionary<string, int> { ["木炭"] = 10 },
        },
    ],
};
ConfigurationManager.Current.SetValue(ConfigurationKeys.WarehouseData, warehouseWithHistory);
var historyPreservingDraftPath = Path.Combine(Path.GetTempPath(), $"matr-update-data-warehouse-history-{Guid.NewGuid():N}.json");
File.WriteAllText(historyPreservingDraftPath,
    """
    {
      "core_resources": {
        "木炭": 20
      },
      "other_items": {},
      "resource_history": [
        {
          "recorded_at": "2026-08-30T12:00:00+08:00",
          "values": {
            "木炭": 20
          }
        }
      ]
    }
    """);
AssertTrue(InvokeTrySaveWarehouseDraft(historyPreservingDraftPath),
    "带有历史记录的仓库草稿应能保存");
DeleteIfExists(historyPreservingDraftPath);
var warehouseAfterHistoryPreservingSave = ConfigurationManager.Current.GetValue(ConfigurationKeys.WarehouseData, new WarehouseData());
AssertTrue(warehouseAfterHistoryPreservingSave.ResourceHistory.Count == 2
    && warehouseAfterHistoryPreservingSave.ResourceHistory[0].Values["木炭"] == 10
    && warehouseAfterHistoryPreservingSave.ResourceHistory[1].Values["木炭"] == 20,
    "更新数据保存仓库时应保留已有折线图记录并追加新记录");

ConfigurationManager.Current.Reset();
ConfigurationManager.Current.SetValue(ConfigurationKeys.WarehouseData, oldWarehouse.Clone());
ConfigurationManager.Current.SetValue(ConfigurationKeys.SwordBookEntries, CloneSwordBookStates(oldSwordBook));
var invalidWarehouseDraftPath = Path.Combine(Path.GetTempPath(), $"matr-update-data-warehouse-invalid-{Guid.NewGuid():N}.json");
File.WriteAllText(invalidWarehouseDraftPath, "{ invalid json");
AssertFalse(InvokeTrySaveWarehouseDraft(invalidWarehouseDraftPath),
    "损坏的仓库识别草稿不应写入正式数据");
DeleteIfExists(invalidWarehouseDraftPath);
var unchangedWarehouse = ConfigurationManager.Current.GetValue(ConfigurationKeys.WarehouseData, new WarehouseData());
var unchangedSwordBook = ConfigurationManager.Current.GetValue(ConfigurationKeys.SwordBookEntries, new List<SwordBookPortraitState>());
AssertTrue(unchangedWarehouse.CoreResources["木炭"] == 10 && unchangedWarehouse.CoreResources["玉钢"] == 20,
    "仓库草稿损坏时应保留原有仓库数据");
AssertTrue(AreSameSwordBookStates(unchangedSwordBook, oldSwordBook),
    "仓库草稿损坏时不应影响刀帐数据");

ConfigurationManager.Current.Reset();
ConfigurationManager.Current.SetValue(ConfigurationKeys.WarehouseData, oldWarehouse.Clone());
ConfigurationManager.Current.SetValue(ConfigurationKeys.SwordBookEntries, CloneSwordBookStates(oldSwordBook));
var swordBookDraftPath = Path.Combine(Path.GetTempPath(), $"matr-update-data-swordbook-{Guid.NewGuid():N}.json");
File.WriteAllText(swordBookDraftPath,
    """
    [
      {
        "Number": "3",
        "Owned": true,
        "Wounded": true,
        "TrueSword": false,
        "InnerCare": false,
        "Casual": true
      }
    ]
    """);
AssertTrue(InvokeTrySaveSwordBookDraft(swordBookDraftPath),
    "有效的刀帐识别草稿应能写入正式刀帐数据");
DeleteIfExists(swordBookDraftPath);
var savedSwordBook = ConfigurationManager.Current.GetValue(ConfigurationKeys.SwordBookEntries, new List<SwordBookPortraitState>());
var untouchedWarehouse = ConfigurationManager.Current.GetValue(ConfigurationKeys.WarehouseData, new WarehouseData());
AssertTrue(savedSwordBook.Count == 1
    && savedSwordBook[0].Number == "3"
    && savedSwordBook[0].Owned
    && savedSwordBook[0].Wounded
    && savedSwordBook[0].Casual,
    "保存刀帐草稿后应使用草稿中的拥有状态覆盖正式数据");
AssertTrue(untouchedWarehouse.CoreResources["木炭"] == 10 && untouchedWarehouse.CoreResources["玉钢"] == 20,
    "保存刀帐草稿时不应改写仓库正式数据");

ConfigurationManager.Current.Reset();
ConfigurationManager.Current.SetValue(ConfigurationKeys.WarehouseData, oldWarehouse.Clone());
ConfigurationManager.Current.SetValue(ConfigurationKeys.SwordBookEntries, CloneSwordBookStates(oldSwordBook));
var invalidSwordBookDraftPath = Path.Combine(Path.GetTempPath(), $"matr-update-data-swordbook-invalid-{Guid.NewGuid():N}.json");
File.WriteAllText(invalidSwordBookDraftPath,
    """
    [
      {
        "Number": "",
        "Owned": true,
        "Wounded": false,
        "TrueSword": false,
        "InnerCare": false,
        "Casual": false
      }
    ]
    """);
AssertFalse(InvokeTrySaveSwordBookDraft(invalidSwordBookDraftPath),
    "损坏的刀帐识别草稿不应写入正式数据");
DeleteIfExists(invalidSwordBookDraftPath);
var unchangedSwordBookAfterInvalid = ConfigurationManager.Current.GetValue(ConfigurationKeys.SwordBookEntries, new List<SwordBookPortraitState>());
var unchangedWarehouseAfterSwordBookInvalid = ConfigurationManager.Current.GetValue(ConfigurationKeys.WarehouseData, new WarehouseData());
AssertTrue(AreSameSwordBookStates(unchangedSwordBookAfterInvalid, oldSwordBook),
    "刀帐草稿损坏时应保留原有刀帐数据");
AssertTrue(unchangedWarehouseAfterSwordBookInvalid.CoreResources["木炭"] == 10
    && unchangedWarehouseAfterSwordBookInvalid.CoreResources["玉钢"] == 20,
    "刀帐草稿损坏时不应影响仓库数据");

var naibanOutfitCatalogPath = Path.Combine(Path.GetTempPath(), $"matr-naiban-outfit-catalog-{Guid.NewGuid():N}.json");
var logisticsNaibanOption = interfaceDefinition["option"]?["内番"];
var honmaruPortraitLinkOption = interfaceDefinition["option"]?["联动本丸刀帐"];
var honmaruPortraitLinkCases = honmaruPortraitLinkOption?["cases"];
var autoNaibanOutfitOption = interfaceDefinition["option"]?["自动刷取内番服"];
var autoNaibanOutfitCases = autoNaibanOutfitOption?["cases"];
var expeditionDefinition = JObject.Parse(File.ReadAllText(Path.Combine(
    Directory.GetCurrentDirectory(), "assets", "resource", "base", "pipeline", "Expedition.json")));
AssertTrue(
    logisticsNaibanOption?["cases"]?.FirstOrDefault(item => item?["name"]?.Value<string>() == "Yes")?["option"]?.Values<string>()
        .SequenceEqual(["联动本丸刀帐"]) == true
    && honmaruPortraitLinkOption?["label"]?.Value<string>() == "联动本丸刀帐"
    && honmaruPortraitLinkOption?["type"]?.Value<string>() == "switch"
    && honmaruPortraitLinkOption?["inline_sub_options"]?.Value<bool>() == true
    && honmaruPortraitLinkCases?.FirstOrDefault(item => item?["name"]?.Value<string>() == "No")?["pipeline_override"]?["E_NaibanOutfitFinish"]?["action"]?["custom_action_param"]?["sync_swordbook"]?.Value<bool>() == false
    && honmaruPortraitLinkCases?.FirstOrDefault(item => item?["name"]?.Value<string>() == "Yes")?["pipeline_override"]?["E_NaibanOutfitFinish"]?["action"]?["custom_action_param"]?["sync_swordbook"]?.Value<bool>() == true,
    "内番设置必须提供默认关闭的联动本丸刀帐开关，并通过收尾 action 参数控制同步");
AssertTrue(
    honmaruPortraitLinkCases?.FirstOrDefault(item => item?["name"]?.Value<string>() == "Yes")?["option"]?.Values<string>()
        .SequenceEqual(["自动刷取内番服"]) == true
    && autoNaibanOutfitOption?["type"]?.Value<string>() == "switch"
    && autoNaibanOutfitOption?["default_case"]?.Value<string>() == "No"
    && autoNaibanOutfitCases?.FirstOrDefault(item => item?["name"]?.Value<string>() == "No")?["pipeline_override"]?["E_NaibanVerifyTodayTable"]?["enabled"]?.Value<bool>() == false
    && autoNaibanOutfitCases?.FirstOrDefault(item => item?["name"]?.Value<string>() == "Yes")?["pipeline_override"]?["E_NaibanVerifyTodayTable"]?["enabled"]?.Value<bool>() == true
    && autoNaibanOutfitCases?.FirstOrDefault(item => item?["name"]?.Value<string>() == "Yes")?["pipeline_override"]?["E_NaibanClickEntry"]?["next"]?.Values<string>()
        .SequenceEqual(["E_NaibanVerifyTodayTable", "E_NaibanClickEntry"]) == true
    && expeditionDefinition["E_NaibanVerifyTodayTable"]?["recognition"]?["param"]?["roi"]?.Values<int>()
        .SequenceEqual([554, 7, 177, 42]) == true
    && expeditionDefinition["E_NaibanOpenSwordSelector1"]?["post_wait_freezes"]?["time"]?.Value<int>() == 200
    && expeditionDefinition["E_NaibanOpenSwordSelector2"]?["recognition"]?["param"]?["roi"]?.Values<int>()
        .SequenceEqual([178, 473, 66, 30]) == true
    && expeditionDefinition["E_NaibanSortAscending1"]?["recognition"]?["param"]?["roi"]?.Values<int>()
        .SequenceEqual([1013, 83, 26, 25]) == true
    && expeditionDefinition["E_NaibanOpenFilter1"]?["recognition"]?["param"]?["roi"]?.Values<int>()
        .SequenceEqual([881, 81, 107, 28]) == true
    && expeditionDefinition["E_NaibanFindSword1"]?["action"]?["custom_action"]?.Value<string>() == "NaibanFindSwordAction",
    "自动刷取内番服必须从入口分流，并使用已确认的今日内番表与选刀坐标");
var naibanOutfitActionSource = File.ReadAllText(Path.Combine(
    Directory.GetCurrentDirectory(), "assets", "resource", "base", "custom", "NaibanOutfitLogAction.cs"));
AssertTrue(naibanOutfitActionSource.Contains("sync_swordbook", StringComparison.Ordinal),
    "内番服收尾 action 必须读取联动本丸刀帐开关");
File.WriteAllText(naibanOutfitCatalogPath,
    """
    [
      { "number": "3", "type": "太刀", "name": "三日月宗近" },
      { "number": "4", "type": "太刀", "name": "三日月宗近" },
      { "number": "99", "type": "短刀", "name": "今剑" },
      { "number": "100", "type": "短刀", "name": "今剑" },
      { "number": "101", "type": "打刀", "name": "加州清光" }
    ]
    """);
ConfigurationManager.Current.Reset();
ConfigurationManager.Current.SetValue(ConfigurationKeys.SwordBookEntries,
new List<SwordBookPortraitState>
{
    new SwordBookPortraitState("3", true, true, false, false, true),
    new SwordBookPortraitState("4", true, false, true, false, false),
    new SwordBookPortraitState("99", false, false, false, false, false),
    new SwordBookPortraitState("100", false, false, false, false, false),
    new SwordBookPortraitState("101", true, false, false, false, false),
});
var naibanOutfitTargets = SwordBookNaibanOutfitService.SelectMissingOutfitTargets(naibanOutfitCatalogPath);
AssertTrue(naibanOutfitTargets.Select(target => target.Name).SequenceEqual(["三日月宗近", "加州清光"])
    && naibanOutfitTargets.Select(target => target.Type).SequenceEqual(["太刀", "打刀"]),
    "自动内番必须按同名已拥有条目中序号最大的未拥有内番服条目，选择至多两把目标刀剑");
var markedNaibanOutfitNames = InvokeMarkNaibanOutfits(["三日月宗近", "今剑", "不存在"], naibanOutfitCatalogPath);
var naibanOutfitStates = ConfigurationManager.Current.GetValue(ConfigurationKeys.SwordBookEntries, new List<SwordBookPortraitState>());
AssertTrue(markedNaibanOutfitNames.SequenceEqual(["三日月宗近", "今剑"])
    && naibanOutfitStates.Single(state => state.Number == "3").InnerCare == false
    && naibanOutfitStates.Single(state => state.Number == "4").InnerCare
    && naibanOutfitStates.Single(state => state.Number == "4").TrueSword
    && naibanOutfitStates.Single(state => state.Number == "99").Owned
    && naibanOutfitStates.Single(state => state.Number == "99").InnerCare
    && naibanOutfitStates.Single(state => state.Number == "100").Owned == false
    && naibanOutfitStates.Single(state => state.Number == "100").InnerCare == false,
    "内番服同步必须更新同名已拥有条目中序号最大的内番状态；没有已拥有条目时应为最小序号同时勾选已拥有和内番");
DeleteIfExists(naibanOutfitCatalogPath);

var naibanOutfitState = new NaibanOutfitRecognitionState();
naibanOutfitState.Begin();
AssertTrue(naibanOutfitState.TryRecord("今剑"), "首次识别到的内番服应被记录");
AssertFalse(naibanOutfitState.TryRecord("今剑"), "同一把刀剑的动画重复命中不应重复记录");
AssertTrue(naibanOutfitState.TryRecord("秋田藤四郎"), "同一次内番安排应允许记录第二把刀剑");
AssertFalse(naibanOutfitState.TryRecord("厚藤四郎"), "同一次内番安排最多记录两把刀剑");
AssertFalse(naibanOutfitState.ShouldLogMissingOutfit, "已识别到内番服时不应输出未显示记录");

naibanOutfitState.Begin();
AssertTrue(naibanOutfitState.ShouldLogMissingOutfit, "对话颜色结束前未识别到内番服时应输出未显示记录");
AssertTrue(naibanOutfitState.TryFinishMissingOutfit(), "未识别到内番服时结束结算应输出一次未显示记录");
AssertFalse(naibanOutfitState.TryFinishMissingOutfit(), "同一轮内番服结束结算不应重复输出未显示记录");

// Windows 计划任务：任务名称、命令行参数与任务 XML
var matrScopeToken = WindowsScheduledTaskDefinitionBuilder.BuildScopeToken(@"D:\Claude_Workspace\MATR\MATR.exe");
var otherScopeToken = WindowsScheduledTaskDefinitionBuilder.BuildScopeToken(@"D:\Apps\小只工具\MATR\MATR.exe");
AssertTrue(matrScopeToken.Length == 8
    && matrScopeToken == WindowsScheduledTaskDefinitionBuilder.BuildScopeToken(@"d:\claude_workspace\matr\MATR.exe"),
    "计划任务归属令牌必须对同一安装目录稳定，并且不区分路径大小写");
AssertTrue(matrScopeToken != otherScopeToken,
    "不同安装目录必须生成不同的归属令牌，避免开发目录与运行目录互相覆盖计划任务");
AssertTrue(WindowsScheduledTaskDefinitionBuilder.BuildTaskName(matrScopeToken, 0)
        == $"MATR.Timer.{matrScopeToken}.1",
    "计划任务名称必须由归属令牌与从 1 开始的定时器序号组成");
AssertTrue(WindowsScheduledTaskDefinitionBuilder.BuildTaskPath($"MATR.Timer.{matrScopeToken}.1")
        == $@"MATR\MATR.Timer.{matrScopeToken}.1",
    "计划任务必须放在 MATR 任务文件夹中，便于只列出并清理本应用创建的任务");
AssertTrue(WindowsScheduledTaskDefinitionBuilder.BuildArguments("1a2b3c4d", false)
        == "--autostart --instance \"1a2b3c4d\"",
    "默认计划任务必须按既有命令行参数启动指定实例");
AssertTrue(WindowsScheduledTaskDefinitionBuilder.BuildArguments("1a2b3c4d", true)
        == "--autostart --instance \"1a2b3c4d\" --forceStart",
    "开启强制定时启动时必须追加 forceStart 参数");
AssertTrue(WindowsScheduledTaskDefinitionBuilder.ToRepeatType(0) == WindowsScheduledTaskRepeatType.Daily
    && WindowsScheduledTaskDefinitionBuilder.ToRepeatType(1) == WindowsScheduledTaskRepeatType.Weekly
    && WindowsScheduledTaskDefinitionBuilder.ToRepeatType(2) == WindowsScheduledTaskRepeatType.Monthly,
    "界面上的每日、按周、按月重复方式必须映射为对应的计划任务触发器");

var scheduledNow = new DateTime(2026, 9, 11, 20, 30, 0);
var scheduledContext = new WindowsScheduledTaskContext(
    @"D:\Claude_Workspace\MATR\MATR.exe",
    @"D:\Claude_Workspace\MATR",
    false,
    ["1a2b3c4d"],
    scheduledNow);
var dailyTimer = new WindowsScheduledTaskTimer(
    0,
    true,
    new TimeSpan(21, 0, 0),
    true,
    "1a2b3c4d",
    WindowsScheduledTaskRepeatType.Daily,
    [],
    []);
var dailyXml = WindowsScheduledTaskDefinitionBuilder.BuildXml(dailyTimer, scheduledContext);
System.Xml.Linq.XNamespace scheduledNamespace = "http://schemas.microsoft.com/windows/2004/02/mit/task";
var dailyDocument = System.Xml.Linq.XDocument.Parse(dailyXml);
AssertTrue(dailyDocument.Root?.Name == scheduledNamespace + "Task"
    && dailyDocument.Descendants(scheduledNamespace + "Triggers").Count() == 1
    && dailyDocument.Descendants(scheduledNamespace + "CalendarTrigger").Count() == 1,
    "计划任务 XML 必须结构合法，并且只有一层触发器容器");
AssertTrue(dailyDocument.Descendants(scheduledNamespace + "StartBoundary").Single().Value
        == "2026-09-11T21:00:00"
    && dailyDocument.Descendants(scheduledNamespace + "DaysInterval").Single().Value == "1",
    "每日定时必须生成当天的开始边界与每日触发器");
AssertTrue(dailyDocument.Descendants(scheduledNamespace + "LogonType").Single().Value == "InteractiveToken"
    && dailyDocument.Descendants(scheduledNamespace + "RunLevel").Single().Value == "LeastPrivilege"
    && dailyDocument.Descendants(scheduledNamespace + "StartWhenAvailable").Single().Value == "true",
    "计划任务必须以当前登录用户身份运行、不要求管理员权限，并补执行错过的计划");
AssertTrue(dailyDocument.Descendants(scheduledNamespace + "ExecutionTimeLimit").Single().Value == "PT0S",
    "计划任务不得限制运行时长，避免长时间运行的任务被系统终止");
AssertTrue(dailyDocument.Descendants(scheduledNamespace + "MultipleInstancesPolicy").Single().Value == "Parallel",
    "计划任务必须允许多个实例并行：MATR 被拉起后会一直运行，IgnoreNew 会让后续每天的触发被系统忽略");
AssertTrue(dailyDocument.Descendants(scheduledNamespace + "Settings").Single()
        .Element(scheduledNamespace + "Enabled")?.Value == "true",
    "计划任务必须处于启用状态，否则到点不会触发");
AssertTrue(dailyDocument.Descendants(scheduledNamespace + "Command").Single().Value
        == @"D:\Claude_Workspace\MATR\MATR.exe"
    && dailyDocument.Descendants(scheduledNamespace + "WorkingDirectory").Single().Value
        == @"D:\Claude_Workspace\MATR"
    && dailyDocument.Descendants(scheduledNamespace + "Arguments").Single().Value
        == "--autostart --instance \"1a2b3c4d\"",
    "计划任务必须以当前程序路径、安装目录与既有命令行参数作为执行目标");

var dailyFingerprint = WindowsScheduledTaskDefinitionBuilder.TryReadFingerprint(dailyXml);
AssertTrue(dailyFingerprint != null
    && dailyFingerprint == WindowsScheduledTaskDefinitionBuilder.ComputeFingerprint(dailyTimer, scheduledContext),
    "任务说明中的指纹必须与按当前配置重新计算的指纹一致");
var nextDayXml = WindowsScheduledTaskDefinitionBuilder.BuildXml(
    dailyTimer,
    scheduledContext with { Now = scheduledNow.AddDays(1) });
AssertTrue(WindowsScheduledTaskDefinitionBuilder.TryReadFingerprint(nextDayXml) == dailyFingerprint,
    "开始边界日期推进不得改变指纹，否则计划任务会每天被重建");
AssertTrue(WindowsScheduledTaskDefinitionBuilder.ComputeFingerprint(
        dailyTimer,
        scheduledContext with { ForceScheduledStart = true }) != dailyFingerprint,
    "强制定时启动开关变化必须触发计划任务重建");
AssertTrue(WindowsScheduledTaskDefinitionBuilder.ComputeFingerprint(
        dailyTimer,
        scheduledContext with
        {
            ExecutablePath = @"E:\MATR\MATR.exe",
            WorkingDirectory = @"E:\MATR"
        }) != dailyFingerprint,
    "安装目录变化必须触发计划任务重建，避免继续启动旧路径的程序");
AssertTrue(WindowsScheduledTaskDefinitionBuilder.ComputeFingerprint(
        dailyTimer with { Time = new TimeSpan(22, 0, 0) },
        scheduledContext) != dailyFingerprint,
    "定时时间变化必须触发计划任务重建");

var weeklyTimer = dailyTimer with
{
    RepeatType = WindowsScheduledTaskRepeatType.Weekly,
    DaysOfWeek = [DayOfWeek.Friday, DayOfWeek.Monday]
};
var weeklyXml = WindowsScheduledTaskDefinitionBuilder.BuildXml(weeklyTimer, scheduledContext);
AssertTrue(weeklyXml.Contains("<ScheduleByWeek>")
    && weeklyXml.Contains("<Monday />")
    && weeklyXml.Contains("<Friday />")
    && weeklyXml.IndexOf("<Monday />", StringComparison.Ordinal)
        < weeklyXml.IndexOf("<Friday />", StringComparison.Ordinal),
    "按周定时必须生成选中的星期并保持稳定顺序");
var monthlyTimer = dailyTimer with
{
    RepeatType = WindowsScheduledTaskRepeatType.Monthly,
    DaysOfMonth = [15, 1]
};
var monthlyXml = WindowsScheduledTaskDefinitionBuilder.BuildXml(monthlyTimer, scheduledContext);
AssertTrue(monthlyXml.Contains("<ScheduleByMonth>")
    && monthlyXml.Contains("<Day>1</Day>")
    && monthlyXml.Contains("<Day>15</Day>")
    && monthlyXml.Contains("<September />"),
    "按月定时必须生成选中的日期并覆盖全部月份");
AssertFalse(
    WindowsScheduledTaskDefinitionBuilder.IsRepeatConfigured(
        dailyTimer with { RepeatType = WindowsScheduledTaskRepeatType.Weekly, DaysOfWeek = [] }),
    "按周定时未选择星期时不得创建计划任务");

// Windows 计划任务：同步决策
var plannedTimers = new List<WindowsScheduledTaskTimer>
{
    dailyTimer,
    dailyTimer with { TimerId = 1, IsEnabled = false },
    dailyTimer with { TimerId = 2, IsStartTask = false },
    dailyTimer with { TimerId = 3, RepeatType = WindowsScheduledTaskRepeatType.Weekly, DaysOfWeek = [] },
    dailyTimer with { TimerId = 4, InstanceId = "ffffffff" },
    dailyTimer with { TimerId = 5, InstanceId = null },
    dailyTimer with { TimerId = 6, Time = new TimeSpan(7, 30, 0) },
};
var managedTaskNames = new List<string>
{
    $"MATR.Timer.{matrScopeToken}.1",
    $"MATR.Timer.{matrScopeToken}.3",
    $"MATR.Timer.{matrScopeToken}.7",
    $"MATR.Timer.{otherScopeToken}.1",
    "其他程序创建的任务",
};
var scheduledPlan = WindowsScheduledTaskPlanner.CreatePlan(plannedTimers, scheduledContext, managedTaskNames);
AssertTrue(scheduledPlan.Tasks.Select(task => task.TaskName)
        .SequenceEqual([$"MATR.Timer.{matrScopeToken}.1", $"MATR.Timer.{matrScopeToken}.7"]),
    "只有启用、动作为启动任务、重复规则有效且实例存在的定时器才创建计划任务");
AssertTrue(scheduledPlan.ObsoleteTaskNames.SequenceEqual([$"MATR.Timer.{matrScopeToken}.3"]),
    "只清理本安装目录归属且不再需要的计划任务，不得删除其他安装目录或其他程序的任务");
AssertTrue(scheduledPlan.Warnings.Count == 4,
    "停止任务、未选重复日期、实例失效与未选实例的定时器都必须记录可诊断的原因");
AssertTrue(WindowsScheduledTaskPlanner.CreatePlan(plannedTimers, scheduledContext, []).Tasks.Count == 2,
    "系统任务文件夹为空时仍需生成全部有效定时器的计划任务");

// Windows 计划任务：安装目录整体移动后的残留清理
AssertTrue(WindowsScheduledTaskPlanner.ResolveStaleScopeToken(matrScopeToken, null, null, _ => true) == null,
    "没有上一次同步记录时不得推断出需要清理的安装目录");
AssertTrue(WindowsScheduledTaskPlanner.ResolveStaleScopeToken(
        matrScopeToken,
        matrScopeToken,
        @"D:\Claude_Workspace\MATR\MATR.exe",
        _ => true) == null,
    "安装目录没有变化时不得清理计划任务");
AssertTrue(WindowsScheduledTaskPlanner.ResolveStaleScopeToken(
        matrScopeToken,
        otherScopeToken,
        @"D:\Apps\小只工具\MATR\MATR.exe",
        _ => true) == null,
    "旧安装目录仍然存在时不得清理它的计划任务，避免影响复制出来的另一份安装");
AssertTrue(WindowsScheduledTaskPlanner.ResolveStaleScopeToken(
        matrScopeToken,
        otherScopeToken,
        @"D:\Apps\小只工具\MATR\MATR.exe",
        path => path != @"D:\Apps\小只工具\MATR\MATR.exe") == otherScopeToken,
    "旧安装目录已经不存在时必须清理上一份安装留下的计划任务");
AssertTrue(WindowsScheduledTaskPlanner.ResolveStaleScopeToken(
        matrScopeToken,
        otherScopeToken,
        @"D:\Apps\小只工具\MATR\MATR.exe",
        _ => throw new IOException("磁盘不可用")) == null,
    "无法判断旧安装目录是否存在时不得清理，宁可留下残留也不误删");

var movedPlan = WindowsScheduledTaskPlanner.CreatePlan(
    plannedTimers,
    scheduledContext,
    [
        $"MATR.Timer.{matrScopeToken}.1",
        $"MATR.Timer.{otherScopeToken}.1",
        $"MATR.Timer.{otherScopeToken}.2",
        "其他程序创建的任务",
    ],
    otherScopeToken);
AssertTrue(movedPlan.Tasks.Select(task => task.TaskName).SequenceEqual([
        $"MATR.Timer.{matrScopeToken}.1",
        $"MATR.Timer.{matrScopeToken}.7"
    ]),
    "安装目录更换后仍按当前安装目录生成全部有效定时器的计划任务");
AssertTrue(movedPlan.ObsoleteTaskNames.SequenceEqual([
        $"MATR.Timer.{otherScopeToken}.1",
        $"MATR.Timer.{otherScopeToken}.2"
    ]),
    "安装目录整体移动后必须清理旧安装目录留下的全部计划任务");

// Windows 计划任务：schtasks 参数与任务列表解析
AssertTrue(SchTasksScheduledTaskClient.ParseManagedTaskNames(
        "\"MATR\\MATR.Timer.abcd1234.1\",\"2026/9/12 21:00:00\",\"Ready\"\r\n"
        + "\"\\MATR\\MATR.Timer.abcd1234.2\",\"2026/9/12 22:00:00\",\"Ready\"\r\n"
        + "\"\\其他程序任务\",\"\",\"\"\r\n")
    .SequenceEqual(["MATR.Timer.abcd1234.1", "MATR.Timer.abcd1234.2"]),
    "任务列表解析必须兼容有无上级文件夹前缀两种输出，并忽略不属于 MATR 的任务");
AssertTrue(SchTasksScheduledTaskClient.ParseManagedTaskNames(null).Count == 0,
    "任务列表为空时不得解析出任何任务名称");
AssertTrue(SchTasksScheduledTaskClient.BuildCreateArguments("MATR.Timer.abcd1234.1", @"C:\Temp\task.xml")
        .SequenceEqual(["/Create", "/TN", @"MATR\MATR.Timer.abcd1234.1", "/XML", @"C:\Temp\task.xml", "/F"]),
    "创建计划任务必须使用任务文件夹路径并覆盖同名任务");
AssertTrue(SchTasksScheduledTaskClient.BuildQueryArguments("MATR.Timer.abcd1234.1")
        .SequenceEqual(["/Query", "/TN", @"MATR\MATR.Timer.abcd1234.1", "/XML"]),
    "查询计划任务必须读取任务 XML，以便比较配置指纹");
AssertTrue(SchTasksScheduledTaskClient.BuildDeleteArguments("MATR.Timer.abcd1234.1")
        .SequenceEqual(["/Delete", "/TN", @"MATR\MATR.Timer.abcd1234.1", "/F"]),
    "删除计划任务必须显式指定任务路径并跳过确认");

Console.WriteLine("Windows 计划任务 测试通过。");

Console.WriteLine("FormationPreset 测试通过。");

static string ExtractSourceSection(string source, string startMarker, string endMarker)
{
    var start = source.IndexOf(startMarker, StringComparison.Ordinal);
    var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
    if (start < 0 || end < 0)
        throw new InvalidOperationException($"未找到源码片段：{startMarker} 至 {endMarker}");

    return source[start..end];
}

static void AssertFalse(bool value, string message)
{
    if (value)
        throw new InvalidOperationException(message);
}

static void AssertTrue(bool value, string message)
{
    if (!value)
        throw new InvalidOperationException(message);
}

static bool InvokeUpdateDataShouldRun(InstanceConfiguration configuration, string interval, DateTime now) =>
    (bool)InvokeStaticMethod("MFAAvalonia.Services.UpdateDataScheduleService", "ShouldRun", configuration, interval, now)!;

static DateTime? InvokeUpdateDataGetLastSucceeded(InstanceConfiguration configuration) =>
    (DateTime?)InvokeStaticMethod("MFAAvalonia.Services.UpdateDataScheduleService", "GetLastSucceeded", configuration);

static void InvokeUpdateDataMarkSucceeded(InstanceConfiguration configuration, DateTime now) =>
    InvokeStaticMethod("MFAAvalonia.Services.UpdateDataScheduleService", "MarkSucceeded", configuration, now);

static bool InvokeDailyTaskShouldRun(string item, DateTime now) =>
    (bool)InvokeStaticMethod("MFAAvalonia.Services.DailyTaskCompletionService", "ShouldRun", item, now)!;

static void InvokeDailyTaskMarkCompleted(string item, DateTime now) =>
    InvokeStaticMethod("MFAAvalonia.Services.DailyTaskCompletionService", "MarkCompleted", item, now);

static bool InvokeTrySaveWarehouseDraft(string draftPath) =>
    (bool)InvokeStaticMethod("MFAAvalonia.Services.UpdateDataPersistenceService", "TrySaveWarehouseDraft", draftPath)!;

static bool InvokeTrySaveSwordBookDraft(string draftPath) =>
    (bool)InvokeStaticMethod("MFAAvalonia.Services.UpdateDataPersistenceService", "TrySaveSwordBookDraft", draftPath)!;

static IReadOnlyList<string> InvokeMarkNaibanOutfits(IReadOnlyList<string> swordNames, string catalogPath) =>
    (IReadOnlyList<string>)InvokeStaticMethod("MFAAvalonia.Services.SwordBookNaibanOutfitService", "MarkOwnedOutfits", swordNames, catalogPath)!;

static object? InvokeStaticMethod(string typeName, string methodName, params object?[] arguments)
{
    var assembly = Assembly.GetExecutingAssembly();
    var targetType = assembly.GetType(typeName);
    if (targetType == null)
        throw new InvalidOperationException($"未找到类型 {typeName}");

    var method = targetType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
    if (method == null)
        throw new InvalidOperationException($"未找到方法 {typeName}.{methodName}");

    try
    {
        return method.Invoke(null, arguments);
    }
    catch (TargetInvocationException exception) when (exception.InnerException != null)
    {
        throw exception.InnerException;
    }
}

static List<SwordBookPortraitState> CloneSwordBookStates(IEnumerable<SwordBookPortraitState> states) =>
    [.. states.Select(state => new SwordBookPortraitState(
        state.Number,
        state.Owned,
        state.Wounded,
        state.TrueSword,
        state.InnerCare,
        state.Casual))];

static bool AreSameSwordBookStates(IReadOnlyList<SwordBookPortraitState> left, IReadOnlyList<SwordBookPortraitState> right)
{
    if (left.Count != right.Count)
        return false;

    for (var i = 0; i < left.Count; i++)
    {
        if (left[i].Number != right[i].Number
            || left[i].Owned != right[i].Owned
            || left[i].Wounded != right[i].Wounded
            || left[i].TrueSword != right[i].TrueSword
            || left[i].InnerCare != right[i].InnerCare
            || left[i].Casual != right[i].Casual)
            return false;
    }

    return true;
}


static void DeleteIfExists(string path)
{
    if (File.Exists(path))
        File.Delete(path);
}

static void DeleteDirectoryIfExists(string path)
{
    if (Directory.Exists(path))
        Directory.Delete(path, true);
}
