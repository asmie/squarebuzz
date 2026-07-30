// Generates the game's sound effects as 16-bit PCM WAV files.
//
//   dotnet run tools/generate-sounds.cs
//
// Why a generator instead of committed audio nobody can trace: every sound here is defined by a
// handful of numbers - pitch, length, envelope, peak level - so what it will sound like is a
// property of this file rather than something you have to open an editor to find out. Change a
// frequency, re-run, and the change is reviewable in a diff.
//
// These are deliberately plain: sine fundamentals with one quiet harmonic, a raised-cosine
// envelope so no sound starts or ends on a discontinuity (which is what a click is), and a peak
// well below full scale so nothing is startling. They are honest placeholders for a sound
// designer's work, not an attempt to be that work. The pitches are all drawn from one C major
// scale, so no two of them can clash with each other.
//
// There is deliberately NO mistake sound. The design doc's motion spec says of the mistake
// shake: "No sound sting by default." Punishing a six-year-old with a noise for guessing is
// exactly the wrong instinct, and the shake plus the warn tint already say it.

const int SampleRate = 44_100;

// -13 dBFS. Quiet enough to sit under a conversation, loud enough to hear over a tablet speaker.
const double Peak = 0.22;

// A little of the octave above keeps a bare sine from sounding like a hearing test.
const double HarmonicMix = 0.18;

// Note frequencies, C major.
const double D5 = 587.330;
const double E5 = 659.255;
const double G5 = 783.991;
const double A4 = 440.000;
const double A5 = 880.000;
const double C5 = 523.251;
const double C6 = 1046.502;

var outputDirectory = Path.Combine("src", "Squarebuzz.App", "Resources", "Raw");
Directory.CreateDirectory(outputDirectory);

// Each sound is a sequence of (frequency, milliseconds) segments.
var sounds = new (string Name, (double Hz, int Ms)[] Segments, string Purpose)[]
{
    // The one heard most often by far, so it is the shortest and softest thing here.
    ("fill", [(A5, 70)], "a cell is filled"),

    // A fifth below fill, so the two are told apart by pitch without either being "wrong".
    ("cross", [(A4, 60)], "a cell is crossed out"),

    // Falling, because something was taken away.
    ("erase", [(E5, 45), (A4, 45)], "a mark is cleared"),

    // Rising fifth: a small, unmistakable "that's done".
    ("line", [(E5, 65), (G5, 75)], "a row or column is complete"),

    // Two soft rising notes, gentler than 'line' so a hint never feels like an achievement.
    ("hint", [(D5, 80), (A5, 100)], "a hint is revealed"),

    // A plain major arpeggio. Consonant by construction - it cannot come out sour.
    ("win", [(C5, 110), (E5, 110), (G5, 110), (C6, 190)], "the picture is finished"),
};

foreach (var (name, segments, purpose) in sounds)
{
    var samples = Render(segments);
    var path = Path.Combine(outputDirectory, name + ".wav");

    WriteWav(path, samples);

    var seconds = samples.Length / (double)SampleRate;
    var peak = samples.Length == 0 ? 0 : samples.Max(Math.Abs);

    Console.WriteLine(
        $"{name,-6} {seconds * 1000,5:0} ms  peak {peak,5:0.000}  " +
        $"{new FileInfo(path).Length,6} bytes  - {purpose}");
}

WriteMusic(Path.Combine(outputDirectory, "music.wav"));

Console.WriteLine($"\n{sounds.Length + 1} files written to {outputDirectory}");

