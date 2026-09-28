using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhisperWind.App.Online;
using WhisperWind.App.Services;

namespace WhisperWind.App.ViewModels;

/// <summary>在线曲库的一行（带下载状态）</summary>
public partial class OnlineRow : ObservableObject
{
    public OnlineRow(OnlineTrackMeta meta) => Meta = meta;

    public OnlineTrackMeta Meta { get; }
    public string Title => Meta.DisplayName;
    public string PlaysText => Meta.PlaysText;

    [ObservableProperty] private bool _isDownloaded;
    [ObservableProperty] private bool _isBusy;
}

public partial class MainViewModel
{
    public ObservableCollection<OnlineRow> OnlineItems { get; } = new();

    [ObservableProperty] private string _onlineQuery = "";
    [ObservableProperty] private int _onlinePage;
    [ObservableProperty] private int _onlinePageTotal;
    [ObservableProperty] private string _onlinePageText = "";
    [ObservableProperty] private bool _onlineBusy;
    [ObservableProperty] private string _onlineStatus = "";
    [ObservableProperty] private string _onlineHeading = "热门曲目";

    private CancellationTokenSource? _onlineCts;
    private string _onlineLastQuery = "";

    public async Task LoadOnlineAsync(string query, int page)
    {
        _onlineCts?.Cancel();
        var cts = _onlineCts = new CancellationTokenSource();
        OnlineBusy = true;
        OnlineStatus = "正在连接 BitMidi……";
        try
        {
            var result = await _online.SearchAsync(query, page, cts.Token);
            if (cts.IsCancellationRequested) return;
            _onlineLastQuery = query;
            OnlineItems.Clear();
            foreach (var m in result.Items) OnlineItems.Add(new OnlineRow(m));
            MarkDownloaded();
            OnlinePage = result.Page;
            OnlinePageTotal = Math.Max(1, result.PageTotal);
            OnlinePageText = $"第 {result.Page + 1} / {OnlinePageTotal} 页";
            OnlineHeading = string.IsNullOrWhiteSpace(query) ? "热门曲目" : $"「{query.Trim()}」的搜索结果";
            OnlineStatus = result.Items.Count > 0
                ? $"共 {result.Total:N0} 首"
                : ContainsCjk(query)
                    ? "没有找到。BitMidi 以英文曲名为主，试试英文名或拼音（如 jasmine、canon、mo li hua）"
                    : "没有找到，换个关键词试试";
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            OnlineStatus = $"连接失败：{ex.Message}（到「自检」页看看网络项）";
        }
        finally
        {
            if (_onlineCts == cts) OnlineBusy = false;
        }
    }

    private static bool ContainsCjk(string s) => s.Any(c => c >= 0x4E00 && c <= 0x9FFF);

    private void MarkDownloaded()
    {
        foreach (var r in OnlineItems) r.IsDownloaded = File.Exists(_online.CachePath(r.Meta));
    }

    [RelayCommand]
    private Task SearchOnlineAsync() => LoadOnlineAsync(OnlineQuery, 0);

    [RelayCommand]
    private Task PopularOnlineAsync()
    {
        OnlineQuery = "";
        return LoadOnlineAsync("", 0);
    }

    [RelayCommand]
    private Task OnlineNextPageAsync() =>
        OnlinePage + 1 < OnlinePageTotal ? LoadOnlineAsync(_onlineLastQuery, OnlinePage + 1) : Task.CompletedTask;

    [RelayCommand]
    private Task OnlinePrevPageAsync() =>
        OnlinePage > 0 ? LoadOnlineAsync(_onlineLastQuery, OnlinePage - 1) : Task.CompletedTask;

    [RelayCommand]
    private void SearchQuick(string q)
    {
        OnlineQuery = q;
        _ = LoadOnlineAsync(q, 0);
    }

    private async Task<TrackItem?> EnsureDownloadedAsync(OnlineRow row)
    {
        if (row.IsBusy) return null;
        row.IsBusy = true;
        try
        {
            var path = await _online.DownloadAsync(row.Meta);
            row.IsDownloaded = true;
            return await Task.Run(() => LibraryService.Describe(path, TrackSource.Online, row.Title, "BitMidi"));
        }
        catch (Exception ex)
        {
            Toast($"下载「{row.Title}」失败：{ex.Message}");
            return null;
        }
        finally
        {
            row.IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DownloadOnlineAsync(OnlineRow? row)
    {
        if (row == null) return;
        if (await EnsureDownloadedAsync(row) is null) return;
        Toast($"「{row.Title}」已收入曲库");
        await RefreshLibraryAsync();
    }

    /// <summary>下载 → 加载 → 本机试听（留在在线页）</summary>
    [RelayCommand]
    private async Task PreviewOnlineAsync(OnlineRow? row)
    {
        if (row == null) return;
        var item = await EnsureDownloadedAsync(row);
        if (item == null) return;
        await LoadTrackAsync(item);
        if (HasSong) await PreviewAsync();
        _ = RefreshLibraryAsync();
    }

    /// <summary>下载 → 加载 → 跳到正在吹</summary>
    [RelayCommand]
    private async Task PlayOnlineAsync(OnlineRow? row)
    {
        if (row == null) return;
        var item = await EnsureDownloadedAsync(row);
        if (item == null) return;
        await LoadTrackAsync(item);
        CurrentPage = "now";
        _ = RefreshLibraryAsync();
    }
}
