using MFAAvalonia.Helper;
using MFAAvalonia.Models;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace MFAAvalonia.Services;

public class SilhouetteService
{
    private const int RX = 706, RY = 238, RS = 337;
    private const int TPL = 300, CELL = 100;
    private const byte TH_M = 80;    // 无法动态分割时使用原有匹配阈值
    private const byte TH_C = 35;    // 涂层检测用二值化
    private const double DC = 0.04;  // TH_C下暗像素>4%判为露出
    private const double UKN = 0.55;

    public IReadOnlyList<SilhouetteTemplate> Templates => _templates;
    private readonly List<SilhouetteTemplate> _templates = new();

    public void LoadTemplates(string dir)
    {
        _templates.Clear();
        if (!Directory.Exists(dir)) return;
        foreach (var f in Directory.GetFiles(dir, "*.png"))
        {
            var t = Parse(f);
            if (t != null) _templates.Add(t);
        }
        LoggerHelper.Info($"[Silhouette] {_templates.Count} 模板");
    }

    static byte Gray(SKColor p) => (byte)(p.Red * .299 + p.Green * .587 + p.Blue * .114);

    static SilhouetteTemplate? Parse(string path)
    {
        var m = Regex.Match(Path.GetFileNameWithoutExtension(path), @"^(\d+)_(.+?)_(head|foot)$");
        if (!m.Success) return null;
        using var b = SKBitmap.Decode(path);
        if (b == null || b.Width != TPL || b.Height != TPL) return null;
        var pixels = ReadGrayPixels(b);
        var threshold = ComputeMatchThreshold([pixels]);
        var c = new bool[3, 3][];
        for (int r = 0; r < 3; r++)
            for (int col = 0; col < 3; col++)
            {
                c[r, col] = new bool[CELL * CELL];
                int ox = col * CELL, oy = r * CELL;
                for (int y = 0; y < CELL; y++)
                    for (int x = 0; x < CELL; x++)
                        c[r, col][y * CELL + x] = pixels[(oy + y) * TPL + ox + x] < threshold;
            }
        return new SilhouetteTemplate
        {
            Id = int.Parse(m.Groups[1].Value), Name = m.Groups[2].Value,
            IsHead = m.Groups[3].Value == "head", FilePath = path, CellMask = c
        };
    }

    public List<RecognitionResult> Recognize(SKBitmap shot) => Recognize(shot, false);

    /// <summary>按刀剑编号合并上下半身，保留低分候选以便准确比较前两名差距。</summary>
    public List<RecognitionResult> RecognizeSwords(SKBitmap shot) => Recognize(shot, true);

    private List<RecognitionResult> Recognize(SKBitmap shot, bool rankBySword)
    {
        if (_templates.Count == 0) return new List<RecognitionResult>();

        using var roi = new SKBitmap(RS, RS);
        using (var cv = new SKCanvas(roi))
            cv.DrawBitmap(shot, new SKRect(RX, RY, RX + RS, RY + RS), new SKRect(0, 0, RS, RS));

        // 1. 涂层检测：保持灰度低于 35 的暗像素超过 4% 才判为露出
        float cs = RS / 3f;
        var exposed = new bool[3, 3];
        var ratios = new double[3, 3];
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
            {
                using var cell = CropCell(roi, r, c, cs);
                int dark = 0, np = CELL * CELL;
                for (int y = 0; y < CELL; y++)
                    for (int x = 0; x < CELL; x++)
                        if (Gray(cell.GetPixel(x, y)) < TH_C) dark++;
                ratios[r, c] = (double)dark / np;
                exposed[r, c] = ratios[r, c] > DC;
            }

        var sbr = new System.Text.StringBuilder("[Silhouette] TH35暗像素比: ");
        for (int r = 0; r < 3; r++)
        {
            sbr.Append('[');
            for (int c = 0; c < 3; c++) { sbr.Append($"{ratios[r, c]:F3}"); if (c < 2) sbr.Append(','); }
            sbr.Append(']'); if (r < 2) sbr.Append(' ');
        }
        LoggerHelper.Info(sbr.ToString());

        var sb = new System.Text.StringBuilder("[Silhouette] ");
        for (int r = 0; r < 3; r++)
        {
            sb.Append('[');
            for (int c = 0; c < 3; c++) { sb.Append(exposed[r, c] ? "露" : "涂"); if (c < 2) sb.Append(','); }
            sb.Append(']'); if (r < 2) sb.Append(' ');
        }
        LoggerHelper.Info(sb.ToString());

        // 2. 仅统计露出格的灰度，共用一个动态阈值，未露出区域不参与统计
        var grayscale = new byte[3, 3][];
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
            {
                if (!exposed[r, c]) continue;
                using var cell = CropCell(roi, r, c, cs);
                grayscale[r, c] = ReadGrayPixels(cell);
            }
        var threshold = ComputeMatchThreshold(grayscale.OfType<byte[]>());
        LoggerHelper.Info($"[Silhouette] Otsu匹配阈值（灰度小于）: {threshold}");
        var cells = new bool[3, 3][];
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
                if (exposed[r, c]) cells[r, c] = Bin(grayscale[r, c], threshold);

        // 3. 匹配
        var scores = new List<(SilhouetteTemplate t, double s)>();
        int np2 = CELL * CELL;
        foreach (var t in _templates)
        {
            double sum = 0, weight = 0;
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 3; c++)
                {
                    if (!exposed[r, c]) continue;
                    var gm = cells[r, c];
                    var tm = t.CellMask[r, c];
                    int eg = 0, same = 0;
                    for (int i = 0; i < np2; i++)
                    {
                        if (gm[i]) eg++;
                        if (gm[i] == tm[i]) same++;
                    }
                    if (eg > 0)
                    {
                        // 黑色和白色一致均计分，黑白不一致计为差异
                        double sc = (double)same / np2;
                        sum += sc * eg;    // 暗像素多的格权重更高
                        weight += eg;
                    }
                }
            if (weight > 0) scores.Add((t, sum / weight));
        }
        if (rankBySword)
            scores = scores.GroupBy(item => item.t.Id).Select(group => group.MaxBy(item => item.s)).ToList();
        scores.Sort((a, b) => b.s.CompareTo(a.s));
        if (scores.Count == 0) return new List<RecognitionResult>();

