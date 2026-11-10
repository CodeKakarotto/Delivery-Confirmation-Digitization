namespace Confirmations.Api.Scans;

/// <summary>Settings from the "ScanStorage" configuration section.</summary>
public class ScanStorageOptions
{
    public const long MaxFileBytes = 10 * 1024 * 1024;   // 10 MB per file
    public const int MaxFilesPerUpload = 20;
    // Cloudflare's free plan refuses requests over 100 MB, so stay under that.
    public const long MaxRequestBytes = 95 * 1000 * 1000;

    /// <summary>
    /// Private folder for scans, outside the project and any public website folder.
    /// Default: %LOCALAPPDATA%\MichaelsFlowers\Scans on the host computer.
    /// </summary>
    public string Folder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MichaelsFlowers", "Scans");
}
