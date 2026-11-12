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
        service.LoadTemplates(Path.Combine(directory.FullName, "assets", "resource", "silhouette"));
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