        var top = scores.Take(5).Select((x, i) => new RecognitionResult
        {
            Rank = i + 1, Id = x.t.Id, Name = x.t.Name,
            Score = x.s, IsHead = x.t.IsHead
        }).ToList();

        if (rankBySword) return top;

        var ret = top.Where(r => r.Score >= UKN).ToList();
        if (ret.Count == 0 && top.Count > 0) ret.Add(top[0]);
        var top5 = string.Join(", ", top.Take(5).Select(x => $"{x.Name}({x.TypeLabel[..2]}){x.ScoreText}"));
        LoggerHelper.Info($"[Silhouette] Top5: {top5}");
        return ret;
    }

    static SKBitmap CropCell(SKBitmap roi, int r, int c, float cs)
    {
        var cell = new SKBitmap(CELL, CELL);
        using var cv = new SKCanvas(cell);
        cv.DrawBitmap(roi,
            new SKRect(c * cs, r * cs, (c + 1) * cs, (r + 1) * cs),
            new SKRect(0, 0, CELL, CELL));
        return cell;
    }

    static byte[] ReadGrayPixels(SKBitmap bitmap)
    {
        var pixels = new byte[bitmap.Width * bitmap.Height];
        for (int y = 0; y < bitmap.Height; y++)
            for (int x = 0; x < bitmap.Width; x++)
                pixels[y * bitmap.Width + x] = Gray(bitmap.GetPixel(x, y));
        return pixels;
    }

    /// <summary>用最大类间方差选择黑白分割点，返回不包含在暗像素内的灰度上界。</summary>
    static int ComputeMatchThreshold(IEnumerable<byte[]> images)
    {
        var histogram = new int[256];
        long count = 0, total = 0;
        foreach (var pixels in images)
            foreach (var value in pixels)
            {
                histogram[value]++;
                count++;
                total += value;
            }

        long darkCount = 0, darkTotal = 0;
        double bestVariance = 0;
        int threshold = TH_M;
        for (int value = 0; value < 255; value++)
        {
            darkCount += histogram[value];
            darkTotal += (long)value * histogram[value];
            if (darkCount == 0) continue;
            long lightCount = count - darkCount;
            if (lightCount == 0) break;
            double difference = (double)darkTotal / darkCount - (double)(total - darkTotal) / lightCount;
            double variance = (double)darkCount * lightCount * difference * difference;
            if (variance > bestVariance)
            {
                bestVariance = variance;
                // 分割点本身属于暗色类，保持后续使用小于比较时需要加一
                threshold = value + 1;
            }
        }
        // 空样本或单一灰度无法分成两类，沿用原有阈值
        return threshold;
    }

    static bool[] Bin(ReadOnlySpan<byte> pixels, int threshold)
    {
        var mask = new bool[pixels.Length];
        for (int i = 0; i < pixels.Length; i++) mask[i] = pixels[i] < threshold;
        return mask;
    }
}
