using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace UpscaleLab.Infrastructure.Payments;

public sealed class BillingKeyProtector(IConfiguration configuration)
{
    private byte[] GetKey()
    {
        var encoded = configuration["BillingCrypto:KeyBase64"];

        if (string.IsNullOrWhiteSpace(encoded))
            throw new InvalidOperationException(
                "빌링키 암호화 키가 설정되지 않았습니다.");

        byte[] key;

        try
        {
            key = Convert.FromBase64String(encoded);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException(
                "빌링키 암호화 키 형식이 올바르지 않습니다.");
        }

        if (key.Length != 32)
            throw new InvalidOperationException(
                "AES-256 암호화 키는 32바이트여야 합니다.");

        return key;
    }

    public string Encrypt(string billingKey, string customerKey)
    {
        if (string.IsNullOrWhiteSpace(billingKey) ||
            string.IsNullOrWhiteSpace(customerKey))
            throw new ArgumentException(
                "암호화 대상 정보가 올바르지 않습니다.");

        var key = GetKey();

        try
        {
            var nonce = RandomNumberGenerator.GetBytes(12);
            var plaintext = Encoding.UTF8.GetBytes(billingKey);
            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[16];
            var aad = Encoding.UTF8.GetBytes(customerKey);

            using (var aes = new AesGcm(key, 16))
            {
                aes.Encrypt(
                    nonce, plaintext, ciphertext, tag, aad);
            }

            var combined = new byte[
                nonce.Length + tag.Length + ciphertext.Length];

            Buffer.BlockCopy(nonce, 0, combined, 0, nonce.Length);
            Buffer.BlockCopy(tag, 0, combined, nonce.Length, tag.Length);
            Buffer.BlockCopy(
                ciphertext, 0, combined,
                nonce.Length + tag.Length, ciphertext.Length);

            return "v1:" + Convert.ToBase64String(combined);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public string Decrypt(string encrypted, string customerKey)
    {
        if (string.IsNullOrWhiteSpace(customerKey) ||
            !encrypted.StartsWith("v1:", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "암호화된 빌링키 형식이 올바르지 않습니다.");

        var data = Convert.FromBase64String(encrypted[3..]);

        if (data.Length < 28)
            throw new InvalidOperationException(
                "암호화된 데이터가 올바르지 않습니다.");

        var nonce = data.AsSpan(0, 12);
        var tag = data.AsSpan(12, 16);
        var ciphertext = data.AsSpan(28);
        var plaintext = new byte[ciphertext.Length];
        var aad = Encoding.UTF8.GetBytes(customerKey);
        var key = GetKey();

        try
        {
            using (var aes = new AesGcm(key, 16))
            {
                aes.Decrypt(
                    nonce, ciphertext, tag, plaintext, aad);
            }

            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }
}
