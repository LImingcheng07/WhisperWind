using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhisperWind.App.Services;

namespace WhisperWind.App.ViewModels;

/// <summary>键位设置里的一个槽（主键 1..8）</summary>
public partial class KeySlot : ObservableObject
{
    public KeySlot(int index, string degree, string id)
    {
        Index = index;
        Degree = degree;
        _id = id;
    }

    public int Index { get; }
    public string Degree { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Display))]
    private string _id;

    [ObservableProperty] private bool _isCapturing;

    public string Display => InputBinding.KeyDisplay(Id);
}

public sealed record Option<T>(T Value, string Label);

public partial class MainViewModel
{
    private static readonly string[] Degrees = { "1 do", "2 re", "3 mi", "4 fa", "5 sol", "6 la", "7 si", "i 高do" };

    public ObservableCollection<KeySlot> KeySlots { get; } = new();

    public IReadOnlyList<Option<string>> ModifierOptions { get; } = new[]
    {
        new Option<string>("MouseLeft", "鼠标左键"),
        new Option<string>("MouseMiddle", "鼠标中键"),
        new Option<string>("MouseRight", "鼠标右键"),
        new Option<string>("MouseX1", "鼠标侧键 1（后退）"),
        new Option<string>("MouseX2", "鼠标侧键 2（前进）"),
        new Option<string>("LeftShift", "左 Shift"),
        new Option<string>("LeftCtrl", "左 Ctrl"),
        new Option<string>("LeftAlt", "左 Alt"),
    };

    public IReadOnlyList<Option<int>> PreviewPrograms { get; } = new[]
    {
        new Option<int>(22, "口琴"),
        new Option<int>(21, "手风琴"),
        new Option<int>(73, "长笛"),
        new Option<int>(79, "陶笛"),
        new Option<int>(0, "钢琴"),
    };

    public IReadOnlyList<Option<string>> AiProviders { get; } = new[]
    {
        new Option<string>("anthropic", "Anthropic Claude"),
        new Option<string>("openai", "OpenAI"),
        new Option<string>("custom", "OpenAI 兼容（自定义地址）"),
    };

    [ObservableProperty] private string _sharpModifier = AppSettings.DefaultSharp;
    [ObservableProperty] private string _octaveModifier = AppSettings.DefaultOctaveUp;
    [ObservableProperty] private string _octaveDownModifier = AppSettings.DefaultOctaveDown;
    [ObservableProperty] private int _modifierLeadMs;
    [ObservableProperty] private int _minNoteGapMs;
    [ObservableProperty] private int _releaseGapMs;
    [ObservableProperty] private bool _requireGameFocus;
    [ObservableProperty] private string _gameWindowKeywords = "";
    [ObservableProperty] private bool _overlayAutoShow;
    [ObservableProperty] private double _overlayOpacity = 0.94;
    [ObservableProperty] private int _previewProgram = 22;
    [ObservableProperty] private string _customIndexUrl = "";
    [ObservableProperty] private string _aiProvider = "anthropic";
    [ObservableProperty] private string _aiApiKey = "";
    [ObservableProperty] private string _aiModel = "";
    [ObservableProperty] private string _aiEndpoint = "";
    [ObservableProperty] private string _bindingError = "";
    [ObservableProperty] private bool _settingsDirty;

    private KeySlot? _capturing;
    private bool _loadingSettings;

    private void InitSettingsPage()
    {
        _loadingSettings = true;
        var s = _settings;
        KeySlots.Clear();
        for (int i = 0; i < 8; i++) KeySlots.Add(new KeySlot(i, Degrees[i], s.MainKeys[i]));
        SharpModifier = s.SharpModifier;
        OctaveModifier = s.OctaveModifier;
        OctaveDownModifier = s.OctaveDownModifier;
        ModifierLeadMs = s.ModifierLeadMs;
        MinNoteGapMs = s.MinNoteGapMs;
        ReleaseGapMs = s.ReleaseGapMs;
        RequireGameFocus = s.RequireGameFocus;
        GameWindowKeywords = s.GameWindowKeywords;
        OverlayAutoShow = s.OverlayAutoShow;
        OverlayOpacity = s.OverlayOpacity;
        PreviewProgram = s.PreviewProgram;
        CustomIndexUrl = s.CustomIndexUrl ?? "";
        AiProvider = s.AiProvider;
        LoadAiKeyFields();
        BindingError = "";
        SettingsDirty = false;
        _loadingSettings = false;
    }

