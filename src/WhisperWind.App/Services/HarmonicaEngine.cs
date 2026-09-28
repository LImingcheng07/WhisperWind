using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WhisperWind.Core;

namespace WhisperWind.App.Services;

/// <summary>
/// 演奏引擎：MidiSong → 选旋律轨/移调 → HarmonicaConverter → PlaybackPlan → INotePlayer。
///
/// 状态机：
///   Idle → Loading → Ready
///   Ready → (游戏模式) WaitingFocus → Countdown → Playing ⇄ Paused → Ready
///   Ready → (试听模式) Playing ⇄ Paused → Ready
/// 游戏模式下切出游戏会自动暂停、切回自动继续；任何退出路径都会松开所有键。
/// </summary>
public sealed class HarmonicaEngine : IAsyncDisposable
{
    public enum State { Idle, Loading, Ready, WaitingFocus, Countdown, Playing, Paused }
    public enum Mode { Game, Preview }

    private readonly WindowsNotePlayer _game;
    private readonly MidiPreviewPlayer _preview;
    private readonly TargetWindowWatcher _watcher;
    private readonly AppSettings _settings;
    private readonly HarmonicaMapping _mapping = new();
    private readonly object _lock = new();

    private IReadOnlyList<MidiNote> _sourceNotes = Array.Empty<MidiNote>();
    private PlaybackControl _control = new();
    private CancellationTokenSource? _runCts;
    private Task? _runTask;
    private int _runId;
    private double _speed = 1.0;
    private bool _autoPaused;

    public HarmonicaEngine(WindowsNotePlayer game, MidiPreviewPlayer preview, TargetWindowWatcher watcher, AppSettings settings)
    {
        _game = game;
        _preview = preview;
        _watcher = watcher;
        _settings = settings;
        _speed = Math.Clamp(settings.Speed, 0.25, 4.0);
        _watcher.FocusChanged += OnFocusChanged;
    }

    public State Current { get; private set; } = State.Idle;
    public Mode CurrentMode { get; private set; } = Mode.Game;
    public bool IsRunning => Current is State.WaitingFocus or State.Countdown or State.Playing or State.Paused;

    public MidiSong? Song { get; private set; }
    public PlaybackPlan? Plan { get; private set; }
    public string Title { get; private set; } = "";
    public string? SongPath { get; private set; }
    public int TrackIndex { get; private set; }
    public int Transpose { get; private set; }
    /// <summary>是否是简谱/NPC 码（不自动移调、不存偏好）</summary>
    public bool IsJianpu { get; private set; }

    /// <summary>当前曲谱位置（ms），播放线程写、UI 定时读</summary>
    public long PositionMs => Interlocked.Read(ref _position);
    private long _position;
    /// <summary>正在发声的事件索引，-1 = 无</summary>
    public int ActiveIndex => Volatile.Read(ref _active);
    private int _active = -1;
    public int CountdownRemaining { get; private set; }

    public double Speed
    {
        get => _speed;
        set
        {
            _speed = Math.Clamp(value, 0.25, 4.0);
            _control.Speed = _speed;
        }
    }

    public event Action<State>? StateChanged;
    public event Action<string>? Error;
    public event Action? PlanChanged;
    /// <summary>自然播放到结尾（不是被停止）</summary>
    public event Action? Finished;
    public event Action<int>? CountdownTick;

    // ===== 加载 =====

    public async Task LoadAsync(string path, string title)
    {
        await StopAsync();
        SetState(State.Loading);
        try
        {
            var song = await Task.Run(() => MidiSong.Load(path));
            var prefs = _settings.Songs.TryGetValue(path, out var p) ? p : null;

            int track = prefs?.TrackIndex is int t && t >= 0 && t < song.Tracks.Count && song.Tracks[t].NoteCount > 0
                ? t
                : MelodyAnalyzer.PickMelodyTrack(song);
            if (track < 0) throw new InvalidOperationException("这首 MIDI 里没有可用的旋律音符");

            var notes = song.GetTrackNotes(track);
            int transpose = prefs?.Transpose ?? MelodyAnalyzer.BestTranspose(notes, _mapping);

            Song = song;
            SongPath = path;
            Title = title;
            IsJianpu = false;
            TrackIndex = track;
            BuildPlan(notes, transpose);
            SetState(State.Ready);
        }
        catch (Exception ex)
        {
            Error?.Invoke($"加载失败：{ex.Message}");
            SetState(Plan != null ? State.Ready : State.Idle);
        }
    }

