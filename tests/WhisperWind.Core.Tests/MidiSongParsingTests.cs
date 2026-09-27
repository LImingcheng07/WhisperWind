using NUnit.Framework;
using WhisperWind.Core;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

namespace WhisperWind.Core.Tests;

/// <summary>解析一个真的 MIDI 文件，验证 MidiSong 提取无误</summary>
[TestFixture]
public class MidiSongParsingTests
{
    private string _tmpMidi = Path.Combine(Path.GetTempPath(), $"ww-test-{Guid.NewGuid():N}.mid");

    [OneTimeSetUp]
    public void Setup()
    {
        // 合成一个简单 MIDI：单轨 3 音 C4(60) E4(64) G4(67) 各 200ms
        var tempoMap = TempoMap.Default;
        var events = new List<MidiEvent>
        {
            new NoteOnEvent { DeltaTime = 0, NoteNumber = (SevenBitNumber)60, Velocity = (SevenBitNumber)80 },
            new NoteOffEvent { DeltaTime = 200, NoteNumber = (SevenBitNumber)60, Velocity = (SevenBitNumber)0 },
            new NoteOnEvent { DeltaTime = 0, NoteNumber = (SevenBitNumber)64, Velocity = (SevenBitNumber)80 },
            new NoteOffEvent { DeltaTime = 200, NoteNumber = (SevenBitNumber)64, Velocity = (SevenBitNumber)0 },
            new NoteOnEvent { DeltaTime = 0, NoteNumber = (SevenBitNumber)67, Velocity = (SevenBitNumber)80 },
            new NoteOffEvent { DeltaTime = 200, NoteNumber = (SevenBitNumber)67, Velocity = (SevenBitNumber)0 },
        };
        var trackChunk = new TrackChunk(events);
        var midi = new MidiFile(trackChunk);
        midi.Write(_tmpMidi);
    }

    [OneTimeTearDown]
    public void Cleanup()
    {
        if (File.Exists(_tmpMidi)) File.Delete(_tmpMidi);
    }

    [Test]
    public void Parse_3_Note_Midi_Succeeds()
    {
        var song = MidiSong.Load(_tmpMidi);
        Assert.That(song.Tracks.Count, Is.GreaterThanOrEqualTo(1));
        var track = song.GetTrackNotes(0);
        Assert.That(track.Count, Is.EqualTo(3));
        Assert.That(track[0].Pitch, Is.EqualTo(60));
        Assert.That(track[2].Pitch, Is.EqualTo(67));
    }

    [Test]
    public void Track_Notes_Are_Sorted_By_Start()
    {
        var song = MidiSong.Load(_tmpMidi);
        var track = song.GetTrackNotes(0);
        for (int i = 1; i < track.Count; i++)
            Assert.That(track[i].StartMs, Is.GreaterThanOrEqualTo(track[i - 1].StartMs));
    }
}