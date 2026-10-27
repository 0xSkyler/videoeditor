# StudioCut — Offline Windows Video Editor

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
- The FFmpeg binaries are downloaded during Windows CI from Gyan.dev. Distribution must comply with the included FFmpeg build's license; inspect build configuration and attach applicable licenses before redistributing.

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
