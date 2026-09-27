using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using WhisperWind.Core;

namespace WhisperWind.App.Services;

/// <summary>
/// Windows SendInput 实现：调 Windows API 输入键鼠事件到「三角洲行动」窗口。
/// 不读内存/不注入/不驱动，纯公开 API。
/// </summary>
public sealed class WindowsNotePlayer : INotePlayer
{
    public bool IsTargetFocused
    {
        get
        {
            // TODO: 用 FindWindow("DeltaForce", ...) 查三角洲窗口
            return true;
        }
    }

    public void Press(IReadOnlyList<GameKey> keys)
    {
        foreach (var k in keys) PressKey(k, true);
    }

    public void Release(IReadOnlyList<GameKey> keys)
    {
        foreach (var k in keys) PressKey(k, false);
    }

    public void ReleaseAll()
    {
        // 兜底：松掉所有修饰键
        PressKey(GameKey.MouseSide, false);
        PressKey(GameKey.MouseMiddle, false);
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

    private static void PressKey(GameKey key, bool down)
    {
        // TODO M2.5: 用 Win32 SendInput（user32.dll）替换 WPF 的 InputManager
        // 当前 M2 阶段：只做占位（用 WPF SendKeys 替代）
        // 未来要支持后台按键（不抢焦点），必须用 SendInput + INPUT struct
        switch (key)
        {
            case GameKey.MouseSide:    // 侧键（XButton1）= 升半音
                SimulateMouseButton(0x0001, down ? (uint)0x0001 : 0x0002, 0); // XBUTTON1 down/up
                break;
            case GameKey.MouseMiddle:  // 中键
                SimulateMouseButton(0x0040, down ? 0x0001u : 0x0002u, 0); // WM_MOUSE click
                break;
            default:
                // 8 个主键：MainKey1..8 → F1..F8（或自定义键位，先用 F1..F8 占位）
                int fKey = (int)key; // MainKey1=1..MainKey8=8 → F1..F8
                var keyCode = Key.F1 + (fKey - 1);
                if (down) Keyboard.Focus(null);
                // 实际 SendInput 在 M2.5 阶段补
                break;
        }
    }

    private static void SimulateMouseButton(uint message, uint wParam, int delta)
    {
        // 占位：M2.5 阶段改用 SendInput INPUT_MOUSE
        // 现阶段不发真实事件
    }
}
