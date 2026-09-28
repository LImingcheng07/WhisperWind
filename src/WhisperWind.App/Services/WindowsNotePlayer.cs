using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Input;
using WhisperWind.Core;

namespace WhisperWind.App.Services;

/// <summary>一个物理输入：键盘键（虚拟键码）或鼠标按键</summary>
public sealed record InputBinding(string Id, ushort Vk, InputBinding.MouseButton Mouse)
{
    public enum MouseButton { None, X1, X2, Middle, Right, Left }

    public bool IsMouse => Mouse != MouseButton.None;

    public string DisplayName => Mouse switch
    {
        MouseButton.X1 => "鼠标侧键(后)",
        MouseButton.X2 => "鼠标侧键(前)",
        MouseButton.Middle => "鼠标中键",
        MouseButton.Right => "鼠标右键",
        MouseButton.Left => "鼠标左键",
        _ => KeyDisplay(Id),
    };

    public static InputBinding Parse(string id)
    {
        switch (id)
        {
            case "MouseX1": return new(id, 0, MouseButton.X1);
            case "MouseX2": return new(id, 0, MouseButton.X2);
            case "MouseMiddle": return new(id, 0, MouseButton.Middle);
            case "MouseRight": return new(id, 0, MouseButton.Right);
            case "MouseLeft": return new(id, 0, MouseButton.Left);
        }
        if (Enum.TryParse<Key>(id, out var key))
        {
            var vk = (ushort)KeyInterop.VirtualKeyFromKey(key);
            if (vk != 0) return new(id, vk, MouseButton.None);
        }
        throw new FormatException($"无法识别的按键: {id}");
    }

    public static string KeyDisplay(string id) => id switch
    {
        "OemComma" => ",",
        "OemPeriod" => ".",
        "OemQuestion" => "/",
        "OemSemicolon" => ";",
        "OemQuotes" => "'",
        "OemOpenBrackets" => "[",
        "Oem6" => "]",
        "OemMinus" => "-",
        "OemPlus" => "=",
        "LeftShift" => "左 Shift",
        "RightShift" => "右 Shift",
        "LeftCtrl" => "左 Ctrl",
        "LeftAlt" => "左 Alt",
        "Space" => "空格",
        _ when id.Length == 2 && id[0] == 'D' && char.IsDigit(id[1]) => id[1].ToString(),
        _ => id,
    };
}

/// <summary>GameKey → 物理输入 的完整键位表</summary>
public sealed class KeyBindingSet
{
    public IReadOnlyList<InputBinding> Main { get; }
    public InputBinding Sharp { get; }
    public InputBinding OctaveUp { get; }
    public InputBinding OctaveDown { get; }

    public KeyBindingSet(IReadOnlyList<InputBinding> main, InputBinding sharp, InputBinding octaveUp, InputBinding octaveDown)
    {
        if (main.Count != 8) throw new ArgumentException("需要 8 个主键");
        Main = main;
        Sharp = sharp;
        OctaveUp = octaveUp;
        OctaveDown = octaveDown;
    }

    public static KeyBindingSet FromSettings(AppSettings s) => new(
        s.MainKeys.Select(InputBinding.Parse).ToList(),
        InputBinding.Parse(s.SharpModifier),
        InputBinding.Parse(s.OctaveModifier),
        InputBinding.Parse(s.OctaveDownModifier));

    public InputBinding For(GameKey key) => key switch
    {
        GameKey.Sharp => Sharp,
        GameKey.OctaveUp => OctaveUp,
        GameKey.OctaveDown => OctaveDown,
        >= GameKey.MainKey1 and <= GameKey.MainKey8 => Main[key - GameKey.MainKey1],
        _ => throw new ArgumentOutOfRangeException(nameof(key)),
    };

    /// <summary>重复/冲突检查，返回问题描述（空 = 没问题）</summary>
    public IEnumerable<string> Validate()
    {
        var all = Main.Append(Sharp).Append(OctaveUp).Append(OctaveDown).ToList();
        foreach (var dup in all.GroupBy(b => b.Id).Where(g => g.Count() > 1))
            yield return $"{dup.First().DisplayName} 被分配了 {dup.Count()} 次";
    }
}

/// <summary>
/// Windows SendInput 实现：调 user32.dll 输入键鼠事件。
/// 不读内存/不注入/不驱动，纯公开 Win32 API。
///   - 键盘用扫描码发送（DirectInput / RawInput 的游戏只认扫描码）
///   - 修饰键（降调/半音/升调）先按下 ModifierLeadMs 再按主键，松开时反序
///   - SendInput 被 UIPI 拦截（游戏以管理员运行、本程序没有）时触发 InputBlocked
/// </summary>
public sealed class WindowsNotePlayer : INotePlayer
{
    private readonly object _lock = new();
    private readonly HashSet<GameKey> _held = new();
    private KeyBindingSet _bindings;

