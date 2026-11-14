using FluentAssertions;
using Tulaziva.Application.Ads;
using Tulaziva.Tests.Fakes;

namespace Tulaziva.Tests.Ads;

public class GeoMathTests
{
    [Fact]
    public void DistanceKm_BetweenTheSamePoint_IsZero()
    {
        GeoMath.DistanceKm(19.4326, -99.1332, 19.4326, -99.1332).Should().BeApproximately(0, 0.001);
    }

    [Fact]
    public void DistanceKm_OneDegreeOfLongitudeAtTheEquator_IsAbout111Km()
    {
        GeoMath.DistanceKm(0, 0, 0, 1).Should().BeApproximately(111.19, 0.5);
    }

    [Fact]
    public void DistanceKm_MexicoCityToGuadalajara_IsAbout460Km()
    {
        GeoMath.DistanceKm(19.4326, -99.1332, 20.6597, -103.3496).Should().BeApproximately(461, 10);
    }

    [Fact]
    public void DistanceKm_IsTheSameInBothDirections()
    {
        GeoMath.DistanceKm(19.4, -99.1, 25.7, -100.3).Should().BeApproximately(GeoMath.DistanceKm(25.7, -100.3, 19.4, -99.1), 0.0001);
    }

    [Theory]
    [InlineData(19.4326, -99.1332, 10)]
    [InlineData(19.4326, -99.1332, 300)]
    [InlineData(60, 10, 100)]
    public void BoundingBox_ContainsEveryPointInsideTheRadius(double latitude, double longitude, double radiusKm)
    {
        var box = GeoMath.BoundingBox(latitude, longitude, radiusKm);

        // Points exactly at the radius toward north, south, east and west must not fall outside the box.
        var degreesOfLatitude = radiusKm / 111.195;
        var degreesOfLongitude = radiusKm / (111.195 * Math.Cos(latitude * Math.PI / 180));

        box.MinLatitude.Should().BeLessThanOrEqualTo(latitude - degreesOfLatitude + 0.0001);
        box.MaxLatitude.Should().BeGreaterThanOrEqualTo(latitude + degreesOfLatitude - 0.0001);
        box.MinLongitude.Should().NotBeNull().And.BeLessThanOrEqualTo(longitude - degreesOfLongitude + 0.0001);
        box.MaxLongitude.Should().NotBeNull().And.BeGreaterThanOrEqualTo(longitude + degreesOfLongitude - 0.0001);
    }

    [Fact]
    public void BoundingBox_HasNoLongitudeRange_NearAPole_OrAcrossTheAntimeridian()
    {
        GeoMath.BoundingBox(89.9, 10, 100).MinLongitude.Should().BeNull();
        GeoMath.BoundingBox(10, 179.9, 100).MaxLongitude.Should().BeNull();
        GeoMath.BoundingBox(10, -179.9, 100).MinLongitude.Should().BeNull();
    }
}

public class ImageSignatureTests
{
    [Fact]
    public void Detect_RecognizesJpegPngAndWebP()
    {
        ImageSignature.Detect(TestImages.Jpeg().AsSpan(0, 12))!.Value.Should().Be(new DetectedImage("image/jpeg", ".jpg"));
        ImageSignature.Detect(TestImages.Png().AsSpan(0, 12))!.Value.Should().Be(new DetectedImage("image/png", ".png"));
        ImageSignature.Detect(TestImages.WebP().AsSpan(0, 12))!.Value.Should().Be(new DetectedImage("image/webp", ".webp"));
    }

    [Fact]
    public void Detect_RejectsTextGifAndSvgAndExecutables()
    {
        ImageSignature.Detect(TestImages.Text().AsSpan(0, 12)).Should().BeNull();
        ImageSignature.Detect("GIF89a......"u8).Should().BeNull();
        ImageSignature.Detect("<svg xmlns=1>"u8).Should().BeNull();
        ImageSignature.Detect(new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00 }).Should().BeNull();
    }

    [Fact]
    public void Detect_RejectsAnEmptyOrTooShortHeader_AndARiffThatIsNotWebP()
    {
        ImageSignature.Detect(ReadOnlySpan<byte>.Empty).Should().BeNull();
        ImageSignature.Detect(new byte[] { 0xFF, 0xD8 }).Should().BeNull();
        ImageSignature.Detect("RIFF\0\0\0\0WAVE"u8).Should().BeNull();
    }
}
