using System.Collections.Generic;
using System.Linq;

namespace WhisperWind.Core;

/// <summary>一条要按下的键（时刻 + 哪些键 + 持续多久）</summary>
public readonly record struct KeyEvent(
    long StartMs,
    long DurationMs,
    IReadOnlyList<GameKey> Keys)
{
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

    public long DurationMs => Events.Count == 0 ? 0 : Events[^1].StartMs + Events[^1].DurationMs;
    public bool IsValid => Errors.Count == 0 && Events.Count > 0;

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
}

/// <summary>
/// 把一条 MIDI 轨道翻译成按键计划：
///   - 超出 [MinPitch, MaxPitch] → 八度折回
///   - 仍不在映射（半音黑键）→ 上下找最近自然音
///   - 同一 ms 多音 → 冲突记 error（单音口琴不能同按）
///   - 同一键太密（&lt; minGapMs）→ 记 error
///   - &lt; 30ms 的极短音 → 拉到 30ms（游戏识别阈值）
/// </summary>
public sealed class HarmonicaConverter
{
    private readonly HarmonicaMapping _mapping;
    private readonly int _minGapMs;

    public HarmonicaConverter(HarmonicaMapping mapping, int minGapMs = 20)
    {
        _mapping = mapping;
        _minGapMs = minGapMs;
    }

    public PlaybackPlan Convert(string songPath, int trackIndex, IReadOnlyList<MidiNote> notes)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var keyEvents = new List<KeyEvent>();
        int dropped = 0;

        // 同一时刻多音 → 冲突（单音口琴不能同时按 2 个主键）
        // 先按 (startMs, pitch) 排序；同 startMs 的折叠到上一个未冲突音
        var sorted = notes.OrderBy(n => n.StartMs).ThenByDescending(n => n.Pitch).ToList();

        long lastKeyTime = -1000;
        var occupiedMainKeys = new HashSet<GameKey>();
        long currentMoment = -1;

        foreach (var n in sorted)
        {
            if (n.StartMs != currentMoment)
            {
                currentMoment = n.StartMs;
                occupiedMainKeys.Clear();
            }

            int originalPitch = n.Pitch;
            int currentPitch = originalPitch;
            int maxMapped = _mapping.MaxPitch;
            while (currentPitch > maxMapped) currentPitch -= 12;
            int minMapped = _mapping.MinPitch;
            while (currentPitch < minMapped) currentPitch += 12;
            int safety = 0;
            while (_mapping.Resolve(currentPitch) == null && currentPitch < 128 && safety++ < 12)
                currentPitch += 1;

            if (currentPitch >= 128 || _mapping.Resolve(currentPitch) == null)
            {
                dropped++;
                warnings.Add($"Note {originalPitch}@{n.StartMs}ms dropped (no playable fold)");
                continue;
            }

            var binding = _mapping.Resolve(currentPitch)!;
            if (currentPitch != originalPitch)
                warnings.Add($"Note pitch {originalPitch}@{n.StartMs}ms shifted to {currentPitch} (out-of-range fold)");

            // 冲突检测：同 ms 主键是否已被本时刻占用
            var mainKeys = binding.Where(k => k != GameKey.MouseSide && k != GameKey.MouseMiddle).ToList();
            if (mainKeys.Any(occupiedMainKeys.Contains))
            {
                errors.Add($"Conflict at {n.StartMs}ms: pitch {originalPitch} (mainKey already pressed)");
                continue;
            }

            if (n.StartMs - lastKeyTime < _minGapMs)
            {
                errors.Add($"Key too close: {lastKeyTime}ms → {n.StartMs}ms (gap={n.StartMs - lastKeyTime}ms < min={_minGapMs}ms)");
                continue;
            }

            long dur = n.EndMs - n.StartMs;
            if (dur < 30) dur = 30;
            keyEvents.Add(new KeyEvent(n.StartMs, dur, binding));
            lastKeyTime = n.StartMs;
            foreach (var k in mainKeys) occupiedMainKeys.Add(k);
        }

        return new PlaybackPlan(
            songPath, trackIndex,
            originalNoteCount: notes.Count,
            events: keyEvents,
            warnings: warnings,
            errors: errors,
            droppedNoteCount: dropped);
    }
}