using BetaGiaiPhong.Data.Services;
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
    public void Unseal_CorruptedCipher_ThrowsCryptographicException()
    {
        var protector = new QrTokenProtector();
        var issued = protector.Issue();
        issued.Cipher[^1] ^= 0xFF; // Corrupt authentication tag / ciphertext

        Assert.ThrowsAny<Exception>(() => protector.Unseal(issued.Cipher));
    }
}
