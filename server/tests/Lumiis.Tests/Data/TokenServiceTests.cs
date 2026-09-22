using Lumiis.Data;

namespace Lumiis.Tests.Data;

public class TokenServiceTests
{
    private const string Secret = "0123456789abcdef0123456789abcdef"; // 32 Byte ASCII, genau am Minimum

    private sealed class FakeClock
    {
        public long NowUnixSeconds;
        public FakeClock(long start) => NowUnixSeconds = start;
    }

    private static TokenService CreateService(FakeClock clock)
    {
        return new TokenService(Secret, () => clock.NowUnixSeconds);
    }

    [Fact]
    public void Issue_Then_TryValidate_RoundTrip_Succeeds()
    {
        TokenService service = CreateService(new FakeClock(1_000_000));
        string token = service.Issue(42);

        Assert.True(service.TryValidate(token, out int accountId));
        Assert.Equal(42, accountId);
    }

    [Fact]
    public void TryValidate_TamperedSignature_Fails()
    {
        TokenService service = CreateService(new FakeClock(1_000_000));
        string token = service.Issue(42);
        int dotIndex = token.IndexOf('.');
        string tampered = token[..dotIndex] + "." + new string('A', token.Length - dotIndex - 1);

        Assert.False(service.TryValidate(tampered, out int accountId));
        Assert.Equal(0, accountId);
    }

    [Fact]
    public void TryValidate_TamperedPayload_Fails()
    {
        TokenService service = CreateService(new FakeClock(1_000_000));
        string token = service.Issue(42);
        int dotIndex = token.IndexOf('.');
        // Ein Zeichen im Payload-Teil ändern, Signatur bleibt (passt dann nicht mehr).
        char[] chars = token.ToCharArray();
        chars[0] = chars[0] == 'A' ? 'B' : 'A';
        string tampered = new string(chars);

        Assert.False(service.TryValidate(tampered, out int accountId));
        Assert.Equal(0, accountId);
    }

    [Fact]
    public void TryValidate_ExpiredToken_Fails()
    {
        var clock = new FakeClock(1_000_000);
        TokenService service = CreateService(clock);
        string token = service.Issue(42);

        clock.NowUnixSeconds += TokenService.ValiditySeconds + 1;

        Assert.False(service.TryValidate(token, out int accountId));
        Assert.Equal(0, accountId);
    }

    [Fact]
    public void TryValidate_JustBeforeExpiry_Succeeds()
    {
        var clock = new FakeClock(1_000_000);
        TokenService service = CreateService(clock);
        string token = service.Issue(42);

        clock.NowUnixSeconds += TokenService.ValiditySeconds - 1;

        Assert.True(service.TryValidate(token, out int accountId));
        Assert.Equal(42, accountId);
    }

    [Fact]
    public void IssueWithExpiry_TokenAndExpiryMatch()
    {
        var clock = new FakeClock(1_000_000);
        TokenService service = CreateService(clock);

        (string token, long expiresAtUnix) = service.IssueWithExpiry(42);

        Assert.Equal(1_000_000 + TokenService.ValiditySeconds, expiresAtUnix);
        Assert.True(service.TryValidate(token, out int accountId));
        Assert.Equal(42, accountId);
    }

    [Fact]
    public void Validate_ExpiredToken_ReturnsExpiredNotInvalid()
    {
        // Der Spielserver (M2b) unterscheidet InvalidToken/TokenExpired als unterschiedliche
        // JoinRejectReason-Werte - anders als TryValidate, das beides gleich behandelt (401 bei Auth).
        var clock = new FakeClock(1_000_000);
        TokenService service = CreateService(clock);
        string token = service.Issue(42);
        clock.NowUnixSeconds += TokenService.ValiditySeconds + 1;

        TokenValidationResult result = service.Validate(token, out int accountId);

        Assert.Equal(TokenValidationResult.Expired, result);
        Assert.Equal(0, accountId);
    }

    [Fact]
    public void Validate_TamperedSignature_ReturnsInvalidNotExpired()
    {
        TokenService service = CreateService(new FakeClock(1_000_000));
        string token = service.Issue(42);
        int dotIndex = token.IndexOf('.');
        string tampered = token[..dotIndex] + "." + new string('A', token.Length - dotIndex - 1);

        TokenValidationResult result = service.Validate(tampered, out int accountId);

        Assert.Equal(TokenValidationResult.Invalid, result);
        Assert.Equal(0, accountId);
    }

    [Fact]
    public void Validate_ValidToken_ReturnsValid()
    {
        TokenService service = CreateService(new FakeClock(1_000_000));
        string token = service.Issue(42);

        TokenValidationResult result = service.Validate(token, out int accountId);

        Assert.Equal(TokenValidationResult.Valid, result);
        Assert.Equal(42, accountId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("kauderwelsch")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("not-base64!!!.also-not-base64!!!")]
    [InlineData("YQ.YQ")] // gültiges Base64, aber kein JSON-Objekt/keine passende Signatur
    public void TryValidate_Garbage_NeverThrows_ReturnsFalse(string? garbage)
    {
        TokenService service = CreateService(new FakeClock(1_000_000));

        bool result = service.TryValidate(garbage, out int accountId);

        Assert.False(result);
        Assert.Equal(0, accountId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("this-secret-has-only-31-chars-x")]
    public void Constructor_SecretTooShort_Throws(string secret)
    {
        Assert.Throws<ArgumentException>(() => new TokenService(secret));
    }

    [Fact]
    public void Constructor_SecretExactlyMinimumLength_Succeeds()
    {
        string exactly32Bytes = new string('a', 32);
        TokenService service = new TokenService(exactly32Bytes);

        Assert.NotNull(service.Issue(1));
    }
}
