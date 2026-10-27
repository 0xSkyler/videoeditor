# StudioCut — Offline Windows Video Editor

## NEW: Auto Copyright-Risk Review (v0.4.0)

After a regular export, click **Auto: Review & Fix Copyright Risks** to make a separately named, high-quality MP4 copy. The automated preset uses H.264/AAC, preserves the current exported video's resolution, frame rate, content and duration as closely as encoding permits, and checks the output for playable video/audio. If audio rights are unconfirmed, it asks for approval before muting **all audio including narration**. Unconfirmed visual rights block the export.

**This mode does not remove copyright and does not bypass or guarantee clearance from YouTube Content ID.** Bitrate, frame-rate, codec and metadata changes cannot confer legal rights. Read [Automatic Copyright-Risk Review](docs/AUTO_COPYRIGHT_RISK_MODE.md) before use.


Native .NET 8 WPF application. Locally processes source video with FFmpeg; no accounts, web services, subscriptions or telemetry.

## Features

- Drag/drop source, probe video metadata, native playback preview (preview shows original source)
- Trim, crop, scale, adjust brightness/contrast/saturation, change FPS, optional fade and image overlay
- Keep, mute, replace or mix audio. Audio track selection, volume, bass/treble EQ, frequency-domain noise reduction, voice compression, loudness normalization, audio fades
- MP4/MOV/MKV, H.264/AAC, quality presets, optional NVENC GPU encoding (CPU fallback)
- Permission confirmations, media validation, disk space check, progress and cancel; failed output is deleted
- Offline processing. No promise of copyright clearance, watermark bypass or Content ID avoidance

## Download and run

1. Open **Actions → Windows build** and download the `StudioCut-win-x64` artifact from a successful main-branch build. For tagged releases (`v*`), see GitHub Releases.
2. Extract the ZIP completely; keep `tools/ffmpeg.exe` and `tools/ffprobe.exe` alongside the app executable.
3. Run `StudioCut.exe` on Windows 10/11 x64. The app is self-contained (does not require separately installing .NET).
4. Select a video, edit options, select output folder, confirm rights, and choose **Start Convert**.

## Building locally

Install .NET 8 SDK on Windows, and place `ffmpeg.exe` and `ffprobe.exe` in `StudioCut/tools/` if running from a local publish folder. `dotnet build StudioCut/StudioCut.csproj` builds the WPF editor. FFmpeg must be on the executable's relative `tools/` directory or system PATH.

## Limitations

- Preview is the unchanged original and depends on Windows' installed media codecs. Export uses FFmpeg and supports more codecs than preview.
- Mixing narration and background music works when narration is in a separate source audio stream, or if the original audio is kept and background music is added. It cannot isolate narration from a pre-mixed soundtrack.
- Audio and video fades apply to the start/end of trimmed output; complex timeline transitions are not included.
- Hardware acceleration currently supports NVIDIA NVENC; if it fails, encoding retries with x264 on CPU.
- Music selection must be licensed by the user. Exporting a file does not make copyrighted material original or guarantee platform approval.
- Windows CI currently reuses pinned FFmpeg executables from the verified StudioCut v0.2.0 release; these were originally obtained from a Gyan.dev build. Distribution must comply with the included FFmpeg build's license; inspect build configuration and attach applicable licenses before redistributing.

## Changelog

### 0.1.0
- Initial desktop editing and export workflow; rights checks, quality presets, offline FFmpeg, WPF preview, GitHub Actions Windows packaging.

## Verified testing and exports

On Windows, run `dotnet run --project StudioCut.Tests/StudioCut.Tests.csproj -c Release` for repeatable media conversion and validator tests. GitHub Actions build #8 passed 77 C# assertions and 7 real Windows FFmpeg exports. A separate Linux FFmpeg smoke suite ran 3 repeat rounds of 5 exports; these results do not replace manual Windows GUI, real GPU-encoding, or long-form stress testing. Copyright ownership remains the user's responsibility; re-encoding does not clear third-party rights.

See `THIRD_PARTY_NOTICES.md` for the licensed FFmpeg components included in Windows builds.

## Updated user experience
- **Preview edited frame** renders the selected crop, color, logo, and short caption as a still without saving a permanent video. Audio and animated fades cannot be represented by a still.
- Each newly imported video resets trim and crop values to avoid hidden out-of-range settings; rights approvals still must be re-confirmed.
- Exports are re-probed before the UI displays success. Failed or invalid outputs are deleted when possible.
- The included Windows GUI acceptance suite tests actual WPF buttons, playback poster generation, edits, and export files. It does not replace human-operated mouse/keyboard checks or hardware GPU benchmarks.

## Performance optimization

Static logo overlays are now decoded once and held by FFmpeg for the duration of the video instead of looping the image every frame. This avoids repeated decoding of large images. For memory-sensitive editing, the CPU H.264 encoder and complex filter graph use two FFmpeg worker threads. A representative 12-second, 960×540 headless Linux FFmpeg comparison with a 2400×1400 static logo changed from 6.78s/400.3MB peak RSS to 1.57s/209.4MB. These are isolated benchmark results, not guarantees on a Windows user's machine.