    public WindowsNotePlayer(KeyBindingSet bindings) => _bindings = bindings;

    public int ModifierLeadMs { get; set; } = 10;
    public bool IsTargetFocused => true;

    /// <summary>最近一次 SendInput 是否被系统拦截</summary>
    public bool LastSendBlocked { get; private set; }
    public event Action? InputBlocked;

    public void UpdateBindings(KeyBindingSet bindings)
    {
        ReleaseAll();
        lock (_lock) _bindings = bindings;
    }

    public void Press(IReadOnlyList<GameKey> keys)
    {
        var mods = keys.Where(k => k.IsModifier()).ToList();
        var mains = keys.Where(k => !k.IsModifier()).ToList();
        if (mods.Count > 0)
        {
            Send(mods, down: true);
            if (ModifierLeadMs > 0) Thread.Sleep(ModifierLeadMs);
        }
        Send(mains, down: true);
    }

    public void Release(IReadOnlyList<GameKey> keys)
    {
        Send(keys.Where(k => !k.IsModifier()).ToList(), down: false);
        Send(keys.Where(k => k.IsModifier()).ToList(), down: false);
    }

    public void ReleaseAll()
    {
        List<GameKey> held;
        lock (_lock) held = _held.ToList();
        // 兜底：修饰键无论是否记录在案都松一次
        Release(held.Union(new[] { GameKey.Sharp, GameKey.OctaveUp, GameKey.OctaveDown }).ToList());
    }

    public ValueTask DisposeAsync()
    {
        ReleaseAll();
        return ValueTask.CompletedTask;
    }

    private void Send(IReadOnlyList<GameKey> keys, bool down)
    {
        if (keys.Count == 0) return;
        INPUT[] inputs;
        lock (_lock)
        {
            inputs = keys.Select(k => BuildInput(_bindings.For(k), down)).ToArray();
            foreach (var k in keys)
                if (down) _held.Add(k); else _held.Remove(k);
        }
        SendRaw(inputs);
    }

    private void SendRaw(INPUT[] inputs)
    {
        uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        bool blocked = sent == 0;
        if (blocked && !LastSendBlocked) InputBlocked?.Invoke();
        LastSendBlocked = blocked;
    }

    /// <summary>直接按一下某个物理输入（自检"按键测试"用）</summary>
    public void Tap(InputBinding binding, int holdMs = 80)
    {
        SendRaw(new[] { BuildInput(binding, true) });
        Thread.Sleep(holdMs);
        SendRaw(new[] { BuildInput(binding, false) });
    }

    // === P/Invoke ===

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public INPUTUNION U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    private const uint INPUT_MOUSE = 0;
    private const uint INPUT_KEYBOARD = 1;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint MOUSEEVENTF_XDOWN = 0x0080;
    private const uint MOUSEEVENTF_XUP = 0x0100;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_SCANCODE = 0x0008;
    private const uint MAPVK_VK_TO_VSC = 0;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    private static readonly HashSet<ushort> ExtendedVks = new()
    {
        0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, // PgUp PgDn End Home 方向键
        0x2D, 0x2E, 0xA3, 0xA5, 0x6F, 0x90,             // Ins Del RCtrl RAlt Num/ NumLock
    };

    private static INPUT BuildInput(InputBinding b, bool down)
    {
        if (b.IsMouse)
        {
            var (flags, data) = b.Mouse switch
            {
                InputBinding.MouseButton.X1 => (down ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP, 0x0001u),
                InputBinding.MouseButton.X2 => (down ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP, 0x0002u),
                InputBinding.MouseButton.Middle => (down ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP, 0u),
                InputBinding.MouseButton.Left => (down ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP, 0u),
                _ => (down ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP, 0u),
            };
            return new INPUT
            {
                type = INPUT_MOUSE,
                U = new INPUTUNION { mi = new MOUSEINPUT { dwFlags = flags, mouseData = data } },
            };
        }

        uint kflags = KEYEVENTF_SCANCODE | (down ? 0 : KEYEVENTF_KEYUP);
        if (ExtendedVks.Contains(b.Vk)) kflags |= KEYEVENTF_EXTENDEDKEY;
        return new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new INPUTUNION
            {
                ki = new KEYBDINPUT
                {
                    wVk = b.Vk,
                    wScan = (ushort)MapVirtualKey(b.Vk, MAPVK_VK_TO_VSC),
                    dwFlags = kflags,
                }
            }
        };
    }
}
