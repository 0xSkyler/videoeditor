# StudioCut v0.5.1 — Final Quality Assurance and Handover

**Release candidate verified:** 27 October 2026  
**Repository:** https://github.com/0xSkyler/videoeditor  
**Automated Windows evidence:** https://github.com/0xSkyler/videoeditor/actions/runs/40493727806  
**Target artifact:** `StudioCut-win-x64.zip` under https://github.com/0xSkyler/videoeditor/releases/tag/v0.5.1

## Executive result

**389 automated assertions passed.** The Windows release candidate passed 110 media-engine assertions (7 regular + 2 auto-review FFmpeg exports), 118 assertions in 26 synthetic-media scenarios, and 161 WPF GUI assertions including **eight GUI-triggered conversions**. Packaged `StudioCut.exe` started successfully on the Windows runner; its observed startup working set was **175.0 MB**. All required executables and license notices were found after extracting the ZIP.

A passing automated suite establishes the tested behaviors on a Windows GitHub runner. It does **not** establish every codec or hardware configuration, actual YouTube copyright results, commercial licensing, or usability on a personally operated Windows 10 desktop.

## Current product workflow

1. **Choose video** through the native Windows picker or drag **one** file into the window.
2. **Convert** begins a local, offline FFmpeg encoding job. Progress and **Cancel** are available; no manual encoding settings exist.
3. A fresh `*_converted_*.mp4` is saved automatically in **Videos/StudioCut Exports** (with Documents/user-home fallback). **Open output folder** becomes available. The imported file remains unchanged.

The output is high-quality H.264 video (CRF 18, yuv420p) and AAC audio where audio exists. All audio tracks—including Higgsfield-style synthetic narration—remain. The source dimensions and FPS are preserved when compatible with H.264; odd dimensions are padded by one pixel, and 10-bit sources become 8-bit for broad compatibility. The converter uses subtle 1% contrast/saturation adjustments. Supported metadata is copied on a best-effort basis.

**No part of this conversion removes copyright or reliably evades AI/YouTube Content ID detection.** Changing bitrate, codec, contrast or metadata cannot confer rights to someone else's work.

## User-perspective acceptance matrix

| User perspective / function | Real test | Outcome |
| --- | --- | --- |
| First-time user, empty state | App launched; Convert disabled; Choose video ready; output path visible | PASS |
| Minimal controls | Counted all text entries, combo boxes, checkboxes, sliders and radio buttons: none | PASS |
| Choose video | Pressed WPF Choose button with injected file-picker result; checked details and first-frame poster | PASS |
| Cancel file picker | Pressed Choose with an empty picker result; original selection preserved | PASS |
| Native picker failure | Simulated exception at picker boundary; failure shown without app crash | PASS |
| Single-file drag/drop path | Exercised shared file-drop handler and checked selected video | PASS |
| Invalid multiple-file/folder drops | Tested rejection message without losing previously chosen file | PASS |
| Narrated Higgsfield-style clip | GUI Convert produced H.264/AAC MP4; narration track count retained | PASS |
| Repeat Convert | Generated unique filenames and retained earlier exports | PASS |
| Silence | Silent source stayed silent | PASS |
| Portrait short-form | WPF export retained orientation and FPS | PASS |
| Two-language narration | Retained both source audio tracks | PASS |
| Unicode filename | Bengali/CJK/emoji path worked in GUI and FFmpeg | PASS |
| Sub-second clip | Encoded a video shorter than one second | PASS |
| Odd 321×181 video | Produced 322×182 H.264 video with minimal padding | PASS |
| Preview poster | FFmpeg produced a real source-frame image displayed by WPF | PASS |
| Open output folder | Pressed WPF button; intercepted path matched the actual export directory | PASS |
| Error on deleted source | File removed after selection; clear error; Convert disabled | PASS |
| Corrupt video | Import rejected; no stale export; readable error with no raw FFmpeg log | PASS |
| Cancellation during encoding | Pressed Cancel after real progress; partial output deleted | PASS |
| Window closed while encoding | Closed WPF window after progress; partial output deleted | PASS |
| Progress and UI recovery | Progress completed; controls re-enabled after success/cancel | PASS |
| Visual QA | Inspected initial, success, cancelled and invalid-input screenshots | PASS |

The native Windows file-picker dialog and Explorer itself were **not manually operated** in this CI suite. Their production invocation is present, but tests substitute a deterministic path or intercepted open-folder action. The actual operating-system mouse drag gesture was not executed; the underlying WPF drop-processing path was exercised.

