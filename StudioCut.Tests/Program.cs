using System.Diagnostics;
using System.Globalization;
using StudioCut;

internal static class Program
{
    private static int assertions, exports;

    private static void Check(bool value, string message)
    {
        assertions++;
        if (!value) throw new Exception($"Assertion {assertions} failed: {message}");
    }

    private static async Task Cmd(string executable, params string[] args)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, RedirectStandardError = true,
            RedirectStandardOutput = true, CreateNoWindow = true
        };
        foreach (string arg in args) start.ArgumentList.Add(arg);
        using var p = Process.Start(start) ?? throw new Exception($"Cannot start {executable}");
        var error = p.StandardError.ReadToEndAsync();
        var stdout = p.StandardOutput.ReadToEndAsync();
        await p.WaitForExitAsync();
        var stderr = await error;
        await stdout;
        if (p.ExitCode != 0) throw new Exception($"{executable} failed: {stderr}");
    }

    private static Options NewOptions(string source, string folder) => new()
    {
        Source = source, OutputFolder = folder, RightsVideo = true, RightsAudio = true
    };

    private static void Invalid(Options o, MediaDetails d, string message)
    {
        var errs = MediaEngine.Validate(o, d);
        Check(errs.Count > 0, message + " was not rejected");
    }

    private static async Task<MediaDetails> ExportVerify(Options options, MediaDetails source,
        string expectedExt, bool expectAudio, double? expectedDuration = null)
    {
        string[] before = Directory.GetFiles(options.OutputFolder, "*_edited_*" + expectedExt);
        double progress = 0;
        await MediaEngine.ExportAsync(options, source, value => progress = value, CancellationToken.None);
        string[] after = Directory.GetFiles(options.OutputFolder, "*_edited_*" + expectedExt);
        var fresh = after.Except(before, StringComparer.OrdinalIgnoreCase).ToArray();
        Check(fresh.Length == 1, "each export must create one new distinct file");
        Check(new FileInfo(fresh[0]).Length > 512, "export file must have data");
        var output = await MediaEngine.ProbeAsync(fresh[0]);
        Check(output.Width > 0 && output.Height > 0, "export has video");
        Check(output.Width % 2 == 0 && output.Height % 2 == 0, "H264 output size must be even");
        Check((output.AudioTracks > 0) == expectAudio, "audio tracks match chosen mode");
        Check(output.VideoCodec == "h264", "output uses H.264");
        if (expectAudio) Check(output.AudioCodec == "aac", "included audio uses AAC");
        Check(Math.Abs(output.Duration - (expectedDuration ?? source.Duration)) < .50,
            $"export duration is close to expected: {output.Duration}");
        Check(progress >= .99, "progress reached completion");
        exports++;
        Console.WriteLine($"[PASS export #{exports}] {Path.GetFileName(fresh[0])}: {output.Width}x{output.Height}, {output.Duration:0.00}s, audio={output.AudioTracks}");
        return output;
    }

    private static async Task Main()
    {
        var dir = Path.Combine(Path.GetTempPath(), "StudioCut-ci-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string source = Path.Combine(dir, "source.mp4");
            string music = Path.Combine(dir, "music.wav");
            string overlay = Path.Combine(dir, "overlay.png");
            string noAudio = Path.Combine(dir, "silent.mp4");
            var ffmpeg = MediaEngine.Tool("ffmpeg");
            Console.WriteLine("[RUN] Generate synthetic licensed test video, audio and overlay");
            await Cmd(ffmpeg, "-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "testsrc2=size=160x120:rate=24:duration=3", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=3", "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", source);
            await Cmd(ffmpeg, "-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=880:sample_rate=48000:duration=1", "-c:a", "pcm_s16le", music);
            await Cmd(ffmpeg, "-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "color=c=red:s=20x20:d=1", "-frames:v", "1", overlay);
            await Cmd(ffmpeg, "-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "testsrc2=size=160x120:rate=24:duration=3", "-c:v", "libx264", "-preset", "ultrafast", "-an", noAudio);
            var d = await MediaEngine.ProbeAsync(source);
            var silent = await MediaEngine.ProbeAsync(noAudio);
            Check(d.Width == 160 && d.Height == 120, "probe reads video resolution");
            Check(d.AudioTracks == 1 && silent.AudioTracks == 0, "probe correctly counts audio streams");
            Check(d.Duration > 2.9 && d.Duration < 3.2, "probe reads duration");

            Console.WriteLine("[RUN] Validation regression tests");
            var valid = NewOptions(source, dir);
            Check(MediaEngine.Validate(valid, d).Count == 0, "valid source accepts export");
            var v = NewOptions(source, dir); v.RightsVideo = false; Invalid(v, d, "visual rights");
            v = NewOptions(source, dir); v.RightsAudio = false; Invalid(v, d, "audio rights");
            v = NewOptions(source, dir); v.AudioMode = "Mute all"; v.RightsAudio = false;
            Check(MediaEngine.Validate(v, d).Count == 0, "muting audio does not require rights to omitted source soundtrack");
            v = NewOptions(source, dir); v.AudioMode = "Replace with music"; Invalid(v, d, "missing licensed music");
            v = NewOptions(source, dir); v.AudioMode = "invalid"; Invalid(v, d, "unsupported audio mode");
            v = NewOptions(source, dir); v.Watermark = true; Invalid(v, d, "missing overlay");
            v = NewOptions(source, dir); v.CropX = 2; Invalid(v, d, "incomplete crop rectangle");
            v = NewOptions(source, dir); v.CropW = 300; v.CropH = 100; Invalid(v, d, "out of bounds crop");
            v = NewOptions(source, dir); v.Width = 159; Invalid(v, d, "odd width");
            v = NewOptions(source, dir); v.Height = 0; v.Width = 2; Invalid(v, d, "too small width");
            v = NewOptions(source, dir); v.Fps = 121; Invalid(v, d, "frame rate out of range");
            v = NewOptions(source, dir); v.Volume = double.NaN; Invalid(v, d, "invalid volume");
            v = NewOptions(source, dir); v.Start = d.Duration; Invalid(v, d, "start after video");
            v = NewOptions(source, dir); v.Start = 2; v.End = 1; Invalid(v, d, "end before start");
            v = NewOptions(source, dir); v.OriginalAudioTrack = 3; Invalid(v, d, "missing audio track");
            v = NewOptions(noAudio, dir); Invalid(v, silent, "no audio with keep-original");
            v = NewOptions(source, Path.Combine(dir, "does-not-exist")); Invalid(v, d, "nonexistent destination folder");
            v = NewOptions(source, dir); v.Container = "avi"; Invalid(v, d, "unsupported container");
            v = NewOptions(source, dir); v.Quality = "lossless"; Invalid(v, d, "unsupported quality");
            Console.WriteLine($"[PASS] {assertions} validation and probe assertions");

            Console.WriteLine("[RUN] Copyright-risk auto-plan tests (not copyright clearance)");
            var unconfirmedVisuals = NewOptions(source, dir);
            unconfirmedVisuals.RightsVideo = false;
            var review = AutoRightsPlanner.Create(unconfirmedVisuals, d);
            Check(!review.CanExport && review.Blockers.Any(x => x.Contains("Visual rights")),
                "auto mode blocks unlicensed/uncertain visuals instead of re-encoding around rights");

            var confirmed = NewOptions(source, dir);
            confirmed.Watermark = true; confirmed.Overlay = overlay;
            confirmed.Text = "Extra caption"; confirmed.Volume = 2;
            confirmed.CropW = 50; confirmed.CropH = 50; confirmed.Fps = 15;
            confirmed.AudioMode = "Mix music"; confirmed.Music = music;
            review = AutoRightsPlanner.Create(confirmed, d);
            Check(review.CanExport && review.Export != null, "confirmed audiovisual content can be auto-reviewed");
            Check(review.Export!.Container == "mp4" && review.Export.Quality == "High Quality",
                "auto mode selects high-quality MP4");
            Check(review.Export.AudioMode == "Keep original" && review.Export.RightsAudio,
                "audio confirmed by the user is retained");
            Check(review.Export.Width == 0 && review.Export.Height == 0 && review.Export.Fps == 0,
                "auto mode preserves original resolution and frame rate");
            Check(review.Export.Start == 0 && review.Export.End == 0 &&
                review.Export.CropW == 0 && review.Export.CropH == 0 && !review.Export.Watermark &&
                review.Export.Text.Length == 0, "auto mode avoids adding new creative distortions");
            Check(review.Export.Source == source && confirmed.Watermark && confirmed.CropW == 50,
                "auto plan does not mutate the user's existing editing settings");
            Check(review.Limitations.Any(x => x.Contains("Content ID")),
                "auto mode discloses it cannot guarantee Content ID clearance");
            Check(!review.RemovesUnconfirmedAudio, "confirmed soundtrack requires no mute warning");

            var uncertainAudio = NewOptions(source, dir);
            uncertainAudio.RightsAudio = false;
            review = AutoRightsPlanner.Create(uncertainAudio, d);
            Check(review.CanExport && review.RemovesUnconfirmedAudio && review.Export?.AudioMode == "Mute all",
                "unconfirmed audio is muted only with explicit consent at the GUI boundary");
            Check(review.Adjustments.Any(x => x.Contains("narration")),
                "auto plan warns that narration is also removed");
            var silentPlan = NewOptions(noAudio, dir);
            silentPlan.RightsAudio = false;
            review = AutoRightsPlanner.Create(silentPlan, silent);
            Check(review.CanExport && review.Export?.AudioMode == "Mute all" && !review.RemovesUnconfirmedAudio,
                "silent video needs no audio-rights approval and no removal prompt");

            Console.WriteLine("[RUN] FFmpeg integration exports");
            await ExportVerify(NewOptions(source, dir), d, ".mp4", true);
            // Repeating an export must NOT overwrite the previous export, even in the same second.
            await ExportVerify(NewOptions(source, dir), d, ".mp4", true);
            v = NewOptions(source, dir); v.AudioMode = "Mute all"; v.Start = .5; v.End = 2.25; v.CropW = 121; v.CropH = 91; v.VideoFade = true; v.Fps = 15;
            var crop = await ExportVerify(v, d, ".mp4", false, 1.75);
            Check(crop.Width == 122 && crop.Height == 92, "odd crop is padded to valid H264 dimensions");
            v = NewOptions(source, dir); v.AudioMode = "Replace with music"; v.Music = music; v.Watermark = true; v.Overlay = overlay; v.Width = 160; v.Height = 120; v.Normalize = true; v.AudioFade = true; v.VideoFade = true; v.Brightness = .08; v.Saturation = 1.2; v.Container = "mkv";
            await ExportVerify(v, d, ".mkv", true);
            v = NewOptions(source, dir); v.AudioMode = "Mix music"; v.Music = music; v.MusicVolume = .25; v.Denoise = true; v.VoiceEnhance = true; v.Bass = 2; v.Treble = -2; v.AudioFade = true; v.Container = "mov";
            await ExportVerify(v, d, ".mov", true);
            v = NewOptions(noAudio, dir); v.AudioMode = "Replace with music"; v.Music = music;
            await ExportVerify(v, silent, ".mp4", true);
            v = NewOptions(source, dir); v.Hardware = true;
            await ExportVerify(v, d, ".mp4", true);
            Console.WriteLine("[RUN] Real rights-review automatic exports");
            var autoPlan = AutoRightsPlanner.Create(NewOptions(source, dir), d);
            Check(autoPlan.Export != null, "licensed source has a runnable auto plan");
            string autoOutput = await MediaEngine.ExportAsync(autoPlan.Export!, d, _ => { },
                CancellationToken.None, "auto_reviewed");
            Check(File.Exists(autoOutput) && Path.GetFileName(autoOutput).Contains("_auto_reviewed_"),
                "auto mode creates a distinct, clearly labeled output");
            Check(Path.GetFileName(autoOutput).StartsWith("source_auto_reviewed_", StringComparison.Ordinal),
                "auto mode uses a short, recognizable filename");
            var reviewed = await MediaEngine.ProbeAsync(autoOutput);
            Check(reviewed.AudioTracks == 1 && reviewed.Width == d.Width && reviewed.Height == d.Height &&
                  Math.Abs(reviewed.Fps - d.Fps) < .3 && Math.Abs(reviewed.Duration - d.Duration) < .25,
                "auto output preserves original dimensions, duration and approved audio");
            Check(reviewed.VideoCodec == "h264" && reviewed.AudioCodec == "aac",
                "auto audio-retaining output is standardized to H.264/AAC");

            var silentAuto = NewOptions(source, dir);
            silentAuto.RightsAudio = false;
            var muted = AutoRightsPlanner.Create(silentAuto, d);
            Check(muted.Export != null, "source with uncertain audio can be exported without its soundtrack");
            string mutedOutput = await MediaEngine.ExportAsync(muted.Export!, d, _ => { },
                CancellationToken.None, "auto_reviewed");
            var mutedDetails = await MediaEngine.ProbeAsync(mutedOutput);
            Check(mutedDetails.AudioTracks == 0 && mutedDetails.Width == d.Width,
                "uncertain-audio automatic export has no sound and keeps video");
            Check(File.Exists(source) && File.Exists(autoOutput),
                "auto mode leaves both the source and prior auto output intact");
            Console.WriteLine("[PASS] Two extra automatic rights-review FFmpeg exports");

            Console.WriteLine("[RUN] Invalid audio/image asset preflight (conversion must not begin)");
            string brokenAudio = Path.Combine(dir, "bad-music.wav");
            string brokenOverlay = Path.Combine(dir, "bad-overlay.png");
            await File.WriteAllTextAsync(brokenAudio, "Not a valid audio stream");
            await File.WriteAllTextAsync(brokenOverlay, "Not a valid image");
            v = NewOptions(source, dir); v.AudioMode = "Replace with music"; v.Music = brokenAudio;
            bool rejected = false;
            try { await MediaEngine.ExportAsync(v, d, _ => { }, CancellationToken.None); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "corrupted music is rejected before encoding");
            v = NewOptions(source, dir); v.Watermark = true; v.Overlay = brokenOverlay;
            rejected = false;
            try { await MediaEngine.ExportAsync(v, d, _ => { }, CancellationToken.None); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "corrupted overlay is rejected before encoding");
            Console.WriteLine("[RUN] Cancellation cleanup");
            v = NewOptions(source, dir);
            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel();
                bool cancelledCorrectly = false;
                try { await MediaEngine.ExportAsync(v, d, _ => { }, cancelled.Token); }
                catch (OperationCanceledException) { cancelledCorrectly = true; }
                Check(cancelledCorrectly, "cancelled encoding returns cancellation");
            }
            Check(Directory.GetFiles(dir, "*_edited_*").Length == exports, "invalid and cancelled exports did not leave partial files");
            Console.WriteLine($"RESULT: PASS: {assertions} assertions, {exports} standard and 2 auto-reviewed real FFmpeg exports");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (IOException) { }
        }
    }
}
