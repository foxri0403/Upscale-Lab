using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using UpscaleLab.Application.Auth;
using UpscaleLab.Application.Common;
using UpscaleLab.Infrastructure.Email;

namespace UpscaleLab.Infrastructure.Auth;

public sealed class EmailVerificationCodeProtector : IEmailVerificationCodeProtector
{
    private readonly byte[] hashKey;

    public EmailVerificationCodeProtector(EmailVerificationOptions options)
    {
        hashKey = Encoding.UTF8.GetBytes(options.CodeHashKey);
        if (hashKey.Length < 32)
        {
            throw new ConfigurationException(
                "EmailVerification:CodeHashKey must be at least 32 bytes and must come from a secret store.");
        }
    }

    public string Generate() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);

    public string Hash(string email, string code)
    {
        var payload = Encoding.UTF8.GetBytes($"{email.Trim().ToLowerInvariant()}\n{code}");
        return Convert.ToHexString(HMACSHA256.HashData(hashKey, payload));
    }

    public bool Verify(string email, string code, string expectedHash)
    {
        byte[] expectedBytes;
        try
        {
            expectedBytes = Convert.FromHexString(expectedHash);
        }
        catch (FormatException)
        {
            return false;
        }

        var actualBytes = Convert.FromHexString(Hash(email, code));
        return CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
    }
}
