# StudioCut v0.5.0 — Simplified conversion specification

## Product experience

**Input → Convert → Output.** Users drag a local video onto the application or choose it in the native file picker. The UI identifies the imported video, displays size/FPS/runtime/audio track count and optionally shows an image preview. There is one prominent **Convert** action and a progress bar. There are **no manual effect or export settings**. Cancellation and opening the completed output folder remain as utility actions.

Exports automatically appear in `Videos\StudioCut Exports`. On machines without a writable Videos folder, the app uses Documents or the user home folder. Each file has a unique `_converted_` suffix.

## One-click processing

- Convert to MP4/H.264 high-quality CRF 18 (8-bit yuv420p), AAC 192 kbps when audio exists.
- Keep all source audio streams, including generated narration and alternative languages; leave silent videos silent.
- Keep source duration, framerate and picture dimensions when the target supports them. H.264 requires even dimensions, so an odd-size original might gain a one-pixel pad.
- Apply a very small 1% contrast/saturation correction. This is a style/encoding choice, **not** a Content ID evasion measure.
- Keep source metadata where reasonably possible rather than intentionally stripping attribution or AI provenance.
- Encode locally; no cloud or video uploads. FFmpeg validates the container and audio/video stream properties afterward.
- Never modify or overwrite the imported file.
- Do not assume rights: the app's simple UI does not check licenses, so users must assess Higgsfield licensing and any additional source music themselves.

## Why this does not guarantee YouTube clearance

AI-generated content may be completely licensed and still be subject to mistaken matching; conversely, changing codec/FPS/bitrate does not remove any underlying intellectual property. StudioCut does **not** detect or bypass Content ID or guarantee a particular AI-authorship classifier result. For a disputed claim, retain the original Higgsfield project/voice terms and use YouTube's legitimate dispute process with appropriate evidence.

## Testing

Three layers of automation are retained: FFmpeg engine tests, diverse synthetic-format conversions, and Windows WPF GUI tests. The v0.5 GUI test asserts no editable option widgets exist and drives five real button-triggered MP4 exports (narrated, repeated, silent, portrait and dual-audio), a rejected corrupt-file import, source integrity and automatic output folder handling. CI also verifies packaged application startup and ZIP completeness.

Manual mouse-and-keyboard testing on a physical Windows machine, performance of long/HDR/4K exports, and direct Higgsfield media/YouTube Content ID submission are **not** proven by this workflow.
