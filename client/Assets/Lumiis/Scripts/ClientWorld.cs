using System;
using System.Collections.Generic;
using Lumiis.Shared;
using Lumiis.Shared.Net;

namespace Lumiis.Client
{
    /// <summary>Eine Entity in der Client-Weltsicht. Reine Daten, kein UnityEngine-Bezug.</summary>
    public sealed class ClientEntity
    {
        public int Id;
        public string Name;
        public EntityKind Kind;
        public int TypeId;
        public double X;
        public double Y;
        public Appearance Appearance;
        public Facing Facing = new Facing(FacingDirection.Down, false);

        public bool IsMoving;
        public int StartX;
        public int StartY;
        public int TargetX;
        public int TargetY;
        public int Speed;
        public double MoveStartTime;

        /// <summary>-1 = noch unbekannt (M4a: JoinAccepted/EntitySpawn tragen kein Leben, der Client erfährt
        /// es erst reaktiv über das erste EntityDamagedPacket für diese Entity, siehe HandleEntityDamaged).</summary>
        public int CurrentHp = -1;
        public int MaxHp = -1;

        public int IntX
        {
            get { return (int)Math.Round(X); }
        }

        public int IntY
        {
            get { return (int)Math.Round(Y); }
        }
    }

    /// <summary>M6a: der vollständige eigene Fortschritts-/Kampfwert-Stand (siehe CharacterStatsPacket) -
    /// reine Daten, kein UnityEngine-Bezug. Enthält bewusst NICHT CurrentHp/MaxHp - die eigenen HP bleiben
    /// ausschließlich in ClientEntity.CurrentHp/MaxHp (siehe ClientWorld.ApplyHp), kein zweiter Speicherort.</summary>
    public sealed class CharacterState
    {
        public int Level;
        public long Xp;
        public long XpToNext;
        public int St;
        public int Ht;
        public int Dx;
        public int Iq;
        public int UnspentStatPoints;
        public int UnspentSkillPoints;
        public int MaxSp;
        public int CurrentSp;
        public int Attack;
        public int Defense;
        public int DodgeBp;

        /// <summary>M6b: gelernte Skillstufen (SkillId -> Level 1-17, kein Eintrag = nicht gelernt/Stufe 0) -
        /// analog zum Rest von CharacterState, direkt aus CharacterStatsPacket.Skills übernommen.</summary>
        public readonly Dictionary<int, int> Skills = new Dictionary<int, int>();
    }

    /// <summary>Reine Client-Weltsicht: verwaltet Entities aus Server-Paketen. Kennt kein UnityEngine, kein Time.* -
    /// die aktuelle Zeit wird von außen übergeben (Client-Empfangszeit, keine Uhrensynchronisation in M1b).
    /// Der Client berechnet nie Spielzustand selbst: eigene Charakterdaten kommen im JoinAccepted, öffentliche
    /// Daten über EntitySpawn - beide laufen über denselben Pfad (ApplyAppearance), keine doppelte Logik.</summary>
    public sealed class ClientWorld
    {
        private readonly Dictionary<int, ClientEntity> _entities = new Dictionary<int, ClientEntity>();

        public int LocalEntityId { get; private set; } = -1;
        public IReadOnlyDictionary<int, ClientEntity> Entities
        {
            get { return _entities; }
        }

        /// <summary>M6a: eigener Fortschritts-/Kampfwert-Stand (siehe CharacterState) - Instanz bleibt über
        /// die gesamte Lebenszeit von ClientWorld bestehen, ApplyCharacterStats aktualisiert nur ihre Felder
        /// (kein Neuanlegen), damit gehaltene Referenzen (z. B. im Debug-Panel) gültig bleiben.</summary>
        public CharacterState CharacterStats { get; } = new CharacterState();

        public event Action<ClientEntity> EntitySpawned;
        public event Action<int> EntityDespawned;
        public event Action<string> Warning;

