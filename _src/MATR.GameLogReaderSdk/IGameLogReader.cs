namespace MATR.GameLogReaderSdk;

/// <summary>读取并解析当前设备的游戏日志，返回脱敏后的本丸快照。</summary>
public interface IGameLogReader : IAsyncDisposable
{
    int ApiVersion { get; }
    Task<GameOverviewSnapshot> ReadAsync(string adbPath, string serial, string packageName, bool keepRoot);
    Task StopAsync();
}
