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
    private static readonly List<object> measurements = new();

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
                await RunUserAcceptanceAsync(window);
                Console.WriteLine($"GUI ACCEPTANCE PASS: {assertions} assertions, 3 user exports.");
            }
            catch (Exception ex)
            {
                result = 1;
                Console.Error.WriteLine("GUI ACCEPTANCE FAILED: " + ex);
            }
            finally
            {
                app.Dispatcher.BeginInvoke(() => app.Shutdown());
            }
        };
        window.Show();
        app.Run(window);
        return result;
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAILED: " + description);
        assertions++;
        Console.WriteLine("[GUI PASS] " + description);
    }

    private static T Ui<T>(MainWindow window, string name) where T : FrameworkElement =>
        window.FindName(name) as T ?? throw new InvalidOperationException("Missing UI element: " + name);

    private static void Click(Button button) =>
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));

    private static void PrivateField(MainWindow window, string name, object value)
    {
        var field = typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new Exception("Missing field " + name);
        field.SetValue(window, value);
    }

    private static async Task LoadAsync(MainWindow window, string file)
    {
        var method = typeof(MainWindow).GetMethod("LoadVideo", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new Exception("Missing LoadVideo");
        await ((Task?)method.Invoke(window, new object[] { file }) ?? throw new Exception("Load failed"));
    }

    private static async Task ExecAsync(params string[] arguments)
    {
        var psi = new ProcessStartInfo(MediaEngine.Tool("ffmpeg"))
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardError = true, RedirectStandardOutput = true
        };
        foreach (var item in arguments) psi.ArgumentList.Add(item);
        using var proc = Process.Start(psi) ?? throw new Exception("Failed to start FFmpeg fixture.");
        var stdErrTask = proc.StandardError.ReadToEndAsync();
        var stdOutTask = proc.StandardOutput.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        await proc.WaitForExitAsync(timeout.Token);
        string stderr = await stdErrTask;
        await stdOutTask;
        if (proc.ExitCode != 0) throw new Exception("Fixture FFmpeg failed: " + stderr);
    }

    private static async Task MakeSamplesAsync(string dir)
    {
        await ExecAsync("-hide_banner", "-loglevel", "error", "-y",
            "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=24",
            "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=44100",
            "-t", "4", "-c:v", "libx264", "-preset", "ultrafast", "-crf", "23",
            "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "128k",
            Path.Combine(dir, "sample.mp4"));
        await ExecAsync("-hide_banner", "-loglevel", "error", "-y",
            "-f", "lavfi", "-i", "sine=frequency=330:sample_rate=44100",
            "-t", "5", "-c:a", "pcm_s16le", Path.Combine(dir, "music.wav"));
        await ExecAsync("-hide_banner", "-loglevel", "error", "-y",
            "-f", "lavfi", "-i", "color=c=red:size=1280x720",
            "-frames:v", "1", "-update", "1", Path.Combine(dir, "large-overlay.png"));
        await ExecAsync("-hide_banner", "-loglevel", "error", "-y", "-i",
            Path.Combine(dir, "sample.mp4"), "-map", "0:v:0", "-c:v", "copy", "-an",
            Path.Combine(dir, "silent.mp4"));
    }

    private static async Task<string> ExportViaUiAsync(MainWindow window, string dir, string scenario)
    {
        var button = Ui<Button>(window, "ConvertButton");
        Check(button.IsEnabled, scenario + ": Start Convert enabled when settings valid");
        var before = Directory.GetFiles(dir, "*_edited_*.mp4").ToHashSet();
        var sw = Stopwatch.StartNew();
        Click(button);
        bool completed = false;
        string status = "";
        for (int i = 0; i < 250; i++)
        {
            status = Ui<TextBlock>(window, "Status").Text;
            if (status.StartsWith("Completed.", StringComparison.Ordinal)) { completed = true; break; }
            if (status is "Export failed" or "Cancelled") break;
            await Task.Delay(150);
        }
        sw.Stop();
        Check(completed, scenario + ": UI reports export completion (status: " + status + ")");
        var files = Directory.GetFiles(dir, "*_edited_*.mp4").Where(x => !before.Contains(x)).ToArray();
        Check(files.Length == 1, scenario + ": exactly one fresh output created");
        string path = files[0];
        Check(new FileInfo(path).Length > 4000, scenario + ": output nonempty");
        Check(Ui<Button>(window, "OpenExportButton").IsEnabled, scenario + ": can open exported video");
        Check(Ui<TextBlock>(window, "OutputInfo").Text.Contains(Path.GetFileName(path)), scenario + ": output path displayed");
        Check(Ui<ProgressBar>(window, "Progress").Value >= 99.0, scenario + ": progress reaches completion");
        var probed = await MediaEngine.ProbeAsync(path);
        Check(probed.Width == 320 && probed.Height == 180, scenario + ": output resolution preserved");
        measurements.Add(new
        {
            scenario, seconds = Math.Round(sw.Elapsed.TotalSeconds, 3), output_bytes = new FileInfo(path).Length,
            duration = Math.Round(probed.Duration, 2), video_width = probed.Width,
            video_height = probed.Height, audio_tracks = probed.AudioTracks
        });
        return path;
    }

    private static void Screenshot(MainWindow window, string path)
    {
        try
        {
            window.UpdateLayout();
            int width = Math.Max(800, (int)window.ActualWidth);
            int height = Math.Max(620, (int)window.ActualHeight);
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(path);
            encoder.Save(stream);
            Console.WriteLine("[GUI] screenshot: " + path);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[GUI] screenshot unavailable on Windows runner: " + ex.Message);
        }
    }

    private static async Task RunUserAcceptanceAsync(MainWindow window)
    {
        var dir = Path.GetFullPath("ux-results");
        Directory.CreateDirectory(dir);
        await MakeSamplesAsync(dir);
        string source = Path.Combine(dir, "sample.mp4");
        string originalHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(source)));
        var convert = Ui<Button>(window, "ConvertButton");
        Check(!convert.IsEnabled, "initial Start Convert disabled");
        await LoadAsync(window, source);
        Check(Ui<TextBlock>(window, "VideoInfo").Text.Contains("320 × 180"), "source dimensions visible");
        Check(Ui<Image>(window, "PosterImage").Source is BitmapSource poster && poster.PixelWidth > 0, "first-frame poster generated from actual source");
        Check(Ui<Image>(window, "PosterImage").Visibility == Visibility.Visible, "preview poster visible before Play");
        Check(Ui<CheckBox>(window, "Normalize").Foreground is SolidColorBrush textBrush
            && textBrush.Color.R > 180 && textBrush.Color.G > 180 && textBrush.Color.B > 180,
            "dark-theme checkbox text has light foreground");
        Check(Ui<ComboBox>(window, "AudioMode").SelectedIndex == 0, "normal source defaults to keep audio");
        Check(!convert.IsEnabled, "copyright confirmations required");
        Ui<CheckBox>(window, "RightsVideo").IsChecked = true;
        Ui<CheckBox>(window, "RightsAudio").IsChecked = true;
        Check(convert.IsEnabled, "valid source and rights enable conversion");
        Ui<TextBox>(window, "TrimEnd").Text = "bad";
        Check(!convert.IsEnabled, "invalid trim disables conversion");
        Ui<TextBox>(window, "TrimEnd").Text = "2.5";
        Ui<TextBox>(window, "TrimStart").Text = "0.5";
        Ui<Slider>(window, "SeekSlider").Value = 1.25;
        Click(Ui<Button>(window, "SetStartButton"));
        Check(Ui<TextBox>(window, "TrimStart").Text == "1.25", "playhead sets trim start");
        Ui<TextBox>(window, "TrimStart").Text = "0.5";

        // This simulates the chosen file after the native Windows OpenFileDialog.
        await MediaEngine.CheckAudioAsync(Path.Combine(dir, "music.wav"));
        await MediaEngine.CheckImageAsync(Path.Combine(dir, "large-overlay.png"));
        PrivateField(window, "music", Path.Combine(dir, "music.wav"));
        PrivateField(window, "overlay", Path.Combine(dir, "large-overlay.png"));
        PrivateField(window, "folder", dir);
        Ui<TextBlock>(window, "FolderName").Text = dir;
        Ui<ComboBox>(window, "AudioMode").SelectedIndex = 3;
        Ui<CheckBox>(window, "Watermark").IsChecked = true;
        Ui<CheckBox>(window, "VideoFade").IsChecked = true;
        Ui<TextBox>(window, "Brightness").Text = "0.05";
        Ui<TextBox>(window, "MusicVolume").Text = "25";
        Check(convert.IsEnabled, "mixed narration/music, trim, fade and large overlay accepted");
        Check(Ui<TextBlock>(window, "EstimateText").Text.Contains("Estimated export"), "size estimate displayed");
        var first = await ExportViaUiAsync(window, dir, "edited narration and music");
        var firstDetails = await MediaEngine.ProbeAsync(first);
        Check(Math.Abs(firstDetails.Duration - 2) < .22, "exported clip matches requested 2-second trim");
        Check(firstDetails.AudioTracks == 1, "music mix retains audio");

        Screenshot(window, Path.Combine(dir, "wpf-user-workflow.png"));
        var second = await ExportViaUiAsync(window, dir, "repeat export without overwrite");
        Check(first != second, "two consecutive exports have unique filenames");
        Check(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(source))) == originalHash,
            "original source bytes unchanged");

        await LoadAsync(window, Path.Combine(dir, "silent.mp4"));
        Check(Ui<ComboBox>(window, "AudioMode").SelectedIndex == 1, "silent source defaults to mute");
        Check(Ui<CheckBox>(window, "RightsVideo").IsChecked != true, "new source resets visual rights");
        Check(Ui<CheckBox>(window, "RightsAudio").IsChecked != true, "new source resets audio rights");
        Ui<CheckBox>(window, "RightsVideo").IsChecked = true;
        Ui<CheckBox>(window, "Watermark").IsChecked = false;
        Ui<TextBox>(window, "TrimStart").Text = "0";
        Ui<TextBox>(window, "TrimEnd").Text = "0";
        Check(convert.IsEnabled, "silent video export needs no audio rights");
        var silentOutput = await ExportViaUiAsync(window, dir, "silent source without soundtrack");
        var silentDetails = await MediaEngine.ProbeAsync(silentOutput);
        Check(silentDetails.AudioTracks == 0, "silent export remains silent");

        await LoadAsync(window, Path.Combine(dir, "does-not-exist.mp4"));
        Check(!convert.IsEnabled, "invalid source blocks conversion");
        Check(Ui<TextBlock>(window, "Status").Text == "Could not open video", "invalid source gives clear error");
        Check(Ui<MediaElement>(window, "Preview").Source == null, "invalid source clears stale preview");
        Check(Ui<Image>(window, "PosterImage").Source == null, "invalid source clears stale thumbnail");
        var result = new
        {
            test_date = DateTimeOffset.UtcNow, assertions, scenario_results = measurements,
            gui_peak_ram_mb = Math.Round(Process.GetCurrentProcess().PeakWorkingSet64 / 1048576.0, 1),
            coverage = "WPF instantiated and interacted with via controls; native file dialog and manual mouse actions not automated."
        };
        await File.WriteAllTextAsync(Path.Combine(dir, "gui_acceptance.json"),
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    }
}
