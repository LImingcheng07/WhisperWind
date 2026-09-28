using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhisperWind.App.Services;

namespace WhisperWind.App.ViewModels;

public partial class MainViewModel
{
    public ObservableCollection<CheckItem> Checks { get; } = new();

    [ObservableProperty] private string _checkSummary = "尚未自检";
    [ObservableProperty] private CheckLevel _checkLevel = CheckLevel.Info;
    [ObservableProperty] private bool _checksBusy;

    private CheckItem? _networkCheck;

    [RelayCommand]
    private async Task RunChecksAsync()
    {
        if (ChecksBusy) return;
        ChecksBusy = true;
        try
        {
            var all = await _diag.RunAllAsync();
            var local = _diag.RunLocal();
            _networkCheck = all.FirstOrDefault(c => local.All(l => l.Title != c.Title));
            ShowChecks(all);
        }
        catch (Exception ex)
        {
            Toast($"自检出错：{ex.Message}");
        }
        finally
        {
            ChecksBusy = false;
        }
    }

    /// <summary>定时刷新本地项（不联网），保留上次的网络检测结果</summary>
    private void RefreshLocalChecks()
    {
        IsAdmin = DiagnosticsService.IsElevated;
        UpdateGameStatus();
        if (ChecksBusy) return;
        List<CheckItem> list;
        try { list = _diag.RunLocal(); }
        catch { return; }
        if (_networkCheck != null) list.Add(_networkCheck);
        ShowChecks(list);
    }

    private void ShowChecks(List<CheckItem> list)
    {
        // 内容没变就不刷新，避免列表闪烁
        if (list.Count == Checks.Count && list.Zip(Checks).All(p =>
                p.First.Title == p.Second.Title && p.First.Detail == p.Second.Detail && p.First.Level == p.Second.Level))
            return;

        Checks.Clear();
        foreach (var c in list.OrderByDescending(c => c.Level)) Checks.Add(c);

        int errors = list.Count(c => c.Level == CheckLevel.Error);
        int warns = list.Count(c => c.Level == CheckLevel.Warn);
        CheckLevel = errors > 0 ? CheckLevel.Error : warns > 0 ? CheckLevel.Warn : CheckLevel.Ok;
        CheckSummary = errors > 0 ? $"{errors} 项需要处理" + (warns > 0 ? $"，{warns} 项提醒" : "")
            : warns > 0 ? $"{warns} 项提醒"
            : "一切就绪";
    }

    [RelayCommand]
    private void RunCheckAction(CheckItem? item)
    {
        try
        {
            item?.Action?.Invoke();
        }
        catch (Exception ex)
        {
            Toast(ex.Message);
        }
        RefreshLocalChecks();
    }

    [RelayCommand]
    private void RestartAsAdmin() => DiagnosticsService.RestartAsAdmin();
}
