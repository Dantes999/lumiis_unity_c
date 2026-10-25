using Lumiis.Server.Game;
using Xunit;

namespace Lumiis.Tests.Game
{
    /// <summary>Gruppenrollen-Bonus fließt über RecomputeDerivedStats in Attack/Defense ein.</summary>
    public class WorldPartyRoleTests
    {
        [Fact]
        public void PartyRoleBonus_RaisesAttackAndDefense_AndIsRemovable()
        {
            var world = new World(maxPlayers: 10);
            world.TryJoin(1, "P1", 50000, 50000, out Entity? player, out _);
            int baseAttack = player!.Attack;
            int baseDefense = player.Defense;

            player.PartyAttackBonusPercent = 10;
            player.PartyDefenseBonusPercent = 20;
            world.RecomputeDerivedStats(player.Id);
            Assert.Equal(baseAttack + (int)System.Math.Round(baseAttack * 0.10), player.Attack);
            Assert.Equal(baseDefense + (int)System.Math.Round(baseDefense * 0.20), player.Defense);

            player.PartyAttackBonusPercent = 0;
            player.PartyDefenseBonusPercent = 0;
            world.RecomputeDerivedStats(player.Id);
            Assert.Equal(baseAttack, player.Attack);
            Assert.Equal(baseDefense, player.Defense);
        }
    }
}
