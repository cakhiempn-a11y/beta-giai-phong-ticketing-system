using System.Security.Cryptography;
using System.Text;

namespace BetaGiaiPhong.Data.Services;

// Only a local payment simulator. For production, verify the actual provider's
// authenticated notification and canonical format before calling a domain service.
public sealed record MockPaymentMessage(
    string ProviderEventRef, string AttemptKey, string ProviderTransactionRef,
    string OrderCode, long AmountVnd, string Outcome);

public sealed class VerifiedMockPaymentMessage
{
    internal VerifiedMockPaymentMessage(MockPaymentMessage message) => Message = message;
    public MockPaymentMessage Message { get; }
}

public static class MockPaymentSecurity
{
    private static byte[] SecretFromEnvironment()
    {
        var raw = Environment.GetEnvironmentVariable("BGP_MOCK_HMAC_KEY_B64")
            ?? throw new InvalidOperationException("Set BGP_MOCK_HMAC_KEY_B64 (32 random bytes, Base64).");
        var secret = Convert.FromBase64String(raw);
        if (secret.Length < 32) throw new InvalidOperationException("Mock webhook key must be >= 32 bytes.");
        return secret;
    }

    private static string Canonical(MockPaymentMessage m) => string.Join("\n",
        "MOCK", m.ProviderEventRef, m.AttemptKey, m.ProviderTransactionRef,
        m.OrderCode, m.AmountVnd.ToString(System.Globalization.CultureInfo.InvariantCulture), m.Outcome);

    public static byte[] PayloadHash(MockPaymentMessage m)
        => SHA256.HashData(Encoding.UTF8.GetBytes(Canonical(m)));

    // Use for local smoke tests only, never as a public production signing endpoint.
    public static string SignForDevelopment(MockPaymentMessage message)
        => Convert.ToHexString(HMACSHA256.HashData(SecretFromEnvironment(), Encoding.UTF8.GetBytes(Canonical(message))));

    public static VerifiedMockPaymentMessage Verify(MockPaymentMessage message, string hexSignature)
    {
        if (message.Outcome is not ("SUCCESS" or "FAILED") ||
            string.IsNullOrWhiteSpace(message.ProviderEventRef) ||
            string.IsNullOrWhiteSpace(message.ProviderTransactionRef) ||
            string.IsNullOrWhiteSpace(message.OrderCode) ||
            string.IsNullOrWhiteSpace(message.AttemptKey))
            throw new BusinessRuleException("INVALID_CALLBACK", "Missing or invalid payment callback fields.");
        byte[] received;
        try { received = Convert.FromHexString(hexSignature); }
        catch (FormatException) { throw new UnauthorizedAccessException("Invalid webhook signature encoding."); }
        var expected = HMACSHA256.HashData(SecretFromEnvironment(), Encoding.UTF8.GetBytes(Canonical(message)));
        if (!CryptographicOperations.FixedTimeEquals(received, expected))
            throw new UnauthorizedAccessException("Invalid authenticated mock payment signature.");
        return new VerifiedMockPaymentMessage(message);
    }
}
