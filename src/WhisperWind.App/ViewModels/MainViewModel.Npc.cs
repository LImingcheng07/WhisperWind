using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhisperWind.Core;

namespace WhisperWind.App.ViewModels;

/// <summary>NPC 任务要求吹的简谱码</summary>
public sealed record NpcCode(string Name, string Code, string Hint);

public partial class MainViewModel
{
    public IReadOnlyList<NpcCode> NpcCodes { get; } = new[]
    {
        new NpcCode("佐拉", "7676354", "NPC 任务口琴码"),
        new NpcCode("老乔", "5123543123", "NPC 任务口琴码"),
        new NpcCode("唐吉", "6712171", "NPC 任务口琴码"),
    };

    [ObservableProperty] private string _customJianpu = "1 2 3 1 | 1 2 3 1 | 3 4 5- | 3 4 5-";
    [ObservableProperty] private int _customBeatMs = 450;

    [RelayCommand]
    private async Task PlayNpcAsync(NpcCode? code)
    {
        if (code == null) return;
        // 任务码一个数字一拍，节奏略快
        await LoadJianpuAsync(code.Code, 420, $"{code.Name} · {code.Code}", code.Hint);
    }

    [RelayCommand]
    private Task LoadCustomJianpuAsync() =>
        LoadJianpuAsync(CustomJianpu, Math.Clamp(CustomBeatMs, 100, 3000), "自定义简谱", "手写小段旋律");

    /// <summary>按键测试：在游戏里依次吹 1..7、高音 1，确认 8 个键都能响</summary>
    [RelayCommand]
    private async Task KeyTestAsync()
    {
        await LoadJianpuAsync("1 2 3 4 5 6 7 1'", 450, "按键测试", "从 do 吹到高音 do，每个孔一次");
        if (HasSong) await _engine.PlayAsync(Services.HarmonicaEngine.Mode.Game);
    }

    private async Task LoadJianpuAsync(string text, int beatMs, string title, string subtitle)
    {
        List<MidiNote> notes;
        try
        {
            notes = Jianpu.Parse(text, beatMs);
        }
        catch (Exception ex)
        {
            Toast($"简谱解析失败：{ex.Message}");
            return;
        }
        CurrentTrack = null;
        SongSubtitle = subtitle;
        await _engine.LoadNotesAsync(notes, title);
        CurrentPage = "now";
    }
}