    /// <summary>加载简谱音符（NPC 歌诀 / 按键测试）。音高原样使用，不自动移调。</summary>
    public async Task LoadNotesAsync(IReadOnlyList<MidiNote> notes, string title)
    {
        await StopAsync();
        Song = null;
        SongPath = null;
        Title = title;
        IsJianpu = true;
        TrackIndex = 0;
        BuildPlan(notes, 0);
        SetState(Plan is { IsValid: true } ? State.Ready : State.Idle);
        if (Plan is { IsValid: false }) Error?.Invoke("简谱里没有可演奏的音");
    }

    /// <summary>换旋律轨 / 改移调；演奏中会先停止</summary>
    public async Task RebuildAsync(int trackIndex, int transpose)
    {
        if (Plan == null) return;
        bool trackChanged = trackIndex != TrackIndex;
        if (trackChanged == false && transpose == Transpose) return;
        await StopAsync();

        IReadOnlyList<MidiNote> notes = _sourceNotes;
        if (Song != null && trackIndex >= 0 && trackIndex < Song.Tracks.Count)
        {
            TrackIndex = trackIndex;
            notes = Song.GetTrackNotes(trackIndex);
            // 换轨时如果用户没动移调，自动重新找最佳移调
            if (trackChanged && transpose == Transpose)
                transpose = MelodyAnalyzer.BestTranspose(notes, _mapping);
        }
        BuildPlan(notes, transpose);
        if (SongPath != null && !IsJianpu)
        {
            _settings.Songs[SongPath] = new SongPrefs { TrackIndex = TrackIndex, Transpose = Transpose };
            try { _settings.Save(); } catch { /* 偏好存不了不影响演奏 */ }
        }
        SetState(State.Ready);
    }

    /// <summary>设置里改了间隔参数后重新编译</summary>
    public void Recompile()
    {
        if (Plan == null || IsRunning) return;
        BuildPlan(_sourceNotes, Transpose);
    }

    private void BuildPlan(IReadOnlyList<MidiNote> notes, int transpose)
    {
        _sourceNotes = notes;
        Transpose = transpose;
        var converter = new HarmonicaConverter(_mapping, _settings.MinNoteGapMs, _settings.ReleaseGapMs);
        Plan = converter.Convert(SongPath ?? Title, TrackIndex, notes, transpose);
        Interlocked.Exchange(ref _position, 0);
        Volatile.Write(ref _active, -1);
        PlanChanged?.Invoke();
    }

    // ===== 播放控制 =====

    /// <summary>开始演奏。游戏模式会先等游戏窗口到前台，再倒计时。</summary>
    public async Task PlayAsync(Mode mode, long fromMs = 0)
    {
        var plan = Plan;
        if (plan is not { IsValid: true })
        {
            Error?.Invoke("还没有加载可演奏的曲目");
            return;
        }
        if (IsRunning) return;

        var cts = new CancellationTokenSource();
        int myRun;
        lock (_lock)
        {
            _runCts = cts;
            myRun = ++_runId;
            _control = new PlaybackControl { Speed = _speed };
            _autoPaused = false;
        }
        CurrentMode = mode;
        var ct = cts.Token;
        if (fromMs >= plan.DurationMs) fromMs = 0;
        Interlocked.Exchange(ref _position, fromMs);

        var task = RunAsync(mode, plan, fromMs, myRun, ct);
        _runTask = task;
        await task;
    }

