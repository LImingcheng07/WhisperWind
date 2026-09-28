using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace WhisperWind.Core;

/// <summary>
/// 播放中可实时调整的控制量（跨线程读写）。
/// </summary>
public sealed class PlaybackControl
{
    private double _speed = 1.0;
    private volatile bool _paused;
    private long _seekRequest = -1;

    /// <summary>速度倍率，0.25..4.0，播放中改动立即生效</summary>
    public double Speed
    {
        get => Volatile.Read(ref _speed);
        set => Volatile.Write(ref _speed, Math.Clamp(value, 0.25, 4.0));
    }

    /// <summary>暂停：当前按住的键会立刻松开，恢复后从原位置继续</summary>
    public bool Paused
    {
        get => _paused;
        set => _paused = value;
    }

    /// <summary>请求跳到曲中某个位置（毫秒，曲谱时间）</summary>
    public void Seek(long positionMs) => Interlocked.Exchange(ref _seekRequest, Math.Max(0, positionMs));

    internal long TakeSeek() => Interlocked.Exchange(ref _seekRequest, -1);
}

/// <summary>
/// 按计划调度按键：独立线程 + 1ms 轮询，支持暂停、实时变速、跳转、进度回调。
/// 任何退出路径（完成/取消/异常）都会松开当前按住的键。
/// </summary>
public static class PlaybackRunner
{
    /// <summary>
    /// 进度回调：(曲谱位置 ms, 当前正在发声的事件索引，-1 表示没有)
    /// 在播放线程上触发，调用方自行节流/切线程。
    /// </summary>
    public delegate void ProgressHandler(long positionMs, int activeIndex);

    public static Task RunAsync(
        INotePlayer player,
        PlaybackPlan plan,
        PlaybackControl control,
        ProgressHandler? progress,
        CancellationToken ct,
        long startPositionMs = 0)
        => Task.Factory.StartNew(
            () => Run(player, plan, control, progress, ct, startPositionMs),
            ct, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    public static void Run(
        INotePlayer player,
        PlaybackPlan plan,
        PlaybackControl control,
        ProgressHandler? progress,
        CancellationToken ct,
        long startPositionMs = 0)
    {
        if (!plan.IsValid) return;

        var events = plan.Events;
        var sw = Stopwatch.StartNew();
        double position = startPositionMs;   // 曲谱时间
        double lastWall = 0;
        int next = plan.IndexAt(startPositionMs);
        int active = -1;
        long lastReport = -1000;

        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                double now = sw.Elapsed.TotalMilliseconds;
                double wallDelta = now - lastWall;
                lastWall = now;

                var seek = control.TakeSeek();
                if (seek >= 0)
                {
                    if (active >= 0) { player.NoteOff(events[active]); active = -1; }
                    position = seek;
                    next = plan.IndexAt(seek);
                }

                if (control.Paused)
                {
                    if (active >= 0) { player.NoteOff(events[active]); active = -1; }
                    Thread.Sleep(5);
                    continue;
                }

                position += wallDelta * control.Speed;

                // 松开到点的音
                if (active >= 0 && position >= events[active].EndMs)
                {
                    player.NoteOff(events[active]);
                    active = -1;
                }

                // 按下到点的音：每轮最多按一个，保证即使调度落后，每个音也至少按下一次
                if (next < events.Count && position >= events[next].StartMs)
                {
                    if (active >= 0) player.NoteOff(events[active]);
                    player.NoteOn(events[next]);
                    active = next;
                    next++;
                }

                if (progress != null && now - lastReport >= 30)
                {
                    lastReport = (long)now;
                    progress((long)position, active);
                }

                if (next >= events.Count && active < 0) break;
                Thread.Sleep(1);
            }
            progress?.Invoke(plan.DurationMs, -1);
        }
        finally
        {
            if (active >= 0) player.NoteOff(events[active]);
            player.ReleaseAll();
        }
    }
}
