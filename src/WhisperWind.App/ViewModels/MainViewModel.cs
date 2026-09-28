using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhisperWind.App.Online;
using WhisperWind.App.Services;
using WhisperWind.Core;

namespace WhisperWind.App.ViewModels;

/// <summary>
/// 主视图模型：正在吹（本文件）、曲库、在线曲库、NPC 歌诀、自检、设置、AI 分别在同名 partial 文件里。
/// 主窗口和悬浮窗共用同一个实例。
/// </summary>
public partial class MainViewModel : ObservableObject, IAsyncDisposable
{
    public static string DataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WhisperWind");

    private readonly AppSettings _settings;
    private readonly WindowsNotePlayer _gamePlayer;
    private readonly MidiPreviewPlayer _preview;
    private readonly TargetWindowWatcher _watcher;
    private readonly GlobalHotkeyService _hotkeys;
    private readonly HarmonicaEngine _engine;
    private readonly LibraryService _library;
    private readonly OnlineLibraryService _online;
    private readonly AiAdvisorService _ai;
    private readonly DiagnosticsService _diag;
    private readonly Dispatcher _ui;
    private readonly DispatcherTimer _tick;
    private readonly DispatcherTimer _slowTick;
    private bool _suppressRebuild;
    private bool _overlayShownOnce;

    public AppSettings Settings => _settings;
    public string Version { get; } = "v" + (typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.2.0");

    // ===== 页面 =====
    [ObservableProperty] private string _currentPage = "now";

    // ===== 当前曲目 =====
    [ObservableProperty] private string _songTitle = "尚未选曲";
    [ObservableProperty] private string _songSubtitle = "从右侧「山间小调」或曲库里挑一首";
    [ObservableProperty] private string _bpmText = "--";
    [ObservableProperty] private string _durationText = "0:00";
    [ObservableProperty] private string _noteCountText = "--";
    [ObservableProperty] private string _transposeText = "--";
    [ObservableProperty] private string _scoreText = "--";
    [ObservableProperty] private string _scoreHint = "";
    [ObservableProperty] private bool _hasSong;
    [ObservableProperty] private IReadOnlyList<KeyEvent>? _planEvents;

    // ===== 播放进度 =====
    [ObservableProperty] private double _positionMs;
    [ObservableProperty] private double _durationMs = 1;
    [ObservableProperty] private string _positionText = "0:00";
    [ObservableProperty] private int _activeIndex = -1;
    [ObservableProperty] private int _activeHole = -1;
    [ObservableProperty] private bool _sharpOn;
    [ObservableProperty] private bool _octaveOn;
    [ObservableProperty] private bool _octaveDownOn;

    // ===== 状态 =====
    [ObservableProperty] private string _stateText = "未选曲";
    [ObservableProperty] private string _stageText = "选一首曲子，吹给山听";
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isPreviewing;
    [ObservableProperty] private string _playGlyph = Glyph.Play;
    [ObservableProperty] private string _previewGlyph = Glyph.Headphone;
    [ObservableProperty] private int _countdown;

    // ===== 调音 =====
    public ObservableCollection<MidiTrackInfo> SongTracks { get; } = new();
    [ObservableProperty] private MidiTrackInfo? _selectedTrack;
    [ObservableProperty] private int _transpose;
    [ObservableProperty] private double _speed = 1.0;
    [ObservableProperty] private int _countdownSeconds = 3;
    [ObservableProperty] private bool _canTune;

    // ===== 游戏/系统状态 =====
    [ObservableProperty] private bool _gameFound;
    [ObservableProperty] private bool _gameFocused;
    [ObservableProperty] private string _gameStatusText = "未找到游戏窗口";
    [ObservableProperty] private bool _isAdmin;
    [ObservableProperty] private bool _inputBlocked;
    [ObservableProperty] private IReadOnlyList<string> _keyLabels = Array.Empty<string>();

    // ===== 提示条 / 悬浮窗 =====
    [ObservableProperty] private string _toastText = "";
    [ObservableProperty] private bool _toastVisible;
    [ObservableProperty] private bool _overlayVisible;
    private DispatcherTimer? _toastTimer;

    /// <summary>进度条拖动中，暂停用引擎位置刷新</summary>
    public bool IsSeeking { get; set; }

    public MainViewModel()
    {
        _ui = Application.Current.Dispatcher;
        _settings = AppSettings.Load();

        KeyBindingSet bindings;
        try { bindings = KeyBindingSet.FromSettings(_settings); }
        catch
        {
            _settings.MainKeys = AppSettings.DefaultMainKeys();
            _settings.ResetModifiers();
            bindings = KeyBindingSet.FromSettings(_settings);
        }

        _gamePlayer = new WindowsNotePlayer(bindings) { ModifierLeadMs = _settings.ModifierLeadMs };
        _preview = new MidiPreviewPlayer { Program = _settings.PreviewProgram };
        _watcher = new TargetWindowWatcher(_settings.GameWindowKeywords);
        _hotkeys = new GlobalHotkeyService();
        _engine = new HarmonicaEngine(_gamePlayer, _preview, _watcher, _settings);
        _library = new LibraryService(DataDir);
        _online = new OnlineLibraryService(DataDir) { Provider = _settings.OnlineProvider, CustomIndexUrl = _settings.CustomIndexUrl };
        _ai = new AiAdvisorService();
        _diag = new DiagnosticsService(_watcher, _hotkeys, _gamePlayer, _settings, DataDir);

        _speed = _engine.Speed;
        _countdownSeconds = _settings.CountdownSeconds;
        _isAdmin = DiagnosticsService.IsElevated;
        KeyLabels = bindings.Main.Select(b => b.DisplayName).ToList();

        _engine.StateChanged += _ => _ui.BeginInvoke(UpdateState);
        _engine.PlanChanged += () => _ui.BeginInvoke(UpdatePlanInfo);
        _engine.Error += msg => _ui.BeginInvoke(() => Toast(msg));
        _gamePlayer.InputBlocked += () => _ui.BeginInvoke(() =>
        {
            InputBlocked = true;
            Toast("按键被 Windows 拦截：游戏以管理员运行，请到「自检」以管理员重启风声");
        });
        _watcher.FocusChanged += _ => _ui.BeginInvoke(UpdateGameStatus);
        _watcher.TargetChanged += () => _ui.BeginInvoke(UpdateGameStatus);

        _hotkeys.PlayPausePressed += () => PlayGameCommand.Execute(null);
        _hotkeys.StopPressed += () => StopCommand.Execute(null);
        _hotkeys.OverlayPressed += () => OverlayVisible = !OverlayVisible;

        _tick = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) => OnTick(), _ui);
        _slowTick = new DispatcherTimer(TimeSpan.FromSeconds(3), DispatcherPriority.Background, (_, _) => RefreshLocalChecks(), _ui);

