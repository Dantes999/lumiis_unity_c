using System.Collections.Generic;
using Lumiis.Data.Parties;
using Lumiis.Server.Game;
using Lumiis.Shared.Net;
using Xunit;

namespace Lumiis.Tests.Game
{
    /// <summary>Gruppenlogik ohne Netzwerk (Protocol.Version 20): Beitritt, Offline-Mitglieder,
    /// Leiterwechsel, Kick, Rollen und Auflösung.</summary>
    public class PartyManagerTests
    {
        private const int Alice = 1, Bob = 2, Carol = 3;

        private static readonly Dictionary<int, (int EntityId, string Name)> Online = new()
        {
            [Alice] = (101, "Alice"),
            [Bob] = (102, "Bob"),
            [Carol] = (103, "Carol")
        };

        private static PartyManager CreateManager(int maxMembers = 8, int offlineSeconds = 600) =>
            new PartyManager(PartyCatalog.LoadFromJson(
                "{ \"maxMembers\": " + maxMembers + ", \"xpSharePercent\": 50, \"offlineRemoveAfterSeconds\": " + offlineSeconds + "," +
                " \"roles\": [ { \"role\": \"Attacker\", \"name\": \"Angreifer\", \"attackPercent\": 5 } ] }"));

        private static Party Join(PartyManager manager, int leader, int member)
        {
            Assert.Equal(PartyActionResult.Ok, manager.Invite(leader, member));
            Assert.Equal(PartyActionResult.Ok, manager.Accept(member, Online[member].Name, Online[member].EntityId,
                id => Online.TryGetValue(id, out var info) ? info : null, out Party? party));
            return party!;
        }

        [Fact]
        public void InviteAndAccept_CreatesPartyWithInviterAsLeader()
        {
            PartyManager manager = CreateManager();
            Party party = Join(manager, Alice, Bob);

            Assert.Equal(Alice, party.LeaderCharacterId);
            Assert.Equal(2, party.Members.Count);
            Assert.True(manager.AreInSameParty(Alice, Bob));
            Assert.Same(party, manager.GetParty(Bob));
        }

        [Fact]
        public void Invite_Rejections()
        {
            PartyManager manager = CreateManager(maxMembers: 2);
            Assert.Equal(PartyActionResult.TargetIsSelf, manager.Invite(Alice, Alice));
            Join(manager, Alice, Bob);
            Assert.Equal(PartyActionResult.NotLeader, manager.Invite(Bob, Carol));
            Assert.Equal(PartyActionResult.PartyFull, manager.Invite(Alice, Carol));
            Assert.Equal(PartyActionResult.AlreadyInParty, manager.Invite(Carol, Bob));
        }

        [Fact]
        public void Accept_WithoutInviteOrWithOfflineInviter_Fails()
        {
            PartyManager manager = CreateManager();
            Assert.Equal(PartyActionResult.NoInvite, manager.Accept(Bob, "Bob", 102, _ => (101, "Alice"), out _));

            manager.Invite(Alice, Bob);
            Assert.Equal(PartyActionResult.InviterGone, manager.Accept(Bob, "Bob", 102, _ => null, out _));
            Assert.Null(manager.GetParty(Alice));
        }

        [Fact]
        public void SetOffline_KeepsMemberAndPassesLeadership()
        {
            PartyManager manager = CreateManager();
            Join(manager, Alice, Bob);
            Join(manager, Alice, Carol);

            Party? party = manager.SetOffline(Alice, 5, 80, 100, nowMs: 1000, out List<PartyMember> removed);

            Assert.NotNull(party);
            Assert.Empty(removed);
            Assert.Equal(Bob, party!.LeaderCharacterId);
            PartyMember alice = party.Find(Alice)!;
            Assert.False(alice.IsOnline);
            Assert.Equal(5, alice.LastLevel);
            Assert.Equal(80, alice.LastHp);

            Assert.Same(party, manager.SetOnline(Alice, 201));
            Assert.Equal(201, party.Find(Alice)!.EntityId);
            Assert.Equal(Bob, party.LeaderCharacterId); // Leitung bleibt beim neuen Leiter.
        }

        [Fact]
        public void SetOffline_LastOnlineMember_DissolvesParty()
        {
            PartyManager manager = CreateManager();
            Join(manager, Alice, Bob);
            manager.SetOffline(Bob, 1, 1, 1, 0, out _);

            Party? party = manager.SetOffline(Alice, 1, 1, 1, 0, out List<PartyMember> removed);

            Assert.Null(party);
            Assert.Equal(2, removed.Count);
            Assert.Null(manager.GetParty(Alice));
            Assert.Null(manager.GetParty(Bob));
        }

