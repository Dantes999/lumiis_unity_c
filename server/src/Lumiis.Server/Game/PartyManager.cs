using Lumiis.Data.Parties;
using Lumiis.Shared.Net;

namespace Lumiis.Server.Game;

/// <summary>Ein Gruppenmitglied, über die CharacterId identifiziert (nicht die EntityId), damit es beim
/// Aus-/Einloggen in der Gruppe bleibt. EntityId ist null, solange der Charakter offline ist; Level/Hp
/// sind dann der letzte bekannte Stand.</summary>
public sealed class PartyMember
{
    public PartyMember(int characterId, string name)
    {
        CharacterId = characterId;
        Name = name;
    }

    public int CharacterId { get; }
    public string Name { get; }
    public int? EntityId { get; internal set; }
    public PartyRole Role { get; internal set; }
    /// <summary>Wandzeit, ab der ein offline gegangenes Mitglied entfernt wird (nur gültig, wenn offline).</summary>
    public long RemoveAtMs { get; internal set; }
    public int LastLevel { get; internal set; }
    public int LastHp { get; internal set; }
    public int LastMaxHp { get; internal set; }

    public bool IsOnline => EntityId.HasValue;
}

public sealed class Party
{
    public int LeaderCharacterId { get; internal set; }
    public List<PartyMember> Members { get; } = new();

    public PartyMember? Find(int characterId) => Members.Find(m => m.CharacterId == characterId);

    public PartyMember? FindByName(string name) =>
        Members.Find(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));

    public int OnlineCount => Members.Count(m => m.IsOnline);
}

public enum PartyActionResult
{
    Ok,
    NotInParty,
    NotLeader,
    AlreadyInParty,
    PartyFull,
    TargetIsSelf,
    TargetNotMember,
    TargetOffline,
    NoInvite,
    InviterGone,
    UnknownRole
}

/// <summary>Gruppenzustand ohne Netzwerkbezug (Protocol.Version 20, ersetzt das bisherige EntityId-basierte
/// Dictionary in GameServer): Einladungen, Beitritt, Verlassen, Kick, Leiterwechsel, Rollen und Offline-
/// Mitglieder. Nur im Speicher - eine Gruppe überlebt das Aus-/Einloggen ihrer Mitglieder, aber keinen
/// Serverneustart. Regeln:
/// - Leiter ist immer ein Online-Mitglied, solange eines existiert; geht der Leiter offline oder verlässt die
///   Gruppe, übernimmt das erste Online-Mitglied.
/// - Eine Gruppe mit weniger als zwei Mitgliedern oder ohne Online-Mitglied löst sich auf.
/// - Offline-Mitglieder werden nach PartyCatalog.OfflineRemoveAfterMs entfernt (ExpireOfflineMembers).
/// Jede ändernde Methode liefert die betroffenen Gruppen bzw. entfernten Mitglieder zurück, damit GameServer
/// die passenden Pakete verschicken kann.</summary>
public sealed class PartyManager
{
    private readonly PartyCatalog _catalog;
    private readonly Dictionary<int, Party> _partyByCharacterId = new();
    /// <summary>Invitee-CharacterId -> Inviter-CharacterId.</summary>
    private readonly Dictionary<int, int> _pendingInvites = new();

    public PartyManager(PartyCatalog? catalog = null)
    {
        _catalog = catalog ?? PartyCatalog.Default;
    }

    public PartyCatalog Catalog => _catalog;

    public Party? GetParty(int characterId) => _partyByCharacterId.GetValueOrDefault(characterId);

    public bool AreInSameParty(int characterIdA, int characterIdB) =>
        _partyByCharacterId.TryGetValue(characterIdA, out Party? party) && party.Find(characterIdB) != null;

    public PartyActionResult Invite(int inviterCharacterId, int inviteeCharacterId)
    {
        if (inviterCharacterId == inviteeCharacterId) return PartyActionResult.TargetIsSelf;
        if (_partyByCharacterId.ContainsKey(inviteeCharacterId)) return PartyActionResult.AlreadyInParty;
        if (_partyByCharacterId.TryGetValue(inviterCharacterId, out Party? party))
        {
            if (party.LeaderCharacterId != inviterCharacterId) return PartyActionResult.NotLeader;
            if (party.Members.Count >= _catalog.MaxMembers) return PartyActionResult.PartyFull;
        }

        _pendingInvites[inviteeCharacterId] = inviterCharacterId;
        return PartyActionResult.Ok;
    }

