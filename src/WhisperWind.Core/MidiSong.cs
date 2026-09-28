using System.Collections.Generic;
using System.IO;
using System.Linq;
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

/// <summary>一条 MIDI 轨道摘要（按"轨道块 × 通道"拆分，format 0 文件也能分出各声部）</summary>
public sealed class MidiTrackInfo
{
    public int Index { get; init; }
    public string Name { get; init; } = "";
    public int Channel { get; init; }
    public int NoteCount { get; init; }
    public int MinPitch { get; init; }
    public int MaxPitch { get; init; }
    public long DurationMs { get; init; }
    /// <summary>GM 第 10 通道 = 打击乐，不能当旋律</summary>
    public bool IsDrum => Channel == 9;

    public string DisplayName =>
        $"{Name}{(IsDrum ? " · 鼓" : "")} · {NoteCount} 音";

    public override string ToString() => DisplayName;
}

/// <summary>一首 MIDI 曲子的解析结果</summary>
public sealed class MidiSong
{
    public string FilePath { get; }
    public long DurationMs { get; }
    /// <summary>开头的速度（BPM），没有 tempo 事件时为 120</summary>
    public double Bpm { get; }
    public IReadOnlyList<MidiTrackInfo> Tracks { get; }
    public IReadOnlyList<MidiNote> GetTrackNotes(int trackIndex) => _notesByTrack[trackIndex];

    private readonly Dictionary<int, List<MidiNote>> _notesByTrack;

    private MidiSong(string filePath, long durationMs, double bpm,
        IReadOnlyList<MidiTrackInfo> tracks,
        Dictionary<int, List<MidiNote>> notesByTrack)
    {
        FilePath = filePath;
        DurationMs = durationMs;
        Bpm = bpm;
        Tracks = tracks;
        _notesByTrack = notesByTrack;
    }

    /// <summary>所有 track 的全部音符（按 StartMs 排序）</summary>
    public IReadOnlyList<MidiNote> AllNotes =>
        _notesByTrack.SelectMany(kv => kv.Value).OrderBy(n => n.StartMs).ToList();

    /// <summary>从 .mid/.midi 文件解析</summary>
    public static MidiSong Load(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException(filePath);

        var midi = MidiFile.Read(filePath, new ReadingSettings
        {
            // 网上的 MIDI 常有小毛病，尽量宽容
            InvalidChunkSizePolicy = InvalidChunkSizePolicy.Ignore,
            NotEnoughBytesPolicy = NotEnoughBytesPolicy.Ignore,
            NoHeaderChunkPolicy = NoHeaderChunkPolicy.Ignore,
            InvalidChannelEventParameterValuePolicy = InvalidChannelEventParameterValuePolicy.SnapToLimits,
            InvalidMetaEventParameterValuePolicy = InvalidMetaEventParameterValuePolicy.SnapToLimits,
            MissedEndOfTrackPolicy = MissedEndOfTrackPolicy.Ignore,
            UnexpectedTrackChunksCountPolicy = UnexpectedTrackChunksCountPolicy.Ignore,
            ExtraTrackChunkPolicy = ExtraTrackChunkPolicy.Read,
            UnknownChunkIdPolicy = UnknownChunkIdPolicy.ReadAsUnknownChunk,
        });
        var tempoMap = midi.GetTempoMap();
        double bpm = tempoMap.GetTempoAtTime(new MidiTimeSpan(0)).BeatsPerMinute;

        var tracks = new List<MidiTrackInfo>();
        var notesByTrack = new Dictionary<int, List<MidiNote>>();
        long totalMs = 0;

        var chunks = midi.GetTrackChunks().ToList();
        int index = 0;
        for (int i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            var trackName = chunk.Events.OfType<SequenceTrackNameEvent>().FirstOrDefault()?.Text?.Trim();

            foreach (var byChannel in chunk.GetNotes().GroupBy(n => (int)n.Channel).OrderBy(g => g.Key))
            {
                var noteRecords = byChannel.Select(n => new MidiNote(
                    Pitch: n.NoteNumber,
                    StartMs: (long)n.TimeAs<MetricTimeSpan>(tempoMap).TotalMilliseconds,
                    EndMs: (long)n.EndTimeAs<MetricTimeSpan>(tempoMap).TotalMilliseconds,
                    Channel: byChannel.Key,
                    TrackIndex: index)).OrderBy(n => n.StartMs).ToList();

                var name = string.IsNullOrEmpty(trackName) ? $"轨 {i + 1}" : trackName;
                tracks.Add(new MidiTrackInfo
                {
                    Index = index,
                    Name = $"{name} / 通道 {byChannel.Key + 1}",
                    Channel = byChannel.Key,
                    NoteCount = noteRecords.Count,
                    MinPitch = noteRecords.Min(n => n.Pitch),
                    MaxPitch = noteRecords.Max(n => n.Pitch),
                    DurationMs = noteRecords.Max(n => n.EndMs),
                });
                totalMs = System.Math.Max(totalMs, noteRecords.Max(n => n.EndMs));
                notesByTrack[index] = noteRecords;
                index++;
            }
        }

        return new MidiSong(filePath, totalMs, bpm, tracks, notesByTrack);
    }
}
