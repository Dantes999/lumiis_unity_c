using Lumiis.Data;

namespace Lumiis.Tests.Data;

public class UsernameRulesTests
{
    [Theory]
    [InlineData("abc")]
    [InlineData("Arthur")]
    [InlineData("player_1")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdef")] // 32 Zeichen, genau am Limit
    public void IsValid_AcceptsAsciiWithinLength(string username)
    {
        Assert.True(UsernameRules.IsValid(username));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ab")] // zu kurz
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefg")] // 33 Zeichen, zu lang
    [InlineData("ärthur")] // kein ASCII
    [InlineData("player name")] // Leerzeichen
    [InlineData("player-1")] // Bindestrich nicht erlaubt
    public void IsValid_RejectsInvalid(string? username)
    {
        Assert.False(UsernameRules.IsValid(username));
    }
}
