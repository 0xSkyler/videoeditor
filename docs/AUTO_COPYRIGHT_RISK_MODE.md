# Automated copyright-risk review: StudioCut v0.4

## What this feature actually does

The post-export button **Auto: Review & Fix Copyright Risks** makes a new, high-quality video copy. This is a *rights-aware export preset*, not an AI copyright detector or a way to defeat YouTube Content ID.

Editing frame rate, bitrate, colors or metadata does **not** remove copyright protection or guarantee that a platform will accept a video. StudioCut has no offline database of ownership information and does not review visual/audio license documents.

## End-user workflow

1. Import and edit your original, licensed video. Select audio, crop, overlays and caption as needed. Confirm the visual and audio permissions you have.
2. Optionally press **Start Convert** first to create your normal edited result. You can also run the automated review directly on the imported source (useful if the source contains unlicensed audio and a normal export is blocked).
3. The **Auto: Review & Fix Copyright Risks** button is available once a video is imported. When a finished export exists, it uses that latest export; otherwise it uses the imported source.
4. Press it. StudioCut automatically probes the latest exported file and plans a high-quality final MP4:
   - H.264 video and AAC audio when audio is retained, with quality preset **High Quality** (CRF 18).
   - Keeps the current exported video's original frame rate, resolution, duration and visible content as closely as encoding permits; creates a new file.
   - Does not add crops, text overlays, effects, image overlays, or new music in this *second* pass.
   - If rights to all the audio are confirmed, keeps the first audio stream. If audio rights are **not** confirmed, explains that all soundtrack and narration will be removed and explicitly asks permission. The user can cancel.
   - If visual rights are not confirmed, **blocks** this action rather than suggesting transcoding can fix the problem.
5. Open the new `*_auto_reviewed_*.mp4` file and check the sound and picture. Both the original and initial edited output remain available.
6. Before publishing, independently verify licenses and permissions. You may still receive third-party claims.

**Important:** If copyrighted music and your own narration are mixed in one audio stream, the app cannot reliably separate them. Confirm clearance, replace the full soundtrack from separately licensed assets, or re-create the narration without unlicensed music.

## Security, quality and limitations

The feature does not connect to YouTube or a recognition service. It never modifies the source video in place. The user must confirm rights to visuals, and optional audio muting is explicitly consented. The source audio, if already mixed or embedded, cannot be analyzed for legal ownership. Re-encoding can cause minor quality loss; CRF 18 minimizes that tradeoff but cannot guarantee losslessness.

## Test coverage

The Windows automated suites cover the planner's permission/blocking cases, preservation of source settings, final H.264/AAC codecs, separate output filenames, two actual automatic exports (retained audio and muted audio), and an additional real WPF post-export button workflow. A licensed soundtrack is never assumed because the audio file looks technically readable.

The tests *do not* certify copyright clearance, compare against YouTube Content ID, or provide a legal assessment. They also do not replace hands-on mouse/keyboard usability testing on physical Windows hardware.

## Higgsfield AI narration workflow

For videos made entirely with Higgsfield (including text-to-speech narration), select **Higgsfield AI narration: I confirm I can use this voice and any included music**, after checking your project and the applicable provider terms. This is a user-confirmed permission statement. StudioCut does not independently validate a Higgsfield account, a Microsoft voice model, or the underlying licenses.

When Auto mode processes the imported Higgsfield video, it preserves the original audio. When Auto mode processes a previous StudioCut export, it applies the Higgsfield confirmation only when that export retained the original audio without mixing in external music. If external music has been mixed, the general audio-rights confirmation is needed, and it should only be given when the added music is cleared.

Do not remove or distort your own AI-generated narration merely to alter its audio fingerprint. Voice synthesis alone does not prove infringement. For a mistaken YouTube claim, use the platform's dispute process only when you can substantiate your rights. Keep your original Higgsfield project, prompts, download records and the relevant terms as supporting records.

References (verify current terms yourself):
- https://higgsfield.ai/terms-of-use-agreement
- https://learn.microsoft.com/en-us/azure/ai-foundry/responsible-ai/speech-service/text-to-speech/transparency-note
- https://support.google.com/youtube/answer/2797454
