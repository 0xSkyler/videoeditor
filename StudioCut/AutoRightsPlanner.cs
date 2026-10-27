using System.IO;

namespace StudioCut;

/// <summary>
/// Offline rights-readiness plan. This is NOT a copyright detector, clearance service,
/// Content ID bypass or a guarantee that a platform will accept the result.
/// </summary>
public static class AutoRightsPlanner
{
    public static AutoRightsPlan Create(Options requested, MediaDetails? source)
    {
        var blockers = new List<string>();
        var adjustments = new List<string>();
        var limitations = new List<string>
        {
            "No technical edit can remove another person's copyright or guarantee YouTube Content ID clearance.",
            "Offline StudioCut cannot identify ownership, search Content ID databases or verify uploaded license documents.",
            "A positive rights confirmation is your statement, not an independently verified license."
        };

        if (!requested.RightsVideo)
            blockers.Add("Visual rights are unconfirmed. License or replace the footage before using Auto mode.");
        if (source == null || source.Width < 1 || source.Height < 1 || source.Duration <= 0)
            blockers.Add("Open a readable video so the editor can inspect its streams.");
        if (string.IsNullOrWhiteSpace(requested.Source) || !File.Exists(requested.Source))
            blockers.Add("The selected source or exported video is missing.");
        if (string.IsNullOrWhiteSpace(requested.OutputFolder) || !Directory.Exists(requested.OutputFolder))
            blockers.Add("Choose a writable output folder.");

        if (blockers.Count != 0)
            return new AutoRightsPlan(null, blockers, adjustments, limitations, false);

        // Never silently inherit creative filters, crop, frame-rate changes or third-party assets.
        // Preserving the footage is the goal; those changes cannot clear copyright claims.
        bool hasSoundtrack = source!.AudioTracks > 0;
        bool keepSoundtrack = hasSoundtrack && requested.RightsAudio;
        var optimized = new Options
        {
            Source = requested.Source,
            OutputFolder = requested.OutputFolder,
            Container = "mp4",
            Quality = "High Quality",
            AudioMode = keepSoundtrack ? "Keep original" : "Mute all",
            RightsVideo = true,
            RightsAudio = keepSoundtrack,
            OriginalAudioTrack = 0,
            Width = 0, Height = 0, Fps = 0,
            Start = 0, End = 0,
            Volume = 1,
            MusicVolume = .3,
            Contrast = 1, Saturation = 1, Brightness = 0,
            Hardware = false
        };
        adjustments.Add("Make a new high-quality MP4 (H.264 video; AAC audio only when audio is retained).");
        adjustments.Add("Preserve the video frame rate, picture dimensions, duration and picture content.");
        adjustments.Add("Remove optional captions, overlaid images, crops and cosmetic changes from this pass; it does not undo edits already baked into the selected video.");
        adjustments.Add("Use quality-based encoding. Bitrate changes, metadata removal and re-encoding do NOT confer usage rights.");
        if (!hasSoundtrack)
            adjustments.Add("Input has no audio; the final MP4 will remain silent.");
        else if (!keepSoundtrack)
            adjustments.Add("All source audio (including any narration) will be muted because audio rights are not confirmed.");
        else
            adjustments.Add("Keep the first audio track because you confirmed rights to all of its content.");
        if (source.AudioTracks > 1 && keepSoundtrack)
            limitations.Add("Only the first source audio track is kept; export additional tracks manually if required.");

        var validation = MediaEngine.Validate(optimized, source);
        if (validation.Count != 0)
            blockers.AddRange(validation);

        return new AutoRightsPlan(blockers.Count == 0 ? optimized : null,
            blockers, adjustments, limitations, hasSoundtrack && !keepSoundtrack);
    }
}

public sealed class AutoRightsPlan
{
    public AutoRightsPlan(Options? export, IReadOnlyList<string> blockers,
        IReadOnlyList<string> adjustments, IReadOnlyList<string> limitations,
        bool removesUnconfirmedAudio)
    {
        Export = export;
        Blockers = blockers;
        Adjustments = adjustments;
        Limitations = limitations;
        RemovesUnconfirmedAudio = removesUnconfirmedAudio;
    }

    public Options? Export { get; }
    public IReadOnlyList<string> Blockers { get; }
    public IReadOnlyList<string> Adjustments { get; }
    public IReadOnlyList<string> Limitations { get; }
    public bool RemovesUnconfirmedAudio { get; }
    public bool CanExport => Export != null && Blockers.Count == 0;
    public string Summary => string.Join(Environment.NewLine, Adjustments);
}
