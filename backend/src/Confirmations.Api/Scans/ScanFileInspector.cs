namespace Confirmations.Api.Scans;

/// <summary>
/// Works out what a file really is from its first bytes, ignoring its
/// name, extension, and whatever type the browser claims.
/// </summary>
public static class ScanFileInspector
{
    public const string Jpeg = "image/jpeg";
    public const string Png = "image/png";

    private static ReadOnlySpan<byte> JpegSignature => [0xFF, 0xD8, 0xFF];
    internal static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>"image/jpeg", "image/png", or null for anything else.</summary>
    public static string? DetectImageType(ReadOnlySpan<byte> data)
    {
        if (data.StartsWith(JpegSignature)) return Jpeg;
        if (data.StartsWith(PngSignature)) return Png;
        return null;
    }
}
