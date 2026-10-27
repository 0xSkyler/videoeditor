using Microsoft.Win32;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace StudioCut;

public partial class MainWindow : Window
{
    private string source = "", music = "", overlay = "";
    private string folder = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
    private string lastOutput = "";
    private MediaDetails? details;
    private CancellationTokenSource? convertCancellation;
    private readonly DispatcherTimer playbackTimer;
    private bool busy, initialized, checkingAsset, updatingSeek, previewBusy, autoReviewed;
    private bool lastExportUsesConfirmedHiggsfieldVoice;
    private int loadVersion;

    public MainWindow()
    {
        InitializeComponent();
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            folder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            folder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        AudioMode.SelectedIndex = 0;
        Format.SelectedIndex = 0;
        Quality.SelectedIndex = 1;
        FolderName.Text = folder;
        playbackTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        playbackTimer.Tick += (_, _) => UpdatePlaybackPosition();
        playbackTimer.Start();
        initialized = true;
        Refresh();
    }

    private static string Selected(ComboBox c) =>
        (c.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
    private static double Numeric(TextBox box) =>
        double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : double.NaN;
    private static int Int(TextBox box) =>
        int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : -999;
    private static string Clock(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) seconds = 0;
        var t = TimeSpan.FromSeconds(seconds);
        return t.TotalHours >= 1 ? t.ToString(@"hh\:mm\:ss") : t.ToString(@"mm\:ss");
    }

    private Options ReadOptions() => new()
    {
        Source = source, Music = music, Overlay = overlay, OutputFolder = folder,
        Container = Selected(Format), Quality = Selected(Quality), AudioMode = Selected(AudioMode),
        RightsVideo = RightsVideo.IsChecked == true,
        RightsAudio = RightsAudio.IsChecked == true ||
            (HiggsfieldVoiceConfirmed.IsChecked == true && Selected(AudioMode) == "Keep original"),
        Hardware = Hardware.IsChecked == true, Normalize = Normalize.IsChecked == true,
        Denoise = Denoise.IsChecked == true, VoiceEnhance = VoiceEnhance.IsChecked == true,
        AudioFade = AudioFade.IsChecked == true, VideoFade = VideoFade.IsChecked == true,
        Watermark = Watermark.IsChecked == true,
        Start = Numeric(TrimStart), End = Numeric(TrimEnd), Volume = Numeric(Volume) / 100,
        MusicVolume = Numeric(MusicVolume) / 100, Offset = Numeric(MusicOffset), Bass = Numeric(Bass), Treble = Numeric(Treble),
        Brightness = Numeric(Brightness), Contrast = Numeric(Contrast), Saturation = Numeric(Saturation),
        Width = Int(VideoWidth), Height = Int(VideoHeight), Fps = Int(VideoFps),
        CropX = Int(CropX), CropY = Int(CropY), CropW = Int(CropW), CropH = Int(CropH),
        Text = TextOverlay.Text,
        OriginalAudioTrack = AudioTrack.SelectedIndex < 0 ? 0 : AudioTrack.SelectedIndex
    };

    private void Refresh()
    {
        if (!initialized) return;
        var options = ReadOptions();
        var errors = MediaEngine.Validate(options, details);
        ConvertButton.IsEnabled = !busy && !checkingAsset && !previewBusy && errors.Count == 0;
        PreviewEditedButton.IsEnabled = details != null && !busy && !checkingAsset && !previewBusy;
        EditorScroll.IsEnabled = !busy && !checkingAsset && !previewBusy;
        ValidationText.Text = busy ? "Exporting with the selected settings..."
            : checkingAsset ? "Checking selected media file..."
            : previewBusy ? "Generating edited still preview..."
            : errors.Count == 0 ? "Ready to export. This creates a new video and keeps the source unchanged."
            : string.Join("  •  ", errors.Take(3)) + (errors.Count > 3 ? $"  •  +{errors.Count - 3} more issues" : "");
        ValidationText.Foreground = errors.Count > 0 && !busy && !checkingAsset
            ? System.Windows.Media.Brushes.LightSalmon : System.Windows.Media.Brushes.LightGreen;
        if (details != null && double.IsFinite(options.Start) && double.IsFinite(options.End)
            && options.Start >= 0 && options.Start < details.Duration
            && (options.End == 0 || options.End > options.Start))
        {
            double mb = MediaEngine.EstimateBytes(options, details) / 1048576.0;
            EstimateText.Text = $"Estimated export: about {mb:0.0} MB (varies with video content)";
        }
        else EstimateText.Text = "";
        OpenExportButton.IsEnabled = File.Exists(lastOutput);
        AutoRightsButton.IsEnabled = !busy && !checkingAsset && !previewBusy
            && !autoReviewed && (File.Exists(lastOutput) || details != null);
    }

