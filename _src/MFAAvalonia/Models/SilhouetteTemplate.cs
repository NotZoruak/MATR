namespace MFAAvalonia.Models;

/// <summary>
/// 剪影模板数据，启动时从 resource/base/image/silhouette/ 加载
/// </summary>
public class SilhouetteTemplate
{
    /// <summary>角色编号（从文件名解析）</summary>
    public int Id { get; init; }

    /// <summary>简体中文角色名（从文件名解析）</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>true = 上半身，false = 下半身</summary>
    public bool IsHead { get; init; }

    /// <summary>模板图片文件路径</summary>
    public string FilePath { get; init; } = string.Empty;

    /// <summary>
    /// 九宫格蒙版 [3,3]，每格为长度 10000 的 bool[]（100×100 展平，true=黑像素）
    /// </summary>
    public bool[,][] CellMask { get; init; } = new bool[3, 3][];
}

/// <summary>
/// 单次识别的一条结果
/// </summary>
public class RecognitionResult
{
    /// <summary>排名（1 起始）</summary>
    public int Rank { get; init; }

    /// <summary>角色编号</summary>
    public int Id { get; init; }

    /// <summary>角色名</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>匹配得分 0.0~1.0</summary>
    public double Score { get; init; }

    /// <summary>true = 匹配到 head 模板</summary>
    public bool IsHead { get; init; }

    /// <summary>得分百分比显示文本</summary>
    public string ScoreText => $"{Score * 100:F1}%";

    /// <summary>类型标签：上半身/下半身</summary>
    public string TypeLabel => IsHead ? "上半身" : "下半身";
}
