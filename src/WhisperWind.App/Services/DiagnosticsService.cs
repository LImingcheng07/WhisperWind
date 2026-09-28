using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows;

namespace WhisperWind.App.Services;

public enum CheckLevel { Ok, Info, Warn, Error }

/// <summary>自检的一项结果</summary>
public sealed class CheckItem
{
    public required string Title { get; init; }
    public required string Detail { get; init; }
    public CheckLevel Level { get; init; }
    public string? ActionText { get; init; }
    public Action? Action { get; init; }

    public bool HasAction => Action != null;
    public string Glyph => Level switch
    {
        CheckLevel.Ok => "✓",
        CheckLevel.Info => "i",
        CheckLevel.Warn => "!",
        _ => "✕",
    };
}

/// <summary>
/// 权限与状态自查：管理员/UIPI、游戏窗口、热键、输入法、键位、试听设备、在线曲库、数据目录。
/// 只读取公开的系统信息，不碰游戏进程内存。
/// </summary>
public sealed class DiagnosticsService
{
    private readonly TargetWindowWatcher _watcher;
    private readonly GlobalHotkeyService _hotkeys;
    private readonly WindowsNotePlayer _player;
    private readonly AppSettings _settings;
    private readonly string _dataDir;

    public DiagnosticsService(TargetWindowWatcher watcher, GlobalHotkeyService hotkeys,
        WindowsNotePlayer player, AppSettings settings, string dataDir)
    {
        _watcher = watcher;
        _hotkeys = hotkeys;
        _player = player;
        _settings = settings;
        _dataDir = dataDir;
    }

    public static bool IsElevated
    {
        get
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    /// <summary>以管理员身份重启本程序（用户在 UAC 里取消则什么都不做）</summary>
    public static void RestartAsAdmin()
    {
        var exe = Environment.ProcessPath;
        if (exe == null) return;
        try
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas" });
            Application.Current.Shutdown();
        }
        catch (Win32Exception)
        {
            // 用户取消了 UAC
        }
    }

    /// <summary>快速的本地检查（不联网），适合定时刷新</summary>
    public List<CheckItem> RunLocal()
    {
        var list = new List<CheckItem>
        {
            CheckElevation(),
            CheckGameWindow(),
            CheckHotkeys(),
            CheckKeyboardLayout(),
            CheckBindings(),
            CheckPreviewDevice(),
            CheckDataDir(),
        };
        return list;
    }

    public async Task<List<CheckItem>> RunAllAsync()
    {
        var list = RunLocal();
        list.Add(await CheckOnlineAsync());
        return list;
    }

    private CheckItem CheckElevation()
    {
        bool self = IsElevated;
        if (_player.LastSendBlocked)
            return new CheckItem
            {
                Title = "按键被系统拦截",
                Detail = "Windows 拒绝了模拟按键（游戏以管理员运行，本程序没有）。请以管理员身份重启风声。",
                Level = CheckLevel.Error,
                ActionText = "以管理员重启",
                Action = RestartAsAdmin,
            };
        if (self)
            return new CheckItem { Title = "管理员权限", Detail = "已以管理员身份运行，按键可以发送到游戏。", Level = CheckLevel.Ok };

        bool? gameElevated = _watcher.IsFound ? IsProcessElevated(_watcher.TargetProcessId) : null;
        return gameElevated switch
        {
            false => new CheckItem { Title = "管理员权限", Detail = "游戏未以管理员运行，普通权限即可发送按键。", Level = CheckLevel.Ok },
            true or null when _watcher.IsFound => new CheckItem
            {
                Title = "管理员权限",
                Detail = "游戏很可能以管理员运行（受反作弊保护，无法读取权限）。普通权限发出的按键会被系统丢弃，建议以管理员重启。",
                Level = CheckLevel.Warn,
                ActionText = "以管理员重启",
                Action = RestartAsAdmin,
            },
            _ => new CheckItem
            {
                Title = "管理员权限",
                Detail = "当前是普通权限。三角洲行动通常以管理员运行，若游戏里没反应请以管理员重启。",
                Level = CheckLevel.Info,
                ActionText = "以管理员重启",
                Action = RestartAsAdmin,
            },
        };
    }

    private CheckItem CheckGameWindow()
    {
        if (!_watcher.IsFound)
            return new CheckItem
            {
                Title = "游戏窗口",
                Detail = $"没有找到游戏窗口（匹配关键词：{_settings.GameWindowKeywords}）。先启动游戏；也可在设置里改关键词。",
                Level = CheckLevel.Warn,
            };
        var proc = string.IsNullOrEmpty(_watcher.TargetProcessName) ? "" : $"（{_watcher.TargetProcessName}）";
        var focus = _watcher.IsTargetFocused ? "当前在前台" : "当前在后台";
        var hint = _watcher.IsTargetFullscreenLike()
            ? "。若悬浮窗被游戏盖住，请把游戏显示模式改为「无边框窗口」"
            : "";
        return new CheckItem
        {
            Title = "游戏窗口",
            Detail = $"已锁定「{_watcher.TargetTitle}」{proc}，{focus}{hint}。",
            Level = CheckLevel.Ok,
        };
    }

