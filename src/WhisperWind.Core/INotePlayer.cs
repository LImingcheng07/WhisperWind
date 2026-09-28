using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace WhisperWind.Core;

/// <summary>
/// 按键下发抽象。Core 只依赖这个接口，平台实现（Windows SendInput / 试听合成器 / mock）由 App 层注入。
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

    /// <summary>一个音开始；默认按下它的键。试听播放器可改成直接发声。</summary>
    void NoteOn(KeyEvent e) => Press(e.Keys);
    /// <summary>一个音结束；默认松开它的键。</summary>
    void NoteOff(KeyEvent e) => Release(e.Keys);

    /// <summary>按 Plan 顺序执行（固定速度，无暂停）</summary>
    Task RunAsync(PlaybackPlan plan, double speed, CancellationToken ct)
        => PlaybackRunner.RunAsync(this, plan, new PlaybackControl { Speed = speed }, null, ct);
}

/// <summary>
/// Core 自带的 Mock 实现：只把按键事件记录下来，便于测试和脱机调试。
/// </summary>
public sealed class FakeNotePlayer : INotePlayer
{
    private readonly object _lock = new();
    public List<(long ms, string action, IReadOnlyList<GameKey> keys)> Log { get; } = new();
    public bool IsTargetFocused => true;

    public void Press(IReadOnlyList<GameKey> keys) => Add("PRESS", keys);
    public void Release(IReadOnlyList<GameKey> keys) => Add("RELEASE", keys);
    public void ReleaseAll() => Add("RELEASE_ALL", Array.Empty<GameKey>());
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public Task RunAsync(PlaybackPlan plan, double speed, CancellationToken ct)
        => PlaybackRunner.RunAsync(this, plan, new PlaybackControl { Speed = speed }, null, ct);

    private void Add(string action, IReadOnlyList<GameKey> keys)
    {
        lock (_lock) Log.Add((DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), action, keys));
    }
}
