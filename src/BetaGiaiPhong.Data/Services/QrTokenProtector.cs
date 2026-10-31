using System.Security.Cryptography;
using System.Text;

namespace BetaGiaiPhong.Data.Services;

public sealed class QrTokenProtector
{
    private readonly byte[] key;
    public string KeyId { get; } = "prototype-v1";

    public QrTokenProtector()
    {
        var raw = Environment.GetEnvironmentVariable("BGP_QR_AES_KEY_B64")
            ?? throw new InvalidOperationException("Set BGP_QR_AES_KEY_B64 (32 random bytes, Base64).");
        key = Convert.FromBase64String(raw);
        if (key.Length != 32) throw new InvalidOperationException("AES-256-GCM key must be exactly 32 bytes.");
    }

    public (string Token, byte[] Hash, byte[] Cipher) Issue()
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return (token, HashToken(token), Encrypt(token));
    }

    public static byte[] HashToken(string token)
        => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    private byte[] Encrypt(string token)
    {
        var plain = Encoding.UTF8.GetBytes(token);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var cipher = new byte[plain.Length];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, plain, cipher, tag);
        return nonce.Concat(tag).Concat(cipher).ToArray();
    }

    public string Unseal(byte[] packed)
    {
        if (packed.Length < 29) throw new CryptographicException("Invalid sealed QR payload.");
        var nonce = packed.AsSpan(0, 12);
        var tag = packed.AsSpan(12, 16);
        var cipher = packed.AsSpan(28);
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }
}
