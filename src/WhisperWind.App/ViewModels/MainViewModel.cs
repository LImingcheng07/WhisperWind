using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhisperWind.App.BuiltIn;
using WhisperWind.App.Services;

namespace WhisperWind.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly HarmonicaEngine _engine;
    private readonly TargetWindowWatcher _watcher;
    private readonly GlobalHotkeyService _hotkey;
    private readonly string _appDataDir;

    [ObservableProperty] private string _statusText = "准备就绪";
    [ObservableProperty] private string _targetWindowText = "未锁定";
    [ObservableProperty] private bool _isTargetFocused;
    [ObservableProperty] private string _currentTrackText = "无";
    [ObservableProperty] private string _playButtonText = "▶ 演奏 (F8)";
    [ObservableProperty] private double _speed = 1.0;
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private bool _hasError;

    public ObservableCollection<TrackMeta> Tracks { get; } = new();

    public MainViewModel(
        HarmonicaEngine engine,
        TargetWindowWatcher watcher,
        GlobalHotkeyService hotkey,
        string appDataDir)
    {
        _engine = engine;
        _watcher = watcher;
        _hotkey = hotkey;
        _appDataDir = appDataDir;

        _engine.StateChanged += OnEngineState;
        _engine.Error += msg => ShowError(msg);
        _watcher.FocusChanged += focused => Application.Current.Dispatcher.Invoke(() =>
        {
            IsTargetFocused = focused;
            TargetWindowText = focused
                ? $"✓ {_watcher.TargetTitle}"
                : _watcher.TargetHwnd == IntPtr.Zero
                    ? "未锁定"
                    : $"○ {_watcher.TargetTitle} (后台)";
        });

        _hotkey.PlayTogglePressed += () => Application.Current.Dispatcher.Invoke(PlayOrToggle);
        _hotkey.PauseResumePressed += () => Application.Current.Dispatcher.Invoke(PauseOrResume);
        _hotkey.EmergencyStopPressed += () => Application.Current.Dispatcher.Invoke(Stop);

        // 加载预置曲
        foreach (var t in BuiltInTracks.EnsureBuilt(_appDataDir))
        {
            Tracks.Add(t);
        }
    }

    private void OnEngineState(HarmonicaEngine.State s)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            StatusText = s switch
            {
                HarmonicaEngine.State.Idle => "准备就绪",
                HarmonicaEngine.State.Loading => "加载中…",
                HarmonicaEngine.State.Ready => "已加载",
                HarmonicaEngine.State.Playing => "演奏中…",
                HarmonicaEngine.State.Paused => "已暂停",
                _ => s.ToString(),
            };
            PlayButtonText = s switch
            {
                HarmonicaEngine.State.Playing => "⏸ 暂停 (F9)",
                HarmonicaEngine.State.Paused => "▶ 继续 (F8)",
                _ => "▶ 演奏 (F8)",
            };
        });
    }

    [RelayCommand]
    private async Task LoadTrackAsync(TrackMeta? meta)
    {
        if (meta == null) return;
        var path = Path.Combine(_appDataDir, "WhisperWind", "tracks", meta.FileName);
        await _engine.LoadAsync(path);
        CurrentTrackText = meta.Name;
    }

    [RelayCommand]
    private async Task PlayOrToggle()
    {
        _engine.SetSpeed(Speed);
        await _engine.PlayAsync();
    }

    [RelayCommand]
    private void PauseOrResume()
    {
        if (_engine.Current == HarmonicaEngine.State.Playing) _engine.Pause();
        else if (_engine.Current == HarmonicaEngine.State.Paused) _engine.Resume();
    }

    [RelayCommand]
    private void Stop() => _engine.Stop();

    [RelayCommand]
    private void DismissError() => HasError = false;

    private void ShowError(string msg) => Application.Current.Dispatcher.Invoke(() =>
    {
        ErrorMessage = msg;
        HasError = true;
    });

    public void StartServices(Window window)
    {
        _watcher.Start();
        _hotkey.Start(window);
    }

    public void StopServices()
    {
        _hotkey.Stop();
        _watcher.Stop();
    }
}
