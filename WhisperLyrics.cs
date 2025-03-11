using NAudio.Wave;
using NAudio.Wave.SampleProviders;

using Whisper.net;

if (args.Length < 1) {
    Console.WriteLine("Usage: WhisperLyrics <Whisper.ggml> <mp3 file(s)>");
    return;
}

string modelPath = args[0];

using var whisperFactory = WhisperFactory.FromPath(modelPath);
using var thread = whisperFactory.CreateBuilder()
    .WithLanguageDetection()
    .Build();

foreach (string path in args[1..]) {
    await Transcribe(new(path));
}

async Task Transcribe(FileInfo file) {
    string destinationPath = Path.ChangeExtension(file.FullName, ".lrc");
    if (File.Exists(destinationPath)) {
        Console.WriteLine($"Skipping {file.Name} as {Path.GetFileName(destinationPath)} already exists.");
        return;
    }

    // This section opens the mp3 file and converts it to a wav file with 16Khz sample rate.
    using var fileStream = file.OpenRead();

    using var wavStream = new MemoryStream();

    using var reader = new Mp3FileReader(fileStream);
    var resampler = new WdlResamplingSampleProvider(reader.ToSampleProvider(), 16000);
    WaveFileWriter.WriteWavFileToStream(wavStream, resampler.ToWaveProvider16());

    // This section sets the wavStream to the beginning of the stream. (This is required because the wavStream was written to in the previous section)
    wavStream.Seek(0, SeekOrigin.Begin);

    var result = new System.Text.StringBuilder();

    // This section processes the audio file and prints the results (start time, end time and text) to the console.
    await foreach (var entry in thread.ProcessAsync(wavStream)) {
        result.AppendLine($"[{entry.Start:mm\\:ss\\.ff}]{entry.Text.Trim()}");
    }

    using var output = new StreamWriter(destinationPath);
    await output.WriteAsync(result.ToString());
    output.Close();
}