        InitSettingsPage();
        InitAi();
        UpdateState();
    }

    /// <summary>主窗口句柄就绪后调用：启动窗口监测、热键、定时器，扫描曲库</summary>
    public async Task StartAsync(IntPtr hwnd)
    {
        _watcher.Start();
        _hotkeys.Start(hwnd);
        _tick.Start();
        _slowTick.Start();
        RefreshLocalChecks();
        await RefreshLibraryAsync();
        _ = RunChecksAsync();
        _ = LoadOnlineAsync(OnlineQuery, 0);
    }

    // ===== 播放控制 =====

    /// <summary>F8 / 主播放键：未开始 → 游戏内吹奏；吹奏中 → 暂停/继续；等待中 → 取消</summary>
    [RelayCommand]
    private async Task PlayGameAsync()
    {
        if (_engine.IsRunning)
        {
            if (_engine.CurrentMode == HarmonicaEngine.Mode.Preview)
            {
                await _engine.StopAsync();
                await _engine.PlayAsync(HarmonicaEngine.Mode.Game, (long)PositionMs);
                return;
            }
            _engine.TogglePause();
            return;
        }
        if (!HasSong)
        {
            Toast("先选一首曲子");
            return;
        }
        await _engine.PlayAsync(HarmonicaEngine.Mode.Game, (long)PositionMs);
    }

    /// <summary>本机试听（不发按键）</summary>
    [RelayCommand]
    private async Task PreviewAsync()
    {
        if (_engine.IsRunning)
        {
            if (_engine.CurrentMode == HarmonicaEngine.Mode.Preview)
            {
                _engine.TogglePause();
                return;
            }
            await _engine.StopAsync();
        }
        if (!HasSong)
        {
            Toast("先选一首曲子");
            return;
        }
        if (MidiPreviewPlayer.DeviceCount == 0)
        {
            Toast("系统里没有 MIDI 输出设备，无法试听");
            return;
        }
        await _engine.PlayAsync(HarmonicaEngine.Mode.Preview, (long)PositionMs);
    }

    [RelayCommand]
    private void Stop()
    {
        _engine.Stop();
        PositionMs = 0;
        PositionText = "0:00";
    }

    [RelayCommand]
    private Task PrevAsync() => StepTrackAsync(-1);

    [RelayCommand]
    private Task NextAsync() => StepTrackAsync(+1);

    public void SeekTo(double ms)
    {
        _engine.Seek((long)ms);
        PositionMs = ms;
        PositionText = FormatTime(ms);
    }

    /// <summary>点击口琴孔：本机放一下这个孔的音（C4..C5）</summary>
    public void PreviewHole(int hole)
    {
        int[] pitches = { 60, 62, 64, 65, 67, 69, 71, 72 };
        if (hole < 0 || hole > 7 || _engine.IsRunning) return;
        _preview.Program = _settings.PreviewProgram;
        _ = _preview.PlayNoteAsync(pitches[hole]);
        ActiveHole = hole;
    }

    [RelayCommand]
    private void ToggleOverlay() => OverlayVisible = !OverlayVisible;

    [RelayCommand]
    private void Navigate(string page) => CurrentPage = page;

    // ===== 调音 =====

    partial void OnSpeedChanged(double value)
    {
        _engine.Speed = value;
        _settings.Speed = _engine.Speed;
    }

    [RelayCommand]
    private void SpeedUp() => Speed = Math.Min(1.5, Math.Round(Speed + 0.05, 2));

    [RelayCommand]
    private void SpeedDown() => Speed = Math.Max(0.5, Math.Round(Speed - 0.05, 2));

    partial void OnCountdownSecondsChanged(int value) => _settings.CountdownSeconds = value;

    partial void OnTransposeChanged(int value)
    {
        TransposeText = value == 0 ? "无" : value > 0 ? $"+{value}" : value.ToString();
        if (_suppressRebuild || !HasSong || _engine.IsJianpu) return;
        _ = _engine.RebuildAsync(_engine.TrackIndex, value);
    }

    partial void OnSelectedTrackChanged(MidiTrackInfo? value)
    {
        if (_suppressRebuild || value == null || !HasSong) return;
        _ = _engine.RebuildAsync(value.Index, Transpose);
    }

    [RelayCommand]
    private async Task AutoTransposeAsync()
    {
        if (_engine.Song == null) return;
        var notes = _engine.Song.GetTrackNotes(_engine.TrackIndex);
        await _engine.RebuildAsync(_engine.TrackIndex, MelodyAnalyzer.BestTranspose(notes, new HarmonicaMapping()));
    }

    // ===== 状态刷新 =====

    private void UpdatePlanInfo()
    {
        var plan = _engine.Plan;
        _suppressRebuild = true;
        try
        {
            HasSong = plan is { IsValid: true };
            SongTitle = string.IsNullOrEmpty(_engine.Title) ? "尚未选曲" : _engine.Title;
            PlanEvents = plan?.Events;
            DurationMs = Math.Max(1, plan?.DurationMs ?? 1);
            DurationText = FormatTime(plan?.DurationMs ?? 0);
            NoteCountText = plan?.ConvertedNoteCount.ToString() ?? "--";
            ScoreText = plan == null ? "--" : plan.Score.ToString();
            ScoreHint = plan == null ? "" : BuildScoreHint(plan);
            BpmText = _engine.Song is { } s ? Math.Round(s.Bpm).ToString() : "--";

            SongTracks.Clear();
            if (_engine.Song != null)
                foreach (var t in _engine.Song.Tracks.Where(t => t.NoteCount > 0 && !t.IsDrum))
                    SongTracks.Add(t);
            SelectedTrack = SongTracks.FirstOrDefault(t => t.Index == _engine.TrackIndex);
            Transpose = _engine.Transpose;
            TransposeText = Transpose == 0 ? "无" : Transpose > 0 ? $"+{Transpose}" : Transpose.ToString();
            CanTune = _engine.Song != null;
            PositionMs = 0;
            PositionText = "0:00";
        }
        finally
        {
            _suppressRebuild = false;
        }
    }

    private static string BuildScoreHint(PlaybackPlan p)
    {
        var parts = new List<string>();
        if (p.ChordReducedCount > 0) parts.Add($"和弦取最高音 {p.ChordReducedCount}");
        if (p.FoldedNoteCount > 0) parts.Add($"越界折叠 {p.FoldedNoteCount}");
        if (p.TooCloseCount > 0) parts.Add($"过密跳过 {p.TooCloseCount}");
        if (p.DroppedNoteCount > 0) parts.Add($"丢弃 {p.DroppedNoteCount}");
        return parts.Count == 0 ? "完整还原" : string.Join(" · ", parts);
    }

    private void UpdateState()
    {
        var s = _engine.Current;
        bool preview = _engine.CurrentMode == HarmonicaEngine.Mode.Preview;
        IsRunning = _engine.IsRunning;
        IsPreviewing = IsRunning && preview;

        PlayGlyph = s == State.Playing && !preview ? Glyph.Pause : Glyph.Play;
        PreviewGlyph = s == State.Playing && preview ? Glyph.Pause : Glyph.Headphone;

        StateText = s switch
        {
            State.Idle => "未选曲",
            State.Loading => "加载中",
            State.Ready => "已就绪",
            State.WaitingFocus => "等待游戏",
            State.Countdown => "倒计时",
            State.Playing => preview ? "试听中" : "吹奏中",
            State.Paused => "已暂停",
            _ => s.ToString(),
        };
        UpdateStageText();
    }

    private void UpdateStageText()
    {
        bool preview = _engine.CurrentMode == HarmonicaEngine.Mode.Preview;
        StageText = _engine.Current switch
        {
            State.Idle => "选一首曲子，吹给山听",
            State.Loading => "展卷中……",
            State.Ready => GameFound
                ? "已就绪 · 按 F8 或点 ▶，切回游戏后开始吹奏"
                : "已就绪 · 点 🎧 可先在本机试听",
            State.WaitingFocus => "请切到游戏窗口，拿出口琴 —— 切过去就开始倒计时",
            State.Countdown => $"{_engine.CountdownRemaining}",
            State.Playing => preview ? "试听中 · 本机发声，不会向游戏发送按键" : "吹奏中 · 切出游戏会自动暂停",
            State.Paused => preview ? "试听已暂停" : "已暂停 · 回到游戏自动继续，或按 F8",
            _ => "",
        };
    }

    private void UpdateGameStatus()
    {
        GameFound = _watcher.IsFound;
        GameFocused = _watcher.IsTargetFocused;
        GameStatusText = !GameFound ? "未找到游戏窗口"
            : GameFocused ? "游戏在前台"
            : "已连接游戏（后台）";
        if (GameFocused && _settings.OverlayAutoShow && !_overlayShownOnce)
        {
            _overlayShownOnce = true;
            OverlayVisible = true;
        }
        UpdateStageText();
    }

    private void OnTick()
    {
        var plan = _engine.Plan;
        if (!IsSeeking)
        {
            double pos = _engine.PositionMs;
            if (Math.Abs(pos - PositionMs) >= 1)
            {
                PositionMs = pos;
                PositionText = FormatTime(pos);
            }
        }

        int idx = _engine.ActiveIndex;
        if (idx != ActiveIndex || (_engine.IsRunning == false && ActiveHole >= 0 && idx < 0))
        {
            ActiveIndex = idx;
            if (plan != null && idx >= 0 && idx < plan.Events.Count)
            {
                var keys = plan.Events[idx].Keys;
                var main = keys.FirstOrDefault(k => k is >= GameKey.MainKey1 and <= GameKey.MainKey8);
                ActiveHole = main == GameKey.None ? -1 : main - GameKey.MainKey1;
                SharpOn = keys.Contains(GameKey.Sharp);
                OctaveOn = keys.Contains(GameKey.OctaveUp);
                OctaveDownOn = keys.Contains(GameKey.OctaveDown);
            }
            else if (_engine.IsRunning)
            {
                ActiveHole = -1;
                SharpOn = OctaveOn = OctaveDownOn = false;
            }
        }

        if (_engine.Current == State.Countdown && Countdown != _engine.CountdownRemaining)
        {
            Countdown = _engine.CountdownRemaining;
            UpdateStageText();
        }
    }

    // ===== 提示 =====

    public void Toast(string text)
    {
        ToastText = text;
        ToastVisible = true;
        _toastTimer?.Stop();
        _toastTimer = new DispatcherTimer(TimeSpan.FromSeconds(Math.Clamp(text.Length / 8.0, 3, 8)),
            DispatcherPriority.Normal, (_, _) => { ToastVisible = false; _toastTimer?.Stop(); }, _ui);
        _toastTimer.Start();
    }

    [RelayCommand]
    private void DismissToast() => ToastVisible = false;

    public static string FormatTime(double ms)
    {
        if (ms < 0) ms = 0;
        var t = TimeSpan.FromMilliseconds(ms);
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : $"{(int)t.TotalMinutes}:{t.Seconds:00}";
    }

    public async ValueTask DisposeAsync()
    {
        _tick.Stop();
        _slowTick.Stop();
        _hotkeys.Dispose();
        await _engine.DisposeAsync();
        _watcher.Dispose();
        _online.Dispose();
        try { _settings.Save(); } catch { }
    }

    private static class State
    {
        public const HarmonicaEngine.State Idle = HarmonicaEngine.State.Idle;
        public const HarmonicaEngine.State Loading = HarmonicaEngine.State.Loading;
        public const HarmonicaEngine.State Ready = HarmonicaEngine.State.Ready;
        public const HarmonicaEngine.State WaitingFocus = HarmonicaEngine.State.WaitingFocus;
        public const HarmonicaEngine.State Countdown = HarmonicaEngine.State.Countdown;
        public const HarmonicaEngine.State Playing = HarmonicaEngine.State.Playing;
        public const HarmonicaEngine.State Paused = HarmonicaEngine.State.Paused;
    }
}

/// <summary>Segoe Fluent Icons 字形</summary>
public static class Glyph
{
    public const string Play = "";
    public const string Pause = "";
    public const string Headphone = "";
}
