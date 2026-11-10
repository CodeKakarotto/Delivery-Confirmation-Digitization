using System.Buffers.Binary;
using System.Text;

namespace Confirmations.Api.Scans;

/// <summary>
/// Removes hidden metadata (EXIF such as GPS location and camera details,
/// XMP, and text comments) from JPEG and PNG files without touching the
/// picture itself. Returns null if the file's structure is damaged.
/// </summary>
public static class ImageMetadataStripper
{
    public static byte[]? StripJpeg(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8)
            return null;

        using var output = new MemoryStream(data.Length);
        output.Write(data[..2]); // start of image

        var pos = 2;
        while (true)
        {
            if (pos + 4 > data.Length || data[pos] != 0xFF)
                return null;

            var marker = data[pos + 1];
            if (marker == 0xFF) { pos++; continue; }  // padding byte
            if (marker == 0xD9) return null;          // ended before any picture data

            var length = (data[pos + 2] << 8) | data[pos + 3];
            if (length < 2 || pos + 2 + length > data.Length)
                return null;

            if (marker == 0xDA)
            {
                // Start of scan: the picture data follows. Keep everything from here.
                output.Write(data[pos..]);
                return output.ToArray();
            }

            if (!IsJpegMetadata(marker))
                output.Write(data.Slice(pos, 2 + length));

            pos += 2 + length;
        }
    }

    /// <summary>
    /// APP1–APP13 and APP15 (EXIF, XMP, ICC, Photoshop, …) and COM (comments).
    /// APP0 (JFIF) and APP14 (Adobe colour info) are kept: they hold no personal data
    /// and are needed to show some files correctly.
    /// </summary>
    private static bool IsJpegMetadata(byte marker) =>
        marker is >= 0xE1 and <= 0xED or 0xEF or 0xFE;

    private static readonly HashSet<string> PngMetadataChunks = ["eXIf", "tEXt", "zTXt", "iTXt", "tIME"];

    public static byte[]? StripPng(ReadOnlySpan<byte> data)
    {
        var signature = ScanFileInspector.PngSignature;
        if (!data.StartsWith(signature))
            return null;

        using var output = new MemoryStream(data.Length);
        output.Write(signature);

        var pos = signature.Length;
        var first = true;
        while (pos < data.Length)
        {
            if (pos + 12 > data.Length)
                return null;

            var length = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(pos, 4));
            if (length > int.MaxValue || pos + 12L + length > data.Length)
                return null;

            var type = Encoding.ASCII.GetString(data.Slice(pos + 4, 4));
            if (first && type != "IHDR")
                return null;
            first = false;

            var chunkSize = 12 + (int)length; // length + type + data + CRC
            if (!PngMetadataChunks.Contains(type))
                output.Write(data.Slice(pos, chunkSize));
            pos += chunkSize;

            if (type == "IEND")
                return output.ToArray();
        }

        return null; // no end marker: damaged
    }
}
