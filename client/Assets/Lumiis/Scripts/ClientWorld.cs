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

        public int IntX
        {
            get { return (int)Math.Round(X); }
        }

        public int IntY
        {
            get { return (int)Math.Round(Y); }
        }
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

        public event Action<ClientEntity> EntitySpawned;
        public event Action<int> EntityDespawned;
        public event Action<string> Warning;

        public void Reset()
        {
            List<int> ids = new List<int>(_entities.Keys);
            _entities.Clear();
            LocalEntityId = -1;
            foreach (int id in ids) EntityDespawned?.Invoke(id);
        }

        public void HandleJoinAccepted(int entityId, int x, int y, string localName, Appearance appearance)
        {
            LocalEntityId = entityId;
            ApplyAppearance(entityId, localName, x, y, appearance);
        }

        public void HandleEntitySpawn(int entityId, string name, int x, int y, Appearance appearance)
        {
            ApplyAppearance(entityId, name, x, y, appearance);
        }

        /// <summary>Gemeinsamer Erzeugungs-/Aktualisierungspfad für die eigene Figur (aus JoinAccepted) und
        /// andere Figuren (aus EntitySpawn) - kein zweiter Codepfad.</summary>
        private void ApplyAppearance(int entityId, string name, int x, int y, Appearance appearance)
        {
            var entity = new ClientEntity { Id = entityId, Name = name, X = x, Y = y, Appearance = appearance };
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
