using System;
using System.Collections.Generic;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>
/// 管理一次内番安排中的内番服识别结果，避免对话动画重复命中产生重复日志。
/// </summary>
public sealed class NaibanOutfitRecognitionState
{
    private const int MaxSwordCount = 2;
    private readonly HashSet<string> _swordNameSet = new(StringComparer.Ordinal);
    private readonly List<string> _swordNames = [];
    private readonly List<string> _failedReadings = [];
    private bool _isFinished;

    /// <summary>
    /// 本轮已确认的刀剑名称。
    /// </summary>
    public IReadOnlyList<string> SwordNames => _swordNames;

    /// <summary>
    /// 本轮未能解析出刀名的名条读取结果（按出现顺序去重）。
    /// </summary>
    public IReadOnlyList<string> FailedReadings => _failedReadings;

    /// <summary>
    /// 本轮结束时是否需要记录未显示内番服立绘。
    /// </summary>
    public bool ShouldLogMissingOutfit => _swordNames.Count == 0;

    /// <summary>
    /// 开始新一轮内番服识别。
    /// </summary>
    public void Begin()
    {
        _swordNameSet.Clear();
        _swordNames.Clear();
        _failedReadings.Clear();
        _isFinished = false;
    }

    /// <summary>
    /// 记录一次未能解析出刀名的对话读取结果，便于事后判断是漏字、形近误识还是名条位置偏移。
    /// 同一对话会连续命中多帧，重复内容只保留一条。
    /// </summary>
    public void NoteFailedReading(string indicatorText, string nameText)
    {
        if (string.IsNullOrEmpty(indicatorText) && string.IsNullOrEmpty(nameText))
            return;

        var reading = $"指示区「{indicatorText}」名条「{nameText}」";
        if (!_failedReadings.Contains(reading))
            _failedReadings.Add(reading);
    }

    /// <summary>
    /// 尝试记录一把刀剑。相同刀剑只记录一次，单轮最多记录两把。
    /// </summary>
    public bool TryRecord(string swordName)
    {
        if (string.IsNullOrWhiteSpace(swordName) || _swordNames.Count >= MaxSwordCount)
            return false;

        if (!_swordNameSet.Add(swordName))
            return false;

        _swordNames.Add(swordName);
        return true;
    }

    /// <summary>
    /// 结束本轮识别。仅当未识别到内番服且此前未结算时返回 true。
    /// </summary>
    public bool TryFinishMissingOutfit()
    {
        if (_isFinished)
            return false;

        _isFinished = true;
        return ShouldLogMissingOutfit;
    }
}