    /// <summary>Holt die offene Einladung ab (ohne Beitritt), z. B. für Ablehnen.</summary>
    public bool TryTakeInvite(int inviteeCharacterId, out int inviterCharacterId) =>
        _pendingInvites.Remove(inviteeCharacterId, out inviterCharacterId);

    public bool HasInvite(int inviteeCharacterId, out int inviterCharacterId) =>
        _pendingInvites.TryGetValue(inviteeCharacterId, out inviterCharacterId);

    /// <summary>Nimmt die offene Einladung an. inviter muss online sein (GameServer übergibt dessen aktuelle
    /// EntityId/Name, sonst InviterGone).</summary>
    public PartyActionResult Accept(int inviteeCharacterId, string inviteeName, int inviteeEntityId,
        Func<int, (int EntityId, string Name)?> onlineInviter, out Party? party)
    {
        party = null;
        if (_partyByCharacterId.ContainsKey(inviteeCharacterId)) return PartyActionResult.AlreadyInParty;
        if (!_pendingInvites.Remove(inviteeCharacterId, out int inviterCharacterId)) return PartyActionResult.NoInvite;

        (int EntityId, string Name)? inviter = onlineInviter(inviterCharacterId);
        if (inviter == null) return PartyActionResult.InviterGone;

        if (!_partyByCharacterId.TryGetValue(inviterCharacterId, out party))
        {
            party = new Party { LeaderCharacterId = inviterCharacterId };
            party.Members.Add(new PartyMember(inviterCharacterId, inviter.Value.Name) { EntityId = inviter.Value.EntityId });
            _partyByCharacterId[inviterCharacterId] = party;
        }
        else if (party.LeaderCharacterId != inviterCharacterId)
        {
            // Der Einladende hat die Leitung inzwischen verloren - Einladung gilt nicht mehr.
            party = null;
            return PartyActionResult.NotLeader;
        }

        if (party.Members.Count >= _catalog.MaxMembers)
        {
            Party full = party;
            party = null;
            // Eine nur für diese Einladung angelegte Ein-Personen-Gruppe nicht stehen lassen.
            if (full.Members.Count < 2) _partyByCharacterId.Remove(inviterCharacterId);
            return PartyActionResult.PartyFull;
        }

        party.Members.Add(new PartyMember(inviteeCharacterId, inviteeName) { EntityId = inviteeEntityId });
        _partyByCharacterId[inviteeCharacterId] = party;
        return PartyActionResult.Ok;
    }

    /// <summary>Mitglied verlässt die Gruppe freiwillig. dissolved: alle Mitglieder, die dadurch keine Gruppe
    /// mehr haben (inkl. des Verlassenden); party: die verbliebene Gruppe oder null.</summary>
    public PartyActionResult Leave(int characterId, out Party? remaining, out List<PartyMember> removed)
    {
        removed = new List<PartyMember>();
        remaining = null;
        if (!_partyByCharacterId.TryGetValue(characterId, out Party? party)) return PartyActionResult.NotInParty;

        RemoveMember(party, characterId, removed);
        remaining = DissolveIfTooSmall(party, removed);
        return PartyActionResult.Ok;
    }

    public PartyActionResult Kick(int leaderCharacterId, string targetName, out Party? remaining, out List<PartyMember> removed)
    {
        removed = new List<PartyMember>();
        remaining = null;
        if (!_partyByCharacterId.TryGetValue(leaderCharacterId, out Party? party)) return PartyActionResult.NotInParty;
        if (party.LeaderCharacterId != leaderCharacterId) return PartyActionResult.NotLeader;
        PartyMember? target = party.FindByName(targetName);
        if (target == null) return PartyActionResult.TargetNotMember;
        if (target.CharacterId == leaderCharacterId) return PartyActionResult.TargetIsSelf;

        RemoveMember(party, target.CharacterId, removed);
        remaining = DissolveIfTooSmall(party, removed);
        return PartyActionResult.Ok;
    }

    public PartyActionResult Promote(int leaderCharacterId, string targetName, out Party? party)
    {
        if (!_partyByCharacterId.TryGetValue(leaderCharacterId, out party)) return PartyActionResult.NotInParty;
        if (party.LeaderCharacterId != leaderCharacterId) return PartyActionResult.NotLeader;
        PartyMember? target = party.FindByName(targetName);
        if (target == null) return PartyActionResult.TargetNotMember;
        if (target.CharacterId == leaderCharacterId) return PartyActionResult.TargetIsSelf;
        if (!target.IsOnline) return PartyActionResult.TargetOffline;

        party.LeaderCharacterId = target.CharacterId;
        return PartyActionResult.Ok;
    }

