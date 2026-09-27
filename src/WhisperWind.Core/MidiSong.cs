using System.Collections.Generic;
using System.IO;
using System.Linq;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

namespace WhisperWind.Core;
/// <summary>一条 MIDI 音符（已转成绝对毫秒）</summary>
public readonly record struct MidiNote(
    int Pitch,
    long StartMs,
    long EndMs,
    int Channel,
    int TrackIndex);

/// <summary>一条 MIDI 轨道摘要</summary>
public sealed class MidiTrackInfo
{
    public int Index { get; init; }
    public string Name { get; init; } = "";
    public int NoteCount { get; init; }
    public int MinPitch { get; init; }
    public int MaxPitch { get; init; }
    public long DurationMs { get; init; }
}

/// <summary>一首 MIDI 曲子的解析结果</summary>
public sealed class MidiSong
{
    public string FilePath { get; }
    public long DurationMs { get; }
    public IReadOnlyList<MidiTrackInfo> Tracks { get; }
    public IReadOnlyList<MidiNote> GetTrackNotes(int trackIndex) => _notesByTrack[trackIndex];

    private readonly Dictionary<int, List<MidiNote>> _notesByTrack;

    private MidiSong(string filePath, long durationMs,
        IReadOnlyList<MidiTrackInfo> tracks,
        Dictionary<int, List<MidiNote>> notesByTrack)
    {
        FilePath = filePath;
        DurationMs = durationMs;
        Tracks = tracks;
        _notesByTrack = notesByTrack;
    }

    /// <summary>从 .mid/.midi 文件解析</summary>
    public static MidiSong Load(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException(filePath);

        var midi = MidiFile.Read(filePath);
        var tempoMap = midi.GetTempoMap();

        var tracks = new List<MidiTrackInfo>();
        var notesByTrack = new Dictionary<int, List<MidiNote>>();
        long totalMs = midi.GetTimedEvents().LastOrDefault()?.Time ?? 0;

        for (int i = 0; i < midi.GetTrackChunks().Count(); i++)
        {
            var chunk = midi.GetTrackChunks().ElementAt(i);
            var notes = chunk.GetNotes().ToList();
            if (notes.Count == 0) continue;

            var noteRecords = notes.Select(n => new MidiNote(
                Pitch: n.NoteNumber,
                StartMs: (long)n.TimeAs<MetricTimeSpan>(tempoMap).TotalMilliseconds,
                EndMs: (long)n.EndTimeAs<MetricTimeSpan>(tempoMap).TotalMilliseconds,
                Channel: n.Channel,
                TrackIndex: i)).ToList();

            tracks.Add(new MidiTrackInfo
            {
                Index = i,
                Name = $"Track {i}",
                NoteCount = notes.Count,
                MinPitch = notes.Min(n => n.NoteNumber),
                MaxPitch = notes.Max(n => n.NoteNumber),
                DurationMs = (long)notes.Max(n => n.EndTimeAs<MetricTimeSpan>(tempoMap).TotalMilliseconds),
            });

            notesByTrack[i] = noteRecords;
        }

        return new MidiSong(filePath, totalMs, tracks, notesByTrack);
    }
}
