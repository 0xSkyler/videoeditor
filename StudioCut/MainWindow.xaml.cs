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
            StatusText.Text = "Video import failed: " + ex.Message;
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

    private async void ChooseVideoClick(object sender, RoutedEventArgs e)
    {
        if (converting) return;
        var dialog = new OpenFileDialog
        {
            Title = "Choose a video",
            Filter = "Video files|*.mp4;*.mov;*.mkv;*.avi;*.webm;*.m4v;*.wmv;*.mts;*.m2ts|All files|*.*"
        };
        if (dialog.ShowDialog() == true) await LoadVideoAsync(dialog.FileName);
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

    private async void OnDrop(object sender, DragEventArgs e)
    {
        DropZone.BorderBrush = new SolidColorBrush(Color.FromRgb(65, 91, 119));
        if (converting || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
        {
            e.Handled = true;
            await LoadVideoAsync(paths[0]);
        }
    }

    private async void ConvertClick(object sender, RoutedEventArgs e)
    {
        if (loading || converting || details is null || !File.Exists(source)) return;

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
            StatusText.Text = "Cannot convert: " + ex.Message;
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
            StatusText.Text = "Conversion failed: " + ex.Message;
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
            Process.Start(new ProcessStartInfo(Path.GetDirectoryName(latestOutput)!) { UseShellExecute = true });
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
