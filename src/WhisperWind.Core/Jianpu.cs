using System;
using System.Collections.Generic;

namespace WhisperWind.Core;

/// <summary>
/// 简谱 → 音符。用于 NPC 任务码（如佐拉 7676354）、内置曲和手写小段旋律。
///   1..7  = C4..B4（do..si），默认一拍
///   0     = 休止（同样可带 _ . -）
///   '     = 前一个音升高八度（可连写 ''）
///   ,     = 紧跟在音后面时降低八度（5, = 低音 5）；其他位置的逗号当分隔符
///   #     = 前一个音升半音
///   _     = 前一个音时值减半（可连写 __ = 1/4 拍）
///   .     = 附点：前一个音时值 ×1.5
///   -     = 前一个音延长一拍
///   空格/逗号/竖线等其他字符忽略
/// </summary>
public static class Jianpu
{
    private static readonly int[] Degree = { 0, 60, 62, 64, 65, 67, 69, 71 };

    private sealed class Token
    {
        public int Pitch;        // 0 = 休止
        public double Beats = 1;
    }

    public static List<MidiNote> Parse(string text, int beatMs = 400)
    {
        if (beatMs <= 0) throw new ArgumentOutOfRangeException(nameof(beatMs));

        var tokens = new List<Token>();
        char prev = ' ';
        foreach (var c in text)
        {
            var last = tokens.Count > 0 ? tokens[^1] : null;
            bool afterNote = char.IsDigit(prev) || prev is '\'' or '#' or ',';
            prev = c;
            switch (c)
            {
                case >= '1' and <= '7':
                    tokens.Add(new Token { Pitch = Degree[c - '0'] });
                    break;
                case '0':
                    tokens.Add(new Token { Pitch = 0 });
                    break;
                case '\'' when last is { Pitch: > 0 }:
                    last.Pitch += 12;
                    break;
                case ',' when afterNote && last is { Pitch: > 0 }:
                    last.Pitch -= 12;
                    break;
                case '#' when last is { Pitch: > 0 }:
                    last.Pitch += 1;
                    break;
                case '_' when last != null:
                    last.Beats /= 2;
                    break;
                case '.' when last != null:
                    last.Beats *= 1.5;
                    break;
                case '-' when last != null:
                    last.Beats += 1;
                    break;
            }
        }

        var notes = new List<MidiNote>(tokens.Count);
        double beat = 0;
        foreach (var t in tokens)
        {
            if (t.Pitch > 0)
            {
                long start = (long)Math.Round(beat * beatMs);
                long len = (long)Math.Round(t.Beats * beatMs);
                // 留一点换气，让相同音连续时能分开
                long end = start + Math.Max(len - Math.Min(beatMs / 8, len / 4), 1);
                notes.Add(new MidiNote(t.Pitch, start, end, 0, 0));
            }
            beat += t.Beats;
        }
        return notes;
    }
}
