using BetaGiaiPhong.Data.Services;
using Xunit;

namespace BetaGiaiPhong.Tests.UnitTests;

public sealed class MockPaymentSecurityTests
{
    private const string TestHmacKeyB64 = "47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU=";

    public MockPaymentSecurityTests()
    {
        Environment.SetEnvironmentVariable("BGP_MOCK_HMAC_KEY_B64", TestHmacKeyB64);
    }

    [Fact]
    public void SignAndVerify_ValidMessage_ReturnsTrue()
    {
        var message = new MockPaymentMessage("EVENT-1", "ATTEMPT-1", "TXN-1", "ORD-1234", 100000, "SUCCESS");
        var signature = MockPaymentSecurity.SignForDevelopment(message);

        var verified = MockPaymentSecurity.Verify(message, signature);

        Assert.NotNull(verified.Message);
        Assert.Equal("EVENT-1", verified.Message.ProviderEventRef);
    }

    [Fact]
    public void Verify_TamperedAmount_ThrowsUnauthorized()
    {
        var message = new MockPaymentMessage("EVENT-1", "ATTEMPT-1", "TXN-1", "ORD-1234", 100000, "SUCCESS");
        var signature = MockPaymentSecurity.SignForDevelopment(message);

        var tampered = new MockPaymentMessage("EVENT-1", "ATTEMPT-1", "TXN-1", "ORD-1234", 90000, "SUCCESS");

        Assert.Throws<UnauthorizedAccessException>(() => MockPaymentSecurity.Verify(tampered, signature));
    }

    [Fact]
    public void Verify_ForgedSignature_ThrowsUnauthorized()
    {
        var message = new MockPaymentMessage("EVENT-1", "ATTEMPT-1", "TXN-1", "ORD-1234", 100000, "SUCCESS");
        var forgedSig = "00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff";

        Assert.Throws<UnauthorizedAccessException>(() => MockPaymentSecurity.Verify(message, forgedSig));
    }
}
