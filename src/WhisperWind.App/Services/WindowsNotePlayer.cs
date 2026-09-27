using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using WhisperWind.Core;

namespace WhisperWind.App.Services;

/// <summary>
/// Windows SendInput 实现：调 user32.dll 输入键鼠事件。
/// 不读内存/不注入/不驱动，纯公开 Win32 API。
///
/// 三角洲行动 8 音口琴：
///   - 8 个主键：F1..F8（可在 Settings 改）
///   - 升半音：鼠标侧键（XButton1）
///   - 高八度：鼠标中键
/// </summary>
public sealed class WindowsNotePlayer : INotePlayer
{
    public bool IsTargetFocused => true; // M3 阶段加 FindWindow 检测

    public void Press(IReadOnlyList<GameKey> keys)
    {
        var inputs = new List<INPUT>(keys.Count * 2);
        foreach (var k in keys) inputs.Add(BuildInput(k, true));
        SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
    }

    public void Release(IReadOnlyList<GameKey> keys)
    {
        var inputs = new List<INPUT>(keys.Count * 2);
        foreach (var k in keys) inputs.Add(BuildInput(k, false));
        SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
    }

    public void ReleaseAll()
    {
        // 兜底：松掉所有修饰键
        Release(new[] { GameKey.MouseSide, GameKey.MouseMiddle });
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public async Task RunAsync(PlaybackPlan plan, double speed, CancellationToken ct)
    {
        if (!plan.IsValid) return;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        foreach (var e in plan.Events)
        {
            long targetMs = (long)(e.StartMs / speed);
            while (sw.ElapsedMilliseconds < targetMs)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(1, ct);
            }
            Press(e.Keys);
            await Task.Delay((int)(e.DurationMs / speed), ct);
            Release(e.Keys);
        }
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
    private const uint MOUSEEVENTF_XDOWN = 0x0080;
    private const uint MOUSEEVENTF_XUP = 0x0100;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    private static INPUT BuildInput(GameKey key, bool down)
    {
        switch (key)
        {
            case GameKey.MouseSide:    // 侧键 XButton1
                return new INPUT
                {
                    type = INPUT_MOUSE,
                    U = new INPUTUNION
                    {
                        mi = new MOUSEINPUT
                        {
                            dwFlags = down ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP,
                            mouseData = 0x0001, // XBUTTON1
                        }
                    }
                };
            case GameKey.MouseMiddle:  // 中键
                return new INPUT
                {
                    type = INPUT_MOUSE,
                    U = new INPUTUNION
                    {
                        mi = new MOUSEINPUT
                        {
                            dwFlags = down ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP,
                        }
                    }
                };
            default:
                // MainKey1..8 → F1..F8
                int fKeyIndex = (int)key; // 1..8
                ushort vk = (ushort)(0x70 + (fKeyIndex - 1)); // VK_F1 = 0x70
                return new INPUT
                {
                    type = INPUT_KEYBOARD,
                    U = new INPUTUNION
                    {
                        ki = new KEYBDINPUT
                        {
                            wVk = vk,
                            dwFlags = down ? 0u : KEYEVENTF_KEYUP,
                        }
                    }
                };
        }
    }
}
