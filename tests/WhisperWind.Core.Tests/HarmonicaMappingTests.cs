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
            Assert.IsFalse(b!.RequiresMouse(), $"natural note {p} should not need mouse");
        }
    }

    [Test]
    public void Default_Contains_All_Five_Sharps_As_MouseSide()
    {
        var m = new HarmonicaMapping();
        int[] sharps = { 61, 63, 66, 68, 70 };
        foreach (var p in sharps)
        {
            var b = m.Resolve(p);
            Assert.IsNotNull(b, $"sharp note {p} should be mappable");
            Assert.IsTrue(b!.Contains(GameKey.MouseSide), $"sharp {p} should use mouse side");
        }
    }

    [Test]
    public void High_Octave_Notes_Use_MouseMiddle()
    {
        var m = new HarmonicaMapping();
        var c5 = m.Resolve(72);
        Assert.IsTrue(c5!.Contains(GameKey.MouseMiddle));
    }

    [Test]
    public void Resolve_Returns_Null_For_Unmapped_Note()
    {
        var m = new HarmonicaMapping();
        // C2 = 36 - far below the default 8-tone range
        Assert.IsNull(m.Resolve(36));
    }
}
