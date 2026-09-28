using System;
using System.Collections.Generic;
using System.Linq;

namespace WhisperWind.Core;

/// <summary>
/// 旋律分析：自动挑旋律轨、自动算最佳移调。
/// </summary>
public static class MelodyAnalyzer
{
    /// <summary>
    /// 给每条轨打"像旋律"的分：排除打击乐通道（10 通道），
    /// 偏好音高偏高、音符数量适中、和弦少（单音线条）的轨。
    /// </summary>
    public static double MelodyScore(MidiTrackInfo track, IReadOnlyList<MidiNote> notes)
    {
        if (notes.Count == 0 || track.IsDrum) return double.MinValue;

        double avgPitch = notes.Average(n => n.Pitch);
        int moments = notes.Select(n => n.StartMs).Distinct().Count();
        double monophony = (double)moments / notes.Count;          // 1.0 = 纯单音
        double density = Math.Log(1 + moments);                    // 太少的轨（几个音）不像旋律

        // 音高：以 C5(72) 附近最佳，过低（贝斯）重扣
        double pitchScore = -Math.Abs(avgPitch - 72) / 6.0;
        if (avgPitch < 52) pitchScore -= 3;

        return density * 1.0 + monophony * 3.0 + pitchScore;
    }

    /// <summary>挑出最像旋律的轨道索引；没有可用轨返回 -1</summary>
    public static int PickMelodyTrack(MidiSong song)
    {
        int best = -1;
        double bestScore = double.MinValue;
        foreach (var t in song.Tracks)
        {
            var s = MelodyScore(t, song.GetTrackNotes(t.Index));
            if (s > bestScore) { bestScore = s; best = t.Index; }
        }
        return best;
    }

    /// <summary>
    /// 在 [-24, +24] 半音里找让"原样可吹（不折叠）"的旋律音最多的移调；
    /// 并列时取绝对值最小的（尽量少改原调）。
    /// </summary>
    public static int BestTranspose(IReadOnlyList<MidiNote> notes, HarmonicaMapping mapping)
    {
        // 与转换器一致：同一时刻只看最高音
        var melody = notes.GroupBy(n => n.StartMs).Select(g => g.Max(n => n.Pitch)).ToList();
        if (melody.Count == 0) return 0;

        int bestShift = 0, bestHits = -1;
        for (int shift = -24; shift <= 24; shift++)
        {
            int hits = 0;
            foreach (var p in melody)
                if (mapping.Resolve(p + shift) != null) hits++;
            if (hits > bestHits || (hits == bestHits && Math.Abs(shift) < Math.Abs(bestShift)))
            {
                bestHits = hits;
                bestShift = shift;
            }
        }
        return bestShift;
    }
}
