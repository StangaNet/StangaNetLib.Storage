using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using StangaNetLib.Cryptography.Extensions;
using StangaNetLib.Storage.Abstractions;
using StangaNetLib.Storage.Extensions;

namespace StangaNetLib.Storage.Tests;

public sealed class EncryptedStorageTests
{
    [Fact]
    public async Task LocalOnly_WithEncryption_RoundTripsPlaintext()
    {
        var ct = TestContext.Current.CancellationToken;
        var dir = Path.Combine(Path.GetTempPath(), "sn-storage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var services = new ServiceCollection();
            services.AddStangaNetLibCryptography();
            services.AddStangaNetLibStorageLocalOnly(
                basePath: dir,
                encryptAtRest: true,
                encryptionPassphrase: "storage-test-pass");

            await using var sp = services.BuildServiceProvider();
            var storage = sp.GetRequiredService<IBlobStorageService>();

            await using var upload = new MemoryStream(Encoding.UTF8.GetBytes("hello encrypted storage"));
            var up = await storage.UploadAsync("c1", "doc.txt", upload, "text/plain", ct: ct);
            up.IsSuccess.Should().BeTrue();

            var down = await storage.DownloadAsync("c1", "doc.txt", ct);
            down.IsSuccess.Should().BeTrue();
            down.Value!.ContentType.Should().Be("text/plain");

            using var reader = new StreamReader(down.Value.Content);
            (await reader.ReadToEndAsync(ct)).Should().Be("hello encrypted storage");

            // On-disk payload must not be plaintext
            var files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories);
            files.Should().NotBeEmpty();
            var raw = await File.ReadAllBytesAsync(files[0], ct);
            Encoding.UTF8.GetString(raw).Should().NotContain("hello encrypted storage");
            raw.AsSpan(0, 4).ToArray().Should().Equal("SNEN"u8.ToArray());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