// The background loop.
//
// Deliberately a chord pad and not a tune. A melody in a game a child plays for an hour is the
// fastest way to make them turn the sound off - and unlike a 70 ms blip, a tune is a composition,
// which is a job for a musician rather than for arithmetic. What this is instead: three held notes
// of one C major chord, breathing slowly at different rates so the texture shifts without
// anything ever "starting". Far quieter than the effects, because it must never compete with them.
//
// Seamlessness is arithmetic, not luck. Every frequency - notes and breathing rates alike - is
// rounded to a whole number of cycles across the loop, so the waveform at the end of the file is
// mid-cycle in exactly the same place as at the start and the wrap is inaudible.
static void WriteMusic(string path)
{
    const double LoopSeconds = 8;
    const double MusicPeak = 0.05;   // about -26 dBFS: present, never in the way

    // The highest partial in the pad is 392 Hz, so 11 kHz leaves five octaves of headroom above
    // anything actually in the file. At 44.1 kHz this one loop would be four times the size of
    // everything else in the app put together, for content that cannot use the bandwidth.
    const int MusicSampleRate = 11_025;

    var count = (int)(LoopSeconds * MusicSampleRate);
    var samples = new double[count];

    // C4, E4, G4 - the same chord 'win' arpeggiates, an octave down so it sits underneath.
    double[] notes = [261.626, 329.628, 391.995];

    // Slow, mutually prime breathing rates, so the three never swell together twice in a loop.
    double[] breaths = [1, 2, 3];

    for (var voice = 0; voice < notes.Length; voice++)
    {
        var hz = Quantise(notes[voice], LoopSeconds);
        var breathHz = breaths[voice] / LoopSeconds;

        for (var n = 0; n < count; n++)
        {
            var t = n / (double)MusicSampleRate;

            // 0.55 to 1.0, so a voice never disappears entirely and never doubles in level.
            var breath = 0.775 + (0.225 * Math.Sin(2 * Math.PI * breathHz * t));

            samples[n] += Math.Sin(2 * Math.PI * hz * t) * breath / notes.Length;
        }
    }

    for (var n = 0; n < count; n++)
    {
        samples[n] *= MusicPeak;
    }

    WriteWav(path, samples, MusicSampleRate);

    var wrapStep = Math.Abs(samples[0] - samples[^1]);

    Console.WriteLine(
        $"{"music",-6} {LoopSeconds * 1000,5:0} ms  peak {samples.Max(Math.Abs),5:0.000}  " +
        $"{new FileInfo(path).Length,6} bytes  - looping background, wrap step {wrapStep:0.00000}");
}

// Nearest frequency that completes a whole number of cycles in the loop.
static double Quantise(double hz, double loopSeconds) =>
    Math.Round(hz * loopSeconds) / loopSeconds;

// Renders the segments back to back, cross-fading the joins so a multi-note sound is one
// continuous waveform rather than several with silence and clicks between them.
static double[] Render((double Hz, int Ms)[] segments)
{
    var total = segments.Sum(s => s.Ms) * SampleRate / 1000;
    var samples = new double[total];
    var offset = 0;

    for (var i = 0; i < segments.Length; i++)
    {
        var (hz, ms) = segments[i];
        var count = ms * SampleRate / 1000;
        var isFirst = i == 0;
        var isLast = i == segments.Length - 1;

        for (var n = 0; n < count; n++)
        {
            var t = n / (double)SampleRate;
            var phase = 2 * Math.PI * hz * t;
            var wave = Math.Sin(phase) + (HarmonicMix * Math.Sin(2 * phase));

            // Normalise so adding the harmonic did not push the peak up.
            wave /= 1 + HarmonicMix;

            samples[offset + n] = wave * Peak * Envelope(n, count, isFirst, isLast);
        }

        offset += count;
    }

    return samples;
}

// Raised-cosine ramps. The ends of the whole sound get long ramps so it swells in and dies away;
// interior joins get short ones, just enough to avoid a step in the waveform.
static double Envelope(int n, int count, bool isFirst, bool isLast)
{
    var attack = Math.Min(count / 2, MillisecondsToSamples(isFirst ? 8 : 3));
    var release = Math.Min(count / 2, MillisecondsToSamples(isLast ? 45 : 3));

    if (n < attack)
    {
        return 0.5 * (1 - Math.Cos(Math.PI * n / attack));
    }

    var fromEnd = count - 1 - n;

    if (fromEnd < release)
    {
        return 0.5 * (1 - Math.Cos(Math.PI * fromEnd / release));
    }

    return 1;
}

static int MillisecondsToSamples(int ms) => Math.Max(1, ms * SampleRate / 1000);

// Canonical 44-byte RIFF/WAVE header followed by mono 16-bit little-endian samples.
static void WriteWav(string path, double[] samples, int sampleRate = SampleRate)
{
    const short Channels = 1;
    const short BitsPerSample = 16;

    var dataBytes = samples.Length * sizeof(short);

    using var stream = File.Create(path);
    using var writer = new BinaryWriter(stream);

    writer.Write("RIFF"u8);
    writer.Write(36 + dataBytes);
    writer.Write("WAVE"u8);

    writer.Write("fmt "u8);
    writer.Write(16);                                              // PCM chunk size
    writer.Write((short)1);                                        // format: PCM
    writer.Write(Channels);
    writer.Write(sampleRate);
    writer.Write(sampleRate * Channels * BitsPerSample / 8);       // byte rate
    writer.Write((short)(Channels * BitsPerSample / 8));           // block align
    writer.Write(BitsPerSample);

    writer.Write("data"u8);
    writer.Write(dataBytes);

    foreach (var sample in samples)
    {
        // Clamp before scaling: a value even slightly outside [-1, 1] would wrap around to the
        // opposite extreme as a short, which is audible as a loud crack.
        var clamped = Math.Clamp(sample, -1, 1);
        writer.Write((short)Math.Round(clamped * short.MaxValue));
    }
}
