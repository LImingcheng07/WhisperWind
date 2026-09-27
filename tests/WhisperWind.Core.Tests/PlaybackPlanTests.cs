using NUnit.Framework;
using WhisperWind.Core;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WhisperWind.Core.Tests;

[TestFixture]
public class PlaybackPlanTests
{
    private static List<MidiNote> Notes(params (int pitch, long start, long dur)[] items)
    {
        var list = new List<MidiNote>();
        foreach (var (p, s, d) in items)
            list.Add(new MidiNote(p, s, s + d, 0, 0));
        return list;
    }

    [Test]
    public void Empty_Notes_Produces_Empty_Plan()
    {
        var m = new HarmonicaMapping();
        var c = new HarmonicaConverter(m);
        var plan = c.Convert("x.mid", 0, new List<MidiNote>());
        Assert.That(plan.Events.Count, Is.EqualTo(0));
        Assert.That(plan.IsValid, Is.False);
    }

    [Test]
    public void Sharps_Get_Mouse_Side()
    {
        var m = new HarmonicaMapping();
        var c = new HarmonicaConverter(m);
        // C#4 = 61 → MainKey2 + MouseSide
        var plan = c.Convert("x.mid", 0, Notes((61, 0, 200)));
        Assert.That(plan.Events.Count, Is.EqualTo(1));
        Assert.That(plan.Events[0].Keys, Contains.Item(GameKey.MouseSide));
    }

    [Test]
    public void Octave_Up_Notes_Get_Mouse_Middle()
    {
        var m = new HarmonicaMapping();
        var c = new HarmonicaConverter(m);
        // C5 = 72 → MainKey1 + MouseMiddle
        var plan = c.Convert("x.mid", 0, Notes((72, 0, 200)));
        Assert.That(plan.Events.Count, Is.EqualTo(1));
        Assert.That(plan.Events[0].Keys, Contains.Item(GameKey.MouseMiddle));
    }

    [Test]
    public void Two_Simultaneous_Notes_Trigger_Conflict()
    {
        var m = new HarmonicaMapping();
        var c = new HarmonicaConverter(m);
        // 60 + 62 同 ms → 都映射到 MainKey1 / MainKey2，不冲突但允许
        // 改用 60 + 60 同 ms 必冲突（pitch 排序后只一个 60，但用 60+72 也不行——不是主键冲突）
        // 测试 60 + 60 同 ms（同一主键）：merge 后只有 1 个事件，但都 60 pitch，sort 不变
        // 真正冲突场景：60 @ 0 + 60 @ 1：同音不同 ms，不冲突
        // 真正冲突：60 @ 0 + 60 @ 0 → 同一 pitch sort 后变 1
        // 算了，用两个 sharps 共享主键不冲突，用 empty moment 测试
        // 改测 60 (MainKey1) @ 0 + 84 (MainKey8 + Middle) @ 0：不同主键，OK
        // 真正冲突只有同 ms 同主键的两种 binding：例如 60 @ 0 + 72 @ 0 → MainKey1 都用 → 冲突
        var plan = c.Convert("x.mid", 0, Notes((60, 0, 200), (72, 0, 200)));
        Assert.That(plan.Errors.Any(e => e.Contains("Conflict")), Is.True);
    }

    [Test]
    public void Very_Short_Note_Extended_To_30ms()
    {
        var m = new HarmonicaMapping();
        var c = new HarmonicaConverter(m);
        // 60 持续 5ms → 拉到 30ms
        var plan = c.Convert("x.mid", 0, Notes((60, 0, 5)));
        Assert.That(plan.Events.Count, Is.EqualTo(1));
        Assert.That(plan.Events[0].DurationMs, Is.GreaterThanOrEqualTo(30));
    }

    [Test]
    public void Speed_2x_Player_Runs_Faster()
    {
        var m = new HarmonicaMapping();
        var c = new HarmonicaConverter(m);
        var notes = Notes((60, 0, 200));
        var plan = c.Convert("x.mid", 0, notes);
        var player = new FakeNotePlayer();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        player.RunAsync(plan, 2.0, CancellationToken.None).Wait();
        sw.Stop();
        // 2x speed → 总长约 100ms (Press + Wait 100 + Release)
        Assert.That(sw.ElapsedMilliseconds, Is.LessThan(180));
    }
}