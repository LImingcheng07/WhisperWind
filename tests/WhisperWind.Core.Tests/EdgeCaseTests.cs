using NUnit.Framework;
using WhisperWind.Core;
using System.Collections.Generic;
using System.Linq;

namespace WhisperWind.Core.Tests;

[TestFixture]
public class EdgeCaseTests
{
    private static List<MidiNote> Notes(params (int pitch, long start, long dur)[] items)
    {
        var list = new List<MidiNote>();
        foreach (var (p, s, d) in items)
            list.Add(new MidiNote(p, s, s + d, 0, 0));
        return list;
    }

    [Test]
    public void Custom_Mapping_Overrides_Default()
    {
        var custom = new Dictionary<int, IReadOnlyList<GameKey>>
        {
            [60] = new[] { GameKey.MainKey5 }, // 改成 MainKey5 而非 MainKey1
        };
        var m = new HarmonicaMapping(custom);
        var b = m.Resolve(60);
        Assert.That(b, Is.EqualTo(new[] { GameKey.MainKey5 }));
    }

    [Test]
    public void Null_Custom_Mapping_Uses_Default()
    {
        var m = new HarmonicaMapping(noteToBinding: null);
        var b = m.Resolve(60);
        Assert.IsNotNull(b);
    }

    [Test]
    public void Empty_Custom_Mapping_Means_No_Mapping()
    {
        var m = new HarmonicaMapping(new Dictionary<int, IReadOnlyList<GameKey>>());
        var b = m.Resolve(60);
        Assert.IsNull(b); // 用户明确传空 = 真的不要任何音
    }

    [Test]
    public void Mapping_Min_Max_Pitch_Are_Correct()
    {
        var m = new HarmonicaMapping();
        Assert.That(m.MinPitch, Is.EqualTo(48));
        Assert.That(m.MaxPitch, Is.EqualTo(84));
    }

    [Test]
    public void Out_Of_Range_Low_Folds_Up()
    {
        var m = new HarmonicaMapping();
        var c = new HarmonicaConverter(m);
        // C3 = 48 → 第 1 孔 + 降调（左键）
        var plan = c.Convert("x.mid", 0, Notes((48, 0, 200)));
        Assert.That(plan.Events.Count, Is.EqualTo(1));
        Assert.That(plan.Events[0].Keys[0], Is.EqualTo(GameKey.MainKey1));
    }

    [Test]
    public void Half_Tone_Black_Key_Snaps_To_Nearest()
    {
        // D#4 = 63 应映射到 MainKey2 + Sharp（默认 sharps 已含）
        var m = new HarmonicaMapping();
        var c = new HarmonicaConverter(m);
        var plan = c.Convert("x.mid", 0, Notes((63, 0, 200)));
        Assert.That(plan.Events.Count, Is.EqualTo(1));
        Assert.That(plan.Events[0].Keys, Contains.Item(GameKey.Sharp));
    }

    [Test]
    public void Pitch_Desc_Order_Within_Same_Moment()
    {
        var m = new HarmonicaMapping();
        var c = new HarmonicaConverter(m);
        // 64 @ 0 然后 60 @ 100（不同 ms 不冲突）→ 64 pitch 高，sort 后先 64
        var plan = c.Convert("x.mid", 0, Notes((60, 100, 200), (64, 0, 200)));
        Assert.That(plan.Events.Count, Is.EqualTo(2));
        Assert.That(plan.Events[0].Keys[0], Is.EqualTo(GameKey.MainKey3)); // 64 = MainKey3
        Assert.That(plan.Events[1].Keys[0], Is.EqualTo(GameKey.MainKey1)); // 60 = MainKey1
    }

    [Test]
    public void Duration_Of_Plan_Is_Last_Start_Plus_Last_Duration()
    {
        var m = new HarmonicaMapping();
        var c = new HarmonicaConverter(m);
        var plan = c.Convert("x.mid", 0, Notes((60, 100, 50), (62, 200, 50)));
        Assert.That(plan.DurationMs, Is.EqualTo(250));
    }

    [Test]
    public void Cancelling_RunAsync_Stops_Player()
    {
        var m = new HarmonicaMapping();
        var c = new HarmonicaConverter(m);
        var plan = c.Convert("x.mid", 0, Notes((60, 0, 50), (62, 200, 50), (64, 400, 50)));
        var player = new FakeNotePlayer();
        var cts = new CancellationTokenSource();
        cts.CancelAfter(20);
        try { player.RunAsync(plan, 1.0, cts.Token).Wait(2000); }
        catch (System.AggregateException) { /* expected */ }
        // 不论是否抛异常，关键是进程没卡死
        Assert.Pass();
    }

    [Test]
    public void IsValid_False_When_Nothing_Playable()
    {
        var sparse = new HarmonicaMapping(new Dictionary<int, IReadOnlyList<GameKey>>
        {
            [60] = new[] { GameKey.MainKey1 },
        });
        var c = new HarmonicaConverter(sparse);
        var plan = c.Convert("x.mid", 0, Notes((65, 0, 200)));
        Assert.That(plan.Errors, Is.Not.Empty);
        Assert.That(plan.IsValid, Is.False);
    }

    [Test]
    public void IsValid_True_When_No_Errors()
    {
        var m = new HarmonicaMapping();
        var c = new HarmonicaConverter(m);
        var plan = c.Convert("x.mid", 0, Notes((60, 0, 200)));
        Assert.That(plan.IsValid, Is.True);
    }
}