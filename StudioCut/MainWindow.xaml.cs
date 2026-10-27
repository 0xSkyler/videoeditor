using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StudioCut;

public partial class MainWindow : Window
{
    private string source = "";
    private string outputFolder = "";
    private string latestOutput = "";
    private MediaDetails? details;
    private CancellationTokenSource? conversionCancel;
    private bool loading, converting;
    private int importVersion;
    // Replaceable only for automated WPF acceptance tests. Production uses the native picker and Explorer.
    private Func<string?> choosePath = ShowNativeVideoPicker;
    private Action<string> openFolder = path => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

    public MainWindow()
    {
        InitializeComponent();
        try
        {
            outputFolder = SimpleConvertPlan.ResolveOutputFolder();
            OutputPathText.Text = "Automatically saves to: " + outputFolder;
        }
        catch (Exception ex)
        {
            OutputPathText.Text = "Output folder error: " + ex.Message;
        }
        RefreshButtons();
    }

    private void RefreshButtons()
    {
        ConvertButton.IsEnabled = !loading && !converting && details is not null &&
            !string.IsNullOrWhiteSpace(source) && File.Exists(source);
        ChooseVideoButton.IsEnabled = !converting;
        CancelButton.Visibility = converting ? Visibility.Visible : Visibility.Collapsed;
        OpenOutputButton.Visibility = !converting && File.Exists(latestOutput) ?
            Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Keep the one-click UI readable; FFmpeg raw stderr is not an actionable end-user message.</summary>
    private static string FriendlyError(Exception ex, bool importing)
    {
        if (ex is UnauthorizedAccessException)
            return "Windows denied access. Check the file or output-folder permissions.";
        if (ex is FileNotFoundException)
            return "The selected video is missing. Choose it again.";
        if (ex is IOException && ex.Message.Contains("timed out", StringComparison.OrdinalIgnoreCase))
            return "Reading this video took too long. Try a local copy.";
        if (ex.Message.Contains("Insufficient free disk", StringComparison.OrdinalIgnoreCase))
            return "Not enough free disk space. Free space in Videos and try again.";
        if (importing)
            return "This file is damaged or is not a supported video. Choose a playable MP4, MOV, MKV or AVI.";
        if (ex is IOException && (ex.Message.Contains("FFmpeg", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("ffprobe", StringComparison.OrdinalIgnoreCase)))
            return "The video could not be encoded. Check the source file and available disk space.";
        string message = ex.Message;
        return message.Length > 190 ? message[..190] + "..." : message;
    }

    private static string Duration(double seconds)
    {
        if (double.IsNaN(seconds) || seconds < 0) return "00:00";
        var time = TimeSpan.FromSeconds(seconds);
        return time.TotalHours >= 1 ? time.ToString(@"hh\:mm\:ss") : time.ToString(@"mm\:ss");
    }

    // Kept as a separate operation so import/drop behavior is testable without
    // opening the native system file dialog in automated Windows acceptance.
    private async Task LoadVideoAsync(string path)
    {
        if (converting) return;
        int generation = ++importVersion;
        source = "";
        details = null;
        latestOutput = "";
        OutputPathText.Text = "Automatically saves to: " + outputFolder;
        loading = true;
        Progress.Value = 0;
        SelectedVideoText.Text = Path.GetFileName(path);
        SelectedDetailsText.Text = "Inspecting the file...";
        DropTitle.Text = "Checking video";
        PosterImage.Source = null;
        PosterImage.Visibility = Visibility.Collapsed;
        EmptyPoster.Visibility = Visibility.Visible;
        StatusText.Text = "Checking video streams...";
        RefreshButtons();
        try
        {
            var media = await MediaEngine.ProbeAsync(path);
            if (generation != importVersion) return;
            source = path;
            details = media;
            SelectedDetailsText.Text =
                $"{media.Width} × {media.Height}  •  {media.Fps:0.##} FPS  •  {Duration(media.Duration)}  •  " +
                (media.AudioTracks == 0 ? "No audio" : $"{media.AudioTracks} audio track(s)");
            DropTitle.Text = "Video ready";
            StatusText.Text = "Ready. Click Convert to make a fresh MP4.";
        }
        catch (Exception ex)
        {
            if (generation != importVersion) return;
            DropTitle.Text = "Could not read video";
            SelectedDetailsText.Text = "Choose a supported video file.";
            StatusText.Text = "Video import failed: " + FriendlyError(ex, importing: true);
        }
        finally
        {
            if (generation == importVersion) { loading = false; RefreshButtons(); }
        }

        if (generation == importVersion && details is not null)
            _ = GenerateThumbnailAsync(path, generation);
    }

    private async Task GenerateThumbnailAsync(string path, int generation)
    {
        try
        {
            byte[] data = await MediaEngine.GeneratePosterAsync(path);
            if (generation != importVersion) return;
            using var stream = new MemoryStream(data);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            PosterImage.Source = image;
            PosterImage.Visibility = Visibility.Visible;
            EmptyPoster.Visibility = Visibility.Collapsed;
        }
        catch (Exception) { /* A thumbnail is optional; the FFmpeg export still works. */ }
    }

    private static string? ShowNativeVideoPicker()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a video",
            Filter = "Video files|*.mp4;*.mov;*.mkv;*.avi;*.webm;*.m4v;*.wmv;*.mts;*.m2ts|All files|*.*"
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private async void ChooseVideoClick(object sender, RoutedEventArgs e)
    {
        if (converting) return;
        try
        {
            string? path = choosePath();
            if (!string.IsNullOrWhiteSpace(path)) await LoadVideoAsync(path);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Could not choose video: " + ex.Message;
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        bool canDrop = !converting && e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = canDrop ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        DropZone.BorderBrush = canDrop ? new SolidColorBrush(Color.FromRgb(52, 211, 153)) :
            new SolidColorBrush(Color.FromRgb(65, 91, 119));
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        DropZone.BorderBrush = new SolidColorBrush(Color.FromRgb(65, 91, 119));
    }

    // The same path is used by actual Windows drag-and-drop and GUI tests.
    private async Task HandleDroppedPathsAsync(string[] paths)
    {
        if (converting) return;
        if (paths.Length != 1 || Directory.Exists(paths[0]))
        {
            StatusText.Text = "Drop exactly one video file, not a folder or several files.";
            return;
        }
        await LoadVideoAsync(paths[0]);
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        DropZone.BorderBrush = new SolidColorBrush(Color.FromRgb(65, 91, 119));
        if (converting || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            e.Handled = true;
            await HandleDroppedPathsAsync(paths);
        }
    }

    private async void ConvertClick(object sender, RoutedEventArgs e)
    {
        if (loading || converting) return;
        if (details is null || !File.Exists(source))
        {
            StatusText.Text = "Cannot convert: the video was removed or is not yet imported. Choose it again.";
            RefreshButtons();
            return;
        }

        Options settings;
        try
        {
            // An output location is automatic; no folder picker or encoding options.
            outputFolder = SimpleConvertPlan.ResolveOutputFolder();
            settings = SimpleConvertPlan.Create(source, outputFolder, details);
            var errors = MediaEngine.Validate(settings, details);
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("; ", errors));
        }
        catch (Exception ex)
        {
            StatusText.Text = "Cannot convert: " + FriendlyError(ex, importing: false);
            return;
        }

        converting = true;
        latestOutput = "";
        Progress.Value = 0;
        StatusText.Text = "Converting...";
        OutputPathText.Text = "Saving automatically to: " + outputFolder;
        conversionCancel = new CancellationTokenSource();
        RefreshButtons();
        bool finished = false;

        try
        {
            var reporter = new Progress<double>(value =>
            {
                if (!converting || finished) return;
                double percent = Math.Clamp(value * 100, 0, 100);
                Progress.Value = percent;
                StatusText.Text = $"Converting... {percent:0}%";
            });

            latestOutput = await MediaEngine.ExportAsync(
                settings, details, value => ((IProgress<double>)reporter).Report(value),
                conversionCancel.Token, "converted");
            finished = true;
            Progress.Value = 100;
            StatusText.Text = "Conversion complete. Your original video is unchanged.";
            OutputPathText.Text = "Saved: " + latestOutput;
        }
        catch (OperationCanceledException)
        {
            finished = true;
            StatusText.Text = "Conversion cancelled. No partial video was kept.";
        }
        catch (Exception ex)
        {
            finished = true;
            StatusText.Text = "Conversion failed: " + FriendlyError(ex, importing: false);
        }
        finally
        {
            converting = false;
            conversionCancel?.Dispose();
            conversionCancel = null;
            RefreshButtons();
        }
    }

    private void CancelClick(object sender, RoutedEventArgs e)
    {
        conversionCancel?.Cancel();
        StatusText.Text = "Cancelling...";
    }

    private void OpenOutputClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!File.Exists(latestOutput))
                throw new FileNotFoundException("The completed export cannot be found.");
            openFolder(Path.GetDirectoryName(latestOutput)!);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Cannot open output folder: " + ex.Message;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        conversionCancel?.Cancel();
        base.OnClosed(e);
    }
}
