using Lumiis.Shared;
using Lumiis.Shared.Net;

namespace Lumiis.Server.Game;

/// <summary>Eine bewegliche Entity in der Welt - seit M3c Spieler ODER Monster (Kind), reine Logik, kein
/// Netzwerk.</summary>
public sealed class Entity
{
    public int Id { get; }

    /// <summary>Die Datenbank-Id des Charakters (Lumiis.Data.Character) - World nutzt sie, um pro
    /// CharacterId nur eine gleichzeitige Verbindung zuzulassen (siehe World._entityIdByCharacterId). Nur bei
    /// Kind=Player gesetzt - Monster haben keine Verbindung/keinen Account (siehe CreateMonster).</summary>
    public int? CharacterId { get; }

    public string Name { get; }

    public EntityKind Kind { get; }

    /// <summary>Nur bei Kind=Monster aussagekräftig (Index in data/mobs.json), bei Kind=Player ungenutzt/0.</summary>
    public int MobId { get; }

    /// <summary>Wird von GameServer direkt nach dem Join aus der geladenen Character-Zeile gesetzt. Bei
    /// Monstern mit Default-Werten befüllt (siehe EntitySpawnPacket-Kommentar), keine Bedeutung.</summary>
    public Appearance Appearance { get; set; }

    public double X { get; private set; }
    public double Y { get; private set; }

    public bool IsMoving { get; private set; }
    public int TargetX { get; private set; }
    public int TargetY { get; private set; }
    public int Speed { get; private set; }

    public int IntX => (int)System.Math.Round(X);
    public int IntY => (int)System.Math.Round(Y);

    /// <summary>Wandzeit (siehe GameServer._nowMs) des letzten Positions-Speicherversuchs - vom Game-Loop-
    /// Thread gesetzt (GameServer.EnqueueDuePositionSaves), rein Buchhaltung für die 30s-Speicherentscheidung,
    /// keine Aussage darüber, ob der Hintergrund-Task den Schreibvorgang schon abgeschlossen hat. Nur bei
    /// Kind=Player relevant (siehe CharacterId).</summary>
    public long LastSaveAtMs { get; set; }

    /// <summary>Nur bei Kind=Monster gesetzt (Spawn-Position, fester Bezugspunkt fürs Wandern, siehe
    /// World.RunWanderAi) - bei Kind=Player die eigene Spawn-Position, ungenutzt.</summary>
    public int WanderAnchorX { get; }
    public int WanderAnchorY { get; }

    /// <summary>Nur bei Kind=Monster gesetzt (aus dem Mob-Typ, siehe MobCatalog/World.SpawnMonster) - bei
    /// Kind=Player 0/ungenutzt.</summary>
    public int WanderRadiusWire { get; }
    public int WanderIntervalMinMs { get; }
    public int WanderIntervalMaxMs { get; }

    /// <summary>Wandzeit (siehe GameServer._nowMs), wann die nächste Wander-Zielwahl fällig ist (siehe
    /// World.RunWanderAi) - von World gesetzt, nur bei Kind=Monster relevant.</summary>
    public long NextWanderAtMs { get; set; }

    /// <summary>Eigene Bewegungsgeschwindigkeit (1/100 Welteinheiten/s) - bei Kind=Player immer
    /// GameConstants.PlayerSpeed, bei Kind=Monster aus dem Mob-Typ (mobs.json SpeedWire). World.SetDestination
    /// nutzt das statt eines hartkodierten Werts, damit Spieler und Monster denselben Bewegungsmechanismus
    /// mit jeweils eigener Geschwindigkeit teilen.</summary>
    public int MovementSpeed { get; }

    private Entity(int id, EntityKind kind, int? characterId, int mobId, string name, int x, int y, int movementSpeed,
        int wanderRadiusWire, int wanderIntervalMinMs, int wanderIntervalMaxMs)
    {
        Id = id;
        Kind = kind;
        CharacterId = characterId;
        MobId = mobId;
        Name = name;
        X = x;
        Y = y;
        MovementSpeed = movementSpeed;
        WanderAnchorX = x;
        WanderAnchorY = y;
        WanderRadiusWire = wanderRadiusWire;
        WanderIntervalMinMs = wanderIntervalMinMs;
        WanderIntervalMaxMs = wanderIntervalMaxMs;
    }

    public Entity(int id, int characterId, string name, int x, int y)
        : this(id, EntityKind.Player, characterId, mobId: 0, name, x, y, movementSpeed: GameConstants.PlayerSpeed,
            wanderRadiusWire: 0, wanderIntervalMinMs: 0, wanderIntervalMaxMs: 0)
    {
    }

    public static Entity CreateMonster(int id, int mobId, string name, int x, int y, int speedWire,
        int wanderRadiusWire, int wanderIntervalMinMs, int wanderIntervalMaxMs)
    {
        return new Entity(id, EntityKind.Monster, characterId: null, mobId, name, x, y, movementSpeed: speedWire,
            wanderRadiusWire, wanderIntervalMinMs, wanderIntervalMaxMs);
    }

    public void MoveTo(int targetX, int targetY, int speed)
    {
        TargetX = targetX;
        TargetY = targetY;
        Speed = speed;
        IsMoving = true;
    }

    /// <summary>Schreibt die Bewegung um deltaSeconds fort. Gibt true zurück, wenn die Entity in diesem Tick exakt am Ziel angekommen ist (kein Überschießen).</summary>
    public bool Tick(double deltaSeconds)
    {
        if (!IsMoving) return false;

        double dx = TargetX - X;
        double dy = TargetY - Y;
        double distance = System.Math.Sqrt(dx * dx + dy * dy);
        double step = Speed * deltaSeconds;

        if (step >= distance)
        {
            X = TargetX;
            Y = TargetY;
            IsMoving = false;
            return true;
        }

        X += dx / distance * step;
        Y += dy / distance * step;
        return false;
    }
}
