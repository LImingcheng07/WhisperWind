using System;
using System.Collections.Generic;
using System.IO;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;

namespace WhisperWind.App.BuiltIn;

public sealed record TrackMeta(string Name, string FileName);

/// <summary>
/// 生成 4 首预置曲的 MIDI 文件到 %AppData%/WhisperWind/tracks/。
/// 第一次启动时若目录为空则全部生成。
///
/// 全部用最简单的单声部旋律（口琴 8 音能完整覆盖）。
/// BPM=120, PPQ=480。
/// </summary>
public static class BuiltInTracks
{
    private static readonly List<(string name, string file, int[] melody)> _tracks = new()
    {
        ("鸟之诗 · Air", "air.mid", new[] {
            60, 64, 67, 72, 67, 64, 60, 64, 67, 72, 71, 67,
            64, 67, 71, 74, 72, 67, 64, 60, 64, 67, 72, 67, 64, 60
        }),
        ("送别 · 李叔同", "farewell.mid", new[] {
            60, 62, 64, 65, 67, 65, 64, 62, 60,
            64, 65, 67, 69, 67, 65, 64, 60
        }),
        ("父亲 · 筷子兄弟", "father.mid", new[] {
            64, 65, 67, 64, 65, 67, 69, 67, 64, 62, 60, 60, 64
        }),
        ("千本桜 · 黒うさP", "senbonzakura.mid", new[] {
            67, 72, 76, 79, 76, 72, 67, 64, 60, 64, 67, 72, 76, 79
        }),
    };

    public static List<TrackMeta> EnsureBuilt(string appDataDir)
    {
        var dir = Path.Combine(appDataDir, "WhisperWind", "tracks");
        Directory.CreateDirectory(dir);
        var result = new List<TrackMeta>();

        foreach (var (name, file, melody) in _tracks)
        {
            var path = Path.Combine(dir, file);
            if (!File.Exists(path))
            {
                WriteMidi(path, melody);
            }
            result.Add(new TrackMeta(name, file));
        }
        return result;
    }

    private static void WriteMidi(string path, int[] melody)
    {
        const int bpm = 120;
        // PPQ=480, 每 4 分音符 480 ticks
        const long ticksPerNote = 480;

        var midiFile = new MidiFile
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision(480),
        };

        // 1) tempo track (MThd + 第一轨有 tempo 事件)
        var tempoTrack = new TrackChunk();
        tempoTrack.Events.Add(new SetTempoEvent(bpm * 10000));
        tempoTrack.Events.Add(new TimeSignatureEvent(4, 4));
        midiFile.Chunks.Add(tempoTrack);

        // 2) 旋律 track
        var track = new TrackChunk();
        long delta = 0; // 第一个事件 DeltaTime=0
        for (int i = 0; i < melody.Length; i++)
        {
            var n = (SevenBitNumber)melody[i];
            if (i == 0)
            {
                // 第一个 note 的 NoteOn 走 delta=0；NoteOff 走 ticksPerNote
                track.Events.Add(new NoteOnEvent { DeltaTime = 0, NoteNumber = n, Velocity = (SevenBitNumber)100 });
                track.Events.Add(new NoteOffEvent { DeltaTime = ticksPerNote, NoteNumber = n, Velocity = (SevenBitNumber)0 });
            }
            else
            {
                track.Events.Add(new NoteOnEvent { DeltaTime = 0, NoteNumber = n, Velocity = (SevenBitNumber)100 });
                track.Events.Add(new NoteOffEvent { DeltaTime = ticksPerNote, NoteNumber = n, Velocity = (SevenBitNumber)0 });
            }
        }
        midiFile.Chunks.Add(track);

        midiFile.Write(path);
    }
}
