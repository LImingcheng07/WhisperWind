using NUnit.Framework;
using WhisperWind.Core;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace WhisperWind.Core.Tests;

[TestFixture]
public class NewFeatureTests
{
    private static List<MidiNote> Notes(params (int pitch, long start, long dur)[] items)
        => items.Select(i => new MidiNote(i.pitch, i.start, i.start + i.dur, 0, 0)).ToList();

    [TestCase(61, GameKey.MainKey1)] // C#
    [TestCase(63, GameKey.MainKey2)] // D#
    [TestCase(66, GameKey.MainKey4)] // F# = F 键 + 升半音
    [TestCase(68, GameKey.MainKey5)] // G#
    [TestCase(70, GameKey.MainKey6)] // A#
    [TestCase(78, GameKey.MainKey4)] // 高八度 F#
    public void Sharps_Use_Their_Natural_Base_Key(int pitch, GameKey expected)
    {
        var b = new HarmonicaMapping().Resolve(pitch)!;
        Assert.That(b[0], Is.EqualTo(expected));
        Assert.That(b, Contains.Item(GameKey.Sharp));
    }

    [Test]
    public void Every_Semitone_From_C4_To_C6_Is_Playable()
    {
        var m = new HarmonicaMapping();
        for (int p = 60; p <= 84; p++)
            Assert.That(m.Resolve(p), Is.Not.Null, $"pitch {p}");
    }

    [Test]
    public void Duration_Is_Clipped_Before_Next_Note()
    {
        var c = new HarmonicaConverter(new HarmonicaMapping(), minGapMs: 20, releaseGapMs: 15);
        // 第一个音拖到 500ms，但第二个音 200ms 就开始 → 必须在 185ms 前松开
        var plan = c.Convert("x.mid", 0, Notes((60, 0, 500), (62, 200, 100)));
        Assert.That(plan.Events[0].EndMs, Is.LessThanOrEqualTo(185));
    }

    [Test]
    public void Score_Is_100_For_In_Range_Melody_And_Lower_When_Folded()
    {
        var c = new HarmonicaConverter(new HarmonicaMapping());
        var good = c.Convert("x", 0, Notes((60, 0, 100), (62, 200, 100)));
        Assert.That(good.Score, Is.EqualTo(100));
        var folded = c.Convert("x", 0, Notes((40, 0, 100), (62, 200, 100)));
        Assert.That(folded.Score, Is.EqualTo(50));
    }

    [Test]
    public void BestTranspose_Moves_Low_Melody_Into_Range()
    {
        // C2 大调音阶低于口琴音域（C3–C6），应整体上移 1~2 个八度
        var notes = Notes((36, 0, 100), (38, 200, 100), (40, 400, 100), (41, 600, 100), (43, 800, 100));
        Assert.That(MelodyAnalyzer.BestTranspose(notes, new HarmonicaMapping()), Is.EqualTo(12).Or.EqualTo(24));
    }

    [Test]
    public void BestTranspose_Keeps_In_Range_Melody()
    {
        var notes = Notes((60, 0, 100), (64, 200, 100), (67, 400, 100));
        Assert.That(MelodyAnalyzer.BestTranspose(notes, new HarmonicaMapping()), Is.EqualTo(0));
    }

    [Test]
    public void Jianpu_Parses_Npc_Code()
    {
        var notes = Jianpu.Parse("7676354", beatMs: 400);
        Assert.That(notes.Select(n => n.Pitch), Is.EqualTo(new[] { 71, 69, 71, 69, 64, 67, 65 }));
        Assert.That(notes[1].StartMs, Is.EqualTo(400));
    }

    [Test]
    public void Jianpu_Supports_High_Octave_Sharp_Rest_And_Hold()
    {
        var notes = Jianpu.Parse("1' 4# 0 5-", beatMs: 100);
        Assert.That(notes.Select(n => n.Pitch), Is.EqualTo(new[] { 72, 66, 67 }));
        Assert.That(notes[2].StartMs, Is.EqualTo(300));   // 休止占一拍
        Assert.That(notes[2].EndMs, Is.GreaterThan(400)); // 延长一拍
    }

    [Test]
    public void Runner_Pause_Holds_Position_And_Resumes()
    {
        var c = new HarmonicaConverter(new HarmonicaMapping());
        var plan = c.Convert("x", 0, Notes((60, 0, 50), (62, 150, 50), (64, 300, 50)));
        var player = new FakeNotePlayer();
        var control = new PlaybackControl();
        long lastPos = 0;
        var task = PlaybackRunner.RunAsync(player, plan, control, (p, _) => Interlocked.Exchange(ref lastPos, p), CancellationToken.None);

        Thread.Sleep(80);
        control.Paused = true;
        Thread.Sleep(50);
        long pausedAt = Interlocked.Read(ref lastPos);
        Thread.Sleep(300);
        Assert.That(task.IsCompleted, Is.False, "暂停期间不应播完");
        Assert.That(Interlocked.Read(ref lastPos), Is.EqualTo(pausedAt), "暂停期间位置不应前进");

        control.Paused = false;
        Assert.That(task.Wait(2000), Is.True);
        Assert.That(player.Log.Count(l => l.action == "PRESS"), Is.EqualTo(3));
    }

    [Test]
    public void Runner_Releases_Keys_On_Cancel()
    {
        var c = new HarmonicaConverter(new HarmonicaMapping());
        var plan = c.Convert("x", 0, Notes((60, 0, 1000)));
        var player = new FakeNotePlayer();
        var cts = new CancellationTokenSource();
        var task = PlaybackRunner.RunAsync(player, plan, new PlaybackControl(), null, cts.Token);
        Thread.Sleep(50);
        cts.Cancel();
        try { task.Wait(1000); } catch (System.AggregateException) { }
        Assert.That(player.Log.Last().action, Is.EqualTo("RELEASE_ALL"));
        Assert.That(player.Log.Count(l => l.action == "RELEASE"), Is.EqualTo(1));
    }

    [Test]
    public void Runner_Seek_Skips_Earlier_Notes()
    {
        var c = new HarmonicaConverter(new HarmonicaMapping());
        var plan = c.Convert("x", 0, Notes((60, 0, 50), (62, 100, 50), (64, 200, 50)));
        var player = new FakeNotePlayer();
        PlaybackRunner.Run(player, plan, new PlaybackControl(), null, CancellationToken.None, startPositionMs: 150);
        Assert.That(player.Log.Count(l => l.action == "PRESS"), Is.EqualTo(1));
    }
}
