using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Confirmations.Api.Tests;

/// <summary>
/// Builds tiny fake JPEG/PNG files with the same structure as real ones,
/// optionally carrying hidden metadata (like a phone's GPS location).
/// Each call returns different bytes, so files never look like duplicates
/// unless a test reuses the same array on purpose.
/// </summary>
public static class TestImages
{
    public const string SecretMarker = "GPS-SECRET-42.1234-N";

    public static byte[] Jpeg(bool withMetadata = true)
    {
        using var ms = new MemoryStream();
        ms.Write([0xFF, 0xD8]); // start of image
        JpegSegment(ms, 0xE0, [.. "JFIF\0"u8, 1, 1, 0, 0, 1, 0, 1, 0, 0]); // APP0 (kept)
        if (withMetadata)
        {
            JpegSegment(ms, 0xE1, Encoding.ASCII.GetBytes("Exif\0\0" + SecretMarker)); // APP1 EXIF
            JpegSegment(ms, 0xFE, Encoding.ASCII.GetBytes("comment " + SecretMarker)); // COM
        }
        JpegSegment(ms, 0xDB, new byte[65]); // quantization table (kept)
        JpegSegment(ms, 0xDA, new byte[10]); // start of scan
        ms.Write(RandomNumberGenerator.GetBytes(64)); // "picture data"
        ms.Write([0xFF, 0xD9]); // end of image
        return ms.ToArray();
    }

    public static byte[] Png(bool withMetadata = true)
    {
        using var ms = new MemoryStream();
        ms.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        PngChunk(ms, "IHDR", [0, 0, 0, 1, 0, 0, 0, 1, 8, 2, 0, 0, 0]);
        if (withMetadata)
        {
            PngChunk(ms, "tEXt", Encoding.ASCII.GetBytes("Author\0" + SecretMarker));
            PngChunk(ms, "eXIf", Encoding.ASCII.GetBytes("MM\0*" + SecretMarker));
        }
        PngChunk(ms, "IDAT", RandomNumberGenerator.GetBytes(32));
        PngChunk(ms, "IEND", []);
        return ms.ToArray();
    }

    public static bool ContainsSecret(byte[] data) =>
        Encoding.ASCII.GetString(data).Contains(SecretMarker);

    private static void JpegSegment(Stream s, byte marker, byte[] payload)
    {
        s.WriteByte(0xFF);
        s.WriteByte(marker);
        var length = payload.Length + 2;
        s.WriteByte((byte)(length >> 8));
        s.WriteByte((byte)length);
        s.Write(payload);
    }

    private static void PngChunk(Stream s, string type, byte[] data)
    {
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(len, (uint)data.Length);
        s.Write(len);
        s.Write(Encoding.ASCII.GetBytes(type));
        s.Write(data);
        s.Write(new byte[4]); // CRC (not checked by the app)
    }
}
