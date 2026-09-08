namespace MFAAvalonia.Extensions.MaaFW;

/// <summary>
/// 游戏客户端包名的配置解析与旧配置迁移规则。
/// </summary>
public static class ClientPackageSettings
{
    public const string OfficialPackageName = "com.youzu.djlw";

    /// <summary>
    /// 按客户端类型返回用于启动游戏的包名。
    /// </summary>
    public static string ResolvePackageName(ClientPackageType type, string? customPackageName)
    {
        if (type == ClientPackageType.Other && !string.IsNullOrWhiteSpace(customPackageName))
            return customPackageName.Trim();

        return OfficialPackageName;
    }

    /// <summary>
    /// 将旧版“目标应用”中的包名转换为新的客户端类型配置。
    /// </summary>
    public static ClientPackageSelection MigrateLegacyPackageName(string? legacyPackageName)
    {
        var packageName = legacyPackageName?.Trim();
        if (string.IsNullOrWhiteSpace(packageName)
            || string.Equals(packageName, OfficialPackageName, System.StringComparison.Ordinal))
        {
            return new ClientPackageSelection(ClientPackageType.Official, null);
        }

        return new ClientPackageSelection(ClientPackageType.Other, packageName);
    }
}

/// <summary>
/// 客户端类型。
/// </summary>
public enum ClientPackageType
{
    Official,
    Other,
}

/// <summary>
/// 客户端类型及其可选的手动包名。
/// </summary>
public sealed record ClientPackageSelection(ClientPackageType Type, string? CustomPackageName);
