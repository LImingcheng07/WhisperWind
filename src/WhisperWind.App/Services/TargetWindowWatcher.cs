using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace WhisperWind.App.Services;

/// <summary>
/// 监测目标游戏窗口：枚举顶层窗口，标题包含任一关键词（设置里可改）或进程名含 DeltaForce 即认定为游戏。
/// 轮询 GetForegroundWindow 判断游戏是否在前台。零依赖（不读内存 / 不注入）。
/// </summary>
public sealed class TargetWindowWatcher : IDisposable
{
    private const int PollingMs = 150;
    private const int SearchIntervalMs = 1000;

    private readonly int _selfPid = Environment.ProcessId;
    private string[] _keywords = Array.Empty<string>();
    private IntPtr _targetHwnd = IntPtr.Zero;
    private bool _targetIsGameProcess;
    private volatile bool _isTargetFocused;
    private Thread? _thread;
    private CancellationTokenSource? _cts;
    private long _lastSearch;

    public IntPtr TargetHwnd => _targetHwnd;
    public string TargetTitle { get; private set; } = "";
    public string TargetProcessName { get; private set; } = "";
    public int TargetProcessId { get; private set; }
    public bool IsFound => _targetHwnd != IntPtr.Zero;
    public bool IsTargetFocused => _isTargetFocused;

    /// <summary>在监测线程上触发</summary>
    public event Action<bool>? FocusChanged;
    /// <summary>找到/丢失游戏窗口时触发（监测线程）</summary>
    public event Action? TargetChanged;

    public TargetWindowWatcher(string keywords) => SetKeywords(keywords);

    public void SetKeywords(string keywords)
    {
        _keywords = keywords
            .Split(new[] { ';', '；', ',', '，' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
        _lastSearch = 0;
        SetTarget(IntPtr.Zero);
    }

    public void Start()
    {
        if (_thread != null) return;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _thread = new Thread(() => Poll(ct)) { IsBackground = true, Name = "TargetWindowWatcher" };
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
                var fg = GetForegroundWindow();

                if (_targetHwnd != IntPtr.Zero && !IsWindow(_targetHwnd))
                    SetTarget(IntPtr.Zero);

                // 前台窗口是游戏的话直接改锁它（游戏可能有启动器、登录窗等多个同名窗口）
                bool focused = false;
                if (fg != IntPtr.Zero)
                {
                    var root = GetAncestor(fg, GA_ROOTOWNER);
                    if (root == IntPtr.Zero) root = fg;
                    if (root == _targetHwnd || fg == _targetHwnd || IsSameProcess(fg, TargetProcessId))
                        focused = true;
                    else if (GameScore(root) is var score && (score == 2 || (score == 1 && !_targetIsGameProcess)))
                    {
                        SetTarget(root);
                        focused = true;
                    }
                }

                // 没锁定，或只锁到了「标题带关键词」的非游戏进程窗口时，每秒全量搜一次更好的
                if (!focused && (_targetHwnd == IntPtr.Zero || !_targetIsGameProcess)
                    && Environment.TickCount64 - _lastSearch > SearchIntervalMs)
                {
                    _lastSearch = Environment.TickCount64;
                    var best = FindTargetWindow();
                    if (best != IntPtr.Zero || _targetHwnd == IntPtr.Zero) SetTarget(best);
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

    private void SetTarget(IntPtr hwnd)
    {
        if (hwnd == _targetHwnd) return;
        _targetHwnd = hwnd;
        if (hwnd == IntPtr.Zero)
        {
            TargetTitle = "";
            TargetProcessName = "";
            TargetProcessId = 0;
            _targetIsGameProcess = false;
        }
        else
        {
            TargetTitle = GetWindowTitle(hwnd);
            GetWindowThreadProcessId(hwnd, out var pid);
            TargetProcessId = (int)pid;
            try { TargetProcessName = Process.GetProcessById((int)pid).ProcessName; }
            catch { TargetProcessName = ""; }
            _targetIsGameProcess = IsGameProcessName(TargetProcessName);
        }
        TargetChanged?.Invoke();
    }

    private static bool IsGameProcessName(string name) =>
        name.Contains("DeltaForce", StringComparison.OrdinalIgnoreCase);

    /// <summary>0 = 不是游戏；1 = 标题匹配关键词；2 = 游戏进程本身（DeltaForce*.exe）</summary>
    private int GameScore(IntPtr hwnd)
    {
        if (!IsWindowVisible(hwnd)) return 0;
        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == _selfPid) return 0;

        try
        {
            if (IsGameProcessName(Process.GetProcessById((int)pid).ProcessName)) return 2;
        }
        catch
        {
            // 受保护进程可能拿不到名字，继续看标题
        }
        var title = GetWindowTitle(hwnd);
        return title.Length > 0 && _keywords.Any(k => title.Contains(k, StringComparison.OrdinalIgnoreCase)) ? 1 : 0;
    }

    private static bool IsSameProcess(IntPtr hwnd, int pid)
    {
        if (pid == 0) return false;
        GetWindowThreadProcessId(hwnd, out var p);
        return p == pid;
    }

    private IntPtr FindTargetWindow()
    {
        IntPtr best = IntPtr.Zero;
        int bestScore = 0;
        EnumWindows((h, _) =>
        {
            // 只看有标题的可见窗口，避免对每个窗口都查进程
            if (!IsWindowVisible(h) || GetWindowTextLength(h) == 0) return true;
            int score = GameScore(h);
            if (score > bestScore) { best = h; bestScore = score; }
            return bestScore < 2;
        }, IntPtr.Zero);
        return best;
    }

    private static string GetWindowTitle(IntPtr hwnd)
    {
        var len = GetWindowTextLength(hwnd);
        if (len == 0) return "";
        var sb = new StringBuilder(len + 1);
        GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    /// <summary>游戏窗口是否覆盖了整块屏幕（全屏 / 无边框全屏）</summary>
    public bool IsTargetFullscreenLike()
    {
        if (_targetHwnd == IntPtr.Zero || !GetWindowRect(_targetHwnd, out var r)) return false;
        var mon = MonitorFromWindow(_targetHwnd, 2);
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(mon, ref mi)) return false;
        return r.Left <= mi.rcMonitor.Left && r.Top <= mi.rcMonitor.Top
               && r.Right >= mi.rcMonitor.Right && r.Bottom >= mi.rcMonitor.Bottom;
    }

    public void Dispose() => Stop();

    // === P/Invoke ===
    private const uint GA_ROOTOWNER = 3;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder s, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr hMon, ref MONITORINFO mi);
}
