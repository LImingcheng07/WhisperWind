using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhisperWind.App.BuiltIn;
using WhisperWind.App.Online;
using WhisperWind.App.Services;

namespace WhisperWind.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly HarmonicaEngine _engine;
    private readonly TargetWindowWatcher _watcher;
    private readonly GlobalHotkeyService _hotkey;
    private readonly MidiImportService _importer;
    private readonly OnlineLibraryService _online;
    private readonly AiAdvisorService _ai;
    private readonly AppSettings _settings;
    private readonly string _appDataDir;

    [ObservableProperty] private string _statusText = "准备就绪";
    [ObservableProperty] private string _targetWindowText = "未锁定";
    [ObservableProperty] private bool _isTargetFocused;
    [ObservableProperty] private string _currentTrackText = "无";
    [ObservableProperty] private string _playButtonText = "▶ 演奏 (F8)";
    [ObservableProperty] private double _speed = 1.0;
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private bool _hasError;

    // AI 状态
    [ObservableProperty] private string _aiChatInput = "";
    [ObservableProperty] private string _aiChatHistory = "（对话将在此显示）";
    [ObservableProperty] private bool _aiBusy;

    // 在线曲库
    [ObservableProperty] private string _onlineStatus = "尚未刷新";
    [ObservableProperty] private bool _onlineBusy;
    [ObservableProperty] private string _onlineQuery = "";

    public ObservableCollection<TrackMeta> Tracks { get; } = new();
    public ObservableCollection<OnlineTrackMeta> OnlineTracks { get; } = new();

    public MainViewModel(
        HarmonicaEngine engine,
        TargetWindowWatcher watcher,
        GlobalHotkeyService hotkey,
        MidiImportService importer,
        OnlineLibraryService online,
        AiAdvisorService ai,
        AppSettings settings,
        string appDataDir)
    {
        _engine = engine;
        _watcher = watcher;
        _hotkey = hotkey;
        _importer = importer;
        _online = online;
        _ai = ai;
        _settings = settings;
        _appDataDir = appDataDir;

        // 注入配置
        _ai.ApiKey = settings.AnthropicApiKey ?? "";
        _ai.Model = settings.AiModel;

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
        if (!File.Exists(path))
            path = _importer.FullPath(meta.FileName);  // 用户曲目
        await _engine.LoadAsync(path);
        CurrentTrackText = meta.Name;
    }

    [RelayCommand]
    private async Task ImportLocalMidiAsync()
    {
        try
        {
            var meta = _importer.PickAndImport();
            if (meta == null) return;
            Tracks.Add(meta);
            await LoadTrackAsync(meta);
        }
        catch (Exception ex)
        {
            ShowError($"导入失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task RefreshOnlineAsync()
    {
        if (OnlineBusy) return;
        OnlineBusy = true;
        OnlineStatus = "拉取索引…";
        OnlineTracks.Clear();
        try
        {
            var list = await _online.SearchAsync(_onlineQuery);
            foreach (var t in list) OnlineTracks.Add(t);
            OnlineStatus = $"已加载 {list.Count} 首";
        }
        catch (Exception ex)
        {
            OnlineStatus = $"失败: {ex.Message}";
        }
        finally
        {
            OnlineBusy = false;
        }
    }

    [RelayCommand]
    private async Task DownloadOnlineAsync(OnlineTrackMeta? meta)
    {
        if (meta == null || OnlineBusy) return;
        OnlineBusy = true;
        OnlineStatus = $"下载 {meta.Name}…";
        try
        {
            var path = await _online.DownloadAsync(meta);
            var trackMeta = new TrackMeta($"{meta.Name} · {meta.Author}", Path.GetFileName(path));
            Tracks.Add(trackMeta);
            OnlineStatus = $"{meta.Name} 已加入曲库";
        }
        catch (Exception ex)
        {
            OnlineStatus = $"下载失败: {ex.Message}";
        }
        finally
        {
            OnlineBusy = false;
        }
    }

    [RelayCommand]
    private async Task LoadOnlinePageAsync(int page)
    {
        if (OnlineBusy) return;
        OnlineBusy = true;
        OnlineStatus = $"拉取第 {page} 页…";
        OnlineTracks.Clear();
        try
        {
            var list = await _online.GetPageAsync(page);
            foreach (var t in list) OnlineTracks.Add(t);
            OnlineStatus = $"第 {page} 页: {list.Count} 首";
        }
        catch (Exception ex)
        {
            OnlineStatus = $"失败: {ex.Message}";
        }
        finally
        {
            OnlineBusy = false;
        }
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

    [RelayCommand]
    private async Task AskAiAsync()
    {
        if (AiBusy) return;
        var input = AiChatInput?.Trim();
        if (string.IsNullOrEmpty(input)) return;

        AiBusy = true;
        AiChatInput = "";
        AiChatHistory += $"\n\n你: {input}\n知音: …";
        try
        {
            var plan = _engine.CurrentPlan;
            var reply = await _ai.AskAsync(input, plan, plan?.Warnings.ToList() ?? new());
            AiChatHistory += reply;
        }
        catch (Exception ex)
        {
            AiChatHistory += $"\n(出错: {ex.Message})";
        }
        finally
        {
            AiBusy = false;
        }
    }

    [RelayCommand]
    private void ClearAiHistory()
    {
        _ai.ClearHistory();
        AiChatHistory = "（对话将在此显示）";
    }

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

    public void ReloadSettings(AppSettings s)
    {
        _ai.CurrentProvider = s.AiProvider switch
        {
            "openai" => AiAdvisorService.Provider.OpenAI,
            "custom" => AiAdvisorService.Provider.Custom,
            _ => AiAdvisorService.Provider.Anthropic,
        };
        _ai.ApiKey = s.AiProvider switch
        {
            "openai" => s.OpenaiApiKey ?? "",
            "custom" => s.CustomApiKey ?? "",
            _ => s.AnthropicApiKey ?? "",
        };
        _ai.Model = s.AiModel;
        _ai.Endpoint = s.AiProvider == "custom" && !string.IsNullOrWhiteSpace(s.CustomEndpoint)
            ? s.CustomEndpoint
            : (_ai.CurrentProvider == AiAdvisorService.Provider.OpenAI
                ? AiAdvisorService.OpenAIEndpoint
                : AiAdvisorService.AnthropicEndpoint);

        // 在线曲库
        if (_online != null)
        {
            _online.Provider = s.OnlineProvider;
            _online.CustomIndexUrl = s.CustomIndexUrl;
        }
    }
}
