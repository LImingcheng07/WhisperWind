using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;
using WhisperWind.Core;

namespace WhisperWind.App.Services;

/// <summary>
/// 用户本地 MIDI 导入服务：
/// 1) OpenFileDialog 选 .mid/.midi 文件
/// 2) 校验文件能解析（MidiSong.Load 不抛）
/// 3) 复制到 %AppData%/WhisperWind/tracks/user/ 下
/// 4) 返回 TrackMeta 给 UI 显示
/// </summary>
public sealed class MidiImportService
{
    private readonly string _userTracksDir;
    private readonly string _tracksDir;

    public MidiImportService(string appDataDir)
    {
        var baseDir = Path.Combine(appDataDir, "WhisperWind");
        _tracksDir = Path.Combine(baseDir, "tracks");
        _userTracksDir = Path.Combine(baseDir, "tracks", "user");
        Directory.CreateDirectory(_userTracksDir);
    }

    public TrackMeta? PickAndImport()
    {
        var dlg = new OpenFileDialog
        {
            Title = "选择 MIDI 文件",
            Filter = "MIDI 文件 (*.mid;*.midi)|*.mid;*.midi|所有文件 (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog() != true) return null;

        var src = dlg.FileName;
        try
        {
            // 校验：能解析才算合法 MIDI
            var song = MidiSong.Load(src);
            var mainTrack = song.Tracks.OrderByDescending(t => t.NoteCount).FirstOrDefault();

            // 复制到 user 目录
            var fileName = SanitizeFileName(Path.GetFileNameWithoutExtension(src))
                + "_" + Guid.NewGuid().ToString("N")[..6]
                + Path.GetExtension(src);
            var dest = Path.Combine(_userTracksDir, fileName);
            File.Copy(src, dest, overwrite: false);

            var name = Path.GetFileNameWithoutExtension(src);
            if (mainTrack is not null) name = $"{name} (user, {mainTrack.NoteCount} notes)";
            return new TrackMeta(name, fileName);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"MIDI 解析失败: {ex.Message}", ex);
        }
    }

    public string FullPath(string fileName) => Path.Combine(_userTracksDir, fileName);

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }
}
