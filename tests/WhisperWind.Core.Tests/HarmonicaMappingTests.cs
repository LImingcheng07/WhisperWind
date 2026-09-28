using NUnit.Framework;
using WhisperWind.Core;
using System.Linq;

namespace WhisperWind.Core.Tests;

[TestFixture]
public class HarmonicaMappingTests
{
    [Test]
    public void Default_Contains_All_Seven_Naturals()
    {
        var m = new HarmonicaMapping();
        int[] naturals = { 60, 62, 64, 65, 67, 69, 71 };
        foreach (var p in naturals)
        {
            var b = m.Resolve(p);
            Assert.IsNotNull(b, $"note {p} should be mappable");
            Assert.IsFalse(b!.Any(k => k.IsModifier()), $"natural note {p} should not need modifier");
        }
    }

    [Test]
    public void Default_Contains_All_Five_Sharps_As_Sharp()
    {
        var m = new HarmonicaMapping();
        int[] sharps = { 61, 63, 66, 68, 70 };
        foreach (var p in sharps)
        {
            var b = m.Resolve(p);
            Assert.IsNotNull(b, $"sharp note {p} should be mappable");
            Assert.IsTrue(b!.Contains(GameKey.Sharp), $"sharp {p} should use sharp");
        }
    }

    [Test]
    public void High_Octave_Notes_Use_OctaveUp()
    {
        var m = new HarmonicaMapping();
        var d5 = m.Resolve(74);
        Assert.IsTrue(d5!.Contains(GameKey.OctaveUp));
        // C5 由第 8 个主键直接吹出，不需要修饰键
        Assert.That(m.Resolve(72), Is.EqualTo(new[] { GameKey.MainKey8 }));
    }

    [Test]
    public void Resolve_Returns_Null_For_Unmapped_Note()
    {
        var m = new HarmonicaMapping();
        // C2 = 36 - far below the default 8-tone range
        Assert.IsNull(m.Resolve(36));
    }

    [Test]
    public void Low_Octave_Notes_Use_OctaveDown()
    {
        var m = new HarmonicaMapping();
        Assert.That(m.Resolve(48), Is.EqualTo(new[] { GameKey.MainKey1, GameKey.OctaveDown }));
        var fs3 = m.Resolve(54)!;
        Assert.That(fs3[0], Is.EqualTo(GameKey.MainKey4));
        Assert.That(fs3, Contains.Item(GameKey.Sharp));
        Assert.That(fs3, Contains.Item(GameKey.OctaveDown));
        Assert.That(m.MinPitch, Is.EqualTo(48));
        Assert.That(m.MaxPitch, Is.EqualTo(84));
    }
}
