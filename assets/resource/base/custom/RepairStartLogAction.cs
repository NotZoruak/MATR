using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>
/// 在确认修复前读取刀剑名与资源消耗，并分别输出文件词条和实时日志。
/// </summary>
public class RepairStartLogAction : IMaaCustomAction
{
    private string _lastMessage = string.Empty;
    private string _capturedDetail = string.Empty;

    public string Name { get; set; } = nameof(RepairStartLogAction);

    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        var json = ActionParamHelper.Parse(args.ActionParam);
        var mode = (string?)json["mode"] ?? "record";
        if (mode == "gui")
        {
            if (!string.IsNullOrWhiteSpace(_lastMessage))
                ActionParamHelper.ResolveOwnerProcessor(context)?.AddLog(_lastMessage);
            return true;
        }

        if (mode == "capture")
        {
            _capturedDetail = ReadRepairDetail(context, json);
            return true;
        }

        if (mode == "emit")
        {
            var prefix = (string?)json["prefix"] ?? "修复";
            var action = (string?)json["action"] ?? "修复";
            var detail = string.IsNullOrWhiteSpace(_capturedDetail) ? "信息识别失败" : _capturedDetail;
            _lastMessage = $"[{prefix}] {action} {detail}";
            LoggerHelper.Warning(_lastMessage);
            return true;
        }

        try
        {
            _capturedDetail = ReadRepairDetail(context, json);
            if (string.IsNullOrWhiteSpace(_capturedDetail))
            {
                _lastMessage = "[后勤] 开始修复信息识别失败";
                LoggerHelper.Warning(_lastMessage);
                return true;
            }

            _lastMessage = $"[后勤] 开始修复 {_capturedDetail}";
            LoggerHelper.Info(_lastMessage);
        }
        finally
        {
            ClickRectangle(context, ParseRoi(json["click"] as JArray, "修复确认区域"));
        }

        return true;
    }

    private static string ReadRepairDetail<T>(T context, JObject json) where T : IMaaContext
    {
        var name = Normalize(ReadText(context, ParseRoi(json["name_roi"] as JArray, "修复刀剑 OCR ROI")));
        var costs = (json["cost_rois"] as JArray ?? throw new Exception("修复资源 OCR ROI 缺失"))
            .OfType<JArray>()
            .Select(roi => ParseNumber(ReadText(context, ParseRoi(roi, "修复资源 OCR ROI"))))
            .ToList();

        return RepairDetailFormatter.Format(name, costs);
    }

    private static string ReadText<T>(T context, int[] roi) where T : IMaaContext
    {
        using var image = context.GetImage();
        if (image == null)
            return string.Empty;

        var node = new MaaNode
        {
            Name = "RepairStartOCR",
            Recognition = "OCR",
            Roi = roi,
        };
        var detail = context.RunRecognition(node, image);
        var query = detail?.Detail == null
            ? null
            : JsonConvert.DeserializeObject<MaaExtensions.RecognitionQuery>(detail.Detail);
        return query?.Best?.Text ?? string.Empty;
    }

    private static int ParseNumber(string text)
    {
        var digits = string.Concat((text ?? string.Empty).Where(char.IsDigit));
        return int.TryParse(digits, out var value) ? value : -1;
    }

    private static void ClickRectangle<T>(T context, int[] rectangle) where T : IMaaContext
    {
        var x = rectangle[0] + (rectangle[2] > 0 ? Random.Shared.Next(rectangle[2]) : 0);
        var y = rectangle[1] + (rectangle[3] > 0 ? Random.Shared.Next(rectangle[3]) : 0);
        context.Click(x, y);
    }

    private static int[] ParseRoi(JArray? value, string name)
    {
        if (value == null || value.Count != 4)
            throw new Exception($"{name}必须是 [x, y, w, h]");
        return value.ToObject<int[]>()!;
    }

    private static string Normalize(string text) => Regex.Replace(text ?? string.Empty, @"\s+", string.Empty);
}
