using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MFAAvalonia.Services;

/// <summary>统一保存本丸页面的界面偏好，按分区更新并保留其他页面设置。</summary>
public sealed class HonmaruUiSettings(string configDirectory)
{
    private static readonly object FileLock = new();
    private string SettingsPath => Path.Combine(configDirectory, "honmaru", "ui.json");

    public T? Read<T>(string section)
    {
        lock (FileLock)
            return Load()[section] is { } value ? value.ToObject<T>() : default;
    }

    public void Write<T>(string section, T value) where T : notnull
    {
        lock (FileLock)
        {
            var document = Load();
            document[section] = JToken.FromObject(value);
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            var temporary = SettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, document.ToString(Formatting.Indented), new UTF8Encoding(false));
                File.Move(temporary, SettingsPath, true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }

    private JObject Load() => File.Exists(SettingsPath)
        ? JObject.Parse(File.ReadAllText(SettingsPath))
        : new JObject();
}