    private void OptionChanged(object sender, RoutedEventArgs e) => Refresh();

    private void UpdatePlaybackPosition()
    {
        if (details == null || updatingSeek || !Preview.NaturalDuration.HasTimeSpan) return;
        double value = Math.Clamp(Preview.Position.TotalSeconds, 0, details.Duration);
        updatingSeek = true;
        SeekSlider.Value = value;
        updatingSeek = false;
        PlaybackTime.Text = $"{Clock(value)} / {Clock(details.Duration)}";
    }

    private void SeekChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!initialized || updatingSeek || details == null) return;
        PlaybackTime.Text = $"{Clock(SeekSlider.Value)} / {Clock(details.Duration)}";
        try { Preview.Position = TimeSpan.FromSeconds(Math.Clamp(SeekSlider.Value, 0, details.Duration)); }
        catch (InvalidOperationException) { /* Codec may not be previewable by Windows */ }
    }

    private void PreviewOpened(object sender, RoutedEventArgs e)
    {
        try { Preview.Pause(); } catch (InvalidOperationException) { }
        UpdatePlaybackPosition();
    }

    private void PreviewEnded(object sender, RoutedEventArgs e)
    {
        Preview.Pause();
        Preview.Position = TimeSpan.Zero;
        if (PosterImage.Source != null) PosterImage.Visibility = Visibility.Visible;
        UpdatePlaybackPosition();
    }

    private void SetStartFromPreviewClick(object sender, RoutedEventArgs e)
    {
        if (details == null) return;
        TrimStart.Text = Math.Min(details.Duration - .01, SeekSlider.Value).ToString("0.##", CultureInfo.InvariantCulture);
    }

    private void SetEndFromPreviewClick(object sender, RoutedEventArgs e)
    {
        if (details == null) return;
        TrimEnd.Text = Math.Min(details.Duration, SeekSlider.Value).ToString("0.##", CultureInfo.InvariantCulture);
    }

    private async Task LoadVideo(string path)
    {
        if (busy || checkingAsset) return;
        int version = ++loadVersion;
        source = path;
        details = null;
        lastOutput = "";
        autoReviewed = false;
        lastExportUsesConfirmedHiggsfieldVoice = false;
        HiggsfieldVoiceConfirmed.IsChecked = false;
        OutputInfo.Text = "";
        AutoRightsInfo.Text = "Auto mode uses your most recent export, or your imported source. It cannot remove copyright or bypass Content ID.";
        VideoInfo.Text = "";
        Preview.Stop();
        Preview.Source = null;
        // A new file must never inherit an out-of-bounds crop or trim range.
        TrimStart.Text = "0"; TrimEnd.Text = "0";
        CropX.Text = "0"; CropY.Text = "0"; CropW.Text = "0"; CropH.Text = "0";
        PosterImage.Source = null;
        PosterImage.Visibility = Visibility.Collapsed;
        PreviewNote.Text = "Preview displays original source (effects are applied only on export).";
        AudioTrack.Items.Clear();
        RightsVideo.IsChecked = false;
        RightsAudio.IsChecked = false;
        SeekSlider.Maximum = 1;
        SeekSlider.Value = 0;
        PlaybackTime.Text = "00:00 / 00:00";
        Status.Text = "Analyzing video...";
        Refresh();
        try
        {
            var probed = await MediaEngine.ProbeAsync(path);
            if (version != loadVersion) return;
            details = probed;
            AudioMode.SelectedIndex = details.AudioTracks > 0 ? 0 : 1;
            for (int i = 0; i < details.AudioTracks; i++)
                AudioTrack.Items.Add(new ComboBoxItem { Content = $"Track {i + 1}" });
            if (details.AudioTracks > 0) AudioTrack.SelectedIndex = 0;
            SeekSlider.Maximum = details.Duration;
            PlaybackTime.Text = $"00:00 / {Clock(details.Duration)}";
            Preview.Source = new Uri(Path.GetFullPath(path));
            try
            {
                byte[] poster = await MediaEngine.GeneratePosterAsync(path);
                if (version != loadVersion) return;
                using var buffer = new MemoryStream(poster);
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = buffer;
                image.EndInit();
                image.Freeze();
                PosterImage.Source = image;
                PosterImage.Visibility = Visibility.Visible;
            }
            catch (Exception)
            {
                if (version != loadVersion) return;
                PreviewNote.Text = "Thumbnail unavailable for this codec; try Play or export directly.";
            }
            VideoInfo.Text = $"{Path.GetFileName(path)}\n{details.Width} × {details.Height} | {details.Fps:0.##} FPS\n{Clock(details.Duration)} | {details.AudioTracks} audio track(s)\n{details.Size / 1048576.0:0.0} MB";
            Status.Text = details.AudioTracks == 0
                ? "Video loaded without audio; Mute mode selected automatically."
                : "Video loaded. Confirm rights, choose settings, and export.";
        }
        catch (Exception ex)
        {
            if (version != loadVersion) return;
            VideoInfo.Text = ex.Message;
            Preview.Source = null;
            Status.Text = "Could not open video";
        }
        Refresh();
    }

    private async void OpenVideoClick(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        var d = new OpenFileDialog
        {
            Filter = "Video files|*.mp4;*.mov;*.mkv;*.avi;*.webm;*.m4v|All files|*.*"
        };
        if (d.ShowDialog() == true) await LoadVideo(d.FileName);
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop)
            && e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            await LoadVideo(files[0]);
    }

    private async void MusicClick(object sender, RoutedEventArgs e)
    {
        if (busy || checkingAsset) return;
        var d = new OpenFileDialog
        {
            Filter = "Audio files|*.mp3;*.wav;*.m4a;*.aac;*.flac;*.ogg|All files|*.*"
        };
        if (d.ShowDialog() != true) return;
        music = "";
        MusicName.Text = "Checking...";
        checkingAsset = true;
        Refresh();
        try
        {
            await MediaEngine.CheckAudioAsync(d.FileName);
            music = d.FileName;
            MusicName.Text = Path.GetFileName(music);
            if (AudioMode.SelectedIndex < 2) AudioMode.SelectedIndex = 2;
            RightsAudio.IsChecked = false;
            HiggsfieldVoiceConfirmed.IsChecked = false;
            Status.Text = "External music loaded. Confirm its rights separately before converting.";
        }
        catch (Exception ex)
        {
            MusicName.Text = "Invalid music file";
            MessageBox.Show(ex.Message, "Cannot use this audio file", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { checkingAsset = false; Refresh(); }
    }

    private async void OverlayClick(object sender, RoutedEventArgs e)
    {
        if (busy || checkingAsset) return;
        var d = new OpenFileDialog { Filter = "Image files|*.png;*.jpg;*.jpeg;*.webp" };
        if (d.ShowDialog() != true) return;
        overlay = "";
        Watermark.IsChecked = false;
        checkingAsset = true;
        Refresh();
        try
        {
            await MediaEngine.CheckImageAsync(d.FileName);
            overlay = d.FileName;
            Watermark.IsChecked = true;
            OverlayName.Text = Path.GetFileName(overlay);
            RightsVideo.IsChecked = false;
            Status.Text = "Overlay loaded and fitted to the corner. Confirm visual rights.";
        }
        catch (Exception ex)
        {
            OverlayName.Text = "Invalid overlay";
            MessageBox.Show(ex.Message, "Cannot use this image", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { checkingAsset = false; Refresh(); }
    }

    private void FolderClick(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        var d = new Microsoft.Win32.OpenFolderDialog { Title = "Choose export folder" };
        if (d.ShowDialog() == true) { folder = d.FolderName; FolderName.Text = folder; Refresh(); }
    }

    private void OpenFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("Choose a valid export folder first.");
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { folder }, UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Cannot open folder", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void OpenExportClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!File.Exists(lastOutput)) throw new FileNotFoundException("The exported file could not be found.");
            Process.Start(new ProcessStartInfo(lastOutput) { UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Cannot open exported video", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private static BitmapImage BitmapFromBytes(byte[] pixels)
    {
        using var buffer = new MemoryStream(pixels);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = buffer;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private async void PreviewEditedClick(object sender, RoutedEventArgs e)
    {
        if (busy || previewBusy || checkingAsset || details == null) return;
        previewBusy = true;
        Refresh();
        try
        {
            var settings = ReadOptions();
            // The preview is local and does not imply permission to publish.
            byte[] pixels = await MediaEngine.GenerateEditedPreviewAsync(settings, details, SeekSlider.Value);
            Preview.Pause();
            PosterImage.Source = BitmapFromBytes(pixels);
            PosterImage.Visibility = Visibility.Visible;
            PreviewNote.Text = "Edited still preview (crop, color, logo, caption). Audio and fades require export.";
            Status.Text = "Edited preview ready. The original source is unchanged.";
        }
        catch (Exception ex)
        {
            Status.Text = "Could not preview selected effects";
            MessageBox.Show(ex.Message, "Edited preview", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { previewBusy = false; Refresh(); }
    }

    private void PlayClick(object sender, RoutedEventArgs e)
    {
        if (details != null)
        {
            PosterImage.Visibility = Visibility.Collapsed;
            Preview.Play();
        }
    }

    private void PauseClick(object sender, RoutedEventArgs e)
    {
        if (details != null) Preview.Pause();
    }

    private void PreviewFailed(object sender, ExceptionRoutedEventArgs e)
    {
        PreviewNote.Text = "Windows cannot play this codec. A still preview is shown; FFmpeg export may still work.";
        if (PosterImage.Source != null) PosterImage.Visibility = Visibility.Visible;
    }

    private async void ConvertClick(object sender, RoutedEventArgs e)
    {
        if (busy || checkingAsset || previewBusy || details == null) return;
        var options = ReadOptions();
        if (MediaEngine.Validate(options, details).Count > 0) { Refresh(); return; }
        await ExecuteExportAsync(options, details, false, null);
    }

    private async void AutoRightsClick(object sender, RoutedEventArgs e)
    {
        if (busy || checkingAsset || previewBusy || autoReviewed || (!File.Exists(lastOutput) && details == null)) return;

        string exportedSource = File.Exists(lastOutput) ? lastOutput : source;
        MediaDetails media;
        checkingAsset = true;
        AutoRightsInfo.Text = "Checking the selected video's streams before automatic processing...";
        Refresh();
        try
        {
            media = await MediaEngine.ProbeAsync(exportedSource);
        }
        catch (Exception ex)
        {
            AutoRightsInfo.Text = "Could not inspect the selected video. No changes were made.";
            MessageBox.Show(ex.Message, "Auto copyright-risk review", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        finally
        {
            checkingAsset = false;
            Refresh();
        }

        // Rights are supplied by the user. No offline program can reliably
        // determine whether a track or image is copyrighted by someone else.
        bool exportedFileSelected = File.Exists(lastOutput);
        bool confirmedHiggsfieldSoundtrack = HiggsfieldVoiceConfirmed.IsChecked == true
            && (!exportedFileSelected || lastExportUsesConfirmedHiggsfieldVoice);
        var current = new Options
        {
            Source = exportedSource,
            OutputFolder = folder,
            RightsVideo = RightsVideo.IsChecked == true,
            RightsAudio = RightsAudio.IsChecked == true || confirmedHiggsfieldSoundtrack
        };
        AutoRightsPlan plan = AutoRightsPlanner.Create(current, media);
        if (!plan.CanExport || plan.Export is not Options autoOptions)
        {
            string reason = string.Join(Environment.NewLine, plan.Blockers);
            AutoRightsInfo.Text = "Blocked: " + reason;
            MessageBox.Show(reason + Environment.NewLine + Environment.NewLine
                + "Changing bitrate, codec or frames cannot repair missing usage rights.",
                "Rights must be confirmed", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (plan.RemovesUnconfirmedAudio)
        {
            var response = MessageBox.Show(
                "The narration may be your original Higgsfield-generated AI speech."
                + Environment.NewLine + "If you are allowed to use that generated voice and any included music,"
                + " tick the Higgsfield AI narration confirmation and try again."
                + Environment.NewLine + Environment.NewLine
                + "Otherwise Auto mode can mute ALL audio, including your narration and any music. "
                + "It cannot separate licensed speech from unlicensed music in an already mixed track."
                + Environment.NewLine + Environment.NewLine
                + "Continue with a completely silent copy?",
                "Confirm automatic audio removal",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (response != MessageBoxResult.Yes)
            {
                AutoRightsInfo.Text = "Auto review cancelled. Your previous export is unchanged.";
                return;
            }
        }

        AutoRightsInfo.Text = plan.RemovesUnconfirmedAudio
            ? "Creating high-quality MP4 with all unconfirmed audio removed. No copyright clearance is implied."
            : confirmedHiggsfieldSoundtrack
                ? "Preserving your confirmed Higgsfield narration and creating a high-quality MP4. No copyright clearance is implied."
                : "Creating high-quality MP4 with confirmed audio preserved. No copyright clearance is implied.";
        await ExecuteExportAsync(autoOptions, media, true, plan);
    }

    private async Task ExecuteExportAsync(Options options, MediaDetails media, bool automatic, AutoRightsPlan? plan)
    {
        if (busy) return;
        busy = true;
        string previousOutput = lastOutput;
        if (!automatic) OutputInfo.Text = "";
        convertCancellation = new CancellationTokenSource();
        CancelButton.IsEnabled = true;
        Progress.Value = 0;
        Status.Text = automatic ? "Auto-review encoding..." : "Encoding...";
        Refresh();
        bool finished = false;
        try
        {
            var progress = new Progress<double>(p =>
            {
                if (!busy || finished) return;
                Progress.Value = p * 100;
                Status.Text = automatic ? $"Auto-review encoding {p:P0}" : $"Encoding {p:P0}";
            });
            string output = await MediaEngine.ExportAsync(options, media,
                p => ((IProgress<double>)progress).Report(p),
                convertCancellation.Token, automatic ? "auto_reviewed" : "edited");
            finished = true;
            lastOutput = output;
            autoReviewed = automatic;
            if (!automatic)
                lastExportUsesConfirmedHiggsfieldVoice = HiggsfieldVoiceConfirmed.IsChecked == true
                    && options.AudioMode == "Keep original" && media.AudioTracks > 0;
            Progress.Value = 100;
            double size = new FileInfo(output).Length / 1048576.0;
            OutputInfo.Text = $"Saved: {output} ({size:0.0} MB)";
            Status.Text = automatic
                ? "Auto-review copy completed. Check the result and your licenses before uploading."
                : "Completed. Open the exported video or folder to inspect it.";
            if (automatic)
            {
                string track = plan?.RemovesUnconfirmedAudio == true
                    ? "Unconfirmed audio muted, including any narration."
                    : media.AudioTracks == 0 ? "Silent source remained silent."
                    : (HiggsfieldVoiceConfirmed.IsChecked == true && options.RightsAudio)
                        ? "Confirmed Higgsfield AI narration retained."
                        : "Confirmed audio retained.";
                AutoRightsInfo.Text = "Completed: MP4 H.264/AAC where applicable; source resolution and FPS preserved. "
                    + track + " Copyright NOT independently cleared; Content ID claims remain possible.";
            }
            else
            {
                AutoRightsInfo.Text = "Initial export ready. Confirm permitted use of Higgsfield AI narration to keep it; "
                    + "auto review cannot remove copyright or guarantee Content ID clearance.";
            }
        }
        catch (OperationCanceledException)
        {
            finished = true;
            lastOutput = previousOutput;
            Status.Text = "Cancelled. No incomplete output was kept.";
            if (automatic) AutoRightsInfo.Text = "Automatic review cancelled. Original export remains unchanged.";
        }
        catch (Exception ex)
        {
            finished = true;
            lastOutput = previousOutput;
            Status.Text = automatic ? "Automatic review failed" : "Export failed";
            if (automatic) AutoRightsInfo.Text = "Auto mode failed. Original export remains unchanged.";
            MessageBox.Show(ex.Message, "Encoding error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            busy = false;
            CancelButton.IsEnabled = false;
            convertCancellation?.Dispose();
            convertCancellation = null;
            Refresh();
        }
    }

    private void CancelClick(object sender, RoutedEventArgs e) => convertCancellation?.Cancel();

    protected override void OnClosed(EventArgs e)
    {
        if (busy) convertCancellation?.Cancel();
        playbackTimer.Stop();
        Preview.Stop();
        base.OnClosed(e);
    }
}
