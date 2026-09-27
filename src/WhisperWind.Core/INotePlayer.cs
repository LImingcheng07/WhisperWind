using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace WhisperWind.Core;

/// <summary>
/// 按键下发抽象。Core 只依赖这个接口，平台实现（Windows SendInput / mock）由 App 层注入。
/// </summary>
public interface INotePlayer : IAsyncDisposable
{
    /// <summary>当前是否已锁定焦点窗口（三角洲进程）</summary>
    bool IsTargetFocused { get; }
    /// <summary>同时按下一组键（按下并保持）</summary>
    void Press(IReadOnlyList<GameKey> keys);
    /// <summary>松开一组键（释放之前按下的）</summary>
    void Release(IReadOnlyList<GameKey> keys);
    /// <summary>完全松开所有键（紧急停止时调用）</summary>
    void ReleaseAll();
    /// <summary>启动调度循环（按 Plan 顺序执行）</summary>
    Task RunAsync(PlaybackPlan plan, double speed, CancellationToken ct);
}

/// <summary>
/// Core 自带的 Mock 实现：只把按键事件记录下来，便于测试和脱机调试。
/// </summary>
public sealed class FakeNotePlayer : INotePlayer
{
    public List<(long ms, string action, IReadOnlyList<GameKey> keys)> Log { get; } = new();
    public bool IsTargetFocused => true;

    public void Press(IReadOnlyList<GameKey> keys) => Log.Add((DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), "PRESS", keys));
    public void Release(IReadOnlyList<GameKey> keys) => Log.Add((DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), "RELEASE", keys));
    public void ReleaseAll() => Log.Add((DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), "RELEASE_ALL", Array.Empty<GameKey>()));
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public async Task RunAsync(PlaybackPlan plan, double speed, CancellationToken ct)
    {
        if (!plan.IsValid) return;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        foreach (var e in plan.Events)
        {
            long targetMs = (long)(e.StartMs / speed);
            while (sw.ElapsedMilliseconds < targetMs)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(1, ct);
            }
            Press(e.Keys);
            await Task.Delay((int)(e.DurationMs / speed), ct);
            Release(e.Keys);
        }
    }
}