    private CheckItem CheckHotkeys()
    {
        var failed = GlobalHotkeyService.All.Where(h => !_hotkeys.Status.TryGetValue(h.Name, out var ok) || !ok).ToList();
        if (failed.Count == 0)
            return new CheckItem
            {
                Title = "全局热键",
                Detail = string.Join("  ", GlobalHotkeyService.All.Select(h => $"{h.Name} {h.Purpose}")),
                Level = CheckLevel.Ok,
            };
        return new CheckItem
        {
            Title = "全局热键",
            Detail = $"{string.Join("、", failed.Select(h => h.Name))} 被其他程序占用，游戏内无法用它们控制，可用悬浮窗按钮代替。",
            Level = CheckLevel.Warn,
        };
    }

    private CheckItem CheckKeyboardLayout()
    {
        var hwnd = _watcher.IsFound ? _watcher.TargetHwnd : GetForegroundWindow();
        var tid = GetWindowThreadProcessId(hwnd, out _);
        var layout = GetKeyboardLayout(tid).ToInt64() & 0xFFFF;
        var name = layout switch
        {
            0x0804 => "简体中文",
            0x0404 => "繁体中文",
            0x0411 => "日文",
            0x0412 => "韩文",
            _ => null,
        };
        if (name == null)
            return new CheckItem { Title = "输入法", Detail = "当前是英文键盘布局，字母键不会被输入法截走。", Level = CheckLevel.Ok };
        return new CheckItem
        {
            Title = "输入法",
            Detail = $"{(_watcher.IsFound ? "游戏" : "当前")}窗口使用{name}输入法。如果按键没反应或弹出候选框，按 Shift 切到英文或换成英文键盘。",
            Level = CheckLevel.Info,
        };
    }

    private CheckItem CheckBindings()
    {
        try
        {
            var set = KeyBindingSet.FromSettings(_settings);
            var problems = set.Validate().ToList();
            if (problems.Count > 0)
                return new CheckItem { Title = "键位配置", Detail = "键位冲突：" + string.Join("；", problems), Level = CheckLevel.Error };
            var keys = string.Join(" ", set.Main.Select(b => b.DisplayName));
            return new CheckItem
            {
                Title = "键位配置",
                Detail = $"主键 {keys}；降调 {set.OctaveDown.DisplayName}，半音 {set.Sharp.DisplayName}，升调 {set.OctaveUp.DisplayName}。需与游戏内口琴键位一致。",
                Level = CheckLevel.Ok,
            };
        }
        catch (Exception ex)
        {
            return new CheckItem { Title = "键位配置", Detail = ex.Message, Level = CheckLevel.Error };
        }
    }

    private static CheckItem CheckPreviewDevice()
    {
        int n = MidiPreviewPlayer.DeviceCount;
        return n > 0
            ? new CheckItem { Title = "试听音源", Detail = $"找到 {n} 个 MIDI 输出设备，可以在本机试听。", Level = CheckLevel.Ok }
            : new CheckItem { Title = "试听音源", Detail = "系统里没有 MIDI 输出设备，试听不可用（不影响游戏内演奏）。", Level = CheckLevel.Warn };
    }

    private CheckItem CheckDataDir()
    {
        try
        {
            Directory.CreateDirectory(_dataDir);
            var probe = Path.Combine(_dataDir, ".probe");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return new CheckItem { Title = "数据目录", Detail = _dataDir, Level = CheckLevel.Ok };
        }
        catch (Exception ex)
        {
            return new CheckItem { Title = "数据目录", Detail = $"无法写入 {_dataDir}：{ex.Message}", Level = CheckLevel.Error };
        }
    }

    private static async Task<CheckItem> CheckOnlineAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.Add("User-Agent", "WhisperWind/1.0 (+https://github.com/LImingcheng07/WhisperWind)");
        // 首次 TLS 握手偶尔失败，重试一次
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                var sw = Stopwatch.StartNew();
                using var resp = await http.GetAsync("https://bitmidi.com/api/midi/all?page=0");
                resp.EnsureSuccessStatusCode();
                return new CheckItem { Title = "在线曲库", Detail = $"BitMidi 可以访问（{sw.ElapsedMilliseconds} ms）。", Level = CheckLevel.Ok };
            }
            catch (Exception) when (attempt == 0)
            {
                await Task.Delay(500);
            }
            catch (Exception ex)
            {
                return new CheckItem
                {
                    Title = "在线曲库",
                    Detail = $"连不上 BitMidi：{ex.Message}。本地曲目和导入不受影响。",
                    Level = CheckLevel.Warn,
                };
            }
        }
    }

    /// <summary>读取进程令牌判断是否提权；拿不到（受保护进程）返回 null</summary>
    private static bool? IsProcessElevated(int pid)
    {
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            if (!OpenProcessToken(h, TOKEN_QUERY, out var token)) return null;
            try
            {
                if (!GetTokenInformation(token, 20 /* TokenElevation */, out int elevated, 4, out _)) return null;
                return elevated != 0;
            }
            finally { CloseHandle(token); }
        }
        finally { CloseHandle(h); }
    }

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint TOKEN_QUERY = 0x0008;

    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(IntPtr token, int cls, out int info, int len, out int retLen);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint threadId);
}
