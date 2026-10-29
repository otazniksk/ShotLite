using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Drawing.Printing;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace ShotLite;

internal sealed record EncodedImage(byte[] Data, string FileName, string MimeType);

/// <summary>What can be done with a finished screenshot: copy, save, print, upload.</summary>
internal sealed class ImageActions
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    private readonly AppSettings _settings;
    private readonly Notifier _notifier;

    public ImageActions(AppSettings settings, Notifier notifier)
    {
        _settings = settings;
        _notifier = notifier;
    }

    private bool IsJpeg => _settings.FileFormat == "jpg";
    private string Extension => IsJpeg ? ".jpg" : ".png";

    public void CopyToClipboard(Bitmap image)
    {
        try
        {
            Clipboard.SetImage(image);
            _notifier.Show(Strings.Copied);
        }
        catch (ExternalException ex)
        {
            _notifier.Error(string.Format(Strings.CopyFailed, ex.Message));
        }
    }

    /// <summary>Saves via dialog (or straight to the folder when auto-save is on). Returns the path, or null if cancelled.</summary>
    public string? Save(Bitmap image, IWin32Window? owner)
    {
        if (_settings.AutoSave)
            return SaveToFolder(image);

        using var dialog = new SaveFileDialog
        {
            Filter = "PNG (*.png)|*.png|JPEG (*.jpg)|*.jpg|BMP (*.bmp)|*.bmp",
            FilterIndex = IsJpeg ? 2 : 1,
            FileName = DefaultFileName() + Extension,
            InitialDirectory = Directory.Exists(_settings.LastSaveDir) ? _settings.LastSaveDir : EnsureSaveFolder(),
            AddExtension = true,
            DefaultExt = Extension.TrimStart('.'),
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(owner) != DialogResult.OK)
            return null;

        _settings.LastSaveDir = Path.GetDirectoryName(dialog.FileName);
        return WriteFile(image, dialog.FileName);
    }

    public string? SaveToFolder(Bitmap image)
    {
        var folder = EnsureSaveFolder();
        var path = Path.Combine(folder, DefaultFileName() + Extension);
        for (int i = 2; File.Exists(path); i++)
            path = Path.Combine(folder, $"{DefaultFileName()} ({i}){Extension}");
        return WriteFile(image, path);
    }

    public bool Print(Bitmap image, IWin32Window? owner)
    {
        using var document = new PrintDocument { DocumentName = Strings.AppName };
        document.PrintPage += (_, e) =>
        {
            // Page units are 1/100 inch; keep the screenshot at its 96 DPI size unless it doesn't fit.
            var area = e.MarginBounds;
            float width = image.Width * 100f / 96f, height = image.Height * 100f / 96f;
            float scale = Math.Min(1f, Math.Min(area.Width / width, area.Height / height));
            e.Graphics!.DrawImage(image, area.Left, area.Top, width * scale, height * scale);
        };

        using var dialog = new PrintDialog { Document = document, UseEXDialog = true };
        if (dialog.ShowDialog(owner) != DialogResult.OK)
            return false;

        try
        {
            document.Print();
            return true;
        }
        catch (Exception ex) when (ex is InvalidPrinterException or Win32Exception)
        {
            _notifier.Error(string.Format(Strings.PrintFailed, ex.Message));
            return false;
        }
    }

    public EncodedImage EncodeForUpload(Bitmap image)
    {
        using var stream = new MemoryStream();
        Encode(image, stream, Extension);
        return new EncodedImage(stream.ToArray(), DefaultFileName() + Extension, IsJpeg ? "image/jpeg" : "image/png");
    }

    /// <summary>Uploads to the user's own endpoint (multipart/form-data) and copies the returned link.</summary>
    public async Task UploadAsync(EncodedImage image)
    {
        var upload = _settings.Upload;
        try
        {
            using var content = new MultipartFormDataContent();
            var file = new ByteArrayContent(image.Data);
            file.Headers.ContentType = new MediaTypeHeaderValue(image.MimeType);
            content.Add(file, string.IsNullOrWhiteSpace(upload.FileField) ? "file" : upload.FileField, image.FileName);

            using var request = new HttpRequestMessage(HttpMethod.Post, upload.Url) { Content = content };
            if (!string.IsNullOrWhiteSpace(upload.AuthorizationHeader))
                request.Headers.TryAddWithoutValidation("Authorization", upload.AuthorizationHeader);

            _notifier.Show(Strings.Uploading);
            using var response = await Http.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {Truncate(body)}");

            var link = ExtractLink(body, upload.ResponseJsonPath);
            Clipboard.SetText(link);
            _notifier.Show(string.Format(Strings.Uploaded, link), () => OpenUrl(link), force: true);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException
                                       or KeyNotFoundException or InvalidOperationException
                                       or IndexOutOfRangeException or ExternalException)
        {
            _notifier.Error(string.Format(Strings.UploadFailed, ex.Message));
        }
    }

    private string? WriteFile(Bitmap image, string path)
    {
        try
        {
            using var stream = File.Create(path);
            Encode(image, stream, Path.GetExtension(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ExternalException)
        {
            _notifier.Error(string.Format(Strings.SaveFailed, ex.Message));
            return null;
        }

        _notifier.Show(string.Format(Strings.Saved, path), () => RevealInExplorer(path));
        return path;
    }

    private void Encode(Bitmap image, Stream stream, string extension)
    {
        switch (extension.ToLowerInvariant())
        {
            case ".jpg":
            case ".jpeg":
            {
                var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                using var parameters = new EncoderParameters(1);
                parameters.Param[0] = new EncoderParameter(
                    System.Drawing.Imaging.Encoder.Quality, (long)Math.Clamp(_settings.JpegQuality, 10, 100));
                image.Save(stream, codec, parameters);
                break;
            }
            case ".bmp":
                image.Save(stream, ImageFormat.Bmp);
                break;
            default:
                image.Save(stream, ImageFormat.Png);
                break;
        }
    }

    private string EnsureSaveFolder()
    {
        var folder = _settings.ResolvedSaveFolder;
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static string DefaultFileName() => $"Screenshot_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}";

    private static string ExtractLink(string body, string jsonPath)
    {
        if (string.IsNullOrWhiteSpace(jsonPath))
            return body.Trim();

        using var document = JsonDocument.Parse(body);
        var element = document.RootElement;
        foreach (var segment in jsonPath.Split('.', StringSplitOptions.RemoveEmptyEntries))
            element = int.TryParse(segment, out var index) ? element[index] : element.GetProperty(segment);
        return element.ValueKind == JsonValueKind.String ? element.GetString()! : element.GetRawText();
    }

    private static string Truncate(string text) => text.Length <= 200 ? text : text[..200] + "…";

    private static void RevealInExplorer(string path) =>
        Process.Start("explorer.exe", $"/select,\"{path}\"");

    private static void OpenUrl(string link)
    {
        if (Uri.TryCreate(link, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
    }
}