        public void Reset()
        {
            List<int> ids = new List<int>(_entities.Keys);
            _entities.Clear();
            LocalEntityId = -1;
            CharacterStats.Level = 0; // 0 = noch kein CharacterStatsPacket erhalten (frisch nach Reset/vor Join).
            CharacterStats.Xp = 0;
            CharacterStats.XpToNext = 0;
            CharacterStats.St = 0;
            CharacterStats.Ht = 0;
            CharacterStats.Dx = 0;
            CharacterStats.Iq = 0;
            CharacterStats.UnspentStatPoints = 0;
            CharacterStats.UnspentSkillPoints = 0;
            CharacterStats.MaxSp = 0;
            CharacterStats.CurrentSp = 0;
            CharacterStats.Attack = 0;
            CharacterStats.Defense = 0;
            CharacterStats.DodgeBp = 0;
            CharacterStats.Skills.Clear();
            foreach (int id in ids) EntityDespawned?.Invoke(id);
        }

        public void HandleJoinAccepted(int entityId, int x, int y, string localName, Appearance appearance)
        {
            LocalEntityId = entityId;
            ApplyAppearance(entityId, localName, EntityKind.Player, typeId: 0, x, y, appearance);
        }

        public void HandleEntitySpawn(int entityId, string name, EntityKind kind, int typeId, int x, int y, Appearance appearance)
        {
            ApplyAppearance(entityId, name, kind, typeId, x, y, appearance);
        }

        /// <summary>Gemeinsamer Erzeugungs-/Aktualisierungspfad für die eigene Figur (aus JoinAccepted, immer
        /// Kind=Player) und andere Figuren/Monster (aus EntitySpawn) - kein zweiter Codepfad.</summary>
        private void ApplyAppearance(int entityId, string name, EntityKind kind, int typeId, int x, int y, Appearance appearance)
        {
            var entity = new ClientEntity { Id = entityId, Name = name, Kind = kind, TypeId = typeId, X = x, Y = y, Appearance = appearance };
            _entities[entityId] = entity;
            EntitySpawned?.Invoke(entity);
        }

        public void HandleEntityDespawn(int entityId)
        {
            if (!_entities.Remove(entityId))
            {
                Warning?.Invoke("EntityDespawn für unbekannte Entity " + entityId);
                return;
            }
            EntityDespawned?.Invoke(entityId);
        }

        public void HandleEntityMove(int entityId, int startX, int startY, int targetX, int targetY, int speed, double receivedAtTime)
        {
            ClientEntity entity;
            if (!_entities.TryGetValue(entityId, out entity))
            {
                Warning?.Invoke("EntityMove für unbekannte Entity " + entityId);
                return;
            }

            entity.IsMoving = true;
            entity.StartX = startX;
            entity.StartY = startY;
            entity.TargetX = targetX;
            entity.TargetY = targetY;
            entity.Speed = speed;
            entity.MoveStartTime = receivedAtTime;
            entity.X = startX;
            entity.Y = startY;
            entity.Facing = FacingMath.FromDelta(targetX - startX, targetY - startY, entity.Facing);
        }

        public void HandleEntityStop(int entityId, int x, int y)
        {
            ClientEntity entity;
            if (!_entities.TryGetValue(entityId, out entity))
            {
                Warning?.Invoke("EntityStop für unbekannte Entity " + entityId);
                return;
            }

            entity.IsMoving = false;
            entity.X = x;
            entity.Y = y;
        }

        /// <summary>M4a: aktualisiert das bekannte Leben einer getroffenen Entity (Debug-Overlay, siehe
        /// GameClient) - unbekannte Entity-Id ist kein Fehler (z. B. Paket für eine Entity, die in derselben
        /// Tick-Runde bereits despawnt wurde), anders als bei Move/Stop keine Warnung nötig.</summary>
        public void HandleEntityDamaged(int targetEntityId, int hpAfter, int maxHp)
        {
            ApplyHp(targetEntityId, hpAfter, maxHp);
        }

