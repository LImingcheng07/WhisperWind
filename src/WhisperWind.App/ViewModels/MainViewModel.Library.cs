using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using WhisperWind.App.Services;

namespace WhisperWind.App.ViewModels;

public partial class MainViewModel
{
    private List<TrackItem> _allTracks = new();

    /// <summary>曲库页显示的（已按搜索/来源过滤）</summary>
    public ObservableCollection<TrackItem> LibraryItems { get; } = new();

    /// <summary>右侧「山间小调」：内置曲</summary>
    public ObservableCollection<TrackItem> QuickSongs { get; } = new();

    [ObservableProperty] private string _libraryQuery = "";
    [ObservableProperty] private string _libraryFilter = "all";
    [ObservableProperty] private TrackItem? _currentTrack;
    [ObservableProperty] private string _libraryCountText = "";

    partial void OnLibraryQueryChanged(string value) => ApplyLibraryFilter();
    partial void OnLibraryFilterChanged(string value) => ApplyLibraryFilter();

    public async Task RefreshLibraryAsync()
    {
        try
        {
            _allTracks = await Task.Run(_library.Scan);
        }
        catch (Exception ex)
        {
            Toast($"扫描曲库失败：{ex.Message}");
            return;
        }
        QuickSongs.Clear();
        foreach (var t in _allTracks.Where(t => t.Source == TrackSource.BuiltIn)) QuickSongs.Add(t);
        ApplyLibraryFilter();
        MarkDownloaded();
    }

    private void ApplyLibraryFilter()
    {
        var q = LibraryQuery.Trim();
        IEnumerable<TrackItem> items = _allTracks;
        items = LibraryFilter switch
        {
            "builtin" => items.Where(t => t.Source == TrackSource.BuiltIn),
            "imported" => items.Where(t => t.Source == TrackSource.Imported),
            "online" => items.Where(t => t.Source == TrackSource.Online),
            _ => items,
        };
        if (q.Length > 0)
            items = items.Where(t => t.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                                  || t.Subtitle.Contains(q, StringComparison.OrdinalIgnoreCase));
        LibraryItems.Clear();
        foreach (var t in items) LibraryItems.Add(t);
        LibraryCountText = $"共 {_allTracks.Count} 首 · 显示 {LibraryItems.Count} 首";
    }

    /// <summary>选曲并跳到「正在吹」</summary>
    [RelayCommand]
    private async Task OpenTrackAsync(TrackItem? item)
    {
        if (item == null) return;
        await LoadTrackAsync(item);
        CurrentPage = "now";
    }

    private async Task LoadTrackAsync(TrackItem item)
    {
        CurrentTrack = item;
        SongSubtitle = $"{item.Subtitle} · {item.SourceText}";
        await _engine.LoadAsync(item.FullPath, item.Title);
    }

    private async Task StepTrackAsync(int delta)
    {
        var list = LibraryItems.Count > 0 ? LibraryItems.ToList() : _allTracks;
        if (list.Count == 0) return;
        int i = CurrentTrack == null ? -1 : list.FindIndex(t => t.FullPath == CurrentTrack.FullPath);
        i = i < 0 ? (delta > 0 ? 0 : list.Count - 1) : (i + delta + list.Count) % list.Count;
        bool wasPlaying = _engine.IsRunning;
        var mode = _engine.CurrentMode;
        await LoadTrackAsync(list[i]);
        if (wasPlaying && HasSong) await _engine.PlayAsync(mode);
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        var dlg = new OpenFileDialog
        {
            Title = "导入 MIDI",
            Filter = "MIDI 文件 (*.mid;*.midi)|*.mid;*.midi|所有文件 (*.*)|*.*",
            Multiselect = true,
        };
        if (dlg.ShowDialog() != true) return;
        int ok = 0;
        TrackItem? last = null;
        foreach (var f in dlg.FileNames)
        {
            try
            {
                last = await Task.Run(() => _library.Import(f));
                ok++;
            }
            catch (Exception ex)
            {
                Toast($"{Path.GetFileName(f)} 导入失败：{ex.Message}");
            }
        }
        if (ok == 0) return;
        await RefreshLibraryAsync();
        Toast($"已导入 {ok} 首");
        if (ok == 1 && last != null) await OpenTrackAsync(last);
    }

    [RelayCommand]
    private async Task DeleteTrackAsync(TrackItem? item)
    {
        if (item is not { CanDelete: true }) return;
        if (CurrentTrack?.FullPath == item.FullPath) await _engine.StopAsync();
        try
        {
            _library.Delete(item);
        }
        catch (Exception ex)
        {
            Toast($"删除失败：{ex.Message}");
            return;
        }
        await RefreshLibraryAsync();
        Toast($"已删除「{item.Title}」");
    }

    [RelayCommand]
    private void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(_library.UserDir);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_library.UserDir}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Toast($"打开失败：{ex.Message}");
        }
    }

    [RelayCommand]
    private Task RefreshLibraryCmdAsync() => RefreshLibraryAsync();
}
