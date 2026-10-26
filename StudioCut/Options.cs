namespace StudioCut;
public sealed class Options
{
    public string Source { get; set; } = "";
    public string Music { get; set; } = "";
    public string Overlay { get; set; } = "";
    public string OutputFolder { get; set; } = "";
    public string Container { get; set; } = "mp4";
    public string Quality { get; set; } = "Balanced";
    public string AudioMode { get; set; } = "Keep original";
    public bool RightsVideo { get; set; }
    public bool RightsAudio { get; set; }
    public bool Hardware { get; set; }
    public bool Normalize { get; set; }
    public bool Denoise { get; set; }
    public bool VoiceEnhance { get; set; }
    public bool AudioFade { get; set; }
    public bool VideoFade { get; set; }
    public bool Watermark { get; set; }
    public double Start { get; set; }
    public double End { get; set; }
    public double Volume { get; set; } = 1;
    public double MusicVolume { get; set; } = .3;
    public double Bass { get; set; }
    public double Treble { get; set; }
    public double Brightness { get; set; }
    public double Contrast { get; set; } = 1;
    public double Saturation { get; set; } = 1;
    public double Offset { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int Fps { get; set; }
    public int CropX { get; set; }
    public int CropY { get; set; }
    public int CropW { get; set; }
    public int CropH { get; set; }
    public string Text { get; set; } = "";
    public int OriginalAudioTrack { get; set; }
}
public sealed class MediaDetails
{
    public double Duration { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public double Fps { get; set; }
    public int AudioTracks { get; set; }
    public long Size { get; set; }
}
