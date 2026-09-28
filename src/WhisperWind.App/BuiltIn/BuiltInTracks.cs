using System;
using System.Collections.Generic;
using System.IO;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using WhisperWind.Core;

namespace WhisperWind.App.BuiltIn;

/// <summary>内置曲目信息</summary>
public sealed record BuiltInSong(string Title, string Subtitle, string FileName, int BeatMs, string Jianpu);

/// <summary>
/// 内置曲：全部是公有领域旋律，用简谱写成，启动时生成 MIDI 到 tracks/builtin/。
/// 每次启动都重新生成，保证更新后旧文件不残留错误旋律。
/// </summary>
public static class BuiltInTracks
{
    public static readonly IReadOnlyList<BuiltInSong> Songs = new[]
    {
        new BuiltInSong("送别", "Farewell · 李叔同", "songbie.mid", 790,
            "5 3_5_ 1'- | 6 1'_ 5- | 5 1_2_ 3 2_1_ | 2- - - | 5 3_5_ 1'. 7_ | 6 1' 5- | 5 2_3_ 4. 7,_ | 1- - - | " +
            "6 1' 1'- | 7 6_7_ 1'- | 6_7_ 1'_6_ 6_5_ 3_1_ | 2- - - | 5 3_5_ 1'. 7_ | 6 1' 5- | 5 2_3_ 4. 7,_ | 1- - -"),
        new BuiltInSong("茉莉花", "Jasmine · 江南民歌", "molihua.mid", 830,
            "3 3_5_ 6_1'_1'_6_ | 5 5_6_ 5- | 3 3_5_ 6_1'_1'_6_ | 5 5_6_ 5- | 5 5 5 3_5_ | 6 6 5- | 3 2_3_ 5 3_2_ | 1 1_2_ 1-"),
        new BuiltInSong("欢乐颂", "Ode to Joy · 贝多芬", "huanlesong.mid", 480,
            "7 7 1' 2' | 2' 1' 7 6 | 5 5 6 7 | 7. 6_ 6- | 7 7 1' 2' | 2' 1' 7 6 | 5 5 6 7 | 6. 5_ 5- | " +
            "6 6 7 5 | 6 7_1'_ 7 5 | 6 7_1'_ 7 6 | 5 6 2- | 7 7 1' 2' | 2' 1' 7 6 | 5 5 6 7 | 6. 5_ 5-"),
        new BuiltInSong("小星星", "Twinkle Twinkle · 法国童谣", "xiaoxingxing.mid", 520,
            "1 1 5 5 | 6 6 5- | 4 4 3 3 | 2 2 1- | 5 5 4 4 | 3 3 2- | 5 5 4 4 | 3 3 2- | " +
            "1 1 5 5 | 6 6 5- | 4 4 3 3 | 2 2 1-"),
        new BuiltInSong("两只老虎", "Frère Jacques · 法国民歌", "liangzhilaohu.mid", 450,
            "1 2 3 1 | 1 2 3 1 | 3 4 5- | 3 4 5- | 5_6_5_4_ 3 1 | 5_6_5_4_ 3 1 | 1 5, 1- | 1 5, 1-"),
        new BuiltInSong("生日快乐", "Happy Birthday", "shengrikuaile.mid", 520,
            "5_. 5__ 6 5 | 1' 7- | 5_. 5__ 6 5 | 2' 1'- | 5_. 5__ 5' 3' | 1' 7 6 | 4'_. 4'__ 3' 1' | 2' 1'-"),
        new BuiltInSong("铃儿响叮当", "Jingle Bells", "linger.mid", 380,
            "3 3 3- | 3 3 3- | 3 5 1. 2_ | 3- - - | 4 4 4. 4_ | 4 3 3 3_3_ | 3 2 2 1 | 2- 5- | " +
            "3 3 3- | 3 3 3- | 3 5 1. 2_ | 3- - - | 4 4 4 4 | 4 3 3 3_3_ | 5 5 4 2 | 1- - -"),
    };

    /// <summary>旧版本生成的错误旋律文件，启动时清理</summary>
    private static readonly string[] LegacyFiles = { "air.mid", "farewell.mid", "father.mid", "senbonzakura.mid" };

    public static string Dir(string dataDir) => Path.Combine(dataDir, "tracks", "builtin");

    public static void EnsureBuilt(string dataDir)
    {
        var tracksDir = Path.Combine(dataDir, "tracks");
        foreach (var f in LegacyFiles)
        {
            try { File.Delete(Path.Combine(tracksDir, f)); } catch { }
        }

        var dir = Dir(dataDir);
        Directory.CreateDirectory(dir);
        foreach (var s in Songs)
        {
            try { WriteMidi(Path.Combine(dir, s.FileName), Jianpu.Parse(s.Jianpu, s.BeatMs), s.BeatMs); }
            catch { /* 单首写失败不影响其他 */ }
        }
    }

    /// <summary>把毫秒音符写成 MIDI：一拍 = 一个四分音符，tempo 由 beatMs 决定</summary>
    public static void WriteMidi(string path, IReadOnlyList<MidiNote> notes, int beatMs)
    {
        const int ppq = 480;
        double ticksPerMs = ppq / (double)beatMs;

        var file = new MidiFile { TimeDivision = new TicksPerQuarterNoteTimeDivision(ppq) };
        var track = new TrackChunk();
        track.Events.Add(new SetTempoEvent(beatMs * 1000L));
        track.Events.Add(new ProgramChangeEvent((SevenBitNumber)22) { Channel = (FourBitNumber)0 });

        // 按时间顺序展开 on/off 事件再转成 delta
        var raw = new List<(long tick, bool on, int pitch)>();
        foreach (var n in notes)
        {
            raw.Add(((long)Math.Round(n.StartMs * ticksPerMs), true, n.Pitch));
            raw.Add(((long)Math.Round(n.EndMs * ticksPerMs), false, n.Pitch));
        }
        raw.Sort((a, b) => a.tick != b.tick ? a.tick.CompareTo(b.tick) : a.on.CompareTo(b.on)); // 同刻先 off 后 on

        long last = 0;
        foreach (var (tick, on, pitch) in raw)
        {
            MidiEvent e = on
                ? new NoteOnEvent((SevenBitNumber)pitch, (SevenBitNumber)96)
                : new NoteOffEvent((SevenBitNumber)pitch, (SevenBitNumber)0);
            e.DeltaTime = tick - last;
            last = tick;
            track.Events.Add(e);
        }
        file.Chunks.Add(track);

        var tmp = path + ".tmp";
        file.Write(tmp, overwriteFile: true);
        File.Move(tmp, path, overwrite: true);
    }
}