## Media and robustness matrix

**26/26 scenarios passed (118 assertions).** These covered licensed narration-tone preservation, silent and uncertain-audio cases, mixed narration and music, portrait/landscape FPS preservation, two audio tracks, legacy AVI/PCM transcoding, distinct repeated exports, missing/corrupt/audio-only inputs, cancel-before-start and mid-encoding cancellation, source SHA-256 preservation, one-click narrated/silent/dual-audio exports, cover-art-only MP3 rejection, Bengali/CJK/emoji filenames, an emoji at the 80-character truncation boundary, 321×181 odd dimensions, a 10-bit source, a 250-millisecond clip, title metadata copying, probe cancellation, and a nonexistent destination folder.

Media probing now has a 30-second maximum and kills the external process when cancelled. Cover-art-only audio is rejected rather than treated as playable video. Output verification checks container readability, expected H.264/AAC codecs, duration, valid dimensions and soundtrack track counts, where applicable. Failed or cancelled exports are cleaned up.

## Measured performance

- **Start-up process working set:** 175.0 MB for the packaged application on Windows GitHub Actions (not peak FFmpeg RAM).
- **26 scenario suite:** 8.69 seconds for generated short synthetic assets and their checks, excluding download/build/setup.
- **GUI conversions:** 0.20–0.54 seconds for very short test files in the referenced prior acceptance run; actual work varies significantly with video size and CPU.
- Encoding uses software x264 with constrained FFmpeg thread use; dedicated GPU throughput and full-length 4K runtime remain unverified.

## Not claimed / remaining manual acceptance

- **Actual Higgsfield outputs:** synthetic color bars and audio tones are not real Higgsfield voice narration. Human listening tests for clarity, emotion and lip sync remain unperformed.
- **YouTube copyright or AI classification:** no upload or Content ID result was tested; no clearance is guaranteed. Keep your generated source files, project records and relevant license terms.
- **Physical desktop experience:** tested with WPF controls on a Windows CI runner, not an operator using a mouse, drag gesture or native dialog on separate physical Windows 10/11 machines.
- **Long-duration/4K/60FPS/HDR/variable-frame-rate videos:** no comprehensive battery with these real files. HDR becomes 8-bit output in the current preset.
- **Security and distribution:** third-party FFmpeg redistribution/license compliance, code signing/SmartScreen, antivirus acceptance and malicious-input fuzzing are not independently certified.
- **Disk-full and inaccessible-profile errors:** validation and exception paths exist, but a real exhausted-drive scenario was not simulated.
- **Accessibility/localization:** automated keyboard-only, screen reader, high-DPI scaling and languages beyond filename support are not fully checked.

## How to install and verify

1. Extract the entire release ZIP to a writable folder on Windows 10/11 x64.
2. Run `StudioCut.exe`; keep the bundled `tools\ffmpeg.exe` and `tools\ffprobe.exe`.
3. Drop a short, original Higgsfield video with generated narration; press **Convert**.
4. Inspect the created `Videos\StudioCut Exports\*_converted_*.mp4`: watch/listen to the beginning and end and confirm narration and aspect ratio.
5. Repeat with an original silent/portrait video. Confirm Cancel cleans up when used and re-running Convert never overwrites a previous file.
6. For a claim on YouTube, review licensing and the platform's official dispute process; do not assume technical re-encoding clears copyright.

## Evidence paths and commands

- GitHub Actions: https://github.com/0xSkyler/videoeditor/actions/runs/40493727806
- Automated media report: `StudioCut-diverse-scenario-evidence` artifact, `results.json` + `report.md`
- GUI screenshots/measurements: `StudioCut-user-acceptance` artifact, `ux-results/simple-ui/results.json`, initial/success/cancel/error PNGs
- Packaging logs: `StudioCut-win-x64` artifact, startup and ZIP verification steps
- Local reproducible commands (Windows with .NET 8 and FFmpeg): `dotnet run --project StudioCut.Tests -c Release`, `dotnet run --project StudioCut.Scenarios -c Release`, and `dotnet run --project StudioCut.GuiTests -c Release`

**Handover decision:** The automated functional acceptance gate is passed for the scoped one-click Windows converter. Physical-device acceptance, genuine Higgsfield voice quality, legal licensing and 4K/long-video stress remain explicitly open rather than being represented as completed.