    /// <summary>Der Leiter darf auch sich selbst eine Rolle geben. PartyRole.None entfernt sie; jede andere
    /// Rolle muss in party.json definiert sein.</summary>
    public PartyActionResult SetRole(int leaderCharacterId, string targetName, PartyRole role, out Party? party, out PartyMember? target)
    {
        target = null;
        if (!_partyByCharacterId.TryGetValue(leaderCharacterId, out party)) return PartyActionResult.NotInParty;
        if (party.LeaderCharacterId != leaderCharacterId) return PartyActionResult.NotLeader;
        if (role != PartyRole.None && !_catalog.TryGetRole(role, out _)) return PartyActionResult.UnknownRole;
        target = party.FindByName(targetName);
        if (target == null) return PartyActionResult.TargetNotMember;

        target.Role = role;
        return PartyActionResult.Ok;
    }

    /// <summary>Beim Disconnect: Mitglied bleibt offline in der Gruppe. Offene Einladungen von/an den
    /// Charakter verfallen. Gibt die Gruppe zurück, falls sie weiter besteht, sonst null (removed listet dann
    /// die aufgelösten Mitglieder).</summary>
    public Party? SetOffline(int characterId, int lastLevel, int lastHp, int lastMaxHp, long nowMs, out List<PartyMember> removed)
    {
        removed = new List<PartyMember>();
        DropInvitesInvolving(characterId);
        if (!_partyByCharacterId.TryGetValue(characterId, out Party? party)) return null;

        PartyMember member = party.Find(characterId)!;
        member.EntityId = null;
        member.LastLevel = lastLevel;
        member.LastHp = lastHp;
        member.LastMaxHp = lastMaxHp;
        member.RemoveAtMs = nowMs + _catalog.OfflineRemoveAfterMs;

        if (party.LeaderCharacterId == characterId)
            PassLeadership(party);

        return DissolveIfTooSmall(party, removed);
    }

    /// <summary>Beim Join: war der Charakter noch in einer Gruppe, ist er wieder online darin.</summary>
    public Party? SetOnline(int characterId, int entityId)
    {
        if (!_partyByCharacterId.TryGetValue(characterId, out Party? party)) return null;
        party.Find(characterId)!.EntityId = entityId;
        return party;
    }

    /// <summary>Entfernt Offline-Mitglieder, deren Frist abgelaufen ist. Liefert die geänderten,
    /// weiterbestehenden Gruppen und alle Mitglieder, die dadurch keine Gruppe mehr haben.</summary>
    public (List<Party> Changed, List<PartyMember> Removed) ExpireOfflineMembers(long nowMs)
    {
        var changed = new List<Party>();
        var removed = new List<PartyMember>();
        foreach (Party party in _partyByCharacterId.Values.Distinct().ToList())
        {
            List<PartyMember> expired = party.Members.Where(m => !m.IsOnline && nowMs >= m.RemoveAtMs).ToList();
            if (expired.Count == 0) continue;
            foreach (PartyMember member in expired)
                RemoveMember(party, member.CharacterId, removed);
            Party? remaining = DissolveIfTooSmall(party, removed);
            if (remaining != null) changed.Add(remaining);
        }
        return (changed, removed);
    }

    private void RemoveMember(Party party, int characterId, List<PartyMember> removed)
    {
        PartyMember? member = party.Find(characterId);
        if (member == null) return;
        party.Members.Remove(member);
        _partyByCharacterId.Remove(characterId);
        DropInvitesInvolving(characterId);
        member.Role = PartyRole.None;
        removed.Add(member);
        if (party.LeaderCharacterId == characterId)
            PassLeadership(party);
    }

    private static void PassLeadership(Party party)
    {
        PartyMember? next = party.Members.Find(m => m.IsOnline && m.CharacterId != party.LeaderCharacterId);
        if (next != null) party.LeaderCharacterId = next.CharacterId;
    }

    private Party? DissolveIfTooSmall(Party party, List<PartyMember> removed)
    {
        if (party.Members.Count >= 2 && party.OnlineCount > 0) return party;

        foreach (PartyMember member in party.Members.ToList())
        {
            party.Members.Remove(member);
            _partyByCharacterId.Remove(member.CharacterId);
            member.Role = PartyRole.None;
            removed.Add(member);
        }
        return null;
    }

    private void DropInvitesInvolving(int characterId)
    {
        _pendingInvites.Remove(characterId);
        foreach (int invitee in _pendingInvites.Where(kv => kv.Value == characterId).Select(kv => kv.Key).ToList())
            _pendingInvites.Remove(invitee);
    }
}
