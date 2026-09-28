using System;
using System.Collections.Generic;
using System.Linq;

namespace WhisperWind.Core;

/// <summary>一条要按下的键（时刻 + 哪些键 + 持续多久 + 实际吹出的音高）</summary>
public readonly record struct KeyEvent(
    long StartMs,
    long DurationMs,
    IReadOnlyList<GameKey> Keys,
    int Pitch = 0)
{
    public long EndMs => StartMs + DurationMs;

    public override string ToString() =>
        $"@{(StartMs)}ms +{(DurationMs)}ms [{string.Join("+", Keys)}]";
}

/// <summary>一次完整播放计划</summary>
public sealed class PlaybackPlan
{
    public string SongPath { get; }
    public int SourceTrackIndex { get; }
    public int OriginalNoteCount { get; }
    public int ConvertedNoteCount { get; }
    public int DroppedNoteCount { get; }
    public IReadOnlyList<KeyEvent> Events { get; }
    public IReadOnlyList<string> Warnings { get; }
    public IReadOnlyList<string> Errors { get; }

    /// <summary>整体移调（半音）</summary>
    public int Transpose { get; init; }
    /// <summary>超出音域、被折叠八度的音数</summary>
    public int FoldedNoteCount { get; init; }
    /// <summary>和弦中被舍弃的音数（单音乐器只保留最高音）</summary>
    public int ChordReducedCount { get; init; }
    /// <summary>因间隔过近被跳过的音数</summary>
    public int TooCloseCount { get; init; }

    public long DurationMs => Events.Count == 0 ? 0 : Events[^1].StartMs + Events[^1].DurationMs;
    public bool IsValid => Errors.Count == 0 && Events.Count > 0;

    /// <summary>
    /// 适配度 0..100：原旋律音有多少能按原样（不折叠、不丢弃）吹出来。
    /// 和弦里舍弃的伴奏音不扣分，只按旋律线计算。
    /// </summary>
    public int Score
    {
        get
        {
            int melody = OriginalNoteCount - ChordReducedCount;
            if (melody <= 0) return 0;
            double bad = FoldedNoteCount + DroppedNoteCount + TooCloseCount;
            return (int)Math.Round(Math.Clamp(100.0 * (1 - bad / melody), 0, 100));
        }
    }

    public PlaybackPlan(
        string songPath,
        int sourceTrackIndex,
        int originalNoteCount,
        IReadOnlyList<KeyEvent> events,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> errors,
        int droppedNoteCount = 0)
    {
        SongPath = songPath;
        SourceTrackIndex = sourceTrackIndex;
        OriginalNoteCount = originalNoteCount;
        ConvertedNoteCount = events.Count;
        DroppedNoteCount = droppedNoteCount;
        Events = events;
        Warnings = warnings;
        Errors = errors;
    }

    /// <summary>找到 positionMs 时刻应该从第几个事件开始（二分）</summary>
    public int IndexAt(long positionMs)
    {
        int lo = 0, hi = Events.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (Events[mid].StartMs < positionMs) lo = mid + 1; else hi = mid;
        }
        return lo;
    }
}

/// <summary>
/// 把一条 MIDI 轨道翻译成按键计划（单音乐器）：
///   - 整体移调 transpose 半音
///   - 同一时刻多音（和弦）→ 只保留最高音（旋律），其余记 warning
///   - 超出 [MinPitch, MaxPitch] → 八度折回
///   - 仍不在映射 → 向上找最近可吹的音，找不到则丢弃
///   - 与上一音起点间隔 &lt; minGapMs → 跳过（游戏来不及识别）
///   - 每个音在下一音开始前 releaseGapMs 松开，保证游戏能识别到两次按键
///   - &lt; 30ms 的极短音 → 拉到 30ms（只要不压到下一音）
/// 只有"一个可演奏的音都没有"才算 error。
/// </summary>
public sealed class HarmonicaConverter
{
    public const int MinPressMs = 30;

    private readonly HarmonicaMapping _mapping;
    private readonly int _minGapMs;
    private readonly int _releaseGapMs;

    public HarmonicaConverter(HarmonicaMapping mapping, int minGapMs = 20, int releaseGapMs = 15)
    {
        _mapping = mapping;
        _minGapMs = minGapMs;
        _releaseGapMs = releaseGapMs;
    }

    public PlaybackPlan Convert(string songPath, int trackIndex, IReadOnlyList<MidiNote> notes, int transpose = 0)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var picked = new List<(long start, long end, int pitch, IReadOnlyList<GameKey> keys)>();
        int dropped = 0, folded = 0, chordReduced = 0, tooClose = 0;

        // 按起点分组，同一时刻取最高音作为旋律
        foreach (var moment in notes.GroupBy(n => n.StartMs).OrderBy(g => g.Key))
        {
            var ordered = moment.OrderByDescending(n => n.Pitch).ToList();
            var n = ordered[0];
            if (ordered.Count > 1)
            {
                chordReduced += ordered.Count - 1;
                warnings.Add($"Chord at {n.StartMs}ms reduced to top note {n.Pitch + transpose} ({ordered.Count - 1} dropped)");
            }

            int originalPitch = n.Pitch + transpose;
            int currentPitch = originalPitch;
            while (currentPitch > _mapping.MaxPitch) currentPitch -= 12;
            while (currentPitch < _mapping.MinPitch) currentPitch += 12;
            int safety = 0;
            while (_mapping.Resolve(currentPitch) == null && currentPitch < 128 && safety++ < 12)
                currentPitch += 1;

            if (currentPitch >= 128 || _mapping.Resolve(currentPitch) == null)
            {
                dropped++;
                warnings.Add($"Note {originalPitch}@{n.StartMs}ms dropped (no playable fold)");
                continue;
            }

            if (currentPitch != originalPitch)
            {
                folded++;
                warnings.Add($"Note pitch {originalPitch}@{n.StartMs}ms shifted to {currentPitch} (out-of-range fold)");
            }

            if (picked.Count > 0 && n.StartMs - picked[^1].start < _minGapMs)
            {
                tooClose++;
                warnings.Add($"Key too close: {picked[^1].start}ms → {n.StartMs}ms (gap={n.StartMs - picked[^1].start}ms < min={_minGapMs}ms), skipped");
                continue;
            }

            picked.Add((n.StartMs, n.EndMs, currentPitch, _mapping.Resolve(currentPitch)!));
        }

        // 时长：至少 MinPressMs，但必须在下一音前 releaseGapMs 松开
        var keyEvents = new List<KeyEvent>(picked.Count);
        for (int i = 0; i < picked.Count; i++)
        {
            var (start, end, pitch, keys) = picked[i];
            long dur = Math.Max(end - start, MinPressMs);
            if (i + 1 < picked.Count)
            {
                long room = picked[i + 1].start - start - _releaseGapMs;
                dur = Math.Min(dur, Math.Max(room, 1));
            }
            keyEvents.Add(new KeyEvent(start, dur, keys, pitch));
        }

        if (keyEvents.Count == 0)
            errors.Add(notes.Count == 0 ? "No notes in track" : "No playable notes after conversion");

        return new PlaybackPlan(
            songPath, trackIndex,
            originalNoteCount: notes.Count,
            events: keyEvents,
            warnings: warnings,
            errors: errors,
            droppedNoteCount: dropped)
        {
            Transpose = transpose,
            FoldedNoteCount = folded,
            ChordReducedCount = chordReduced,
            TooCloseCount = tooClose,
        };
    }
}
