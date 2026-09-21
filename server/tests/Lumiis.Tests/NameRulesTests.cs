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
        [InlineData("Höld")] // Homoglyph-Schutz: nur ASCII-Buchstaben erlaubt
        [InlineData("Ᏼot")] // kyrillisch/cherokee-ähnliches Zeichen statt "B" (Homoglyph)
        public void InvalidNames_AreRejected(string? name)
        {
            Assert.False(NameRules.IsValid(name));
        }
    }
}
