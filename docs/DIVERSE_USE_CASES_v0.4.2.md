# StudioCut v0.4.2 — Windows Diverse-Use-Case Verification

**Date:** 27 October 2026  
**System:** GitHub Actions windows-latest; .NET 8; pinned FFmpeg; Windows automated WPF tests.  
**Verified run:** https://github.com/0xSkyler/videoeditor/actions/runs/40460105673

## Results

| Test category | Outcome |
| --- | --- |
| Diverse scenarios | **14/14 passed** |
| Diverse scenario assertions | **94 passed** |
| Scenario execution time (small synthetic media) | **9.74 seconds** |
| Existing engine assertions | **110 passed** |
| Existing engine exports | **7 standard + 2 auto-reviewed** |
| Automated WPF GUI assertions | **95 passed** |
| GUI-triggered export cases | **6** |
| Portable executable startup | **Passed** |
| Release package extraction and binary presence | **Passed** |
| Windows packaged-app working set at startup test | **193.3 MB** |

## User-oriented use cases

| Use case | Result | Meaning |
| --- | --- | --- |
| Original Higgsfield-style TTS narration | PASS | Retained synthetic speech after high-quality re-encoding; frequency test found original 440-Hz test tone |
| Audio rights uncertain | PASS | Creates a silent copy only as an explicitly selected option; no false guarantee of clearance |
| Visual rights unconfirmed | PASS | Auto plan blocks the conversion |
| Narration and background music already mixed together | PASS | Both source audio components remain in the same mix; software does not promise to separate ownership |
| Silent AI video | PASS | Output stays silent |
| Portrait 360×640 at 25 FPS | PASS | Keeps orientation, dimensions and frame rate |
| Landscape 1280×720 at 29.97 FPS | PASS | Keeps HD resolution and fractional frame rate |
| Two distinct approved audio language tracks | PASS | Both audio streams are retained; **fixed from v0.4.1** |
| Legacy AVI MPEG4 video + PCM audio | PASS | Outputs portable MP4 H.264/AAC |
| Two consecutive exports | PASS | Unique output filenames; no overwriting |
| Missing video, corrupt video, audio-only file | PASS | All rejected as invalid source video |
| Cancellation before export starts | PASS | No export is produced |
| Cancellation during a longer encode | PASS | Cancellation reported after actual encoder progress; incomplete file cleaned up |
| Original source preservation | PASS | Source SHA-256 matched before and after testing |

## Additional GUI rights checks

Automated Windows GUI tests confirmed that explicitly approving Higgsfield-generated narration allows the original voice to be retained; that confirmation **does not** authorize separately added external music. The editor requests separate audio rights for mixed music. Testing also verifies selection/rights reset on new import and previews, file opening, actual exports, and audio consistency.

## Scope and important caveats

These tests use local **synthetic audio and video fixtures**, not a connected Higgsfield account. They establish encode correctness and observable behavior, not voice rights, ownership or YouTube approval. The test tones are not human speech, so they do not establish intelligibility of arbitrary Microsoft/Higgsfield TTS voices. AI narration does not inherently create a copyright claim. Re-encoding and codec, bitrate or frame-rate changes do **not** bypass Content ID or confer licensing.

The GUI tests drive WPF components in a hosted Windows test runner; they are not manual mouse/keyboard sessions on a personal computer. Full 4K/hour-long stress testing, NVIDIA hardware encode testing on physical GPUs, malformed video fuzzing, commercial voice-licensing verification, YouTube Content ID submissions, multilingual language-label metadata retention and subjective listening tests remain outside verified scope.

**Raw test results:** Download the `StudioCut-diverse-scenario-evidence` artifact from the verified run. It includes `results.json`, `report.md`, and scenario test logs.
