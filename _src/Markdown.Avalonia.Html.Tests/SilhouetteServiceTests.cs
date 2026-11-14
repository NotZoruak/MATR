using System;
using System.IO;
using System.Linq;
using MFAAvalonia.Services;
using SkiaSharp;
using Xunit;

namespace Markdown.Avalonia.Html.Tests;

public class SilhouetteServiceTests
{
    [Fact]
    public void 大和守安定仅露出上排时不应把相邻头发算入中心涂层格()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "assets", "interface.json")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        using var fixture = SKBitmap.Decode(Path.Combine(directory.FullName,
            "_src", "Markdown.Avalonia.Html.Tests", "TestData", "Silhouette", "yasusada-top-row.png"));
        Assert.NotNull(fixture);
        using var shot = new SKBitmap(1280, 720);
        using (var canvas = new SKCanvas(shot))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawBitmap(fixture, 703, 235);
        }
        var service = new SilhouetteService();
        service.LoadTemplates(Path.Combine(directory.FullName, "assets", "resource", "base", "image", "silhouette"));
        var results = service.RecognizeSwords(shot);
        Assert.NotEmpty(results);
        Assert.Equal(87, results[0].Id);
    }

    [Theory]
    [InlineData(0, 255)]
    [InlineData(120, 220)]
    public void 模板应分开两类灰度并包含分割边界上的暗像素(byte dark, byte light)
    {
        WithTemplate(dark, light, service =>
        {
            var template = Assert.Single(service.Templates);
            for (var row = 0; row < 3; row++)
                for (var column = 0; column < 3; column++)
                    Assert.Equal(5000, template.CellMask[row, column].Count(pixel => pixel));
        });
    }

    [Fact]
    public void 已露出的剪影灰色边缘应参与匹配()
    {
        WithTemplate(20, 220, service =>
        {
            using var region = DrawPattern(20, 220);
            using (var canvas = new SKCanvas(region))
            using (var paint = new SKPaint { Color = new SKColor(120, 120, 120) })
                for (var column = 0; column < 3; column++)
                    canvas.DrawRect(column * 100 + 25, 0, 25, 300, paint);
            using var shot = CreateShot(region);
            var result = Assert.Single(service.RecognizeSwords(shot));
            Assert.True(result.Score > 0.97, $"灰色边缘参与匹配后得分应接近完整模板，实际为 {result.Score:F4}");
        });
    }

    [Fact]
    public void 白色一致应和黑色一致共同贡献得分()
    {
        WithTemplate(20, 220, service =>
        {
            using var region = DrawPattern(20, 220);
            using (var canvas = new SKCanvas(region))
            using (var paint = new SKPaint { Color = new SKColor(220, 220, 220) })
                for (var column = 0; column < 3; column++)
                    canvas.DrawRect(column * 100 + 25, 0, 25, 300, paint);
            using var shot = CreateShot(region);
            var result = Assert.Single(service.RecognizeSwords(shot));
            // 每格四分之一黑色一致、二分之一白色一致，总一致比例为四分之三
            Assert.InRange(result.Score, 0.74, 0.76);
        });
    }

    [Fact]
    public void 模板在露出格全白时仍应比较黑白差异()
    {
        WithTemplate(220, 220, service =>
        {
            using var region = DrawPattern(20, 220);
            using var shot = CreateShot(region);
            var result = Assert.Single(service.RecognizeSwords(shot));
            // 截图一半黑一半白，纯白模板只能匹配其中的白色部分
            Assert.InRange(result.Score, 0.49, 0.51);
        });
    }

    [Fact]
    public void 没有露出格时动态阈值不应产生识别结果()
    {
        WithTemplate(20, 220, service =>
        {
            using var region = new SKBitmap(300, 300);
            using (var canvas = new SKCanvas(region)) canvas.Clear(new SKColor(120, 120, 120));
            using var shot = CreateShot(region);
            Assert.Empty(service.RecognizeSwords(shot));
        });
    }

    [Fact]
    public void 不同截图分辨率应得到相同的刀剑排名()
    {
        WithTemplate(20, 220, service =>
        {
            using var region = DrawPattern(20, 220);
            using var shot = CreateShot(region);
            using var scaled = new SKBitmap(2560, 1440);
            using (var canvas = new SKCanvas(scaled))
                canvas.DrawBitmap(shot, new SKRect(0, 0, 2560, 1440));
            var expected = Assert.Single(service.RecognizeSwords(shot));
            var actual = Assert.Single(service.RecognizeSwords(scaled));
            Assert.Equal(expected.Id, actual.Id);
            Assert.InRange(System.Math.Abs(expected.Score - actual.Score), 0, 0.01);
        });
    }

    [Fact]
    public void 客户端图库应完整加载并识别实际画面的日本号()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "assets", "interface.json")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var templates = Path.Combine(directory.FullName, "assets", "resource", "base", "image", "silhouette");
        var service = new SilhouetteService();
        service.LoadTemplates(templates);
        Assert.Equal(Directory.GetFiles(templates, "*.png").Length, service.Templates.Count);
        Assert.True(service.Templates.Count >= 321);
        using var fixture = SKBitmap.Decode(Path.Combine(directory.FullName,
            "_src", "Markdown.Avalonia.Html.Tests", "TestData", "Silhouette", "nihongou-client.png"));
        using var shot = new SKBitmap(1280, 720);
        using (var canvas = new SKCanvas(shot))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawBitmap(fixture, 703, 235);
        }
        var ranked = service.RecognizeSwords(shot);
        Assert.Equal(136, ranked[0].Id);
        Assert.True(ranked[0].Score >= 0.98);
        Assert.True(ranked[0].Score - ranked[1].Score > 0.03);
        Assert.Equal(ranked[0].Id, service.Recognize(shot)[0].Id);
        Assert.True(MFAAvalonia.Extensions.MaaFW.Custom.TacticalTargetSwordRecognition.ShouldRefresh(ranked, [3]));
        Assert.False(MFAAvalonia.Extensions.MaaFW.Custom.TacticalTargetSwordRecognition.ShouldRefresh(ranked, [136]));
    }

    private static SKBitmap DrawPattern(byte dark, byte light)
    {
        var image = new SKBitmap(300, 300);
        using var canvas = new SKCanvas(image);
        using var paint = new SKPaint { Color = new SKColor(dark, dark, dark) };
        canvas.Clear(new SKColor(light, light, light));
        for (var column = 0; column < 3; column++)
            canvas.DrawRect(column * 100, 0, 50, 300, paint);
        return image;
    }

    private static SKBitmap CreateShot(SKBitmap region)
    {
        var shot = new SKBitmap(1280, 720);
        using var canvas = new SKCanvas(shot);
        canvas.Clear(SKColors.White);
        canvas.DrawBitmap(region, new SKRect(706, 238, 1043, 575));
        return shot;
    }

    private static void WithTemplate(byte dark, byte light, Action<SilhouetteService> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "matr-silhouette-threshold-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "00001_测试刀剑_head.png");
        try
        {
            using (var image = DrawPattern(dark, light))
            using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
            using (var output = File.Create(path)) data.SaveTo(output);
            var service = new SilhouetteService();
            service.LoadTemplates(directory);
            check(service);
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(directory);
        }
    }
}
