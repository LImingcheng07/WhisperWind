using System.Collections.Generic;
using System.Linq;

namespace WhisperWind.Core;

/// <summary>一个游戏按键标识</summary>
public enum GameKey
{
    None = 0,
    MainKey1, MainKey2, MainKey3, MainKey4,
    MainKey5, MainKey6, MainKey7, MainKey8,
    MouseSide,    // 升半音
    MouseMiddle,  // 高八度
}

public static class GameKeyList
{
    public static IReadOnlyList<GameKey> MainKeys { get; } = new[]
    {
        GameKey.MainKey1, GameKey.MainKey2, GameKey.MainKey3, GameKey.MainKey4,
        GameKey.MainKey5, GameKey.MainKey6, GameKey.MainKey7, GameKey.MainKey8,
    };
}

public static class NoteBindingExtensions
{
    public static bool RequiresMouse(this IReadOnlyList<GameKey> keys)
        => keys.Any(k => k is GameKey.MouseSide or GameKey.MouseMiddle);
}

/// <summary>
/// 三角洲口琴 8 音 + 鼠标修饰的按键映射。
///
/// 游戏机制（2026-09 实测）：
/// - 8 个主音键：固定 8 个自然音（C=Do）
/// - 鼠标侧键（默认）= 升半音
/// - 鼠标中键（默认）= 高八度
///
/// 键位和鼠标修饰都可以在 Settings 里改。
/// </summary>
public sealed class HarmonicaMapping
{
    /// <summary>MIDI 音号 → 同时按下的按键集合</summary>
    public IReadOnlyDictionary<int, IReadOnlyList<GameKey>> NoteToBinding { get; }

    public int MinPitch { get; }
    public int MaxPitch { get; }

    public HarmonicaMapping(IReadOnlyDictionary<int, IReadOnlyList<GameKey>>? noteToBinding = null)
    {
        NoteToBinding = noteToBinding ?? DefaultMapping();
        if (NoteToBinding.Count == 0)
        {
            MinPitch = 0; MaxPitch = 127;
        }
        else
        {
            MinPitch = NoteToBinding.Keys.Min();
            MaxPitch = NoteToBinding.Keys.Max();
        }
    }

    public IReadOnlyList<GameKey>? Resolve(int midiNote)
        => NoteToBinding.TryGetValue(midiNote, out var b) ? b : null;

    /// <summary>返回一个不可变的默认映射表（C 大调 8 音 + 5 升半 + 高八度）</summary>
    public static IReadOnlyDictionary<int, IReadOnlyList<GameKey>> DefaultMapping()
    {
        var dict = new Dictionary<int, IReadOnlyList<GameKey>>();

        // 7 个主音（自然音）：C4 D4 E4 F4 G4 A4 B4
        int[] naturals = { 60, 62, 64, 65, 67, 69, 71 };
        for (int i = 0; i < naturals.Length; i++)
            dict[naturals[i]] = new[] { GameKeyList.MainKeys[i] };

        // 5 个升半音：#C #D #F #G #A → 主键 + 鼠标侧键
        int[] sharps = { 61, 63, 66, 68, 70 };
        for (int i = 0; i < sharps.Length; i++)
            dict[sharps[i]] = new[] { GameKeyList.MainKeys[i], GameKey.MouseSide };

        // 高八度（C5..B5）完整音阶
        int[] octaveUpNat = { 72, 74, 76, 77, 79, 81, 83, 84 };
        for (int i = 0; i < octaveUpNat.Length; i++)
            dict[octaveUpNat[i]] = new[] { GameKeyList.MainKeys[i], GameKey.MouseMiddle };
        int[] octaveUpSharp = { 73, 75, 78, 80, 82 };
        for (int i = 0; i < octaveUpSharp.Length; i++)
            dict[octaveUpSharp[i]] = new[] { GameKeyList.MainKeys[i], GameKey.MouseSide, GameKey.MouseMiddle };

        return dict;
    }
}