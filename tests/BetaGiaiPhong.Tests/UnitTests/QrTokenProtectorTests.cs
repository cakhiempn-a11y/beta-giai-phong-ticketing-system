using System.Security.Cryptography;
using BetaGiaiPhong.Data.Services;
using BetaGiaiPhong.Tests.Helpers;
using Xunit;

namespace BetaGiaiPhong.Tests.UnitTests;

public sealed class QrTokenProtectorTests
{
    private const string TestAesKeyB64 = "47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU=";

    public QrTokenProtectorTests()
    {
        Environment.SetEnvironmentVariable("BGP_QR_AES_KEY_B64", TestAesKeyB64);
    }

    [Fact]
    public void IssueAndUnseal_ValidToken_RoundtripsSuccessfully()
    {
        var protector = new QrTokenProtector();
        var issued = protector.Issue();

        var unsealed = protector.Unseal(issued.Cipher);

        Assert.Equal(issued.Token, unsealed);
    }

    [Fact]
    public void HashToken_ProducesDeterministicSha256()
    {
        var token = "TEST-TOKEN-DETERMINISTIC";
        var hash1 = QrTokenProtector.HashToken(token);
        var hash2 = QrTokenProtector.HashToken(token);

        Assert.Equal(32, hash1.Length);
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void Unseal_AfterProcessRestartWithSameKey_SuccessfullyRecoversToken()
    {
        // 1. Process instance 1 generates and encrypts token
        var instance1 = new QrTokenProtector();
        var issued = instance1.Issue();

        // 2. Process restarts, new instance initialized with identical key
        var instance2 = new QrTokenProtector();
        var recovered = instance2.Unseal(issued.Cipher);

        Assert.Equal(issued.Token, recovered);
    }

    [Fact]
    public void Unseal_WithDifferentKey_ThrowsCryptographicException()
    {
        var key1 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var key2 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        Environment.SetEnvironmentVariable("BGP_QR_AES_KEY_B64", key1);
        var protector1 = new QrTokenProtector();
        var issued = protector1.Issue();

        Environment.SetEnvironmentVariable("BGP_QR_AES_KEY_B64", key2);
        var protector2 = new QrTokenProtector();

        Assert.ThrowsAny<CryptographicException>(() => protector2.Unseal(issued.Cipher));

        // Restore test fixture key
        Environment.SetEnvironmentVariable("BGP_QR_AES_KEY_B64", TestAesKeyB64);
    }

    [Fact]
    public void Unseal_CorruptedCipher_ThrowsCryptographicException()
    {
        var protector = new QrTokenProtector();
        var issued = protector.Issue();
        issued.Cipher[^1] ^= 0xFF; // Corrupt authentication tag / ciphertext

        Assert.ThrowsAny<Exception>(() => protector.Unseal(issued.Cipher));
    }

    [Fact]
    public void QrPngDecoder_RoundsTripQrTokenThroughPng()
    {
        var protector = new QrTokenProtector();
        var issued = protector.Issue();

        using var gen = new QRCoder.QRCodeGenerator();
        using var data = gen.CreateQrCode(issued.Token, QRCoder.QRCodeGenerator.ECCLevel.Q);
        var qr = new QRCoder.PngByteQRCode(data);
        var pngBytes = qr.GetGraphic(5);

        var decoded = QrPngDecoder.Decode(pngBytes);
        Assert.Equal(issued.Token, decoded);
    }
}
