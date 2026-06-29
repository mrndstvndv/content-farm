# TTS + Subtitle + Video Pipeline

How the services fit together to go from text → final video with word-level subtitles.

Services are registered in `Program.cs` as singletons — inject them into any Blazor component:

```razor
@inject EdgeTtsService EdgeTts
@inject SubtitleGeneratorService SubGen
@inject FfmpegService Ffmpeg
```

Or in a code-behind:

```csharp
[Inject] private EdgeTtsService EdgeTts { get; set; } = null!;
[Inject] private SubtitleGeneratorService SubGen { get; set; } = null!;
[Inject] private FfmpegService Ffmpeg { get; set; } = null!;
```

## Pipeline overview

```
Text ──► EdgeTtsService ──► audio.mp3 + WordBoundary list
                                    │
                                    ▼
                          SubtitleGeneratorService ──► subs.ass
                                    │
                  ┌─────────────────┘
                  ▼
          FfmpegService.ComposeAsync(video, audio, ass) ──► final.mp4
```

## Step by step

### 1. Generate TTS audio + word boundaries

```csharp
var result = await EdgeTts.SynthesizeAsync(
    text: "Your script here.",
    voice: "en-US-GuyNeural",
    rate: 0
);

// result.Audio          → byte[] (MP3)
// result.WordBoundaries → IReadOnlyList<WordBoundary>
```

Each `WordBoundary` has:
- `Text` — the spoken word
- `StartSec` / `EndSec` — precise timing in seconds

### 2. Generate ASS subtitles

```csharp
// Customise style
var style = new AssStyle
{
    FontName = "Arial",
    FontSize = 100,
    OutlineSize = 4,
    Uppercase = true,
    MinDurationSec = 0.12,
};

// Generate and save
await SubGen.SaveAsync("output/subs.ass", result.WordBoundaries, style);

// Or get the string
string ass = SubGen.Generate(result.WordBoundaries, style);
```

### 3. Composite final video

```csharp
await Ffmpeg.ComposeAsync(new CompositionOptions
{
    VideoPath     = "input/background.mp4",
    AudioPath     = "output/tts.mp3",
    SubtitlePath  = "output/subs.ass",
    OutputPath    = "output/final.mp4",
    FontsDir      = "./fonts",       // optional, for custom fonts
    DeleteInputs  = true,             // cleanup intermediates
});
```

## Custom ASS style reference

| Property | Default | Notes |
|---|---|---|
| `FontName` | `"Arial"` | Installed on all major OSes |
| `FontSize` | `123` | Points |
| `PrimaryColor` | `"#FFFFFF"` | Text fill colour |
| `OutlineColor` | `"#000000"` | Stroke colour |
| `OutlineSize` | `3.0` | Stroke width |
| `Bold` | `true` | |
| `Alignment` | `Center` | Centre-aligned (5) |
| `Uppercase` | `true` | Matches reference style |
| `MinDurationSec` | `0.15` | Prevents flash frames |
| `PlayResX` / `PlayResY` | `1080` / `1920` | Portrait layout |

## FFmpeg fonts

See [ffmpeg-ass-fonts.md](./ffmpeg-ass-fonts.md) for details on fontsdir, MKV attachments, and troubleshooting.

## Full example

```csharp
private async Task GenerateVideo(string text)
{
    // 1. TTS
    var ttsResult = await EdgeTts.SynthesizeAsync(text);

    var dir = Path.Combine("outputs", Guid.NewGuid().ToString());
    Directory.CreateDirectory(dir);

    var audioPath = Path.Combine(dir, "audio.mp3");
    var subsPath  = Path.Combine(dir, "subs.ass");
    var finalPath = Path.Combine(dir, "final.mp4");

    await File.WriteAllBytesAsync(audioPath, ttsResult.Audio);
    await SubGen.SaveAsync(subsPath, ttsResult.WordBoundaries);

    // 2. Composite
    await Ffmpeg.ComposeAsync(new CompositionOptions
    {
        VideoPath    = "inputs/background.mp4",
        AudioPath    = audioPath,
        SubtitlePath = subsPath,
        OutputPath   = finalPath,
        DeleteInputs = true,
    });
}
```

## Subtitle Timing & Alignment Rules

### Preventing Subtitle Overlap with the Title Card
Because body TTS data starts at `0.0` seconds relative to its own audio stream, the generated word boundaries initially start at `0.0` seconds. When a title card (with its own voice audio) is concatenated first, the body subtitles will overlap the title card unless shifted.

To resolve this, the pipeline probes the title card audio duration first and passes it to `SubtitleGeneratorService` as an offset:

```csharp
// 1. Probe duration of the title voice clip
double titleDuration = await Ffmpeg.GetDurationAsync(titleVoicePath);

// 2. Generate and save subtitles shifted by the title card duration
await SubGen.SaveAsync(subsPath, bodyResult.WordBoundaries, titleDuration, style);
```
Passing the `titleDuration` offset shifts the start and end times of every word dialogue event in the `.ass` file, keeping the title card sequence completely subtitle-free.

### Preventing Double-Word Subtitle Displays
When consecutive subtitle words are spoken rapidly close together, they can overlap in time if their durations are extended by minimum visibility buffers. In ASS subtitle styling, overlapping dialogue events are stacked and rendered together (showing two words on screen).

To guarantee only one word is shown at a time:
- Subtitle end times are strictly clamped to the start time of the next word.
- Overlap clamping is applied after all minimum duration extensions, preventing any time overlap.

```csharp
// In SubtitleGeneratorService.cs
if (i + 1 < boundaries.Count)
{
    var nextStart = boundaries[i + 1].StartSec + offsetSec;
    if (end > nextStart)
        end = nextStart; // Strict clamping to prevent time-overlaps
}
```
