using System.IO;

namespace StudioCut;

/// <summary>
/// Consistent, offline, high-quality conversion settings with no user-entered
/// controls. Neither these settings nor re-encoding circumvent copyright or AI
/// content detection; they simply produce a portable MP4.
/// </summary>
public static class SimpleConvertPlan
{
    public static Options Create(string source, string destination, MediaDetails media)
    {
        if (media is null || media.Width <= 0 || media.Height <= 0 || media.Duration <= 0)
            throw new ArgumentException("Import a valid video before converting.", nameof(media));
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
            throw new FileNotFoundException("The selected video was not found.", source);
        if (string.IsNullOrWhiteSpace(destination) || !Directory.Exists(destination))
            throw new DirectoryNotFoundException("The automatic output folder is unavailable.");

        // Make only a tiny color correction; do not rearrange time, alter the
        // audio waveform or add effects that would change the user's work.
        return new Options
        {
            Source = source,
            OutputFolder = destination,
            Container = "mp4",
            Quality = "High Quality",
            AudioMode = media.AudioTracks > 0 ? "Keep original" : "Mute all",
            KeepAllAudioTracks = media.AudioTracks > 1,
            NoRightsPrompt = true,
            PreserveSourceMetadata = true,
            Contrast = 1.01,
            Saturation = 1.01,
            Volume = 1,
            Start = 0,
            End = 0,
            Width = 0,
            Height = 0,
            Fps = 0,
            Hardware = false
        };
    }

    public static string ResolveOutputFolder()
    {
        string[] roots = {
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };
        foreach (string root in roots)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;
            try
            {
                string folder = Path.Combine(root, "StudioCut Exports");
                Directory.CreateDirectory(folder);
                return folder;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        throw new IOException("Could not create StudioCut Exports in Videos, Documents or your user folder.");
    }
}
