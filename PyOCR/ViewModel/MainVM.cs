using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PyOCR.ViewModel;

public partial class MainVM : ObservableObject
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(120) };
    private const string ServerUrl    = "http://127.0.0.1:5005/ocr/file";
    private const string ServerExe    = @"D:\dist\trocr_server.exe";
    private const string ServerAppData = @"D:\01- Pam\AppData";   // model cache lands here
    private const string ServerTemp    = @"D:\01- Pam\Temp";      // PyInstaller extraction here

    private Process? _serverProcess;
    private bool _serverReady;
    private bool _weStartedServer; // true only if this session launched the process

    [ObservableProperty] private BitmapImage? _previewSource;
    [ObservableProperty] private string _ocrText = string.Empty;
    [ObservableProperty] private bool _hasImage;
    [ObservableProperty] private bool _isProcessing;
    [ObservableProperty] private bool _hasResult;
    [ObservableProperty] private string _statusMessage = "Initializing...";
    [ObservableProperty] private Brush _statusDotBrush = new SolidColorBrush(Color.FromRgb(245, 158, 11));
    [ObservableProperty] private string _serverStatusLabel = "Connecting";
    [ObservableProperty] private string _charCount = string.Empty;

    public MainVM()
    {
        // Only kill server on exit if it is fully ready (model loaded).
        // If still downloading, leave it running so progress is not lost.
        Application.Current.Exit += (_, _) =>
        {
            if (_serverReady && _weStartedServer)
                try { _serverProcess?.Kill(); } catch { }
        };
        _ = InitializeAsync();
    }

   
    private async Task InitializeAsync()
    {
        // Already responding — nothing to do.
        if (await PingServer()) { SetServerReady(); return; }

        // Server process is already running (e.g. still downloading from a previous session).
        // Attach to it and wait instead of spawning a duplicate.
        var existing = Process.GetProcessesByName("trocr_server").FirstOrDefault();
        if (existing is not null)
        {
            _serverProcess = existing;
            SetStatus("Server already running — waiting for model to finish loading...", "#F59E0B", "Resuming");
            // Fall through to the polling loop below.
        }
        else
        {
            if (!File.Exists(ServerExe))
            {
                SetStatus($"Server not found at {ServerExe}", "#EF4444", "Not found");
                return;
            }

            SetStatus("Starting OCR server...", "#F59E0B", "Starting");
        }

        if (existing is null)
        {
            Directory.CreateDirectory(ServerAppData);
            Directory.CreateDirectory(ServerTemp);

            var psi = new ProcessStartInfo
            {
                FileName               = ServerExe,
                WorkingDirectory       = Path.GetDirectoryName(ServerExe)!,
                CreateNoWindow         = true,
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
            };
            psi.Environment["APPDATA"]          = ServerAppData;
            psi.Environment["TEMP"]             = ServerTemp;
            psi.Environment["TMP"]              = ServerTemp;
            psi.Environment["HF_HUB_OFFLINE"]   = ModelCacheExists() ? "1" : "0";

            _serverProcess    = Process.Start(psi);
            _weStartedServer  = true;
        }

        // Poll every 2 s until the server responds or the process dies.
        // No hard timeout — first run downloads ~2 GB which can take 10+ min.
        var started = DateTime.Now;
        while (true)
        {
            await Task.Delay(2000);

            if (_serverProcess is { HasExited: true })
            {
                var stderr = await _serverProcess.StandardError.ReadToEndAsync();
                var stdout = await _serverProcess.StandardOutput.ReadToEndAsync();
                var detail = (stderr + stdout).Trim().Replace("\r\n", " ").Replace("\n", " ");
                if (detail.Length > 120) detail = detail[..120] + "…";
                SetStatus(string.IsNullOrEmpty(detail)
                              ? $"Server crashed (exit {_serverProcess.ExitCode})."
                              : $"Server crashed: {detail}",
                          "#EF4444", "Crashed");
                return;
            }

            if (await PingServer()) { SetServerReady(); return; }

            var elapsed = DateTime.Now - started;
            var label   = elapsed.TotalSeconds < 60
                ? $"Loading... ({(int)elapsed.TotalSeconds}s)"
                : $"Loading... ({(int)elapsed.TotalMinutes}m {elapsed.Seconds}s)";
            var msg = ModelCacheExists()
                ? "Loading model into memory, please wait..."
                : "First run: downloading model (~2 GB). Please wait.";
            SetStatus(msg, "#F59E0B", label);
        }
    }

    private async Task<bool> PingServer()
    {
        try
        {
            // Use GET — Flask may not support HEAD on all routes.
            using var resp = await _http.GetAsync("http://127.0.0.1:5005/health",
                                                   HttpCompletionOption.ResponseHeadersRead);
            return true; // any HTTP response means server is up
        }
        catch { return false; }
    }

    private void SetServerReady()
    {
        _serverReady = true;
        SetStatus("Server ready. Drop or browse an image to start.", "#22C55E", "Ready");
    }

    
    [RelayCommand]
    private void Browse()
    {
        var dlg = new OpenFileDialog
        {
            Title  = "Select an Image",
            Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.tiff;*.tif;*.webp|All files|*.*",
        };
        if (dlg.ShowDialog() == true)
            _ = ProcessImageAsync(dlg.FileName);
    }

    [RelayCommand]
    private Task ProcessImage(string path) => ProcessImageAsync(path);

    [RelayCommand(CanExecute = nameof(HasResult))]
    private void Copy() => Clipboard.SetText(OcrText);

    [RelayCommand(CanExecute = nameof(HasImage))]
    private void Clear()
    {
        PreviewSource = null;
        HasImage      = false;
        OcrText       = string.Empty;
        HasResult     = false;
        CharCount     = string.Empty;
        SetStatus("Ready.", "#22C55E", "Ready");
    }

    partial void OnHasResultChanged(bool value) => CopyCommand.NotifyCanExecuteChanged();
    partial void OnHasImageChanged(bool value)  => ClearCommand.NotifyCanExecuteChanged();

   
    private async Task ProcessImageAsync(string path)
    {
        if (IsProcessing) return;

        LoadPreview(path);
        OcrText   = string.Empty;
        HasResult = false;
        CharCount = string.Empty;

        if (!_serverReady)
        {
            SetStatus("Server not ready — please wait.", "#F59E0B", "Waiting");
            return;
        }

        IsProcessing = true;
        SetStatus($"Processing: {Path.GetFileName(path)}", "#4F6BF4", "Working");

        try
        {
            var text = await SendToServerAsync(path);
            OcrText   = text;
            HasResult = !string.IsNullOrWhiteSpace(text);
            CharCount = HasResult ? $"{text.Length:N0} characters" : string.Empty;
            SetStatus("Done.", "#22C55E", "Ready");
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}", "#EF4444", "Error");
        }
        finally
        {
            IsProcessing = false;
        }
    }

    private void LoadPreview(string path)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource        = new Uri(path);
            bmp.CacheOption      = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 800;
            bmp.EndInit();
            bmp.Freeze();
            PreviewSource = bmp;
            HasImage      = true;
        }
        catch
        {
            SetStatus("Could not load image preview.", "#F59E0B", "Ready");
        }
    }

    private static async Task<string> SendToServerAsync(string imagePath)
    {
        await using var fs = File.OpenRead(imagePath);

        var imgContent = new StreamContent(fs);
        imgContent.Headers.ContentType = new MediaTypeHeaderValue(GetMimeType(imagePath));

        using var form = new MultipartFormDataContent();
        form.Add(imgContent, "image", Path.GetFileName(imagePath));

        var response = await _http.PostAsync(ServerUrl, form);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        foreach (var key in new[] { "text", "result", "output" })
        {
            if (doc.RootElement.TryGetProperty(key, out var val))
            {
                return val.GetString() ?? string.Empty;
            }
        }

        return json;
    }

    
    private void SetStatus(string message, string hexColor, string label)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            StatusMessage     = message;
            ServerStatusLabel = label;
            var c = (Color)ColorConverter.ConvertFromString(hexColor);
            StatusDotBrush = new SolidColorBrush(c);
        });
    }

    private static bool ModelCacheExists() =>
        Directory.Exists(Path.Combine(ServerAppData, "TrOCRServer", "models",
                                      "models--microsoft--trocr-large-handwritten", "snapshots"));

    private static string GetMimeType(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png"            => "image/png",
            ".bmp"            => "image/bmp",
            ".tiff" or ".tif" => "image/tiff",
            ".webp"           => "image/webp",
            _                 => "application/octet-stream",
        };
}
