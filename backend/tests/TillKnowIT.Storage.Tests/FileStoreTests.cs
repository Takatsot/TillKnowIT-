using System.Security.Cryptography;
using TillKnowIT.Storage;

namespace TillKnowIT.Storage.Tests;

public sealed class FileStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tk-storage-" + Guid.NewGuid().ToString("N"));
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    private readonly byte[] _pdf = [.. "%PDF-1.4 hello"u8.ToArray(), .. new byte[100]];

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private EncryptedLocalFileStore Store(byte[]? key = null) => new(_dir, key ?? _key);

    [Fact]
    public async Task A_saved_file_comes_back_exactly_with_its_name_and_type()
    {
        var url = await Store().Save("compliance", "trading licence.pdf", "application/pdf", _pdf, default);

        var file = await Store().Open(url, default);

        Assert.NotNull(file);
        Assert.Equal(_pdf, file.Content);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal("trading licence.pdf", file.FileName);
        Assert.StartsWith("tkfile://compliance/", url);
    }

    [Fact]
    public async Task Nothing_readable_is_left_on_disk()
    {
        var secret = "SECRET-LICENCE-NUMBER-12345"u8.ToArray();
        var url = await Store().Save("compliance", "licence.pdf", "application/pdf", [.. "%PDF-"u8.ToArray(), .. secret], default);

        var raw = await File.ReadAllBytesAsync(Directory.GetFiles(_dir, "*.bin", SearchOption.AllDirectories).Single());

        Assert.DoesNotContain("SECRET-LICENCE", System.Text.Encoding.Latin1.GetString(raw));
        Assert.DoesNotContain("licence.pdf", System.Text.Encoding.Latin1.GetString(raw));
        Assert.NotNull(url);
    }

    [Fact]
    public async Task Tampering_with_the_stored_bytes_makes_the_file_unreadable()
    {
        var url = await Store().Save("compliance", "a.pdf", "application/pdf", _pdf, default);
        var path = Directory.GetFiles(_dir, "*.bin", SearchOption.AllDirectories).Single();
        var bytes = await File.ReadAllBytesAsync(path);
        bytes[^1] ^= 0xFF;
        await File.WriteAllBytesAsync(path, bytes);

        Assert.Null(await Store().Open(url, default));
    }

    [Fact]
    public async Task A_wrong_key_cannot_read_the_file()
    {
        var url = await Store().Save("compliance", "a.pdf", "application/pdf", _pdf, default);
        Assert.Null(await Store(RandomNumberGenerator.GetBytes(32)).Open(url, default));
    }

    [Fact]
    public async Task A_file_cannot_be_opened_through_another_container()
    {
        var url = await Store().Save("compliance", "a.pdf", "application/pdf", _pdf, default);
        var moved = url.Replace("compliance", "photos");
        Directory.CreateDirectory(Path.Combine(_dir, "photos"));
        File.Copy(Directory.GetFiles(_dir, "*.bin", SearchOption.AllDirectories).Single(), Path.Combine(_dir, "photos", url.Split('/')[^1] + ".bin"));

        Assert.Null(await Store().Open(moved, default));
    }

    [Theory]
    [InlineData("tkfile://../../etc/passwd")]
    [InlineData("tkfile://compliance/../../secret")]
    [InlineData("tkfile://compliance/zzzz")]
    [InlineData("tkfile://Compliance/0123456789abcdef0123456789abcdef")]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData("")]
    public async Task Odd_urls_never_reach_the_disk(string url)
    {
        Assert.Null(await Store().Open(url, default));
        await Store().Delete(url, default);   // does nothing, does not throw
    }

    [Fact]
    public async Task Deleting_removes_the_file_and_a_missing_file_is_simply_null()
    {
        var url = await Store().Save("compliance", "a.pdf", "application/pdf", _pdf, default);
        await Store().Delete(url, default);

        Assert.Null(await Store().Open(url, default));
        Assert.Empty(Directory.GetFiles(_dir, "*.bin", SearchOption.AllDirectories));
    }

    [Fact]
    public void The_key_must_be_32_bytes() => Assert.Throws<ArgumentException>(() => new EncryptedLocalFileStore(_dir, new byte[16]));

    // ---- rules ----

    [Fact]
    public void Photos_and_pdfs_are_accepted_when_the_content_matches()
    {
        Assert.True(FileRules.Validate("a.pdf", "application/pdf", _pdf).IsSuccess);
        Assert.True(FileRules.Validate("a.jpg", "image/jpeg", [0xFF, 0xD8, 0xFF, 0xE0, 0, 0]).IsSuccess);
        Assert.True(FileRules.Validate("a.png", "image/png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0]).IsSuccess);
    }

    [Theory]
    [InlineData("", "application/pdf", "file.name_invalid")]
    [InlineData("a.exe", "application/octet-stream", "file.type_invalid")]
    [InlineData("a.html", "text/html", "file.type_invalid")]
    public void Wrong_names_and_types_are_refused(string name, string type, string code) =>
        Assert.Equal(code, FileRules.Validate(name, type, [1, 2, 3, 4, 5, 6, 7, 8, 9]).Error!.Code);

    [Fact]
    public void An_exe_renamed_to_pdf_is_refused_and_so_are_empty_and_huge_files()
    {
        Assert.Equal("file.content_mismatch", FileRules.Validate("a.pdf", "application/pdf", "MZ-not-a-pdf"u8.ToArray()).Error!.Code);
        Assert.Equal("file.empty", FileRules.Validate("a.pdf", "application/pdf", []).Error!.Code);
        Assert.Equal("file.too_large", FileRules.Validate("a.pdf", "application/pdf", new byte[FileRules.MaxBytes + 1]).Error!.Code);
    }
}
