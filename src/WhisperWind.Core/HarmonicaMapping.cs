using System.Collections.Generic;
using System.Linq;

namespace WhisperWind.Core;

/// <summary>一个游戏按键标识</summary>
public enum GameKey
{
    None = 0,
    MainKey1, MainKey2, MainKey3, MainKey4,
    MainKey5, MainKey6, MainKey7, MainKey8,
    Sharp,       // 半音（升半音，游戏默认鼠标中键）
    OctaveUp,    // 升调（高八度，游戏默认鼠标右键）
    OctaveDown,  // 降调（低八度，游戏默认鼠标左键）
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
    public static bool IsModifier(this GameKey key)
        => key is GameKey.Sharp or GameKey.OctaveUp or GameKey.OctaveDown;

    public static bool HasModifier(this IReadOnlyList<GameKey> keys) => keys.Any(IsModifier);
}

/// <summary>
/// 三角洲口琴 8 音 + 鼠标修饰的按键映射。
///
/// 游戏机制（口琴界面底部提示）：
/// - 8 个主音键（默认 Z X C V B N M ,）：C4..C5 八个自然音
/// - 降调 = 鼠标左键（低八度）
/// - 半音 = 鼠标中键（升半音）
/// - 升调 = 鼠标右键（高八度）
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

    /// <summary>
    /// 默认映射：三个八度 C3..C6。
    /// 原调：主键 1..8 = C4 D4 E4 F4 G4 A4 B4 C5；升调 / 降调 = 同一主键 ± 12；
    /// 半音 = 该音的自然音主键 + 半音修饰（+1）。每个音取修饰键最少的按法。
    /// </summary>
    public static IReadOnlyDictionary<int, IReadOnlyList<GameKey>> DefaultMapping()
    {
        var dict = new Dictionary<int, IReadOnlyList<GameKey>>();
        int[] naturals = { 60, 62, 64, 65, 67, 69, 71, 72 };
        // E、B（以及高 do）上没有半音：E#=F、B#=C，直接用下一个自然音
        bool[] canSharp = { true, true, false, true, true, true, false, false };

        (int shift, GameKey? mod)[] octaves = { (0, null), (12, GameKey.OctaveUp), (-12, GameKey.OctaveDown) };
        foreach (var (shift, mod) in octaves)
        {
            for (int i = 0; i < naturals.Length; i++)
            {
                var key = GameKeyList.MainKeys[i];
                Add(naturals[i] + shift, mod is { } m ? new[] { key, m } : new[] { key });
                if (canSharp[i])
                    Add(naturals[i] + shift + 1, mod is { } m2 ? new[] { key, GameKey.Sharp, m2 } : new[] { key, GameKey.Sharp });
            }
        }
        return dict;

        void Add(int pitch, GameKey[] keys)
        {
            if (!dict.TryGetValue(pitch, out var old) || old.Count > keys.Length)
                dict[pitch] = keys;
        }
    }
}
