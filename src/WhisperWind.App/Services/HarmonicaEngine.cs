using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WhisperWind.Core;

namespace WhisperWind.App.Services;

/// <summary>
/// 演奏引擎：MidiSong → HarmonicaConverter → PlaybackPlan → INotePlayer。
/// 状态机：Idle → Loading → Ready → Playing → Paused/Stopped
/// 任何状态点 EmergencyStop 都会 force ReleaseAll。
/// </summary>
public sealed class HarmonicaEngine : IAsyncDisposable
{
    public enum State { Idle, Loading, Ready, Playing, Paused }

    private readonly INotePlayer _player;
    private readonly TargetWindowWatcher _watcher;
    private readonly SemaphoreSlim _stateLock = new(1, 1);

    private PlaybackPlan? _plan;
    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private double _speed = 1.0;

    public State Current { get; private set; } = State.Idle;
    public string? CurrentTrackPath { get; private set; }
    public double Speed => _speed;

    public event Action<State>? StateChanged;
    public event Action<string>? Error;

    public HarmonicaEngine(INotePlayer player, TargetWindowWatcher watcher)
    {
        _player = player;
        _watcher = watcher;
    }

    public async Task LoadAsync(string midiPath)
    {
        await _stateLock.WaitAsync();
        try
        {
            if (Current is State.Playing or State.Paused)
                throw new InvalidOperationException("演奏中无法加载曲目，先停止。");
            SetState(State.Loading);
            CurrentTrackPath = midiPath;
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            // 解析 → 转换 → 编译计划
            var song = await Task.Run(() => MidiSong.Load(midiPath), ct);
            _plan = await Task.Run(() => HarmonicaConverter.Convert(song), ct);
            SetState(State.Ready);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Error?.Invoke($"加载失败: {ex.Message}");
            SetState(State.Idle);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public void SetSpeed(double speed) => _speed = Math.Clamp(speed, 0.25, 4.0);

    public async Task PlayAsync()
    {
        await _stateLock.WaitAsync();
        try
        {
            if (_plan == null) { Error?.Invoke("尚未加载曲目。"); return; }
            if (Current == State.Playing) return;
            if (Current == State.Paused) { SetState(State.Playing); return; }
            if (!_watcher.IsTargetFocused)
            {
                Error?.Invoke("目标窗口未在前台，按键将发往桌面。");
                // 不强制阻止，用户确认后继续
            }
            SetState(State.Playing);
            var ct = _cts!.Token;
            _runTask = Task.Run(() => _player.RunAsync(_plan, _speed, ct), ct);
        }
        finally
        {
            _stateLock.Release();
        }

        // 监听运行结束
        if (_runTask != null)
        {
            try { await _runTask; }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Error?.Invoke($"演奏异常: {ex.Message}"); }
            finally
            {
                if (Current != State.Idle) SetState(State.Idle);
            }
        }
    }

    public void Pause()
    {
        if (Current == State.Playing) SetState(State.Paused);
    }

    public void Resume()
    {
        if (Current == State.Paused) SetState(State.Playing);
    }

    public void EmergencyStop()
    {
        _cts?.Cancel();
        _player.ReleaseAll();
        SetState(State.Idle);
    }

    public void Stop() => EmergencyStop();

    private void SetState(State s)
    {
        if (Current == s) return;
        Current = s;
        StateChanged?.Invoke(s);
    }

    public async ValueTask DisposeAsync()
    {
        EmergencyStop();
        if (_runTask != null)
        {
            try { await _runTask; } catch { }
        }
        _cts?.Dispose();
        _stateLock.Dispose();
    }
}
