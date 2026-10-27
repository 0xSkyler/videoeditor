using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace StudioCut;
public static class MediaEngine
{
    static readonly CultureInfo C = CultureInfo.InvariantCulture;
    public static string F(double d) => d.ToString("0.###", C);
    public static string Tool(string name)
    {
        string local = Path.Combine(AppContext.BaseDirectory, "tools", name + ".exe");
        if (File.Exists(local)) return local;
        return name + ".exe";
    }
    public static async Task<MediaDetails> ProbeAsync(string file, CancellationToken token = default)
    {
        var psi = new ProcessStartInfo(Tool("ffprobe")) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in new[] { "-v", "error", "-show_streams", "-show_format", "-of", "json", file }) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new IOException("Cannot start ffprobe. Install FFmpeg or use the bundled release.");
        string data = await p.StandardOutput.ReadToEndAsync(token);
        string error = await p.StandardError.ReadToEndAsync(token);
        await p.WaitForExitAsync(token);
        if (p.ExitCode != 0) throw new IOException("Cannot read media: " + error);
        using var doc = JsonDocument.Parse(data);
        var d = new MediaDetails { Size = new FileInfo(file).Length };
        double duration = 0;
        if (doc.RootElement.TryGetProperty("format", out var fmt) && fmt.TryGetProperty("duration", out var dur)) double.TryParse(dur.GetString(), NumberStyles.Any, C, out duration);
        d.Duration = duration;
        foreach (var s in doc.RootElement.GetProperty("streams").EnumerateArray())
        {
            var kind = s.GetProperty("codec_type").GetString();
            if (kind == "audio")
            {
                d.AudioTracks++;
                if (d.AudioCodec.Length == 0 && s.TryGetProperty("codec_name", out var audioCodec))
                    d.AudioCodec = audioCodec.GetString() ?? "";
            }
            if (kind != "video" || d.Width != 0) continue;
            if (s.TryGetProperty("codec_name", out var codec))
                d.VideoCodec = codec.GetString() ?? "";
            d.Width = s.GetProperty("width").GetInt32(); d.Height = s.GetProperty("height").GetInt32();
            if (s.TryGetProperty("avg_frame_rate", out var fps))
            {
                var parts = (fps.GetString() ?? "0/1").Split('/');
                if (parts.Length == 2 && double.TryParse(parts[0], NumberStyles.Any, C, out var n) && double.TryParse(parts[1], NumberStyles.Any, C, out var den) && den != 0) d.Fps = n / den;
            }
        }
        if (d.Width <= 0 || d.Height <= 0 || d.Duration <= 0) throw new IOException("A valid video stream and duration are required.");
        return d;
    }
    public static IReadOnlyList<string> Validate(Options o, MediaDetails? media)
    {
        var errors = new List<string>();
        if (!File.Exists(o.Source)) errors.Add("Select a readable source video.");
        if (media == null) errors.Add("Probe the source video before exporting.");
        if (!o.RightsVideo) errors.Add("Confirm permission to use the video visuals.");
        if (o.AudioMode != "Mute all" && !o.RightsAudio) errors.Add("Confirm rights to every included audio track/effect.");
        if ((o.AudioMode is "Replace with music" or "Mix music") && !File.Exists(o.Music)) errors.Add("Select a valid licensed music file.");
        if (o.Watermark && !File.Exists(o.Overlay)) errors.Add("Select a valid image overlay.");
        if (o.AudioMode is not ("Mute all" or "Keep original" or "Replace with music" or "Mix music")) errors.Add("Invalid audio mode.");
        if (o.Container is not ("mp4" or "mov" or "mkv")) errors.Add("Invalid output format.");
        if (o.Quality is not ("High Quality" or "Balanced" or "Small File")) errors.Add("Invalid quality preset.");
        if (string.IsNullOrWhiteSpace(o.OutputFolder) || !Directory.Exists(o.OutputFolder)) errors.Add("Choose an existing output folder.");
        if (media != null)
        {
            if (!double.IsFinite(o.Start) || o.Start < 0 || o.Start >= media.Duration) errors.Add("Trim start must be within the video.");
            if (o.End > 0 && (o.End <= o.Start || o.End > media.Duration + .05)) errors.Add("Trim end must follow the start and fit the video.");
            if (o.OriginalAudioTrack < 0 || (media.AudioTracks > 0 && o.OriginalAudioTrack >= media.AudioTracks)) errors.Add("Select an existing audio track.");
            if ((o.AudioMode is "Keep original" or "Mix music") && media.AudioTracks == 0) errors.Add("Source has no audio; use Mute or Replace.");
            if (o.CropW != 0 || o.CropH != 0 || o.CropX != 0 || o.CropY != 0)
            {
                if (o.CropW <= 0 || o.CropH <= 0 || o.CropX < 0 || o.CropY < 0 || o.CropX + o.CropW > media.Width || o.CropY + o.CropH > media.Height) errors.Add("Crop rectangle must fit inside source dimensions.");
            }
        }
        if (!double.IsFinite(o.Offset) || Math.Abs(o.Offset) > 60) errors.Add("Music offset must be between -60 and +60 seconds.");
        if (o.Text.Length > 72 || !Regex.IsMatch(o.Text, @"^[\p{L}\p{N} .,!?+()@#-]*$"))
            errors.Add("Text overlay must be one line (up to 72 characters); only letters, numbers and simple punctuation are supported.");
        if (o.Width < 0 || o.Height < 0 || o.Fps < 0 || o.Width > 7680 || o.Height > 4320 || o.Fps > 120
            || (o.Width != 0 && (o.Width < 16 || o.Width % 2 != 0))
            || (o.Height != 0 && (o.Height < 16 || o.Height % 2 != 0)))
            errors.Add("Resolution must use even dimensions (16 to 7680 wide, 16 to 4320 high); FPS is 0 or 1–120.");
        if (!double.IsFinite(o.Volume) || !double.IsFinite(o.MusicVolume) || !double.IsFinite(o.Contrast) || !double.IsFinite(o.Saturation) || !double.IsFinite(o.Brightness) || !double.IsFinite(o.Start) || !double.IsFinite(o.End) || !double.IsFinite(o.Bass) || !double.IsFinite(o.Treble) || o.Volume is < 0 or > 3 || o.MusicVolume is < 0 or > 3 || o.Contrast is < 0 or > 3 || o.Saturation is < 0 or > 3 || o.Brightness is < -1 or > 1 || o.Bass is < -20 or > 20 || o.Treble is < -20 or > 20) errors.Add("Audio/color value outside supported range.");
        if (Directory.Exists(o.OutputFolder) && File.Exists(o.Source))
        {
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(o.OutputFolder))!);
                if (drive.IsReady && drive.AvailableFreeSpace < Math.Max(200L * 1024 * 1024, new FileInfo(o.Source).Length / 4)) errors.Add("Insufficient free disk space for a safe export estimate.");
            }
            catch (Exception ex) when (ex is IOException or ArgumentException) { errors.Add("Cannot determine output disk space."); }
        }
        return errors;
    }
    static string FilterAudio(Options o, double duration, bool original)
    {
        var filters = new List<string>();
        if (original)
        {
            filters.Add($"volume={F(o.Volume)}");
            if (o.Bass != 0) filters.Add($"bass=g={F(o.Bass)}");
            if (o.Treble != 0) filters.Add($"treble=g={F(o.Treble)}");
            if (o.Denoise) filters.Add("afftdn=nf=-25");
            if (o.VoiceEnhance) filters.Add("highpass=f=100,lowpass=f=9000,acompressor=threshold=0.1:ratio=3:attack=10:release=180");
        }
        else filters.Add($"volume={F(o.MusicVolume)}");
        if (o.Normalize) filters.Add("loudnorm=I=-16:TP=-1.5:LRA=11");
        if (o.AudioFade && duration > .5)
        {
            filters.Add($"afade=t=in:st=0:d={F(Math.Min(1, duration / 3))}");
            filters.Add($"afade=t=out:st={F(Math.Max(0,duration-1))}:d={F(Math.Min(1,duration))}");
        }
        return string.Join(',', filters);
    }
    public static List<string> Arguments(Options o, MediaDetails d, string target, bool gpu)
    {
        double duration = (o.End > 0 ? o.End : d.Duration) - o.Start;
        var args = new List<string> { "-hide_banner", "-n", "-progress", "pipe:1", "-nostats", "-ss", F(o.Start), "-i", o.Source };
        bool music = o.AudioMode is "Mix music" or "Replace with music";
        if (music) { args.AddRange(new[] { "-stream_loop", "-1", "-i", o.Music }); }
        int overlayIdx = music ? 2 : 1;
        // A static image is decoded once. overlay=repeatlast keeps it visible to the end.
        if (o.Watermark) args.AddRange(new[] { "-i", o.Overlay });
        var videoFilters = new List<string>();
        if (o.CropW > 0 && o.CropH > 0) videoFilters.Add($"crop={o.CropW}:{o.CropH}:{o.CropX}:{o.CropY}:exact=1");
        if (o.Width > 0 || o.Height > 0)
        {
            int w = o.Width > 0 ? o.Width : -2;
            int h = o.Height > 0 ? o.Height : -2;
            videoFilters.Add($"scale={w}:{h}");
        }
        if (o.Width == 0 && o.Height == 0) videoFilters.Add("pad=ceil(iw/2)*2:ceil(ih/2)*2");
        if (o.Brightness != 0 || o.Contrast != 1 || o.Saturation != 1) videoFilters.Add($"eq=brightness={F(o.Brightness)}:contrast={F(o.Contrast)}:saturation={F(o.Saturation)}");
        if (!string.IsNullOrWhiteSpace(o.Text))
        {
            double baseH = o.CropH > 0 ? o.CropH : d.Height;
            double baseW = o.CropW > 0 ? o.CropW : d.Width;
            double finalH = o.Height > 0 ? o.Height : o.Width > 0 ? o.Width * baseH / baseW : baseH;
            int textSize = Math.Clamp((int)Math.Round(finalH / 28), 14, 44);
            // Validated single-line text is safe to include in the filter graph.
            videoFilters.Add($"drawtext=text='{o.Text}':fontcolor=white:fontsize={textSize}:box=1:boxcolor=black@0.6:boxborderw=6:x=20:y=h-text_h-24");
        }
        if (o.VideoFade && duration > .5) { videoFilters.Add($"fade=t=in:st=0:d={F(Math.Min(1,duration/3))}"); videoFilters.Add($"fade=t=out:st={F(Math.Max(0,duration-1))}:d={F(Math.Min(1,duration))}"); }
        if (o.Fps > 0) videoFilters.Add($"fps={o.Fps}");
        var chains = new List<string>();
        string vf = videoFilters.Count > 0 ? string.Join(',',videoFilters) : "null";
        chains.Add($"[0:v:0]{vf}[vbase]");
        if (o.Watermark)
        {
            // Keep large photographs/logos as a small corner overlay. Never upscale a small logo.
            double cropW = o.CropW > 0 ? o.CropW : d.Width;
            double cropH = o.CropH > 0 ? o.CropH : d.Height;
            double finalW = o.Width > 0 ? o.Width : (o.Height > 0 ? o.Height * cropW / cropH : cropW);
            double finalH = o.Height > 0 ? o.Height : (o.Width > 0 ? o.Width * cropH / cropW : cropH);
            int maxW = Math.Max(16, (int)Math.Round(finalW * .25));
            int maxH = Math.Max(16, (int)Math.Round(finalH * .25));
            chains.Add($"[{overlayIdx}:v:0]scale=w='min(iw,{maxW})':h='min(ih,{maxH})':force_original_aspect_ratio=decrease[logo]");
            chains.Add("[vbase][logo]overlay=W-w-16:H-h-16:shortest=0:repeatlast=1:eof_action=repeat[vout]");
        }
        else chains.Add("[vbase]null[vout]");
        args.AddRange(new[] { "-map", "[vout]" });
        // A positive offset delays background music. A negative offset skips that
        // much of its intro before mixing/replacing; narration remains aligned.
        string musicTrim = o.Offset < 0
            ? $"atrim=start={F(-o.Offset)}:duration={F(duration)}"
            : $"atrim=duration={F(duration)}";
        string musicDelay = o.Offset > 0 ? $",adelay={F(Math.Round(o.Offset * 1000))}:all=1" : "";
        string musicChain = $"[1:a:0]{musicTrim},asetpts=PTS-STARTPTS,{FilterAudio(o,duration,false)}{musicDelay},apad,atrim=duration={F(duration)},asetpts=PTS-STARTPTS";
        if (o.AudioMode == "Keep original")
        {
            chains.Add($"[0:a:{o.OriginalAudioTrack}]{FilterAudio(o,duration,true)}[aout]");
            args.AddRange(new[] { "-map", "[aout]" });
        }
        else if (o.AudioMode == "Replace with music")
        {
            chains.Add(musicChain + "[aout]");
            args.AddRange(new[] { "-map", "[aout]" });
        }
        else if (o.AudioMode == "Mix music")
        {
            chains.Add($"[0:a:{o.OriginalAudioTrack}]{FilterAudio(o,duration,true)}[orig]");
            chains.Add(musicChain + "[bg]");
            chains.Add("[orig][bg]amix=inputs=2:duration=first:dropout_transition=2[aout]");
            args.AddRange(new[] { "-map", "[aout]" });
        }
        args.AddRange(new[] { "-filter_complex_threads", "2", "-filter_complex", string.Join(';',chains), "-c:v", gpu ? "h264_nvenc" : "libx264" });
        if (gpu) args.AddRange(new[] { "-cq", o.Quality == "High Quality" ? "19" : o.Quality == "Balanced" ? "23" : "28", "-preset", "p5" });
        else args.AddRange(new[] { "-crf", o.Quality == "High Quality" ? "18" : o.Quality == "Balanced" ? "21" : "27", "-preset", "medium", "-threads", "2" });
        args.AddRange(new[] { "-pix_fmt", "yuv420p", "-t", F(duration) });
        if (o.AudioMode == "Mute all") args.Add("-an"); else args.AddRange(new[] { "-c:a", "aac", "-b:a", "192k" });
        if (o.Container == "mp4" || o.Container == "mov") args.AddRange(new[] { "-movflags", "+faststart" });
        args.AddRange(new[] { "-map_metadata", "-1", "-map_chapters", "-1", target });
        return args;
    }
    public static async Task RunAsync(string executable, IReadOnlyList<string> arguments, Action<double> onProgress, double duration, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var psi = new ProcessStartInfo(executable) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var s in arguments) psi.ArgumentList.Add(s);
        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        if (!p.Start()) throw new IOException("Could not start FFmpeg.");
        using var registration = token.Register(() => { try { if (!p.HasExited) p.Kill(true); } catch { } });
        var error = new StringBuilder();
        var readErrors = Task.Run(async () => { while (await p.StandardError.ReadLineAsync() is { } line) { if (error.Length > 16000) error.Remove(0, 8000); error.AppendLine(line); } });
        while (await p.StandardOutput.ReadLineAsync(token) is { } line)
        {
            if (line.StartsWith("out_time_us=", StringComparison.Ordinal) && long.TryParse(line[12..], out long us)) onProgress(Math.Clamp(us / 1e6 / duration, 0, 1));
            else if (line.StartsWith("out_time_ms=", StringComparison.Ordinal) && long.TryParse(line[12..], out long ms)) onProgress(Math.Clamp(ms / 1e6 / duration, 0, 1));
        }
        await p.WaitForExitAsync(token); await readErrors;
        if (p.ExitCode != 0) throw new IOException("FFmpeg failed:\n" + error);
        onProgress(1);
    }
    static async Task EnsureStreamAsync(string file, string kind, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        // Bound external probes to prevent a malformed media asset from freezing export.
        var psi = new ProcessStartInfo(Tool("ffprobe"))
        {
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true
        };
        foreach (var arg in new[] { "-v", "error", "-select_streams", kind == "audio" ? "a:0" : "v:0", "-read_intervals", "%+1", "-show_frames", "-show_entries", "frame=media_type", "-of", "csv=p=0", file })
            psi.ArgumentList.Add(arg);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        using var p = Process.Start(psi) ?? throw new IOException("Could not start ffprobe to check selected media.");
        using var registration = deadline.Token.Register(() => { try { if (!p.HasExited) p.Kill(true); } catch { } });
        try
        {
            Task<string> outputTask = p.StandardOutput.ReadToEndAsync(deadline.Token);
            Task<string> errorTask = p.StandardError.ReadToEndAsync(deadline.Token);
            await p.WaitForExitAsync(deadline.Token);
            string output = await outputTask;
            string err = await errorTask;
            if (p.ExitCode != 0 || !output.Split('\n', StringSplitOptions.TrimEntries).Contains(kind))
                throw new InvalidOperationException($"Selected {kind} asset is not a readable {kind} stream: {Path.GetFileName(file)}. {err.Trim()}");
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new InvalidOperationException($"Timed out verifying the selected {kind} asset: {Path.GetFileName(file)}");
        }
    }

    /// <summary>Returns the absolute path of the successfully encoded output.</summary>
    /// <summary>Generate one lightweight frame for an immediate, offline preview poster.</summary>
    public static async Task<byte[]> GeneratePosterAsync(string file, CancellationToken token = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(12));
        var psi = new ProcessStartInfo(Tool("ffmpeg"))
        {
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true
        };
        foreach (var a in new[]
        {
            "-hide_banner", "-loglevel", "error", "-ss", "0.1", "-i", file,
            "-vf", "scale=w='min(iw,640)':h='min(ih,360)':force_original_aspect_ratio=decrease",
            "-frames:v", "1", "-vcodec", "png", "-f", "image2pipe", "pipe:1"
        }) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new IOException("Could not start FFmpeg preview.");
        using var cancellation = deadline.Token.Register(() => { try { if (!p.HasExited) p.Kill(true); } catch { } });
        try
        {
            using var memory = new MemoryStream();
            var err = p.StandardError.ReadToEndAsync(deadline.Token);
            await p.StandardOutput.BaseStream.CopyToAsync(memory, deadline.Token);
            await p.WaitForExitAsync(deadline.Token);
            if (p.ExitCode != 0 || memory.Length < 64)
                throw new IOException("Cannot generate video preview: " + await err);
            return memory.ToArray();
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new IOException("Preview image generation timed out; export is still available.");
        }
    }


    /// <summary>Render a single still with the currently selected video effects; no persistent export.</summary>
    public static async Task<byte[]> GenerateEditedPreviewAsync(Options input, MediaDetails source, double atSeconds, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (!double.IsFinite(atSeconds)) throw new ArgumentException("Preview time must be a valid number.");
        double frameAt = Math.Clamp(atSeconds, 0, Math.Max(0, source.Duration - .45));
        double frameEnd = Math.Min(source.Duration, frameAt + .8);
        if (frameEnd - frameAt < .15) throw new InvalidOperationException("Video too short to make a frame preview.");
        string tempDirectory = Path.Combine(Path.GetTempPath(), "StudioCut-preview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var visual = new Options
        {
            Source = input.Source, OutputFolder = tempDirectory, RightsVideo = true, RightsAudio = true,
            AudioMode = "Mute all", Container = "mp4", Quality = "Small File",
            Start = frameAt, End = frameEnd,
            CropX = input.CropX, CropY = input.CropY, CropW = input.CropW, CropH = input.CropH,
            Width = input.Width, Height = input.Height, Fps = input.Fps,
            Brightness = input.Brightness, Contrast = input.Contrast, Saturation = input.Saturation,
            Watermark = input.Watermark, Overlay = input.Overlay, Text = input.Text,
            // This preview is a still frame, not a transition/fade animation.
            VideoFade = false
        };
        try
        {
            string clip = await ExportAsync(visual, source, _ => { }, token);
            return await GeneratePosterAsync(clip, token);
        }
        finally
        {
            try { Directory.Delete(tempDirectory, recursive: true); }
            catch (IOException) { /* Temp cleanup may finish after an antivirus scan. */ }
            catch (UnauthorizedAccessException) { }
        }
    }

    public static Task CheckAudioAsync(string file, CancellationToken token = default) => EnsureStreamAsync(file, "audio", token);
    public static Task CheckImageAsync(string file, CancellationToken token = default) => EnsureStreamAsync(file, "video", token);

    /// <summary>Quick indicative CRF file-size estimate, not an output guarantee.</summary>
    public static long EstimateBytes(Options o, MediaDetails d)
    {
        double videoWidth = o.Width > 0 ? o.Width : o.Height > 0 ? o.Height * (double)(o.CropW > 0 ? o.CropW : d.Width) / (o.CropH > 0 ? o.CropH : d.Height) : (o.CropW > 0 ? o.CropW : d.Width);
        double videoHeight = o.Height > 0 ? o.Height : o.Width > 0 ? o.Width * (double)(o.CropH > 0 ? o.CropH : d.Height) / (o.CropW > 0 ? o.CropW : d.Width) : (o.CropH > 0 ? o.CropH : d.Height);
        double fps = o.Fps > 0 ? o.Fps : d.Fps > 0 ? d.Fps : 30;
        double duration = Math.Max(.1, (o.End > 0 ? o.End : d.Duration) - o.Start);
        double bpp = o.Quality == "High Quality" ? .16 : o.Quality == "Small File" ? .06 : .10;
        double videoBitsPerSecond = Math.Clamp(videoWidth * videoHeight * fps * bpp, 250000, 30000000);
        double audioBitsPerSecond = o.AudioMode == "Mute all" ? 0 : 192000;
        return (long)((videoBitsPerSecond + audioBitsPerSecond) * duration / 8 * 1.03);
    }

    public static async Task<string> ExportAsync(Options o, MediaDetails details, Action<double> progress, CancellationToken ct, string outputTag = "edited")
    {
        ct.ThrowIfCancellationRequested();
        var errors = Validate(o, details);
        if (errors.Count != 0) throw new InvalidOperationException(string.Join("\n", errors));
        if (o.AudioMode is "Replace with music" or "Mix music") await EnsureStreamAsync(o.Music, "audio", ct);
        if (o.Watermark) await EnsureStreamAsync(o.Overlay, "video", ct);
        string name = Path.GetFileNameWithoutExtension(o.Source);
        // A reviewed export should have a short, recognizable name, rather than
        // recursively appending its previous full timestamp and GUID.
        if (outputTag == "auto_reviewed")
            name = Regex.Replace(name, @"_edited_\d{8}_\d{6}_\d{3}_[0-9a-fA-F]{32}$", "", RegexOptions.IgnoreCase);
        if (name.Length > 80) name = name[..80];
        string suffix = outputTag == "auto_reviewed" ? "auto_reviewed" : "edited";
        string output = Path.Combine(o.OutputFolder, $"{name}_{suffix}_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}.{o.Container}");
        if (Path.GetFullPath(output).Equals(Path.GetFullPath(o.Source), StringComparison.OrdinalIgnoreCase)) throw new IOException("Output cannot overwrite the input.");
        double duration = (o.End > 0 ? o.End : details.Duration) - o.Start;
        try
        {
            try
            {
                await RunAsync(Tool("ffmpeg"), Arguments(o, details, output, o.Hardware), progress, duration, ct);
            }
            catch (IOException) when (o.Hardware && !ct.IsCancellationRequested)
            {
                if (File.Exists(output)) File.Delete(output);
                await RunAsync(Tool("ffmpeg"), Arguments(o, details, output, false), progress, duration, ct);
            }
            if (!File.Exists(output) || new FileInfo(output).Length < 1024)
                throw new IOException("The output file was not created correctly.");
            // A successful FFmpeg exit code alone is insufficient: verify the written container.
            var result = await ProbeAsync(output, ct);
            bool audioWanted = o.AudioMode != "Mute all";
            if (result.AudioTracks == 0 && audioWanted)
                throw new IOException("The exported file is missing its expected audio.");
            if (result.AudioTracks != 0 && !audioWanted)
                throw new IOException("The exported file unexpectedly contains audio.");
            if (Math.Abs(result.Duration - duration) > Math.Max(.6, duration * .02))
                throw new IOException("The exported video duration does not match the requested trim.");
            if (result.Width % 2 != 0 || result.Height % 2 != 0)
                throw new IOException("The exported H.264 dimensions are not supported.");
            if (!string.Equals(result.VideoCodec, "h264", StringComparison.OrdinalIgnoreCase))
                throw new IOException("The exported video does not contain the expected H.264 stream.");
            if (audioWanted && !string.Equals(result.AudioCodec, "aac", StringComparison.OrdinalIgnoreCase))
                throw new IOException("The exported audio is not encoded as AAC.");
            return output;
        }
        catch
        {
            try { if (File.Exists(output)) File.Delete(output); } catch (IOException) { }
            throw;
        }
    }
}
