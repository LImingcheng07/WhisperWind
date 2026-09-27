using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace WhisperWind.App.Services;

/// <summary>
/// 全局热键服务（F8/F9/F10 不被游戏独占时仍能触发）。
/// 走 user32 RegisterHotKey；hotkey id 0xB001 ~ 0xB00F。
/// 必须从 WPF UI 线程 Start；Stop 时反注册。
/// </summary>
public sealed class GlobalHotkeyService : IDisposable
{
    public enum Action
    {
        PlayToggle,    // F8
        PauseResume,   // F9
        EmergencyStop, // F10
    }

    private const int HotkeyIdPlay = 0xB001;
    private const int HotkeyIdPause = 0xB002;
    private const int HotkeyIdStop = 0xB003;

    private const uint MOD_NONE = 0;
    private const uint VK_F8 = 0x77;
    private const uint VK_F9 = 0x78;
    private const uint VK_F10 = 0x79;

    private const int WM_HOTKEY = 0x0312;

    private HwndSource? _hwndSource;
    private bool _registered;

    public event Action? PlayTogglePressed;
    public event Action? PauseResumePressed;
    public event Action? EmergencyStopPressed;

    public void Start(Window window)
    {
        if (_registered) return;
        var helper = new WindowInteropHelper(window);
        if (helper.Handle == IntPtr.Zero)
        {
            // 窗口未显示，先 ensure handle
            window.SourceInitialized += (_, _) => Start(window);
            return;
        }
        _hwndSource = HwndSource.FromHwnd(helper.Handle);
        _hwndSource?.AddHook(WndProc);

        // 注册三个全局热键
        if (!RegisterHotKey(helper.Handle, HotkeyIdPlay, MOD_NONE, VK_F8))
            throw new InvalidOperationException("F8 全局热键注册失败，可能被占用。");
        if (!RegisterHotKey(helper.Handle, HotkeyIdPause, MOD_NONE, VK_F9))
            throw new InvalidOperationException("F9 全局热键注册失败。");
        if (!RegisterHotKey(helper.Handle, HotkeyIdStop, MOD_NONE, VK_F10))
            throw new InvalidOperationException("F10 全局热键注册失败。");

        _registered = true;
    }

    public void Stop()
    {
        if (!_registered) return;
        var helper = new WindowInteropHelper(Application.Current.MainWindow ?? new Window());
        if (helper.Handle != IntPtr.Zero)
        {
            UnregisterHotKey(helper.Handle, HotkeyIdPlay);
            UnregisterHotKey(helper.Handle, HotkeyIdPause);
            UnregisterHotKey(helper.Handle, HotkeyIdStop);
        }
        _hwndSource?.RemoveHook(WndProc);
        _registered = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_HOTKEY) return IntPtr.Zero;
        switch (wParam.ToInt32())
        {
            case HotkeyIdPlay:  PlayTogglePressed?.Invoke(); break;
            case HotkeyIdPause: PauseResumePressed?.Invoke(); break;
            case HotkeyIdStop:  EmergencyStopPressed?.Invoke(); break;
        }
        handled = true;
        return IntPtr.Zero;
    }

    public void Dispose() => Stop();

    // === P/Invoke ===
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
