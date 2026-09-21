using Lumiis.Shared;
using Xunit;

namespace Lumiis.Tests
{
    public class NameRulesTests
    {
        [Theory]
        [InlineData("Bot")]
        [InlineData("Arthur")]
        [InlineData("Held_1")]
        [InlineData("a_b_c_d_e_f")]
        [InlineData("ABCDEFGHIJKLMNOP")]
        [InlineData("Höld")]
        public void ValidNames_AreAccepted(string name)
        {
            Assert.True(NameRules.IsValid(name));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("ab")]
        [InlineData("ABCDEFGHIJKLMNOPQ")]
        [InlineData("Held Name")]
        [InlineData("Held-1")]
        [InlineData("Held!")]
        public void InvalidNames_AreRejected(string? name)
        {
            Assert.False(NameRules.IsValid(name));
        }
    }
}
