using Microsoft.Win32;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
namespace StudioCut;
public partial class MainWindow : Window
{
    private string source = "", music = "", overlay = "", folder = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
    private MediaDetails? details;
    private CancellationTokenSource? convertCancellation;
    private bool busy;
    // XAML option-change events can fire before all named controls exist.
    private bool initialized;
    private int loadVersion;
    public MainWindow()
    {
        InitializeComponent();
        AudioMode.SelectedIndex = 0; Format.SelectedIndex = 0; Quality.SelectedIndex = 1;
        FolderName.Text = folder; initialized = true; Refresh();
    }
    private static string Selected(ComboBox c) => (c.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
    private static double Numeric(TextBox box, double fallback = 0) => double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : double.NaN;
    private static int Int(TextBox box) => int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : -999;
    private Options ReadOptions() => new()
    {
        Source = source, Music = music, Overlay = overlay, OutputFolder = folder,
        Container = Selected(Format), Quality = Selected(Quality), AudioMode = Selected(AudioMode),
        RightsVideo = RightsVideo.IsChecked == true, RightsAudio = RightsAudio.IsChecked == true,
        Hardware = Hardware.IsChecked == true, Normalize = Normalize.IsChecked == true,
        Denoise = Denoise.IsChecked == true, VoiceEnhance = VoiceEnhance.IsChecked == true,
        AudioFade = AudioFade.IsChecked == true, VideoFade = VideoFade.IsChecked == true,
        Watermark = Watermark.IsChecked == true,
        Start = Numeric(TrimStart), End = Numeric(TrimEnd), Volume = Numeric(Volume) / 100,
        MusicVolume = Numeric(MusicVolume) / 100, Bass = Numeric(Bass), Treble = Numeric(Treble),
        Brightness = Numeric(Brightness), Contrast = Numeric(Contrast), Saturation = Numeric(Saturation),
        Width = Int(VideoWidth), Height = Int(VideoHeight), Fps = Int(VideoFps),
        CropX = Int(CropX), CropY = Int(CropY), CropW = Int(CropW), CropH = Int(CropH),
        OriginalAudioTrack = AudioTrack.SelectedIndex < 0 ? 0 : AudioTrack.SelectedIndex
    };
    private void Refresh()
    {
        if (!initialized) return;
        var errors = MediaEngine.Validate(ReadOptions(), details);
        ConvertButton.IsEnabled = !busy && errors.Count == 0;
        ValidationText.Text = busy ? "" : string.Join("  •  ", errors);
    }
    private void OptionChanged(object sender, RoutedEventArgs e) => Refresh();
    private async Task LoadVideo(string path)
    {
        if (busy) return;
        int version = ++loadVersion;
        source = path; details = null; Status.Text = "Analyzing video..."; Refresh();
        try
        {
            var probed = await MediaEngine.ProbeAsync(path);
            if (version != loadVersion) return;
            details = probed;
            Preview.Source = new Uri(Path.GetFullPath(path));
            AudioTrack.Items.Clear();
            for (int i = 0; i < details.AudioTracks; i++) AudioTrack.Items.Add(new ComboBoxItem { Content = $"Track {i + 1}" });
            if (details.AudioTracks > 0) AudioTrack.SelectedIndex = 0;
            VideoInfo.Text = $"{Path.GetFileName(path)}\n{details.Width} × {details.Height} | {details.Fps:0.##} FPS\n{TimeSpan.FromSeconds(details.Duration):g} | {details.AudioTracks} audio track(s)\n{details.Size / 1048576.0:0.0} MB";
            Status.Text = "Video loaded";
        }
        catch (Exception ex) { if (version != loadVersion) return; VideoInfo.Text = ex.Message; Status.Text = "Could not open video"; }
        Refresh();
    }
    private async void OpenVideoClick(object sender, RoutedEventArgs e)
    {
        var d = new OpenFileDialog { Filter = "Video files|*.mp4;*.mov;*.mkv;*.avi;*.webm;*.m4v|All files|*.*" };
        if (d.ShowDialog() == true) await LoadVideo(d.FileName);
    }
    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0) await LoadVideo(files[0]);
    }
    private void MusicClick(object sender, RoutedEventArgs e)
    {
        var d = new OpenFileDialog { Filter = "Audio files|*.mp3;*.wav;*.m4a;*.aac;*.flac;*.ogg|All files|*.*" };
        if (d.ShowDialog() == true) { music = d.FileName; MusicName.Text = Path.GetFileName(music); Refresh(); }
    }
    private void OverlayClick(object sender, RoutedEventArgs e)
    {
        var d = new OpenFileDialog { Filter = "Image files|*.png;*.jpg;*.jpeg;*.webp" };
        if (d.ShowDialog() == true) { overlay = d.FileName; Watermark.IsChecked = true; Refresh(); }
    }
    private void FolderClick(object sender, RoutedEventArgs e)
    {
        var d = new Microsoft.Win32.OpenFolderDialog { Title = "Choose export folder" };
        if (d.ShowDialog() == true) { folder = d.FolderName; FolderName.Text = folder; initialized = true; Refresh(); }
    }
    private void PlayClick(object sender, RoutedEventArgs e) => Preview.Play();
    private void PauseClick(object sender, RoutedEventArgs e) => Preview.Pause();
    private void PreviewFailed(object sender, ExceptionRoutedEventArgs e) => Status.Text = "Windows cannot preview this codec; FFmpeg export may still work.";
    private async void ConvertClick(object sender, RoutedEventArgs e)
    {
        if (busy || details == null) return;
        var options = ReadOptions();
        if (MediaEngine.Validate(options, details).Count > 0) { Refresh(); return; }
        busy = true; convertCancellation = new CancellationTokenSource();
        CancelButton.IsEnabled = true; Progress.Value = 0; Status.Text = "Encoding..."; Refresh();
        try
        {
            var progress = new Progress<double>(p => { Progress.Value = p * 100; Status.Text = $"Encoding {p:P0}"; });
            await MediaEngine.ExportAsync(options, details, p => ((IProgress<double>)progress).Report(p), convertCancellation.Token);
            Status.Text = "Completed. Open the selected export folder to find the new file.";
        }
        catch (OperationCanceledException) { Status.Text = "Cancelled"; }
        catch (Exception ex) { Status.Text = "Export failed"; MessageBox.Show(ex.Message, "Encoding error", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { busy = false; CancelButton.IsEnabled = false; convertCancellation?.Dispose(); convertCancellation = null; Refresh(); }
    }
    private void CancelClick(object sender, RoutedEventArgs e) => convertCancellation?.Cancel();
}
