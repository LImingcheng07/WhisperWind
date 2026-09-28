using NUnit.Framework;
using WhisperWind.Core;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace WhisperWind.Core.Tests;

[TestFixture]
public class FakeNotePlayerTests
{
    [Test]
    public async Task Run_Async_Presses_And_Releases_All_Events()
    {
        var m = new HarmonicaMapping();
        var c = new HarmonicaConverter(m, minGapMs: 0);
        var plan = c.Convert("t.mid", 0, new[] {
            new MidiNote(60, 0, 50, 0, 0),
            new MidiNote(62, 100, 150, 0, 0),
            new MidiNote(64, 200, 250, 0, 0),
        });
        var player = new FakeNotePlayer();
        await player.RunAsync(plan, speed: 10, CancellationToken.None);
        // 3 notes × (press + release)，结束时再兜底 RELEASE_ALL 一次
        Assert.That(player.Log.Count(l => l.action == "PRESS"), Is.EqualTo(3));
        Assert.That(player.Log.Count(l => l.action == "RELEASE"), Is.EqualTo(3));
        Assert.That(player.Log[^1].action, Is.EqualTo("RELEASE_ALL"));
        // 第 0/2/4 是 press，1/3/5 是 release
        Assert.That(player.Log[0].action, Is.EqualTo("PRESS"));
        Assert.That(player.Log[1].action, Is.EqualTo("RELEASE"));
    }

    [Test]
    public void Run_Async_With_Invalid_Plan_Does_Nothing()
    {
        var plan = new PlaybackPlan("t.mid", 0, 0, new System.Collections.Generic.List<KeyEvent>(),
            new System.Collections.Generic.List<string>(),
            new System.Collections.Generic.List<string> { "broken" });
        var player = new FakeNotePlayer();
        player.RunAsync(plan, 1, CancellationToken.None).Wait();
        Assert.That(player.Log, Is.Empty);
    }
}
