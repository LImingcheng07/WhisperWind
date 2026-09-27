using NUnit.Framework;
using WhisperWind.Core;
using System.Collections.Generic;
using System.Linq;

namespace WhisperWind.Core.Tests;

[TestFixture]
public class HarmonicaConverterTests
{
    private static IReadOnlyList<MidiNote> MakeNotes(params (int pitch, long start, long end)[] notes)
        => notes.Select(n => new MidiNote(n.pitch, n.start, n.end, 0, 0)).ToList();

    [Test]
    public void Simple_C_Major_Scale_Converts_All_Seven()
    {
        var m = new HarmonicaMapping();
        var c = new HarmonicaConverter(m, minGapMs: 20);
        var notes = MakeNotes(
            (60, 0, 300), (62, 400, 700), (64, 800, 1100), (65, 1200, 1500),
            (67, 1600, 1900), (69, 2000, 2300), (71, 2400, 2700), (72, 2800, 3100));
        var plan = c.Convert("test.mid", 0, notes);
        Assert.That(plan.Errors, Is.Empty);
        Assert.That(plan.Events.Count, Is.EqualTo(8));
        Assert.That(plan.DroppedNoteCount, Is.EqualTo(0));
    }

    [Test]
    public void Unmapped_Low_Note_Is_Dropped_With_Warning()
    {
        // 用一个稀疏 mapping（只支持 C 大调 3 个音），超出的都 dropped
        var sparseDict = new Dictionary<int, IReadOnlyList<GameKey>>
        {
            [60] = new[] { GameKey.MainKey1 },
            [62] = new[] { GameKey.MainKey2 },
            [64] = new[] { GameKey.MainKey3 },
        };
        var m = new HarmonicaMapping(sparseDict);
        var c = new HarmonicaConverter(m);
        // 65 (F4) 不在稀疏 mapping，且 65+12=77 (不在)、+12=89 (不在)、+12=101 (不在)、+12=113 (不在)、+12=125 (不在)、+12=137&ge;128 → dropped
        var notes = MakeNotes((65, 0, 300));
        var plan = c.Convert("t.mid", 0, notes);
        Assert.That(plan.Warnings.Any(w => w.Contains("dropped")), Is.True);
        Assert.That(plan.DroppedNoteCount, Is.GreaterThan(0));
    }

    [Test]
    public void Out_Of_Range_High_Note_Folds_Up_An_Octave()
    {
        var m = new HarmonicaMapping();
        var c = new HarmonicaConverter(m);
        // F#6 = 90 不在默认映射（默认最大 84）→ 折到 78（在 octaveUpSharp）
        // 78 = octaveUpSharp[3] = MainKey4 + MouseSide + MouseMiddle
        var notes = MakeNotes((90, 0, 300));
        var plan = c.Convert("t.mid", 0, notes);
        Assert.That(plan.Events.Count, Is.EqualTo(1));
        Assert.That(plan.Warnings.Any(w => w.Contains("shifted to")), Is.True);
    }

    [Test]
    public void Sharps_Are_Playable_And_Use_MouseSide()
    {
        var m = new HarmonicaMapping();
        var c = new HarmonicaConverter(m);
        // F# = 66 (mapped) - in default mapping
        var notes = MakeNotes((66, 0, 300));
        var plan = c.Convert("t.mid", 0, notes);
        Assert.That(plan.Events.Count, Is.EqualTo(1));
        Assert.That(plan.Events[0].Keys.Contains(GameKey.MouseSide), Is.True);
    }

    [Test]
    public void Too_Close_Notes_Produce_Error_And_Are_Skipped()
    {
        var m = new HarmonicaMapping();
        var c = new HarmonicaConverter(m, minGapMs: 100);
        // gap = 50ms, which is &lt; 100ms
        var notes = MakeNotes((60, 0, 30), (62, 50, 80));
        var plan = c.Convert("t.mid", 0, notes);
        Assert.That(plan.Errors.Any(e => e.Contains("too close")), Is.True);
    }
}
