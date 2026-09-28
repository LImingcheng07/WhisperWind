using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WhisperWind.App.BuiltIn;
using WhisperWind.Core;

namespace WhisperWind.App.Services;

public enum TrackSource { BuiltIn, Imported, Online }

/// <summary>曲库里的一首歌（本地文件）</summary>
public sealed class TrackItem
{
    public required string Title { get; init; }
    public string Subtitle { get; init; } = "";
    public required string FullPath { get; init; }
    public TrackSource Source { get; init; }
    public long DurationMs { get; init; }
    public int NoteCount { get; init; }

    public string SourceText => Source switch
    {
        TrackSource.BuiltIn => "内置",
        TrackSource.Imported => "导入",
        _ => "在线",
    };

    public string DurationText => DurationMs <= 0 ? "--:--" : $"{DurationMs / 60000}:{DurationMs / 1000 % 60:00}";
    public bool CanDelete => Source != TrackSource.BuiltIn;
}

/// <summary>
/// 本地曲库：内置曲 tracks/builtin、导入曲 tracks/user、在线下载缓存 online/。
/// </summary>
public sealed class LibraryService
{
    private readonly string _dataDir;

    public LibraryService(string dataDir)
    {
        _dataDir = dataDir;
        Directory.CreateDirectory(UserDir);
    }

    public string UserDir => Path.Combine(_dataDir, "tracks", "user");
    public string OnlineDir => Path.Combine(_dataDir, "online");

    /// <summary>扫描所有目录（会解析 MIDI 取时长，放后台线程调用）</summary>
    public List<TrackItem> Scan()
    {
        var list = new List<TrackItem>();
        var builtInDir = BuiltInTracks.Dir(_dataDir);
        foreach (var s in BuiltInTracks.Songs)
        {
            var path = Path.Combine(builtInDir, s.FileName);
            if (File.Exists(path)) list.Add(Describe(path, TrackSource.BuiltIn, s.Title, s.Subtitle));
        }
        list.AddRange(ScanDir(UserDir, TrackSource.Imported));
        list.AddRange(ScanDir(OnlineDir, TrackSource.Online));
        return list;
    }

    private static IEnumerable<TrackItem> ScanDir(string dir, TrackSource source)
    {
        if (!Directory.Exists(dir)) return Enumerable.Empty<TrackItem>();
        return Directory.EnumerateFiles(dir)
            .Where(f => f.EndsWith(".mid", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".midi", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Select(f => Describe(f, source, CleanTitle(f), source == TrackSource.Online ? "BitMidi" : "本地导入"));
    }

    public static TrackItem Describe(string path, TrackSource source, string title, string subtitle)
    {
        long duration = 0;
        int notes = 0;
        try
        {
            var song = MidiSong.Load(path);
            duration = song.DurationMs;
            notes = song.Tracks.Where(t => !t.IsDrum).Sum(t => t.NoteCount);
        }
        catch
        {
            subtitle = "无法解析";
        }
        return new TrackItem
        {
            Title = title,
            Subtitle = subtitle,
            FullPath = path,
            Source = source,
            DurationMs = duration,
            NoteCount = notes,
        };
    }

    /// <summary>"Canon [21743].mid" → "Canon"</summary>
    public static string CleanTitle(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var i = name.LastIndexOf(" [", StringComparison.Ordinal);
        if (i > 0 && name.EndsWith(']')) name = name[..i];
        return name.Replace('_', ' ').Trim();
    }

    /// <summary>校验并复制到用户目录，返回新条目</summary>
    public TrackItem Import(string srcPath)
    {
        MidiSong.Load(srcPath); // 解析失败直接抛给调用方
        var name = Path.GetFileNameWithoutExtension(srcPath);
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        var dest = Path.Combine(UserDir, name + ".mid");
        for (int n = 2; File.Exists(dest); n++)
            dest = Path.Combine(UserDir, $"{name} ({n}).mid");
        File.Copy(srcPath, dest);
        return Describe(dest, TrackSource.Imported, CleanTitle(dest), "本地导入");
    }

    public void Delete(TrackItem item)
    {
        if (!item.CanDelete) return;
        File.Delete(item.FullPath);
    }
}