        /// <summary>Bugfix nach M5a: Leben-Änderung ohne Treffer (Heiltrank, Spieler-Respawn, bereits
        /// verletzte Entity beim Betreten der AOI - siehe EntityHpChangedPacket-Kommentar). Derselbe
        /// Zustandspfad wie HandleEntityDamaged (ApplyHp) - kein zweiter Codepfad, das Overlay liest in
        /// beiden Fällen denselben ClientEntity.CurrentHp/MaxHp.</summary>
        public void HandleEntityHpChanged(int entityId, int currentHp, int maxHp)
        {
            ApplyHp(entityId, currentHp, maxHp);
        }

        /// <summary>Gemeinsamer Schreibpfad für jede Art von Leben-Änderung (Treffer oder nicht) - siehe
        /// HandleEntityDamaged/HandleEntityHpChanged. Unbekannte Entity-Id ist kein Fehler, siehe dort.</summary>
        private void ApplyHp(int entityId, int currentHp, int maxHp)
        {
            if (!_entities.TryGetValue(entityId, out ClientEntity entity)) return;
            entity.CurrentHp = currentHp;
            entity.MaxHp = maxHp;
        }

        /// <summary>M6a: voller eigener Fortschritts-/Kampfwert-Stand (siehe CharacterStatsPacket/
        /// CharacterState) - die eigenen HP laufen dabei über die BESTEHENDE gemeinsame ApplyHp (kein
        /// zweiter Speicherort für CurrentHp/MaxHp, siehe CharacterState-Kommentar), alle anderen Werte
        /// landen in CharacterStats. Unbekannter LocalEntityId (Paket vor JoinAccepted - sollte nicht
        /// vorkommen) überspringt nur den ApplyHp-Aufruf, CharacterStats wird trotzdem aktualisiert.</summary>
        public void ApplyCharacterStats(CharacterStatsPacket packet)
        {
            if (LocalEntityId >= 0)
                ApplyHp(LocalEntityId, packet.CurrentHp, packet.MaxHp);

            CharacterStats.Level = packet.Level;
            CharacterStats.Xp = packet.Xp;
            CharacterStats.XpToNext = packet.XpToNext;
            CharacterStats.St = packet.St;
            CharacterStats.Ht = packet.Ht;
            CharacterStats.Dx = packet.Dx;
            CharacterStats.Iq = packet.Iq;
            CharacterStats.UnspentStatPoints = packet.UnspentStatPoints;
            CharacterStats.UnspentSkillPoints = packet.UnspentSkillPoints;
            CharacterStats.MaxSp = packet.MaxSp;
            CharacterStats.CurrentSp = packet.CurrentSp;
            CharacterStats.Attack = packet.Attack;
            CharacterStats.Defense = packet.Defense;
            CharacterStats.DodgeBp = packet.DodgeBp;

            // M6b: voller Resync wie der Rest des Pakets, kein Delta.
            CharacterStats.Skills.Clear();
            foreach ((int skillId, int level) in packet.Skills)
                CharacterStats.Skills[skillId] = level;
        }

        /// <summary>M4a: EntityDamaged geht dem hier immer voraus (siehe GameServer.HandleAttackRequest), das
        /// hier ist nur eine defensive Absicherung des angezeigten Werts.</summary>
        public void HandleEntityDied(int entityId)
        {
            if (!_entities.TryGetValue(entityId, out ClientEntity entity)) return;
            entity.CurrentHp = 0;
        }

        /// <summary>Aktualisiert die interpolierte Position aller bewegten Entities für den gegebenen Zeitpunkt (Sekunden, Client-Uhr).</summary>
        public void Tick(double currentTime)
        {
            foreach (ClientEntity entity in _entities.Values)
            {
                if (!entity.IsMoving) continue;

                double elapsed = currentTime - entity.MoveStartTime;
                MovementSample sample = MovementMath.Evaluate(entity.StartX, entity.StartY, entity.TargetX, entity.TargetY, entity.Speed, elapsed);
                entity.X = sample.X;
                entity.Y = sample.Y;
                if (sample.Arrived) entity.IsMoving = false;
            }
        }
    }
}
