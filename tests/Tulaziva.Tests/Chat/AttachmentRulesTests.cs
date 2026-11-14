using FluentAssertions;
using Tulaziva.Application.Chat;
using Tulaziva.Tests.Fakes;

namespace Tulaziva.Tests.Chat;

public class AttachmentRulesTests
{
    [Theory]
    [InlineData(".jpg", "image/jpeg")]
    [InlineData(".jpeg", "image/jpeg")]
    [InlineData(".JPG", "image/jpeg")]
    [InlineData(".png", "image/png")]
    [InlineData(".PNG", "image/png")]
    public void OnlyJpgAndPng_AreOnTheList_WithTheContentTypeDecidedByTheServer(string extension, string contentType)
    {
        var type = AttachmentRules.TypeOf(extension);

        type.Should().NotBeNull();
        type!.Value.ContentType.Should().Be(contentType);
        type.Value.IsImage.Should().BeTrue();
    }

    [Fact]
    public void TheListOfExtensions_IsExactlyJpgJpegAndPng()
    {
        AttachmentRules.Extensions.Should().BeEquivalentTo(".jpg", ".jpeg", ".png");
    }

    [Theory]
    [InlineData(".pdf")]
    [InlineData(".doc")]
    [InlineData(".docx")]
    [InlineData(".xls")]
    [InlineData(".xlsx")]
    [InlineData(".ppt")]
    [InlineData(".txt")]
    [InlineData(".csv")]
    [InlineData(".zip")]
    [InlineData(".rar")]
    [InlineData(".7z")]
    [InlineData(".gif")]
    [InlineData(".webp")]
    [InlineData(".bmp")]
    [InlineData(".svg")]
    [InlineData(".exe")]
    [InlineData(".dll")]
    [InlineData(".bat")]
    [InlineData(".ps1")]
    [InlineData(".js")]
    [InlineData(".html")]
    [InlineData(".php")]
    [InlineData(".jar")]
    [InlineData("")]
    public void DocumentsZipFilesOtherImagesProgramsAndScripts_AreNotOnTheList(string extension)
    {
        AttachmentRules.TypeOf(extension).Should().BeNull();
        AttachmentRules.ContentMatches(extension, TestImages.Png()).Should().BeFalse();
        AttachmentRules.ContentMatches(extension, TestImages.Jpeg()).Should().BeFalse();
    }

    [Theory]
    [InlineData(".jpg")]
    [InlineData(".jpeg")]
    [InlineData(".png")]
    public void AnExecutableOrADocumentRenamedToAnImage_IsRefused(string extension)
    {
        AttachmentRules.ContentMatches(extension, TestFiles.Exe()).Should().BeFalse();
        AttachmentRules.ContentMatches(extension, TestFiles.Pdf()).Should().BeFalse();
        AttachmentRules.ContentMatches(extension, TestFiles.Zip()).Should().BeFalse();
        AttachmentRules.ContentMatches(extension, TestFiles.Text()).Should().BeFalse();
    }

    [Fact]
    public void ARealJpgAndARealPng_AreAccepted_ButNotWithTheExtensionOfTheOther()
    {
        AttachmentRules.ContentMatches(".jpg", TestImages.Jpeg()).Should().BeTrue();
        AttachmentRules.ContentMatches(".jpeg", TestImages.Jpeg()).Should().BeTrue();
        AttachmentRules.ContentMatches(".png", TestImages.Png()).Should().BeTrue();

        AttachmentRules.ContentMatches(".png", TestImages.Jpeg()).Should().BeFalse();
        AttachmentRules.ContentMatches(".jpg", TestImages.Png()).Should().BeFalse();
    }

    [Fact]
    public void AnEmptyFile_IsNotAnImage()
    {
        AttachmentRules.ContentMatches(".jpg", ReadOnlySpan<byte>.Empty).Should().BeFalse();
        AttachmentRules.ContentMatches(".png", ReadOnlySpan<byte>.Empty).Should().BeFalse();
    }

    [Theory]
    [InlineData("photo.png", "photo.png")]
    [InlineData("C:\\Users\\ana\\Desktop\\photo.jpg", "photo.jpg")]
    [InlineData("../../etc/passwd.png", "passwd.png")]
    [InlineData("/var/www/photo.jpg", "photo.jpg")]
    [InlineData("pho\"to;.png", "photo.png")]
    [InlineData("  spaced name.png  ", "spaced name.png")]
    [InlineData("", "file")]
    [InlineData("///", "file")]
    public void CleanFileName_LeavesOnlyAHarmlessName(string input, string expected)
    {
        AttachmentRules.CleanFileName(input).Should().Be(expected);
    }

    [Fact]
    public void CleanFileName_RemovesControlCharacters_AndKeepsTheExtensionOfALongName()
    {
        AttachmentRules.CleanFileName("a\r\nb\0c.png").Should().Be("abc.png");

        var clean = AttachmentRules.CleanFileName(new string('x', 300) + ".png");

        clean.Should().HaveLength(ChatRules.MaxFileNameLength).And.EndWith(".png");
    }
}
