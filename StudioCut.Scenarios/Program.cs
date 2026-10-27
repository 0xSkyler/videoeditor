using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using StudioCut;

internal static class Program
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly List<ScenarioResult> Results = new();
    private static string directory = "", audioVoice = "", music = "", silent = "", portrait = "", mixed = "", dual = "", avi = "", wide = "", longVideo = "";
    private static int assertions;

    private sealed record ScenarioResult(
        string scenario, string outcome, double elapsed_seconds, long bytes, string details);

    private static void Assert(bool ok, string reason)
    {
        if (!ok) throw new InvalidOperationException("Assertion failed: " + reason);
        assertions++;
        Console.WriteLine("[SCENARIO PASS] " + reason);
    }

    private static async Task RunToolAsync(string executable, int seconds, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable) {
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true
        };
        foreach (var arg in arguments) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("Could not launch " + executable);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        using var closeOnCancel = deadline.Token.Register(() => {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
        });
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException) { throw new TimeoutException("Timeout running " + executable + " " + string.Join(" ", arguments.Take(10))); }
        string err = await stderr;
        await stdout;
        if (process.ExitCode != 0) throw new IOException(executable + " exited " + process.ExitCode + ": " + err[..Math.Min(err.Length, 1800)]);
    }

    private static async Task Ffmpeg(params string[] args)
    {
        await RunToolAsync(MediaEngine.Tool("ffmpeg"), 60,
            new[] { "-hide_banner", "-loglevel", "error", "-y" }.Concat(args).ToArray());
    }

    private static async Task MakeFixturesAsync()
    {
        audioVoice = Path.Combine(directory, "Higgsfield original voice 2026.mp4");
        music = Path.Combine(directory, "separate royalty-free test tone.wav");
        silent = Path.Combine(directory, "Silent AI clip.mp4");
        portrait = Path.Combine(directory, "Portrait 9-16 25fps.mp4");
        mixed = Path.Combine(directory, "voice and music already mixed.mp4");
        dual = Path.Combine(directory, "two narration languages.mkv");
        avi = Path.Combine(directory, "old input MPEG4 PCM.avi");
        wide = Path.Combine(directory, "Landscape 29.97fps.mp4");
        longVideo = Path.Combine(directory, "Long render for cancellation.mp4");

        await Ffmpeg("-f","lavfi","-i","testsrc2=size=640x360:rate=30",
            "-f","lavfi","-i","sine=frequency=440:sample_rate=44100",
            "-t","2.4","-c:v","libx264","-preset","ultrafast","-crf","25",
            "-pix_fmt","yuv420p","-c:a","aac","-b:a","128k",audioVoice);
        await Ffmpeg("-f","lavfi","-i","sine=frequency=330:sample_rate=44100",
            "-t","2.4","-c:a","pcm_s16le",music);
        await Ffmpeg("-i",audioVoice,"-map","0:v:0","-c:v","copy","-an",silent);
        await Ffmpeg("-f","lavfi","-i","testsrc2=size=360x640:rate=25",
            "-f","lavfi","-i","sine=frequency=510:sample_rate=44100",
            "-t","1.8","-c:v","libx264","-preset","ultrafast","-crf","26",
            "-pix_fmt","yuv420p","-c:a","aac","-b:a","96k",portrait);
        await Ffmpeg("-i",audioVoice,"-i",music,
            "-filter_complex","[0:a:0][1:a:0]amix=inputs=2:duration=first[a]",
            "-map","0:v:0","-map","[a]","-t","2.4","-c:v","copy","-c:a","aac","-b:a","128k",mixed);
        await Ffmpeg("-i",audioVoice,"-i",music,
            "-map","0:v:0","-map","0:a:0","-map","1:a:0","-t","2.4",
            "-c:v","copy","-c:a","aac","-b:a","128k",dual);
        await Ffmpeg("-i",audioVoice,"-c:v","mpeg4","-q:v","8",
            "-c:a","pcm_s16le","-t","2.4",avi);
        await Ffmpeg("-f","lavfi","-i","testsrc2=size=1280x720:rate=30000/1001",
            "-f","lavfi","-i","sine=frequency=660:sample_rate=44100",
            "-t","1.9","-c:v","libx264","-preset","ultrafast","-crf","30",
            "-pix_fmt","yuv420p","-c:a","aac","-b:a","96k",wide);
        await Ffmpeg("-f","lavfi","-i","testsrc2=size=960x540:rate=30",
            "-f","lavfi","-i","sine=frequency=580:sample_rate=44100",
            "-t","9","-c:v","libx264","-preset","ultrafast","-crf","30",
            "-pix_fmt","yuv420p","-c:a","aac","-b:a","64k",longVideo);
        Console.WriteLine("Synthetic offline video fixtures generated; no real Higgsfield data is used.");
    }

    private static Options Input(string file, bool audioRights = true, bool videoRights = true)
        => new() { Source = file, OutputFolder = directory,
            RightsAudio = audioRights, RightsVideo = videoRights,
            Container = "mp4", Quality = "High Quality",
            AudioMode = audioRights ? "Keep original" : "Mute all" };

    private static async Task<(string output, MediaDetails data)> ExportReviewedAsync(
        string input, bool voiceRights, int audioTracks, bool videoRights = true)
    {
        MediaDetails d = await MediaEngine.ProbeAsync(input);
        var p = AutoRightsPlanner.Create(Input(input, voiceRights, videoRights), d);
        Assert(p.CanExport && p.Export != null, "plan accepted valid rights and source");
        string outFile = await MediaEngine.ExportAsync(p.Export!, d, _ => { },
            CancellationToken.None, "auto_reviewed");
        var outInfo = await MediaEngine.ProbeAsync(outFile);
        Assert(File.Exists(outFile) && new FileInfo(outFile).Length > 1000, "nonempty playable output exists");
        Assert(outInfo.VideoCodec == "h264", "output codec is H.264");
        Assert(outInfo.AudioTracks == audioTracks, "expected number of soundtrack tracks preserved");
        if (audioTracks > 0) Assert(outInfo.AudioCodec == "aac", "retained audio is AAC");
        Assert(Math.Abs(outInfo.Duration - d.Duration) < .36,
            $"export duration within 0.36 seconds ({d.Duration:F2} vs {outInfo.Duration:F2})");
        Assert(Math.Abs(outInfo.Fps - d.Fps) < .3, "input FPS maintained to 0.3 FPS");
        return (outFile, outInfo);
    }

    private static async Task<double> StrengthAtAsync(string file, double targetFrequency)
    {
        var psi = new ProcessStartInfo(MediaEngine.Tool("ffmpeg")) {
            RedirectStandardError = true, RedirectStandardOutput = true,
            UseShellExecute = false, CreateNoWindow = true
        };
        foreach (string a in new[] {"-v","error","-ss","0.4","-i",file,"-t","0.65",
            "-vn","-ac","1","-ar","8000","-f","f32le","pipe:1"}) psi.ArgumentList.Add(a);
        using var proc = Process.Start(psi) ?? throw new IOException("Cannot start audio analyzer");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        using var registration = deadline.Token.Register(() => { try { if (!proc.HasExited) proc.Kill(true); } catch {} });
        using var mem = new MemoryStream();
        Task<string> errors = proc.StandardError.ReadToEndAsync();
        await proc.StandardOutput.BaseStream.CopyToAsync(mem, deadline.Token);
        await proc.WaitForExitAsync(deadline.Token);
        if (proc.ExitCode != 0) throw new IOException("Audio analyzer failed: " + await errors);
        byte[] bytes=mem.ToArray();
        int n=bytes.Length/4;
        Assert(n>=1500,"enough decoded audio frames for frequency comparison");
        double re=0,im=0;
        for(int i=0;i<n;i++) {
            double value=BitConverter.ToSingle(bytes,i*4);
            double phase=2*Math.PI*targetFrequency*i/8000;
            re+=value*Math.Cos(phase); im-=value*Math.Sin(phase);
        }
        return Math.Sqrt(re*re+im*im)/n;
    }

    private static async Task ScenarioAsync(string name, Func<Task<(string output,string note)>> operation)
    {
        var sw=Stopwatch.StartNew();
        try
        {
            var result=await operation();
            sw.Stop();
            long size=File.Exists(result.output) ? new FileInfo(result.output).Length : 0;
            Results.Add(new(name,"PASS",sw.Elapsed.TotalSeconds,size,result.note));
            Console.WriteLine($"[USE CASE PASS] {name}: {sw.Elapsed.TotalSeconds:F2}s, {size} bytes");
        }
        catch(Exception ex)
        {
            sw.Stop();
            Results.Add(new(name,"FAIL",sw.Elapsed.TotalSeconds,0,ex.ToString()[..Math.Min(1000,ex.ToString().Length)]));
            Console.Error.WriteLine($"[USE CASE FAIL] {name}: {ex}");
        }
    }

    private static async Task MainCasesAsync()
    {
        byte[] originalHash=SHA256.HashData(await File.ReadAllBytesAsync(audioVoice));

        await ScenarioAsync("Higgsfield narration kept with confirmed license",async () => {
            var (output,data)=await ExportReviewedAsync(audioVoice,true,1);
            double speech=await StrengthAtAsync(output,440);
            Assert(speech>.008,"original synthetic narration remains audible (440Hz)");
            Assert(data.Width==640 && data.Height==360,"landscape dimensions unchanged");
            return (output,$"voice amplitude {speech:F5}");
        });

        await ScenarioAsync("Higgsfield audio uncertain: mute only with user consent",async () => {
            var (output,data)=await ExportReviewedAsync(audioVoice,false,0);
            Assert(data.AudioTracks==0,"uncertain audio is completely absent");
            Assert(data.Width==640,"video remains present");
            return (output,"Muting requires separate positive UI consent, not inferred by engine.");
        });

        await ScenarioAsync("Original visuals unconfirmed: never export",async () => {
            var md=await MediaEngine.ProbeAsync(audioVoice);
            var p=AutoRightsPlanner.Create(Input(audioVoice,true,false),md);
            Assert(!p.CanExport && p.Blockers.Any(x=>x.Contains("Visual rights")),
                "unconfirmed video permissions block automated changes");
            return ("","No file created");
        });

        await ScenarioAsync("Higgsfield narration with music already mixed",async () => {
            var (output,data)=await ExportReviewedAsync(mixed,true,1);
            double voice=await StrengthAtAsync(output,440);
            double backing=await StrengthAtAsync(output,330);
            Assert(voice>.004 && backing>.004, "both narration and already-mixed music persist");
            return (output,$"voice tone={voice:F4}, music tone={backing:F4}. App cannot clear bundled music rights.");
        });

        await ScenarioAsync("Silent Higgsfield video does not get fake audio",async () => {
            var (output,data)=await ExportReviewedAsync(silent,false,0);
            Assert(data.AudioTracks==0, "silent source stays silent");
            return (output,"Video is still fully playable.");
        });

        await ScenarioAsync("Portrait short-form 9:16 at 25 FPS",async () => {
            var (output,data)=await ExportReviewedAsync(portrait,true,1);
            Assert(data.Width==360 && data.Height==640 && Math.Abs(data.Fps-25)<.2,
                "vertical aspect ratio, dimensions and 25 FPS preserved");
            return (output,"Vertical export compatible with YouTube Shorts-sized content.");
        });

        await ScenarioAsync("Landscape 1280x720 at 29.97 FPS",async () => {
            var (output,data)=await ExportReviewedAsync(wide,true,1);
            Assert(data.Width==1280 && data.Height==720 && Math.Abs(data.Fps-29.97)<.25,
                "landscape HD frame dimensions and fractional FPS preserved");
            return (output,"Checks non-integer NTSC frame rate.");
        });

        await ScenarioAsync("Original with two audio language tracks",async () => {
            var input=await MediaEngine.ProbeAsync(dual);
            Assert(input.AudioTracks==2,"fixture contains two separate soundtracks");
            var (output,data)=await ExportReviewedAsync(dual,true,2);
            Assert(data.AudioTracks==2,"both licensed narration tracks remain present");
            return (output,"Previously only the first narration stream survived Auto mode.");
        });

        await ScenarioAsync("Legacy AVI MPEG-4 video and PCM audio",async () => {
            var input=await MediaEngine.ProbeAsync(avi);
            Assert(input.VideoCodec=="mpeg4" && input.AudioCodec=="pcm_s16le",
                "input genuinely uses legacy MPEG-4 and uncompressed audio");
            var (output,data)=await ExportReviewedAsync(avi,true,1);
            Assert(data.VideoCodec=="h264" && data.AudioCodec=="aac","legacy codec converted successfully");
            return (output,"Original input was converted to portable H.264/AAC.");
        });

        await ScenarioAsync("Two consecutive exports never overwrite previous files",async () => {
            var a=await ExportReviewedAsync(audioVoice,true,1);
            var b=await ExportReviewedAsync(audioVoice,true,1);
            Assert(a.output!=b.output,"unique filenames on repeated clicks");
            Assert(File.Exists(a.output)&&File.Exists(b.output),"both outputs remain available");
            Assert(Path.GetFileName(a.output).StartsWith("Higgsfield original voice 2026_auto_reviewed_",StringComparison.Ordinal),
                "filenames stay readable and clearly indicate reviewed copy");
            return (b.output,"First export retained and source not overwritten.");
        });

        await ScenarioAsync("Missing source, corrupt input and audio-only file",async () => {
            string fake=Path.Combine(directory,"broken-media.mp4");
            await File.WriteAllTextAsync(fake,"This is not video data");
            int rejects=0;
            foreach(string file in new[]{fake,music,Path.Combine(directory,"no-file.mp4")})
            {
                try { await MediaEngine.ProbeAsync(file); }
                catch(Exception) {rejects++;}
            }
            Assert(rejects==3,"corrupt, audio-only and missing inputs are rejected");
            return ("","All three invalid inputs blocked.");
        });

        await ScenarioAsync("Cancellation does not create a partial output",async () => {
            var d=await MediaEngine.ProbeAsync(audioVoice);
            var plan=AutoRightsPlanner.Create(Input(audioVoice),d);
            using var cts=new CancellationTokenSource();
            cts.Cancel();
            bool stopped=false;
            try { await MediaEngine.ExportAsync(plan.Export!,d,_=>{},cts.Token,"auto_reviewed"); }
            catch(OperationCanceledException) { stopped=true; }
            Assert(stopped,"pre-cancelled user request stops before FFmpeg starts");
            return ("","No partial file after cancellation.");
        });

        await ScenarioAsync("Cancel an in-progress longer export and clean partial output",async () => {
            var d=await MediaEngine.ProbeAsync(longVideo);
            var plan=AutoRightsPlanner.Create(Input(longVideo),d);
            Assert(plan.CanExport && plan.Export!=null,"long-form render accepted for cancellation test");
            var existing=Directory.GetFiles(directory,"*_auto_reviewed_*").ToHashSet();
            using var stop=new CancellationTokenSource();
            bool progressSeen=false, interrupted=false;
            try {
                await MediaEngine.ExportAsync(plan.Export!,d,p=> {
                    if(p>=.035 && !stop.IsCancellationRequested) {
                        progressSeen=true;
                        stop.Cancel();
                    }
                },stop.Token,"auto_reviewed");
            }
            catch(OperationCanceledException) { interrupted=true; }
            Assert(progressSeen,"user cancellation requested after actual encoder progress");
            Assert(interrupted,"in-progress FFmpeg export reports user cancellation");
            var partial=Directory.GetFiles(directory,"*_auto_reviewed_*").Where(x=>!existing.Contains(x)).ToArray();
            Assert(partial.Length==0,"cancelled run leaves no partial output file");
            return ("","Cancellation occurred after real encoder progress, not before invocation.");
        });

        await ScenarioAsync("One-click preset preserves Higgsfield narration with no settings", async () => {
            var d = await MediaEngine.ProbeAsync(audioVoice);
            var options = SimpleConvertPlan.Create(audioVoice, directory, d);
            Assert(options.Container == "mp4" && options.Quality == "High Quality",
                "one-click automatically selects high-quality MP4");
            Assert(options.NoRightsPrompt && options.PreserveSourceMetadata,
                "one-click has no manual attestations and copies supported metadata");
            Assert(options.AudioMode == "Keep original" && options.Contrast > 1
                && options.Saturation > 1 && options.Fps == 0 && options.Width == 0,
                "one-click preserves voice, FPS and resolution with a minimal automatic color adjustment");
            string output = await MediaEngine.ExportAsync(options, d, _ => { },
                CancellationToken.None, "converted");
            var result = await MediaEngine.ProbeAsync(output);
            Assert(result.AudioTracks == 1 && result.VideoCodec == "h264" && result.AudioCodec == "aac",
                "one-click output retains original voice in H.264/AAC");
            Assert(result.Width == d.Width && result.Height == d.Height && Math.Abs(result.Fps-d.Fps)<.25,
                "dimensions and FPS preserved automatically");
            Assert(Path.GetFileName(output).Contains("_converted_") && File.Exists(audioVoice),
                "one-click makes a new recognizable file without modifying the input");
            return (output, "Single-button output with no user parameter selection.");
        });

        await ScenarioAsync("One-click silent clip remains silent", async () => {
            var d = await MediaEngine.ProbeAsync(silent);
            var options = SimpleConvertPlan.Create(silent, directory, d);
            Assert(options.AudioMode == "Mute all", "silent video mode selected automatically");
            string output = await MediaEngine.ExportAsync(options, d, _ => { },
                CancellationToken.None, "converted");
            var converted = await MediaEngine.ProbeAsync(output);
            Assert(converted.AudioTracks == 0, "simple export never invents narration or music");
            return (output, "Silent remains silent.");
        });

        await ScenarioAsync("One-click dual-language narration keeps both audio tracks", async () => {
            var d = await MediaEngine.ProbeAsync(dual);
            var options = SimpleConvertPlan.Create(dual, directory, d);
            Assert(options.KeepAllAudioTracks && d.AudioTracks==2,
                "two audio tracks are detected without any track dropdown");
            string output = await MediaEngine.ExportAsync(options, d, _ => { },
                CancellationToken.None, "converted");
            var converted = await MediaEngine.ProbeAsync(output);
            Assert(converted.AudioTracks == 2 && converted.VideoCodec == "h264",
                "both audio tracks preserved by automatic conversion");
            return (output, "Multitrack source handled without input controls.");
        });

        await ScenarioAsync("Original source remains byte-for-byte unchanged",async () => {
            byte[] after=SHA256.HashData(await File.ReadAllBytesAsync(audioVoice));
            Assert(after.SequenceEqual(originalHash),"original video SHA-256 remains identical");
            return ("","Read-only source property maintained across all scenarios.");
        });
    }

    public static async Task<int> Main()
    {
        directory=Path.GetFullPath("scenario-results/media");
        Directory.CreateDirectory(directory);
        var sw=Stopwatch.StartNew();
        try { await MakeFixturesAsync(); await MainCasesAsync(); }
        catch(Exception ex)
        {
            Results.Add(new ScenarioResult("Test setup/runtime","FAIL",sw.Elapsed.TotalSeconds,0,ex.ToString()));
            Console.Error.WriteLine(ex);
        }
        sw.Stop();
        string folder=Path.GetFullPath("scenario-results");
        Directory.CreateDirectory(folder);
        int failures=Results.Count(x=>x.outcome!="PASS");
        var obj=new {
            generated_at=DateTimeOffset.UtcNow,
            assertions, scenario_count=Results.Count, passed=Results.Count-failures, failed=failures,
            execution_seconds=Math.Round(sw.Elapsed.TotalSeconds,2),
            info="Synthetic local fixtures represent user workflows; not real Higgsfield account files or licensing verification.",
            results=Results
        };
        await File.WriteAllTextAsync(Path.Combine(folder,"results.json"),
            JsonSerializer.Serialize(obj,new JsonSerializerOptions { WriteIndented=true }));
        var rows=new List<string> { "# StudioCut diverse use cases (Windows)", "",
            $"Cases: {Results.Count} | Passed: {Results.Count-failures} | Failed: {failures} | Assertions: {assertions} | Elapsed: {sw.Elapsed.TotalSeconds:F1}s",
            "", "| Scenario | Result | Seconds | File size |", "| --- | --- | ---: | ---: |" };
        foreach(var item in Results) rows.Add($"| {item.scenario} | {item.outcome} | {item.elapsed_seconds:F2} | {item.bytes} |");
        rows.Add(""); rows.Add("No online Content ID or license validation. AI-voice rights are user-confirmed; a mix of music and narration cannot be separated merely by changing bitrate.");
        await File.WriteAllLinesAsync(Path.Combine(folder,"report.md"),rows);
        Console.WriteLine($"SCENARIO RESULT: {(failures==0?"PASS":"FAIL")}; {Results.Count-failures}/{Results.Count} cases; {assertions} assertions; {sw.Elapsed.TotalSeconds:F2}s");
        return failures==0?0:1;
    }
}