        [Fact]
        public void ExpireOfflineMembers_RemovesAfterDeadline_AndDissolvesTooSmallParty()
        {
            PartyManager manager = CreateManager(offlineSeconds: 10);
            Join(manager, Alice, Bob);
            Join(manager, Alice, Carol);
            manager.SetOffline(Carol, 1, 1, 1, nowMs: 0, out _);

            (List<Party> changed, List<PartyMember> removed) = manager.ExpireOfflineMembers(9_999);
            Assert.Empty(changed);
            Assert.Empty(removed);

            (changed, removed) = manager.ExpireOfflineMembers(10_000);
            Assert.Single(changed);
            Assert.Equal(Carol, Assert.Single(removed).CharacterId);
            Assert.Equal(2, manager.GetParty(Alice)!.Members.Count);

            manager.SetOffline(Bob, 1, 1, 1, nowMs: 20_000, out _);
            (changed, removed) = manager.ExpireOfflineMembers(30_000);
            Assert.Empty(changed);
            Assert.Equal(2, removed.Count); // Bob abgelaufen, Alice allein -> Gruppe aufgelöst.
            Assert.Null(manager.GetParty(Alice));
        }

        [Fact]
        public void Leave_TwoPersonParty_Dissolves()
        {
            PartyManager manager = CreateManager();
            Join(manager, Alice, Bob);

            Assert.Equal(PartyActionResult.Ok, manager.Leave(Bob, out Party? remaining, out List<PartyMember> removed));
            Assert.Null(remaining);
            Assert.Equal(2, removed.Count);
            Assert.Equal(PartyActionResult.NotInParty, manager.Leave(Bob, out _, out _));
        }

        [Fact]
        public void Leave_Leader_PassesLeadershipToOnlineMember()
        {
            PartyManager manager = CreateManager();
            Join(manager, Alice, Bob);
            Join(manager, Alice, Carol);
            manager.SetOffline(Bob, 1, 1, 1, 0, out _);

            manager.Leave(Alice, out Party? remaining, out _);

            Assert.Equal(Carol, remaining!.LeaderCharacterId);
        }

        [Fact]
        public void Kick_OnlyLeader_WorksOnOfflineMember()
        {
            PartyManager manager = CreateManager();
            Join(manager, Alice, Bob);
            Join(manager, Alice, Carol);
            manager.SetOffline(Carol, 1, 1, 1, 0, out _);

            Assert.Equal(PartyActionResult.NotLeader, manager.Kick(Bob, "Carol", out _, out _));
            Assert.Equal(PartyActionResult.TargetIsSelf, manager.Kick(Alice, "alice", out _, out _));
            Assert.Equal(PartyActionResult.TargetNotMember, manager.Kick(Alice, "Dave", out _, out _));
            Assert.Equal(PartyActionResult.Ok, manager.Kick(Alice, "carol", out Party? remaining, out List<PartyMember> removed));
            Assert.Equal(Carol, Assert.Single(removed).CharacterId);
            Assert.Equal(2, remaining!.Members.Count);
        }

        [Fact]
        public void Promote_RequiresOnlineTarget()
        {
            PartyManager manager = CreateManager();
            Join(manager, Alice, Bob);
            Join(manager, Alice, Carol);
            manager.SetOffline(Carol, 1, 1, 1, 0, out _);

            Assert.Equal(PartyActionResult.TargetOffline, manager.Promote(Alice, "Carol", out _));
            Assert.Equal(PartyActionResult.Ok, manager.Promote(Alice, "Bob", out Party? party));
            Assert.Equal(Bob, party!.LeaderCharacterId);
            Assert.Equal(PartyActionResult.NotLeader, manager.Promote(Alice, "Bob", out _));
        }

        [Fact]
        public void SetRole_OnlyKnownRolesByLeader_AndRoleIsClearedOnLeave()
        {
            PartyManager manager = CreateManager();
            Join(manager, Alice, Bob);
            Join(manager, Alice, Carol);

            Assert.Equal(PartyActionResult.UnknownRole, manager.SetRole(Alice, "Bob", PartyRole.Defender, out _, out _));
            Assert.Equal(PartyActionResult.NotLeader, manager.SetRole(Bob, "Bob", PartyRole.Attacker, out _, out _));
            Assert.Equal(PartyActionResult.Ok, manager.SetRole(Alice, "Bob", PartyRole.Attacker, out _, out PartyMember? bob));
            Assert.Equal(PartyRole.Attacker, bob!.Role);
            Assert.Equal(PartyActionResult.Ok, manager.SetRole(Alice, "Bob", PartyRole.None, out _, out _));
            Assert.Equal(PartyRole.None, bob.Role);

            manager.SetRole(Alice, "Bob", PartyRole.Attacker, out _, out _);
            manager.Leave(Bob, out _, out List<PartyMember> removed);
            Assert.Equal(PartyRole.None, Assert.Single(removed).Role);
        }

        [Fact]
        public void SetOffline_DropsPendingInvitesOfThatCharacter()
        {
            PartyManager manager = CreateManager();
            manager.Invite(Alice, Bob);
            manager.SetOffline(Alice, 1, 1, 1, 0, out _);
            Assert.False(manager.HasInvite(Bob, out _));
        }
    }
}