    private async Task RunAsync(Mode mode, PlaybackPlan plan, long fromMs, int myRun, CancellationToken ct)
    {
        bool finished = false;
        try
        {
            if (mode == Mode.Game)
            {
                _game.ModifierLeadMs = _settings.ModifierLeadMs;
                await WaitForGameAndCountdownAsync(ct);
            }
            else
            {
                _preview.Program = _settings.PreviewProgram;
            }

            SetState(State.Playing);
            INotePlayer player = mode == Mode.Game ? _game : _preview;
            await PlaybackRunner.RunAsync(player, plan, _control, OnProgress, ct, fromMs);
            finished = !ct.IsCancellationRequested;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Error?.Invoke($"演奏出错：{ex.Message}");
        }
        finally
        {
            _game.ReleaseAll();
            _preview.ReleaseAll();
            if (Volatile.Read(ref _runId) == myRun)
            {
                Volatile.Write(ref _active, -1);
                if (finished) Interlocked.Exchange(ref _position, 0);
                CountdownRemaining = 0;
                SetState(State.Ready);
            }
        }
        if (finished) Finished?.Invoke();
    }

    private async Task WaitForGameAndCountdownAsync(CancellationToken ct)
    {
        bool needFocus = _settings.RequireGameFocus;
        while (true)
        {
            if (needFocus && !_watcher.IsTargetFocused)
            {
                SetState(State.WaitingFocus);
                while (!_watcher.IsTargetFocused) await Task.Delay(100, ct);
            }

            SetState(State.Countdown);
            bool lostFocus = false;
            for (int s = Math.Max(0, _settings.CountdownSeconds); s > 0 && !lostFocus; s--)
            {
                CountdownRemaining = s;
                CountdownTick?.Invoke(s);
                for (int i = 0; i < 10; i++)
                {
                    await Task.Delay(100, ct);
                    if (needFocus && !_watcher.IsTargetFocused) { lostFocus = true; break; }
                }
            }
            CountdownRemaining = 0;
            if (!lostFocus) return;
        }
    }

    private void OnProgress(long positionMs, int activeIndex)
    {
        Interlocked.Exchange(ref _position, positionMs);
        Volatile.Write(ref _active, activeIndex);
    }

    public void TogglePause()
    {
        switch (Current)
        {
            case State.Playing:
                _control.Paused = true;
                _autoPaused = false;
                SetState(State.Paused);
                break;
            case State.Paused:
                _autoPaused = false;
                _control.Paused = false;
                SetState(State.Playing);
                break;
            case State.WaitingFocus:
            case State.Countdown:
                Stop();
                break;
        }
    }

    public void Seek(long positionMs)
    {
        positionMs = Math.Clamp(positionMs, 0, Plan?.DurationMs ?? 0);
        Interlocked.Exchange(ref _position, positionMs);
        if (Current is State.Playing or State.Paused) _control.Seek(positionMs);
    }

    public void Stop()
    {
        CancellationTokenSource? cts;
        lock (_lock) cts = _runCts;
        try { cts?.Cancel(); } catch (ObjectDisposedException) { }
        _game.ReleaseAll();
        _preview.ReleaseAll();
        Interlocked.Exchange(ref _position, 0);
    }

    public async Task StopAsync()
    {
        Stop();
        var t = _runTask;
        if (t != null)
        {
            try { await t; } catch { }
        }
    }

    /// <summary>游戏模式下切出游戏自动暂停（松开所有键，避免按键落到别的窗口），切回自动继续</summary>
    private void OnFocusChanged(bool focused)
    {
        if (CurrentMode != Mode.Game || !_settings.RequireGameFocus) return;
        if (!focused && Current == State.Playing)
        {
            _control.Paused = true;
            _autoPaused = true;
            SetState(State.Paused);
        }
        else if (focused && Current == State.Paused && _autoPaused)
        {
            _autoPaused = false;
            _control.Paused = false;
            SetState(State.Playing);
        }
    }

    private void SetState(State s)
    {
        if (Current == s) return;
        Current = s;
        StateChanged?.Invoke(s);
    }

    public async ValueTask DisposeAsync()
    {
        _watcher.FocusChanged -= OnFocusChanged;
        await StopAsync();
        await _game.DisposeAsync();
        await _preview.DisposeAsync();
    }
}
