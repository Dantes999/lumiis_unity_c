using Lumiis.Data;

namespace Lumiis.Tests.Data;

public class PasswordHasherTests
{
    [Fact]
    public void Hash_Then_Verify_RoundTrip_Succeeds()
    {
        (byte[] hash, byte[] salt, int iterations) = PasswordHasher.Hash("correct horse battery staple");

        Assert.True(PasswordHasher.Verify("correct horse battery staple", hash, salt, iterations));
    }

    [Fact]
    public void Verify_WrongPassword_Fails()
    {
        (byte[] hash, byte[] salt, int iterations) = PasswordHasher.Hash("correct horse battery staple");

        Assert.False(PasswordHasher.Verify("wrong password", hash, salt, iterations));
    }

    [Fact]
    public void Hash_SamePassword_TwiceProducesDifferentHashes()
    {
        (byte[] hashA, byte[] saltA, _) = PasswordHasher.Hash("same password");
        (byte[] hashB, byte[] saltB, _) = PasswordHasher.Hash("same password");

        Assert.NotEqual(saltA, saltB);
        Assert.NotEqual(hashA, hashB);
    }

    [Fact]
    public void Hash_ReturnsConfiguredSizesAndCurrentIterations()
    {
        (byte[] hash, byte[] salt, int iterations) = PasswordHasher.Hash("correct horse battery staple");

        Assert.Equal(PasswordHasher.HashSize, hash.Length);
        Assert.Equal(PasswordHasher.SaltSize, salt.Length);
        Assert.Equal(PasswordHasher.CurrentIterations, iterations);
    }

    [Fact]
    public void Verify_DifferentLengthHash_ReturnsFalseInsteadOfThrowing()
    {
        (byte[] hash, byte[] salt, int iterations) = PasswordHasher.Hash("correct horse battery staple");
        byte[] shorterHash = new byte[hash.Length - 1];
        byte[] longerHash = new byte[hash.Length + 1];

        Assert.False(PasswordHasher.Verify("correct horse battery staple", shorterHash, salt, iterations));
        Assert.False(PasswordHasher.Verify("correct horse battery staple", longerHash, salt, iterations));
    }

    [Theory]
    [InlineData("1234567")] // 7 Zeichen, zu kurz
    [InlineData("")]
    [InlineData(null)]
    public void IsValidPassword_RejectsTooShort(string? password)
    {
        Assert.False(PasswordHasher.IsValidPassword(password));
    }

    [Fact]
    public void IsValidPassword_AcceptsMinLength()
    {
        Assert.True(PasswordHasher.IsValidPassword("12345678")); // genau 8 Zeichen
    }
}
