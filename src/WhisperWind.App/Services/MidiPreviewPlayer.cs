using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using WhisperWind.Core;

namespace WhisperWind.App.Services;

/// <summary>
/// 试听：不发任何按键，而是用 Windows 自带的 GS 波表合成器（winmm midiOut）
/// 把"转换后游戏里会吹出的音"直接放出来，默认音色 = GM 口琴。
/// </summary>
public sealed class MidiPreviewPlayer : INotePlayer
{
    private readonly object _lock = new();
    private IntPtr _handle;
    private int _program = 22;
    private int _sounding = -1;

    public bool IsTargetFocused => true;

    public int Program
    {
        get => _program;
        set
        {
            _program = Math.Clamp(value, 0, 127);
            lock (_lock) if (_handle != IntPtr.Zero) Short(0xC0, _program, 0);
        }
    }

    /// <summary>系统里有没有可用的 MIDI 输出设备</summary>
    public static int DeviceCount => (int)midiOutGetNumDevs();

    public void NoteOn(KeyEvent e)
    {
        lock (_lock)
        {
            if (!EnsureOpen() || e.Pitch <= 0) return;
            if (_sounding >= 0) Short(0x80, _sounding, 0);
            Short(0x90, e.Pitch, 100);
            _sounding = e.Pitch;
        }
    }

    public void NoteOff(KeyEvent e)
    {
        lock (_lock)
        {
            if (_handle == IntPtr.Zero || e.Pitch <= 0) return;
            Short(0x80, e.Pitch, 0);
            if (_sounding == e.Pitch) _sounding = -1;
        }
    }

    /// <summary>直接放一个音（自检/点击口琴孔试音用）</summary>
    public async Task PlayNoteAsync(int pitch, int ms = 350)
    {
        var e = new KeyEvent(0, ms, Array.Empty<GameKey>(), pitch);
        NoteOn(e);
        await Task.Delay(ms);
        NoteOff(e);
    }

    public void Press(IReadOnlyList<GameKey> keys) { }
    public void Release(IReadOnlyList<GameKey> keys) { }

    public void ReleaseAll()
    {
        lock (_lock)
        {
            if (_handle == IntPtr.Zero) return;
            Short(0xB0, 123, 0); // All Notes Off
            _sounding = -1;
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_lock)
        {
            if (_handle != IntPtr.Zero)
            {
                midiOutReset(_handle);
                midiOutClose(_handle);
                _handle = IntPtr.Zero;
            }
        }
        return ValueTask.CompletedTask;
    }

    private bool EnsureOpen()
    {
        if (_handle != IntPtr.Zero) return true;
        if (midiOutOpen(out _handle, MIDI_MAPPER, IntPtr.Zero, IntPtr.Zero, 0) != 0)
        {
            _handle = IntPtr.Zero;
            return false;
        }
        Short(0xC0, _program, 0);
        Short(0xB0, 7, 110); // 音量
        return true;
    }

    private void Short(int status, int data1, int data2)
        => midiOutShortMsg(_handle, (uint)(status | (data1 << 8) | (data2 << 16)));

    private const uint MIDI_MAPPER = 0xFFFFFFFF;

    [DllImport("winmm.dll")] private static extern uint midiOutGetNumDevs();
    [DllImport("winmm.dll")] private static extern int midiOutOpen(out IntPtr handle, uint deviceId, IntPtr callback, IntPtr instance, uint flags);
    [DllImport("winmm.dll")] private static extern int midiOutShortMsg(IntPtr handle, uint message);
    [DllImport("winmm.dll")] private static extern int midiOutReset(IntPtr handle);
    [DllImport("winmm.dll")] private static extern int midiOutClose(IntPtr handle);
}
