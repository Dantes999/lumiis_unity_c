using System;
using System.IO;
using Lumiis.Data.Parties;
using Lumiis.Shared.Net;
using Xunit;

namespace Lumiis.Tests.Data.Parties;

public class PartyCatalogTests
{
    private const string ValidJson = @"{
      ""maxMembers"": 8, ""xpSharePercent"": 50, ""offlineRemoveAfterSeconds"": 600,
      ""roles"": [
        { ""role"": ""Attacker"", ""name"": ""Angreifer"", ""attackPercent"": 5, ""defensePercent"": 0 },
        { ""role"": ""Defender"", ""name"": ""Verteidiger"", ""attackPercent"": 0, ""defensePercent"": 5 }
      ]
    }";

    [Fact]
    public void LoadFromJson_ValidData_ParsesRulesAndRoles()
    {
        PartyCatalog catalog = PartyCatalog.LoadFromJson(ValidJson);
        Assert.Equal(8, catalog.MaxMembers);
        Assert.Equal(50, catalog.XpSharePercent);
        Assert.Equal(600_000, catalog.OfflineRemoveAfterMs);
        Assert.True(catalog.TryGetRole(PartyRole.Attacker, out PartyRoleDefinition attacker));
        Assert.Equal(5, attacker.AttackPercent);
        Assert.True(catalog.TryGetRole(PartyRole.Defender, out PartyRoleDefinition defender));
        Assert.Equal("Verteidiger", defender.Name);
        Assert.False(catalog.TryGetRole(PartyRole.None, out _));
    }

    [Theory]
    [InlineData(@"{ ""maxMembers"": 1, ""xpSharePercent"": 50, ""offlineRemoveAfterSeconds"": 0 }", "maxMembers")]
    [InlineData(@"{ ""maxMembers"": 8, ""xpSharePercent"": 101, ""offlineRemoveAfterSeconds"": 0 }", "xpSharePercent")]
    [InlineData(@"{ ""maxMembers"": 8, ""xpSharePercent"": 50, ""offlineRemoveAfterSeconds"": -1 }", "offlineRemoveAfterSeconds")]
    [InlineData(@"{ ""maxMembers"": 8, ""xpSharePercent"": 50, ""offlineRemoveAfterSeconds"": 0, ""roles"": [ { ""role"": ""Healer"", ""name"": ""X"" } ] }", "unbekannte Rolle")]
    [InlineData(@"{ ""maxMembers"": 8, ""xpSharePercent"": 50, ""offlineRemoveAfterSeconds"": 0, ""roles"": [ { ""role"": ""None"", ""name"": ""X"" } ] }", "unbekannte Rolle")]
    [InlineData(@"{ ""maxMembers"": 8, ""xpSharePercent"": 50, ""offlineRemoveAfterSeconds"": 0, ""roles"": [ { ""role"": ""Attacker"", ""name"": ""A"" }, { ""role"": ""Attacker"", ""name"": ""B"" } ] }", "doppelt")]
    [InlineData(@"{ ""maxMembers"": 8, ""xpSharePercent"": 50, ""offlineRemoveAfterSeconds"": 0, ""roles"": [ { ""role"": ""Attacker"", ""name"": ""A"", ""attackPercent"": 150 } ] }", "Boni")]
    [InlineData(@"kein json", "kein gültiges JSON")]
    public void LoadFromJson_InvalidData_Throws(string json, string expectedMessagePart)
    {
        var ex = Assert.Throws<PartyCatalogException>(() => PartyCatalog.LoadFromJson(json));
        Assert.Contains(expectedMessagePart, ex.Message);
    }

    [Fact]
    public void LoadFromFile_RealDataFile_IsValid()
    {
        string path = Path.Combine(FindRepoRoot(), "data", "party.json");
        PartyCatalog catalog = PartyCatalog.LoadFromFile(path);
        Assert.True(catalog.MaxMembers >= 2);
        Assert.True(catalog.TryGetRole(PartyRole.Attacker, out _));
        Assert.True(catalog.TryGetRole(PartyRole.Defender, out _));
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "data", "party.json"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("Repo-Root mit data/party.json nicht gefunden.");
    }
}
