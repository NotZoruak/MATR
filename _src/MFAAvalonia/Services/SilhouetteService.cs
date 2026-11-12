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
    private const int RX = 708, RY = 233, RS = 337;
    private const int TPL = 300, CELL = 100;
    private const byte TH_M = 80;    // 匹配用二值化
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
        var c = new bool[3, 3][];
        for (int r = 0; r < 3; r++)
            for (int col = 0; col < 3; col++)
            {
                c[r, col] = new bool[CELL * CELL];
                int ox = col * CELL, oy = r * CELL;
                for (int y = 0; y < CELL; y++)
                    for (int x = 0; x < CELL; x++)
                        c[r, col][y * CELL + x] = Gray(b.GetPixel(ox + x, oy + y)) < TH_M;
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

        // 1. 涂层检测：用低阈值(35)二值化，暗像素<2%判为涂层
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

        // 2. 匹配用二值化（阈值80）
        var cells = new bool[3, 3][];
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
            {
                if (!exposed[r, c]) continue;
                using var cell = CropCell(roi, r, c, cs);
                cells[r, c] = Bin(cell);
            }

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
                    int eg = 0, inter = 0;
                    for (int i = 0; i < np2; i++)
                    {
                        if (gm[i]) { eg++; if (tm[i]) inter++; }
                    }
                    int tg = 0;
                    for (int i = 0; i < np2; i++) if (tm[i]) tg++;
                    if (eg > 0 && tg > 0)
                    {
                        double sc = inter / Math.Sqrt((double)eg * tg);
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

    static bool[] Bin(SKBitmap b)
    {
        var m = new bool[CELL * CELL];
        for (int y = 0; y < CELL; y++)
            for (int x = 0; x < CELL; x++)
                m[y * CELL + x] = Gray(b.GetPixel(x, y)) < TH_M;
        return m;
    }
}
