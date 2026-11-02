using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;

namespace UpscaleLab.Application.Auth;

public static class CopyrightTerms
{
    // The browser and API embed the same immutable, versioned document.
    private static readonly byte[] Document = ReadDocument();
    public static string Version { get; } = JsonDocument.Parse(Document).RootElement.GetProperty("version").GetString()!;
    public static string DocumentHash { get; } = Convert.ToHexString(SHA256.HashData(Document));

    private static byte[] ReadDocument()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ImageCopyrightTerms.json")!;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(buffer.ToArray()).Replace("\r\n", "\n"));
    }
}
