using System.Diagnostics;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

using Whisper.net;

string file = Environment.GetEnvironmentVariable("MODEL")
    ?? throw new ArgumentNullException("MODEL");
string dir = Environment.GetEnvironmentVariable("DIR")
    ?? throw new ArgumentNullException("DIR");

string modelPath = file;

using var whisperFactory = WhisperFactory.FromPath(modelPath);

// we get 50x real-time on 3090
// the service cost 

string[] files = Directory.GetFiles(dir, "*.mp3");
long transcribed = 0;
var stopwatch = Stopwatch.StartNew();

var decodeSemaphore = new SemaphoreSlim(24);
var semaphore = new SemaphoreSlim(8);

await Task.WhenAll(files.Select(file => Transcribe(new(file))));

async Task Transcribe(FileInfo file) {
    await decodeSemaphore.WaitAsync().ConfigureAwait(false);
    // This section opens the mp3 file and converts it to a wav file with 16Khz sample rate.
    using var fileStream = file.OpenRead();

    using var wavStream = new MemoryStream();

    using var reader = new Mp3FileReader(fileStream);
    var resampler = new WdlResamplingSampleProvider(reader.ToSampleProvider(), 16000);
    WaveFileWriter.WriteWavFileToStream(wavStream, resampler.ToWaveProvider16());

    // This section sets the wavStream to the beginning of the stream. (This is required because the wavStream was written to in the previous section)
    wavStream.Seek(0, SeekOrigin.Begin);

    var result = new System.Text.StringBuilder();

    using var thread = whisperFactory.CreateBuilder()
        .WithLanguageDetection()
        .Build();

    await semaphore.WaitAsync().ConfigureAwait(false);
    try {
        // This section processes the audio file and prints the results (start time, end time and text) to the console.
        await foreach (var entry in thread.ProcessAsync(wavStream)) {
            result.AppendLine($"[{entry.Start:mm\\:ss\\.ff}]{entry.Text.Trim()}");
        }
    } finally {
        semaphore.Release();
    }

    long duration = reader.TotalTime.Ticks;
    var transcribedSoFar = TimeSpan.FromTicks(Interlocked.Add(ref transcribed, duration));
    double rate = transcribedSoFar.TotalSeconds / stopwatch.Elapsed.TotalSeconds;
    Console.WriteLine($"Rate: {rate:0.00}x");

    decodeSemaphore.Release();
}