using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;

namespace WhisperWind.App.Services;

/// <summary>
/// 监测目标游戏窗口（"Delta Force" / "三角洲行动" / "DeltaForce"）。
/// 通过轮询 GetForegroundWindow 比对 Process.MainWindowHandle 实现。
/// 零依赖（不调 OpenProcess / 不读内存）。
/// </summary>
public sealed class TargetWindowWatcher : IDisposable
{
    private const int PollingMs = 200;

    // 游戏窗口候选标题（按用户可配置顺序）
    private static readonly string[] _candidateTitles =
    {
        "Delta Force",        // EN
        "DeltaForce",         // EN 紧凑
        "三角洲行动",           // CN
        "三角洲",              // CN 简称
    };

    private IntPtr _targetHwnd = IntPtr.Zero;
    private string _targetTitle = "";
    private bool _isTargetFocused;
    private Thread? _thread;
    private CancellationTokenSource? _cts;

    public IntPtr TargetHwnd => _targetHwnd;
    public string TargetTitle => _targetTitle;
    public bool IsTargetFocused => _isTargetFocused;

    public event Action<bool>? FocusChanged;

    public void Start()
    {
        if (_thread != null) return;
        _cts = new CancellationTokenSource();
        _thread = new Thread(() => Poll(_cts.Token))
        {
            IsBackground = true,
            Name = "TargetWindowWatcher",
        };
        _thread.Start();
    }

    public void Stop()
    {
        _cts?.Cancel();
        _thread?.Join(500);
        _thread = null;
        _cts?.Dispose();
        _cts = null;
    }

    private void Poll(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // 1. 找目标窗口（首次 / 失去后重找）
                if (_targetHwnd == IntPtr.Zero || !IsWindow(_targetHwnd))
                {
                    _targetHwnd = FindTargetWindow();
                    _targetTitle = _targetHwnd != IntPtr.Zero
                        ? GetWindowTitle(_targetHwnd)
                        : "";
                }

                // 2. 检查是否在前台
                bool focused = false;
                if (_targetHwnd != IntPtr.Zero)
                {
                    var fg = GetForegroundWindow();
                    focused = fg == _targetHwnd || IsChild(fg, _targetHwnd);
                }

                if (focused != _isTargetFocused)
                {
                    _isTargetFocused = focused;
                    FocusChanged?.Invoke(focused);
                }
            }
            catch
            {
                // 静默错误，避免线程崩溃
            }

            Thread.Sleep(PollingMs);
        }
    }

    private static IntPtr FindTargetWindow()
    {
        foreach (var title in _candidateTitles)
        {
            var hwnd = FindWindow(null, title);
            if (hwnd != IntPtr.Zero && IsWindowVisible(hwnd)) return hwnd;
        }
        return IntPtr.Zero;
    }

    private static string GetWindowTitle(IntPtr hwnd)
    {
        var len = GetWindowTextLength(hwnd);
        if (len == 0) return "";
        var sb = new System.Text.StringBuilder(len + 1);
        GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public void Dispose() => Stop();

    // === P/Invoke ===
    [DllImport("user32.dll")]
    private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);
}
