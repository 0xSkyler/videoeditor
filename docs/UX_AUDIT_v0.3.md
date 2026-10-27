# StudioCut v0.3 — User-Workflow Audit and Verified Fixes

**Scope:** Automated WPF GUI actions on a GitHub-hosted Windows runner, plus practical 960×540 offline FFmpeg editing with a 16-second four-scene Apparel PLM explainer on Linux. This is **not** a human-operated usability session on a physical Windows 10/11 machine.

## Latest verified tests

- Windows build #31: 77 processing assertions, 7 FFmpeg exports; 60 automated WPF GUI assertions, 3 real GUI-triggered exports.
- A new user-facing **Preview edited frame** command displays a rendered still of the selected color/crop/logo/text effects. Audio and animated transitions cannot appear in a still.
- Export postflight checks the written video header, duration, H.264 frame dimensions and expected presence of audio. Invalid outputs are cleaned up.
- Source import clears the previous video's trim and crop fields rather than leaving stale numbers.
- Rights confirmations are reset for every source; Mute mode is chosen for silent source files.

## Real editing scenario

Original locally produced Apparel PLM explainer: 16 sec at 960×540, H.264/AAC with simulated voice narration. Editor operation: trim to 12 sec, adjust color, add 350 ms music delay, mix source narration with separate music, place original 2400×1400 logo in the bottom-right and add original caption.

- Output: 12-second 960×540 MP4 H.264/AAC, 377.4 KB.
- Source unchanged (SHA-256 matched before/after).
- Audio sample test: the added 330 Hz frequency was near zero at 0.05 sec and audible by 0.9 sec; 440 Hz original narration component present before music started.
- Same two-thread settings, static overlay **before:** 6.78 sec and 400.3 MB peak FFmpeg memory; **after:** 1.57 sec and 209.4 MB, with logo still visible near 10.5 sec. This is an environment-specific benchmark, not universal speed or RAM performance.
- High Quality CRF 18: 321.8 KB / SSIM 0.999738; Small File CRF 27: 309.9 KB / SSIM 0.998263, measured against the simple original test clip. Real camera footage will differ.

## Issues found and resolved

| Severity | User-observed gap | Fix | Evidence |
| --- | --- | --- | --- |
| High | Large overlay covered 90.02% of video | Auto-fit to max 25% of output frame | Affected pixel area decreased to 5.80% in reference test |
| High | Static PNG loop wasted CPU and RAM | Decode image once, repeat last frame; limit FFmpeg filter/CPU threads | Linux 960×540 benchmark and final-frame screenshot |
| High | No way to see edits before export | Add on-demand edited still preview | Windows WPF acceptance |
| Medium | New video inherited stale crop/trim | Reset on source change | Windows WPF acceptance |
| Medium | Successful exit code alone did not prove readable output | FFprobe output, duration, audio and even-size checks | Automated exports |
| Medium | No visible export-file actions | Display saved file; Open Video / Open Folder | Windows WPF acceptance |
| Medium | Silent source required manual Mute | Choose Mute automatically | Windows WPF acceptance |
| Medium | Dark checkbox labels lacked contrast | Set light text; add poster preview | Windows WPF screenshot/acceptance |
| Medium | Music synchronization and caption controls missing | Add validated audio offset and simple caption | Positive-offset audio-spectrum and GUI tests |

## Limitations and remaining manual checks

- Windows 10 and specific physical GPU testing (NVIDIA encoding and real hardware memory/power use) remains open. CI runs on GitHub's Windows environment.
- Drag-and-drop and native file dialogs are not operated manually by the automated acceptance suite; it drives WPF controls directly and renders screenshots.
- Multitrack separation requires actual separate source tracks, not removal of already-mixed background music.
- Multi-clip timelines and cross-clip transitions are not part of this simple single-video editor.
- Effects preview is one still frame, not full-speed real-time effect playback. File-size estimates are approximate and may differ widely from actual CRF output.
- Manual listening/visual review, large/long videos (4K and hour-long), security audits, code signing, legal license checks and multiple Windows hardware setups remain open.
- Re-encoding neither removes copyright nor guarantees avoidance of YouTube Content ID. All footage and audio must be properly licensed.

## How to run the Windows tests

`dotnet run --project StudioCut.Tests/StudioCut.Tests.csproj -c Release`

`dotnet run --project StudioCut.GuiTests/StudioCut.GuiTests.csproj -c Release`

Windows CI also checks the built portable executable and the packaged ZIP's content.
