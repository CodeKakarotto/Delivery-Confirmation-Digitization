using System.Text;
using Confirmations.Api.Scans;

namespace Confirmations.Api.Tests;

/// <summary>Step 4: recognising real image files and removing hidden metadata.</summary>
public class ScanFileTests
{
    [Fact]
    public void DetectsJpegAndPng_FromTheirContents()
    {
        Assert.Equal("image/jpeg", ScanFileInspector.DetectImageType(TestImages.Jpeg()));
        Assert.Equal("image/png", ScanFileInspector.DetectImageType(TestImages.Png()));
    }

    [Theory]
    [InlineData("%PDF-1.7 fake pdf")]           // a PDF
    [InlineData("MZ\x90\0 fake windows program")] // an .exe renamed to .jpg
    [InlineData("<html><script>alert(1)</script>")]
    [InlineData("just some text")]
    [InlineData("")]
    public void RejectsAnythingThatIsNotJpegOrPng(string content)
    {
        Assert.Null(ScanFileInspector.DetectImageType(Encoding.Latin1.GetBytes(content)));
    }

    [Fact]
    public void Jpeg_MetadataIsRemoved_PictureIsKept()
    {
        var original = TestImages.Jpeg(withMetadata: true);
        Assert.True(TestImages.ContainsSecret(original));

        var cleaned = ImageMetadataStripper.StripJpeg(original)!;

        Assert.False(TestImages.ContainsSecret(cleaned));
        Assert.Equal("image/jpeg", ScanFileInspector.DetectImageType(cleaned));
        // Picture data (everything from "start of scan" on) is unchanged.
        Assert.Equal(original[^76..], cleaned[^76..]);
    }

    [Fact]
    public void Jpeg_WithoutMetadata_IsUnchanged()
    {
        var original = TestImages.Jpeg(withMetadata: false);

        Assert.Equal(original, ImageMetadataStripper.StripJpeg(original));
    }

    [Fact]
    public void Png_MetadataIsRemoved_PictureIsKept()
    {
        var original = TestImages.Png(withMetadata: true);

        var cleaned = ImageMetadataStripper.StripPng(original)!;

        Assert.False(TestImages.ContainsSecret(cleaned));
        Assert.Equal(TestImages.Png(withMetadata: false).Length, cleaned.Length);
        var text = Encoding.ASCII.GetString(cleaned);
        Assert.Contains("IHDR", text);
        Assert.Contains("IDAT", text);
        Assert.Contains("IEND", text);
    }

    [Fact]
    public void DamagedFiles_AreRefused()
    {
        var jpeg = TestImages.Jpeg();
        var png = TestImages.Png();

        Assert.Null(ImageMetadataStripper.StripJpeg(jpeg[..20]));  // cut off early
        Assert.Null(ImageMetadataStripper.StripPng(png[..30]));    // cut off early
        Assert.Null(ImageMetadataStripper.StripPng(png[..8]));     // header only
    }
}