    private void LoadAiKeyFields()
    {
        var s = _settings;
        AiApiKey = AiProvider switch
        {
            "openai" => s.OpenaiApiKey ?? "",
            "custom" => s.CustomApiKey ?? "",
            _ => s.AnthropicApiKey ?? "",
        };
        AiModel = s.AiModel;
        AiEndpoint = s.CustomEndpoint;
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_loadingSettings) return;
        switch (e.PropertyName)
        {
            case nameof(SharpModifier) or nameof(OctaveModifier) or nameof(OctaveDownModifier) or nameof(ModifierLeadMs) or nameof(MinNoteGapMs)
                or nameof(ReleaseGapMs) or nameof(RequireGameFocus) or nameof(GameWindowKeywords) or nameof(OverlayAutoShow)
                or nameof(PreviewProgram) or nameof(CustomIndexUrl) or nameof(AiProvider) or nameof(AiApiKey)
                or nameof(AiModel) or nameof(AiEndpoint):
                SettingsDirty = true;
                break;
            case nameof(OverlayOpacity):
                // 透明度即时生效
                _settings.OverlayOpacity = OverlayOpacity;
                break;
        }
    }

    partial void OnAiProviderChanged(string value)
    {
        if (_loadingSettings) return;
        _loadingSettings = true;
        AiApiKey = value switch
        {
            "openai" => _settings.OpenaiApiKey ?? "",
            "custom" => _settings.CustomApiKey ?? "",
            _ => _settings.AnthropicApiKey ?? "",
        };
        AiModel = value == "anthropic" ? AiAdvisorService.AnthropicModel : AiAdvisorService.OpenAIModel;
        _loadingSettings = false;
    }

    // ===== 键位捕获 =====

    public bool IsCapturingKey => _capturing != null;

    [RelayCommand]
    private void BeginCapture(KeySlot? slot)
    {
        if (_capturing != null) _capturing.IsCapturing = false;
        _capturing = slot;
        if (slot != null) slot.IsCapturing = true;
    }

    /// <summary>设置页收到按键时调用（id = WPF Key 名或 MouseX1 等）。返回是否消费。</summary>
    public bool CaptureKey(string id)
    {
        var slot = _capturing;
        if (slot == null) return false;
        slot.IsCapturing = false;
        _capturing = null;
        if (id == "Escape") return true;
        try
        {
            InputBinding.Parse(id);
        }
        catch
        {
            Toast($"不支持的按键：{id}");
            return true;
        }
        // 与其他槽重复则交换
        var dup = KeySlots.FirstOrDefault(k => k != slot && k.Id == id);
        if (dup != null) dup.Id = slot.Id;
        slot.Id = id;
        SettingsDirty = true;
        return true;
    }

    [RelayCommand]
    private void ResetKeys()
    {
        var def = AppSettings.DefaultMainKeys();
        for (int i = 0; i < 8; i++) KeySlots[i].Id = def[i];
        SharpModifier = AppSettings.DefaultSharp;
        OctaveModifier = AppSettings.DefaultOctaveUp;
        OctaveDownModifier = AppSettings.DefaultOctaveDown;
        SettingsDirty = true;
    }

    [RelayCommand]
    private void RevertSettings() => InitSettingsPage();

    [RelayCommand]
    private void SaveSettings()
    {
        var s = _settings;
        var trial = new AppSettings
        {
            MainKeys = KeySlots.Select(k => k.Id).ToArray(),
            SharpModifier = SharpModifier,
            OctaveModifier = OctaveModifier,
            OctaveDownModifier = OctaveDownModifier,
        };
        KeyBindingSet bindings;
        try
        {
            bindings = KeyBindingSet.FromSettings(trial);
            var problems = bindings.Validate().ToList();
            if (problems.Count > 0)
            {
                BindingError = string.Join("；", problems);
                return;
            }
        }
        catch (Exception ex)
        {
            BindingError = ex.Message;
            return;
        }
        BindingError = "";

        s.MainKeys = trial.MainKeys;
        s.SharpModifier = SharpModifier;
        s.OctaveModifier = OctaveModifier;
        s.OctaveDownModifier = OctaveDownModifier;
        s.ModifierLeadMs = Math.Clamp(ModifierLeadMs, 0, 100);
        s.MinNoteGapMs = Math.Clamp(MinNoteGapMs, 0, 500);
        s.ReleaseGapMs = Math.Clamp(ReleaseGapMs, 0, 200);
        s.RequireGameFocus = RequireGameFocus;
        s.GameWindowKeywords = GameWindowKeywords.Trim();
        s.OverlayAutoShow = OverlayAutoShow;
        s.OverlayOpacity = OverlayOpacity;
        s.PreviewProgram = PreviewProgram;
        s.CustomIndexUrl = string.IsNullOrWhiteSpace(CustomIndexUrl) ? null : CustomIndexUrl.Trim();
        s.OnlineProvider = s.CustomIndexUrl == null ? "bitmidi" : "custom";
        s.AiProvider = AiProvider;
        switch (AiProvider)
        {
            case "openai": s.OpenaiApiKey = AiApiKey.Trim(); break;
            case "custom": s.CustomApiKey = AiApiKey.Trim(); break;
            default: s.AnthropicApiKey = AiApiKey.Trim(); break;
        }
        s.AiModel = AiModel.Trim();
        s.CustomEndpoint = AiEndpoint.Trim();

        // 应用到各服务
        _gamePlayer.UpdateBindings(bindings);
        _gamePlayer.ModifierLeadMs = s.ModifierLeadMs;
        _preview.Program = s.PreviewProgram;
        _watcher.SetKeywords(s.GameWindowKeywords);
        _online.Provider = s.OnlineProvider;
        _online.CustomIndexUrl = s.CustomIndexUrl;
        _online.ResetCustomCache();
        KeyLabels = bindings.Main.Select(b => b.DisplayName).ToList();
        ApplyAiSettings();
        _engine.Recompile();

        try
        {
            s.Save();
            SettingsDirty = false;
            Toast("设置已保存");
        }
        catch (Exception ex)
        {
            Toast($"保存失败：{ex.Message}");
        }
        RefreshLocalChecks();
    }
}
