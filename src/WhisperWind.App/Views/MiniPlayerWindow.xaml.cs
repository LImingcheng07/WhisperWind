using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WhisperWind.App.ViewModels;

namespace WhisperWind.App.Views;

/// <summary>
/// 游戏内悬浮迷你播放器：不抢焦点（WS_EX_NOACTIVATE），不进任务栏，定时重新置顶，
/// 这样在无边框窗口模式的游戏上也能一直看到。
/// </summary>
public partial class MiniPlayerWindow : Window
{
    private readonly DispatcherTimer _topmostTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _forceClose;
    private IntPtr _hwnd;

    public MiniPlayerWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) => RestorePosition();
        Closing += OnClosing;
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) _topmostTimer.Start(); else _topmostTimer.Stop();
        };
        _topmostTimer.Tick += (_, _) => AssertTopmost();
        Harp.HoleClicked += hole => Vm?.PreviewHole(hole);
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        long ex = GetWindowLongPtr(_hwnd, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(_hwnd, GWL_EXSTYLE, new IntPtr(ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));
    }

    private void RestorePosition()
    {
        var s = Vm?.Settings;
        var area = SystemParameters.WorkArea;
        double left = s?.OverlayLeft ?? area.Right - ActualWidth - 16;
        double top = s?.OverlayTop ?? area.Top + 80;
        // 防止保存的位置跑到屏幕外（换了显示器之类）
        left = Math.Clamp(left, SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 60);
        top = Math.Clamp(top, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 60);
        Left = left;
        Top = top;
    }

    public void SavePosition()
    {
        if (Vm?.Settings is not { } s || double.IsNaN(Left)) return;
        s.OverlayLeft = Left;
        s.OverlayTop = Top;
        s.Save();
    }

    public void ForceClose()
    {
        _forceClose = true;
        _topmostTimer.Stop();
        Close();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_forceClose) return;
        e.Cancel = true;
        if (Vm != null) Vm.OverlayVisible = false; else Hide();
    }

    private void AssertTopmost()
    {
        if (_hwnd != IntPtr.Zero)
            SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    private void OnDragStart(object sender, MouseButtonEventArgs e)
    {
        for (var cur = e.OriginalSource as DependencyObject; cur != null; cur = VisualTreeHelper.GetParent(cur))
            if (cur is ButtonBase) return;
        try { DragMove(); } catch (InvalidOperationException) { }
        SavePosition();
    }

    private void OnHide(object sender, RoutedEventArgs e)
    {
        if (Vm != null) Vm.OverlayVisible = false;
    }

    private void OnOpenMain(object sender, RoutedEventArgs e)
    {
        var main = Application.Current.MainWindow;
        if (main == null) return;
        if (main.WindowState == WindowState.Minimized) main.WindowState = WindowState.Normal;
        main.Activate();
    }

    private void OnSeekStart(object sender, MouseButtonEventArgs e)
    {
        if (Vm != null) Vm.IsSeeking = true;
    }

    private void OnSeekEnd(object sender, MouseButtonEventArgs e)
    {
        if (Vm == null) return;
        Vm.IsSeeking = false;
        Vm.SeekTo(Progress.Value);
    }

    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_NOACTIVATE = 0x08000000;
    private const long WS_EX_TOOLWINDOW = 0x00000080;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
