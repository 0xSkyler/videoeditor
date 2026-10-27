# StudioCut — One-click Windows video converter

**Drop a video or click Choose video → press Convert → get a new MP4.**

StudioCut is a native Windows 10/11 x64 desktop application. It converts video entirely offline using FFmpeg. The v0.5 interface has **no manual editing or export parameters**: no sliders, checkboxes, audio dropdowns, effect settings, folder chooser or quality presets.

## The entire workflow

1. Drag a video onto the window **or** click **Choose video**.
2. StudioCut reads the video information and enables **Convert**.
3. Click **Convert**. A progress bar shows encoding status; **Cancel** is available during an active conversion.
4. StudioCut automatically saves a new file in **Videos/StudioCut Exports**. If Videos isn't accessible, it tries Documents or the user home folder.
5. When finished, click **Open output folder** to see the MP4. The source file remains unchanged.

## Automatic export settings

| Setting | Automatic behavior |
| --- | --- |
| Video container | MP4 |
| Video codec and pixel format | H.264 (libx264), 8-bit yuv420p |
| Video quality | CRF 18 (High Quality) |
| Audio codec | AAC at 192 kbps per encoded track |
| Narration and soundtrack | Preserve all source audio tracks, including Higgsfield AI narration |
| Frame rate, frame dimensions, runtime | Preserve original values where compatible with H.264; odd dimensions may be padded by 1 pixel |
| Color | Very slight 1% contrast and saturation adjustment |
| Metadata | Attempt to copy source metadata (including attribution/provenance) |
| Hardware | Conservative CPU encoding to avoid GPU-compatibility errors |
| Files | Unique `*_converted_*.mp4` filename; source never overwritten |
| Processing | Local FFmpeg; no upload, cloud service, API key or telemetry |

**Important:** Re-encoding and minor video changes are compatibility/quality operations. They do not make copyrighted material original, bypass YouTube Content ID, or reliably hide that a video was AI-generated. Use only content you have permission to publish. The app cannot validate music licenses or identify AI-origin claims. Confirm usage rights for your Higgsfield-generated content and voice under the applicable service terms.

## Download / install

1. Download the `StudioCut-win-x64.zip` file from [GitHub Releases](https://github.com/0xSkyler/videoeditor/releases).
2. Extract the entire ZIP to a folder (do not run from inside the ZIP).
3. Run `StudioCut.exe`.
4. Keep the bundled `tools/ffmpeg.exe` and `tools/ffprobe.exe` next to the executable.

No separate .NET installation is required for the self-contained Windows x64 package.

## Build and tests

Requirements: .NET 8 SDK on Windows. For development, put FFmpeg/FFprobe in `StudioCut/tools/` or on PATH.

```powershell
dotnet build StudioCut/StudioCut.csproj -c Release
dotnet run --project StudioCut.Tests/StudioCut.Tests.csproj -c Release
dotnet run --project StudioCut.Scenarios/StudioCut.Scenarios.csproj -c Release
dotnet run --project StudioCut.GuiTests/StudioCut.GuiTests.csproj -c Release
```

GitHub Actions builds the Windows executable, runs real FFmpeg conversion/regression cases, checks the WPF import/convert workflow, launches the packaged application and verifies ZIP integrity. Tests use local synthetic media, not licensed Higgsfield account material, and cannot certify actual YouTube claim outcomes.

## Limitations

- The new v0.5 simplified app intentionally removes manual audio, crop, overlays, audio replacement, FPS and quality settings. For advanced editing, refer to the older v0.4.2 release.
- Preview is an optional still image; there is no timeline or video playback control.
- The color and codec conversion can cause some quality loss even at high quality. Existing HDR/10-bit media is converted to 8-bit H.264 for compatibility.
- Output audio is re-encoded; each retained audio track is kept, but specialized audio metadata and chapter structures may not survive.
- Hardware GPU encoding, long 4K processing, subjective voice-quality evaluations and actual YouTube uploads are not proven by synthetic CI tests.
- [FFmpeg third-party notices](THIRD_PARTY_NOTICES.md) apply to redistribution.

## Changelog

### 0.5.1 — Final handover verification
- Windows CI passed **389 automated assertions**: 110 media-engine, 118 media-scenario and 161 WPF GUI assertions.
- Extended diverse-media coverage to **26 scenarios**: 10-bit input, MP3 album-cover false positives, 321x181 odd dimensions, sub-second videos, Unicode/emoji filenames, source metadata, source deletion and repeated exports.
- One-click WPF acceptance now drives **eight real video exports** plus picker cancellations, injected native picker errors, single/multiple-file drop paths, output-folder action, in-progress Cancel and closing during encoding.
- Fixed a missing-source silent failure, infinite/hung probing risk (bounded at 30 seconds), album-cover-only false video detection and invalid Unicode filename truncation.
- Invalid media now displays short, actionable text rather than raw FFmpeg stderr and internal file paths.
- Full acceptance matrix, limitations and evidence are in [Final QA and handover](docs/FINAL_HANDOVER_v0.5.1.md).

### 0.5.0 — Complete UI simplification
- New one-screen drag-and-drop app with **Choose video** and **Convert** as the only primary actions.
- Removed every manual editing, audio, color, speed, resolution, quality, rights-confirmation and output-folder input.
- One automatic high-quality MP4 pipeline, preserving Higgsfield-style generated narration and all other audio tracks.
- Optional Cancel and Open output folder controls provide safety without configuration.
- Unique exports in the user's Videos folder; no source overwrite.
- User-journey tests updated to verify the absence of manual input controls.

### 0.4.2 — Previous advanced editor
- 14 Windows media-use-case scenarios, multi-audio preservation, live cancellation testing and rights-aware export.
- The advanced manual UI is preserved only in the older v0.4.2 GitHub release and is not part of this version.
