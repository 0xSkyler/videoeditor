using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StudioCut;

internal static class Program
{
    private static int assertions;
    private static readonly List<object> observations = new();

    [STAThread]
    private static int Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var window = new MainWindow();
        int result = 0;
        window.Loaded += async (_, _) =>
        {
            try
            {
                await TestUserWorkflowAsync(window);
                Console.WriteLine($"SIMPLIFIED GUI RESULT: PASS; {assertions} assertions, 5 video exports");
            }
            catch (Exception e)
            {
                result = 1;
                Console.Error.WriteLine("SIMPLIFIED GUI RESULT: FAIL: " + e);
            }
            finally { app.Dispatcher.BeginInvoke(() => app.Shutdown()); }
        };
        window.Show();
        app.Run(window);
        return result;
    }

    private static T Ui<T>(MainWindow window, string id) where T : FrameworkElement =>
        window.FindName(id) as T ?? throw new InvalidOperationException($"Missing UI element: {id}");

    private static void Check(bool valid, string message)
    {
        if (!valid) throw new Exception("Assertion failed: " + message);
        assertions++;
        Console.WriteLine("[GUI PASS] " + message);
    }

    private static void Click(Button button) =>
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));

    private static void SetPrivate(MainWindow window, string name, object value)
    {
        var f = typeof(MainWindow).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new Exception("Missing dependency: " + name);
        f.SetValue(window, value);
    }

    private static async Task LoadAsync(MainWindow window, string path)
    {
        var loader = typeof(MainWindow).GetMethod("LoadVideoAsync", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new Exception("Missing source import handler");
        await ((Task?)loader.Invoke(window, new object[] { path }) ?? throw new Exception("Import returned no Task"));
    }

    private static async Task DropPathsAsync(MainWindow window, params string[] paths)
    {
        var handler = typeof(MainWindow).GetMethod("HandleDroppedPathsAsync",
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new Exception("Missing shared drag/drop import handler");
        await ((Task?)handler.Invoke(window, new object[] { paths })
            ?? throw new Exception("Drop path handler returned no Task"));
    }

    private static async Task ChooseViaButtonAsync(MainWindow window, string path)
    {
        SetPrivate(window, "choosePath", new Func<string?>(() => path));
        Click(Ui<Button>(window, "ChooseVideoButton"));
        bool loaded = false;
        for (int i = 0; i < 100; i++)
        {
            if (Ui<TextBlock>(window, "StatusText").Text.StartsWith("Ready. Click Convert", StringComparison.Ordinal)
                && Ui<TextBlock>(window, "SelectedVideoText").Text == Path.GetFileName(path))
            { loaded = true; break; }
            if (Ui<TextBlock>(window, "StatusText").Text.StartsWith("Video import failed", StringComparison.Ordinal))
                break;
            await Task.Delay(70);
        }
        Check(loaded, "native Choose video UI button loads selected file via picker result");
    }

    private static async Task FFmpegAsync(params string[] args)
    {
        var psi = new ProcessStartInfo(MediaEngine.Tool("ffmpeg"))
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardError = true, RedirectStandardOutput = true
        };
        foreach (string arg in new[] { "-hide_banner", "-loglevel", "error", "-y" }.Concat(args))
            psi.ArgumentList.Add(arg);
        using var proc = Process.Start(psi) ?? throw new Exception("Cannot start FFmpeg");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        using var kill = deadline.Token.Register(() => { try { if (!proc.HasExited) proc.Kill(true); } catch { } });
        Task<string> stderr = proc.StandardError.ReadToEndAsync();
        Task<string> stdout = proc.StandardOutput.ReadToEndAsync();
        await proc.WaitForExitAsync(deadline.Token);
        string error = await stderr;
        await stdout;
        if (proc.ExitCode != 0) throw new Exception("Fixture setup failed: " + error);
    }

    private static async Task<string[]> MakeMediaAsync(string dir)
    {
        string voiced = Path.Combine(dir, "Higgsfield narrated sample.mp4");
        string silent = Path.Combine(dir, "silent.mp4");
        string portrait = Path.Combine(dir, "portrait.mp4");
        string multiple = Path.Combine(dir, "two voice tracks.mkv");
        string heavy = Path.Combine(dir, "long render to cancel.mp4");
        await FFmpegAsync("-f","lavfi","-i","testsrc2=size=320x180:rate=24",
            "-f","lavfi","-i","sine=frequency=440:sample_rate=44100",
            "-t","2.8","-c:v","libx264","-preset","ultrafast","-crf","24",
            "-pix_fmt","yuv420p","-c:a","aac","-b:a","96k",voiced);
        await FFmpegAsync("-i",voiced,"-an","-c:v","copy",silent);
        await FFmpegAsync("-f","lavfi","-i","testsrc2=size=240x426:rate=25",
            "-f","lavfi","-i","sine=frequency=550:sample_rate=44100",
            "-t","1.6","-c:v","libx264","-preset","ultrafast","-crf","25",
            "-pix_fmt","yuv420p","-c:a","aac","-b:a","96k",portrait);
        await FFmpegAsync("-i",voiced,"-f","lavfi","-i","sine=frequency=660:sample_rate=44100",
            "-map","0:v:0","-map","0:a:0","-map","1:a:0","-t","2.8",
            "-c:v","copy","-c:a","aac",multiple);
        await FFmpegAsync("-f","lavfi","-i","testsrc2=size=1280x720:rate=30",
            "-f","lavfi","-i","sine=frequency=700:sample_rate=44100",
            "-t","12","-c:v","libx264","-preset","ultrafast","-crf","30",
            "-pix_fmt","yuv420p","-c:a","aac","-b:a","64k",heavy);
        return new[] { voiced, silent, portrait, multiple, heavy };
    }

    private static int CountManualControls(DependencyObject parent)
    {
        int count = 0;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is TextBox or CheckBox or ComboBox or Slider or RadioButton)
                count++;
            count += CountManualControls(child);
        }
        return count;
    }

    private static async Task<string> ConvertViaButtonAsync(MainWindow window, string scenario, MediaDetails sourceDetails)
    {
        var convert = Ui<Button>(window, "ConvertButton");
        var cancel = Ui<Button>(window, "CancelButton");
        Check(convert.IsEnabled, scenario + ": Convert is enabled with a valid imported file");
        Check(cancel.Visibility == Visibility.Collapsed, scenario + ": Cancel hidden until conversion");
        var watch = Stopwatch.StartNew();
        Click(convert);
        bool finished = false;
        string status = "";
        for (int i = 0; i < 400; i++)
        {
            status = Ui<TextBlock>(window, "StatusText").Text;
            if (status.StartsWith("Conversion complete.", StringComparison.Ordinal))
            { finished = true; break; }
            if (status.StartsWith("Conversion failed:", StringComparison.Ordinal) ||
                status.StartsWith("Cannot convert:", StringComparison.Ordinal) ||
                status.StartsWith("Conversion cancelled.", StringComparison.Ordinal))
                break;
            await Task.Delay(100);
        }
        watch.Stop();
        Check(finished, scenario + ": end-to-end conversion succeeds; " + status);
        string pathText = Ui<TextBlock>(window, "OutputPathText").Text;
        Check(pathText.StartsWith("Saved: ", StringComparison.Ordinal), scenario + ": final output path is shown");
        string path = pathText["Saved: ".Length..];
        Check(File.Exists(path) && new FileInfo(path).Length > 1000, scenario + ": new MP4 exists");
        Check(Path.GetExtension(path).Equals(".mp4", StringComparison.OrdinalIgnoreCase),
            scenario + ": one-click output is always MP4");
        Check(Path.GetFileName(path).Contains("_converted_"), scenario + ": filename distinguishes new export");
        Check(Ui<Button>(window, "OpenOutputButton").Visibility == Visibility.Visible,
            scenario + ": Open output folder appears after success");
        Check(Ui<ProgressBar>(window, "Progress").Value >= 99,
            scenario + ": progress displays completed state");
        Check(cancel.Visibility == Visibility.Collapsed && convert.IsEnabled,
            scenario + ": user can convert again without restarting");
        var result = await MediaEngine.ProbeAsync(path);
        Check(result.VideoCodec == "h264" && (result.AudioTracks == 0 || result.AudioCodec == "aac"),
            scenario + ": codecs standardized to H.264 and AAC");
        Check(result.AudioTracks == sourceDetails.AudioTracks,
            scenario + ": all original audio tracks retained");
        Check(result.Width == sourceDetails.Width && result.Height == sourceDetails.Height,
            scenario + ": original frame dimensions preserved");
        Check(Math.Abs(result.Fps - sourceDetails.Fps) < .3,
            scenario + ": source frame rate preserved");
        Check(Math.Abs(result.Duration - sourceDetails.Duration) < .4,
            scenario + ": duration preserved");
        observations.Add(new {
            scenario, elapsed_seconds = Math.Round(watch.Elapsed.TotalSeconds, 2),
            output_bytes = new FileInfo(path).Length,
            width = result.Width, height = result.Height,
            fps = Math.Round(result.Fps, 2), audio_tracks = result.AudioTracks
        });
        Console.WriteLine($"[UI CONVERSION] {scenario} in {watch.Elapsed.TotalSeconds:F2}s: {path}");
        return path;
    }

    private static void SaveScreenshot(MainWindow window, string path)
    {
        try
        {
            window.UpdateLayout();
            var bmp = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight,
                96, 96, PixelFormats.Pbgra32);
            bmp.Render(window);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using var output = File.Create(path);
            encoder.Save(output);
        }
        catch (Exception e) { Console.WriteLine("Screenshot unavailable: " + e.Message); }
    }

    private static async Task TestUserWorkflowAsync(MainWindow window)
    {
        var dir = Path.GetFullPath("ux-results/simple-ui");
        Directory.CreateDirectory(dir);
        string[] media = await MakeMediaAsync(dir);

        Check(!Ui<Button>(window, "ConvertButton").IsEnabled,
            "initial Convert is disabled without any selected video");
        Check(Ui<Button>(window, "ChooseVideoButton").IsEnabled,
            "Choose video is immediately available");
        Check(window.AllowDrop && Ui<Border>(window,"DropZone").AllowDrop,
            "video file drag-and-drop is enabled");
        Check(CountManualControls(window) == 0,
            "no text inputs, dropdowns, sliders, checkboxes or advanced manual option controls");
        Check(Ui<Button>(window, "OpenOutputButton").Visibility == Visibility.Collapsed,
            "output-folder action hidden before first success");
        Check(Ui<TextBlock>(window, "OutputPathText").Text.Contains("StudioCut Exports"),
            "automatic output folder is clearly identified");
        SaveScreenshot(window, Path.Combine(dir,"initial-simple-ui.png"));

        await ChooseViaButtonAsync(window,media[0]);
        var firstMetadata=await MediaEngine.ProbeAsync(media[0]);
        bool thumbnailReady = false;
        for (int n = 0; n < 45; n++)
        {
            if (Ui<Image>(window, "PosterImage").Source is BitmapSource)
            { thumbnailReady = true; break; }
            await Task.Delay(100);
        }
        Check(thumbnailReady, "real FFmpeg poster frame is displayed after import");
        Check(Ui<TextBlock>(window, "EmptyPoster").Visibility == Visibility.Collapsed,
            "placeholder is hidden when real first-frame preview appears");
        Check(Ui<TextBlock>(window,"SelectedVideoText").Text==Path.GetFileName(media[0]),
            "imported filename appears in simple UI");
        Check(Ui<TextBlock>(window, "SelectedDetailsText").Text.Contains("320") &&
              Ui<TextBlock>(window, "SelectedDetailsText").Text.Contains("24"),
            "basic dimensions and FPS are displayed without controls");
        var originalHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(media[0])));
        string first = await ConvertViaButtonAsync(window,"Higgsfield generated video with narration",firstMetadata);
        Check(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(media[0]))) == originalHash,
            "source stays byte-for-byte unchanged");
        SaveScreenshot(window,Path.Combine(dir,"completed-simple-ui.png"));

        string folderOpened = "";
        SetPrivate(window, "openFolder", new Action<string>(path => folderOpened = path));
        Click(Ui<Button>(window, "OpenOutputButton"));
        Check(Path.GetFullPath(folderOpened) == Path.GetFullPath(Path.GetDirectoryName(first)!),
            "Open output folder button targets the exact directory containing the export");
        Check(Path.GetFullPath(folderOpened) == Path.GetFullPath(SimpleConvertPlan.ResolveOutputFolder()),
            "output folder automatically resolves to Videos/StudioCut Exports or its fallback");

        // Cancelling a native picker must keep the existing video selected.
        SetPrivate(window, "choosePath", new Func<string?>(() => null));
        Click(Ui<Button>(window, "ChooseVideoButton"));
        Check(Ui<Button>(window, "ConvertButton").IsEnabled &&
              Ui<TextBlock>(window, "SelectedVideoText").Text == Path.GetFileName(media[0]),
            "cancelled Choose video dialog keeps the current selection");

        SetPrivate(window, "choosePath", new Func<string?>(() => throw new IOException("Simulated picker error")));
        Click(Ui<Button>(window, "ChooseVideoButton"));
        Check(Ui<TextBlock>(window, "StatusText").Text.Contains("Could not choose video"),
            "picker errors are visible and do not crash the application");

        await DropPathsAsync(window, media[0], media[1]);
        Check(Ui<TextBlock>(window, "StatusText").Text.Contains("Drop exactly one video"),
            "dropping two files gives an actionable message instead of silently picking the first");
        await DropPathsAsync(window, dir);
        Check(Ui<TextBlock>(window, "StatusText").Text.Contains("not a folder"),
            "dropping a folder is rejected visibly");
        Check(Ui<Button>(window, "ConvertButton").IsEnabled,
            "invalid drops do not discard the previously selected valid video");

        string second = await ConvertViaButtonAsync(window,"repeat Convert click",firstMetadata);
        Check(first != second && File.Exists(first),
            "repeating conversion creates a unique file and keeps the previous export");

        await DropPathsAsync(window,media[1]);
        Check(Ui<TextBlock>(window, "SelectedVideoText").Text == Path.GetFileName(media[1]),
            "single-file drop handler imports the intended video");
        Check(Ui<Button>(window,"OpenOutputButton").Visibility == Visibility.Collapsed,
            "importing another video clears stale output-file action");
        string silent = await ConvertViaButtonAsync(window,"silent video",await MediaEngine.ProbeAsync(media[1]));
        Check((await MediaEngine.ProbeAsync(silent)).AudioTracks == 0,
            "silent input does not accidentally receive an audio track");

        await LoadAsync(window,media[2]);
        var portrait = await MediaEngine.ProbeAsync(media[2]);
        await ConvertViaButtonAsync(window,"vertical short-form video",portrait);

        await LoadAsync(window,media[3]);
        var dualAudio = await MediaEngine.ProbeAsync(media[3]);
        Check(dualAudio.AudioTracks == 2,"multi-language fixture includes two separate audio tracks");
        await ConvertViaButtonAsync(window,"two-track narration",dualAudio);

        // Button-driven cancellation with a real live FFmpeg process.
        await LoadAsync(window,media[4]);
        var outputDir = SimpleConvertPlan.ResolveOutputFolder();
        var beforeCancel = Directory.GetFiles(outputDir, "long render to cancel_converted_*.mp4").ToHashSet();
        Click(Ui<Button>(window, "ConvertButton"));
        Check(Ui<Button>(window, "CancelButton").Visibility == Visibility.Visible &&
              !Ui<Button>(window, "ConvertButton").IsEnabled,
            "during encoding Cancel is visible and Convert is disabled");
        bool activeEncoder = false;
        for (int n = 0; n < 100; n++)
        {
            if (Ui<ProgressBar>(window,"Progress").Value > 0.05)
            { activeEncoder = true; break; }
            if (Ui<TextBlock>(window, "StatusText").Text.StartsWith("Conversion complete"))
                break;
            await Task.Delay(40);
        }
        Check(activeEncoder, "actual encoder reported progress before user clicked Cancel");
        Click(Ui<Button>(window, "CancelButton"));
        bool wasCancelled = false;
        for (int n = 0; n < 150; n++)
        {
            if (Ui<TextBlock>(window, "StatusText").Text.StartsWith("Conversion cancelled"))
            { wasCancelled = true; break; }
            if (Ui<TextBlock>(window, "StatusText").Text.StartsWith("Conversion complete")) break;
            await Task.Delay(50);
        }
        Check(wasCancelled, "Cancel button stops in-progress FFmpeg");
        Check(Directory.GetFiles(outputDir,"long render to cancel_converted_*.mp4")
            .All(path => beforeCancel.Contains(path)), "cancelled conversion leaves no new partial MP4");
        Check(Ui<Button>(window, "ConvertButton").IsEnabled,
            "Convert becomes available again after cancellation");
        SaveScreenshot(window, Path.Combine(dir,"cancelled-simple-ui.png"));

        // A missing/deleted file after successful import should produce a visible explanation.
        string deletedSource = Path.Combine(dir,"delete-after-import.mp4");
        File.Copy(media[0], deletedSource, true);
        await LoadAsync(window, deletedSource);
        File.Delete(deletedSource);
        Click(Ui<Button>(window,"ConvertButton"));
        Check(Ui<TextBlock>(window, "StatusText").Text.Contains("video was removed"),
            "deleting a selected file produces a specific conversion error");
        Check(!Ui<Button>(window, "ConvertButton").IsEnabled,
            "Convert is disabled after the source disappears");

        string corrupt=Path.Combine(dir,"invalid-video.mp4");
        await File.WriteAllTextAsync(corrupt,"not an actual video stream");
        await LoadAsync(window,corrupt);
        Check(!Ui<Button>(window,"ConvertButton").IsEnabled,
            "corrupted input blocks Convert");
        Check(Ui<TextBlock>(window,"StatusText").Text.StartsWith("Video import failed:",StringComparison.Ordinal),
            "invalid file displays a useful error");
        Check(Ui<Button>(window,"OpenOutputButton").Visibility == Visibility.Collapsed,
            "failed import does not expose a stale export");
        SaveScreenshot(window,Path.Combine(dir,"invalid-input-simple-ui.png"));

        var report = new {
            date_utc = DateTimeOffset.UtcNow,
            assertions,
            exports = observations,
            notes = "Automated WPF controls and real FFmpeg processes on Windows; native file picker and Explorer are injected at their boundaries, not manually operated with a mouse. Drag/drop path processing is exercised but not the physical Windows pointer drag gesture.",
            no_manual_options = CountManualControls(window) == 0
        };
        await File.WriteAllTextAsync(Path.Combine(dir,"results.json"),
            JsonSerializer.Serialize(report,new JsonSerializerOptions { WriteIndented=true }));
    }
}
