using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace WhisperWind.App.Services;

/// <summary>
/// 全局热键（游戏在前台时也能触发）：
///   F8 = 吹奏 / 暂停    F9 = 停止    F7 = 显示/隐藏悬浮窗
/// 走 user32 RegisterHotKey；注册失败不抛异常，结果交给自检页展示。
/// </summary>
public sealed class GlobalHotkeyService : IDisposable
{
    public sealed record Hotkey(int Id, uint Vk, string Name, string Purpose);

    public static readonly IReadOnlyList<Hotkey> All = new[]
    {
        new Hotkey(0xB001, 0x77, "F8", "吹奏 / 暂停"),
        new Hotkey(0xB002, 0x78, "F9", "停止"),
        new Hotkey(0xB003, 0x76, "F7", "悬浮窗"),
    };

    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_NOREPEAT = 0x4000;

    private HwndSource? _source;
    private IntPtr _hwnd;
    private readonly Dictionary<string, bool> _status = new();

    /// <summary>每个热键是否注册成功（被别的程序占用时为 false）</summary>
    public IReadOnlyDictionary<string, bool> Status => _status;

    public event Action? PlayPausePressed;
    public event Action? StopPressed;
    public event Action? OverlayPressed;

    public void Start(IntPtr hwnd)
    {
        if (_hwnd != IntPtr.Zero) return;
        _hwnd = hwnd;
        _source = HwndSource.FromHwnd(hwnd);
        _source?.AddHook(WndProc);
        foreach (var hk in All)
            _status[hk.Name] = RegisterHotKey(hwnd, hk.Id, MOD_NOREPEAT, hk.Vk);
    }

    public void Stop()
    {
        if (_hwnd == IntPtr.Zero) return;
        foreach (var hk in All) UnregisterHotKey(_hwnd, hk.Id);
        _source?.RemoveHook(WndProc);
        _hwnd = IntPtr.Zero;
        _status.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_HOTKEY) return IntPtr.Zero;
        switch (wParam.ToInt32())
        {
            case 0xB001: PlayPausePressed?.Invoke(); break;
            case 0xB002: StopPressed?.Invoke(); break;
            case 0xB003: OverlayPressed?.Invoke(); break;
            default: return IntPtr.Zero;
        }
        handled = true;
        return IntPtr.Zero;
    }

    public void Dispose() => Stop();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
