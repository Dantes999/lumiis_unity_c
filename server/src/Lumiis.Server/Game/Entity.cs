using Lumiis.Shared;
using Lumiis.Shared.Net;

namespace Lumiis.Server.Game;

/// <summary>Eine bewegliche Entity in der Welt - seit M3c Spieler ODER Monster (Kind), reine Logik, kein
/// Netzwerk.</summary>
public sealed class Entity
{
    /// <summary>Default-Kampfwerte für Call-Sites ohne Kampfbezug (Bewegung/AOI/Positions-Speicherung-Tests,
    /// die new Entity(...)/World.TryJoin/World.SpawnMonster ohne explizite Werte aufrufen) - reale Spieler
    /// bekommen ihre Werte immer explizit aus der ClassCatalog (siehe GameServer.CompleteJoin), reale Monster
    /// aus der MobCatalog (siehe World.SpawnMonster-Aufruf im GameServer-Konstruktor).</summary>
    internal const int DefaultMaxHp = 100;
    internal const int DefaultAttack = 10;
    internal const int DefaultDefense = 0;
    internal const int DefaultMeleeRangeWire = 150;
    internal const int DefaultAttackCooldownMs = 1000;

    public int Id { get; }

    /// <summary>Die Datenbank-Id des Charakters (Lumiis.Data.Character) - World nutzt sie, um pro
    /// CharacterId nur eine gleichzeitige Verbindung zuzulassen (siehe World._entityIdByCharacterId). Nur bei
    /// Kind=Player gesetzt - Monster haben keine Verbindung/keinen Account (siehe CreateMonster).</summary>
    public int? CharacterId { get; }

    public string Name { get; }

    public EntityKind Kind { get; }

    /// <summary>Nur bei Kind=Monster aussagekräftig (Index in data/mobs.json), bei Kind=Player ungenutzt/0.</summary>
    public int MobId { get; }

    /// <summary>Nur bei Kind=Monster gesetzt - die Id des SpawnPoint aus spawns.json, an dem dieses Monster
    /// entstanden ist (siehe World.SpawnMonster/GameServer-Konstruktor). Null bei Spielern UND bei per
    /// Dev-Tools-Debug-Befehl gespawnten Monstern (siehe GameServer.HandleDebugSpawnMonster) - Letztere sind
    /// bewusst an KEINEN SpawnPoint gebunden, damit ihr Tod (M4b) keinen Respawn an einem SpawnPoint auslöst,
    /// der eigentlich schon ein "echtes" Monster hat. M4b darf sich beim Respawn-Scheduling ausschließlich auf
    /// SpawnPointId.HasValue verlassen, nie auf Kind allein.</summary>
    public int? SpawnPointId { get; }

    /// <summary>Nur bei Kind=Monster MIT gesetzter SpawnPointId aussagekräftig (aus SpawnPoint.RespawnSeconds,
    /// siehe World.SpawnMonster/GameServer-Konstruktor) - Sekunden bis zum Respawn nach dem Tod (M4b). Wird
    /// bei jedem Respawn an die neue Entity weitergereicht, damit derselbe SpawnPoint bei erneutem Tod wieder
    /// korrekt respawnt (siehe World.ScheduleMonsterRespawn). 0/ungenutzt bei Spielern und bei per
    /// Dev-Tools-Debug-Befehl gespawnten Monstern (SpawnPointId == null, respawnen nie).</summary>
    public int RespawnSeconds { get; }

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

    public int MaxHp { get; }
    public int CurrentHp { get; set; }
    public int Attack { get; }
    public int Defense { get; }

    /// <summary>Nur bei Kind=Player aussagekräftig (aus ClassCatalog) - Monster greifen in M4a nicht an,
    /// bei Kind=Monster 0/ungenutzt.</summary>
    public int MeleeRangeWire { get; }
    public int AttackCooldownMs { get; }

    /// <summary>Wandzeit (siehe GameServer._nowMs), ab wann der nächste Angriff dieser Entity erlaubt ist -
    /// von World.TryAttack gesetzt, nur bei Kind=Player relevant.</summary>
    public long AttackReadyAtMs { get; set; }

    /// <summary>Wandzeit, wann ein toter Spieler respawnt (0 = kein ausstehender Respawn) - siehe
    /// World.Tick/World.ApplyDamage. Nur bei Kind=Player relevant, Monster werden bei 0 Leben stattdessen
    /// sofort aus World entfernt (siehe World.ApplyDamage).</summary>
    public long RespawnAtMs { get; set; }

    public bool IsDead => CurrentHp <= 0;

    /// <summary>Nur bei Kind=Monster gesetzt (aus dem Mob-Typ, siehe MobCatalog) - Yang-Spanne, die beim Tod
    /// ausgezahlt wird (siehe World.ApplyDamage). Bei Kind=Player 0/ungenutzt.</summary>
    public int YangMin { get; }
    public int YangMax { get; }

    /// <summary>Laufender Yang-Gesamtbetrag - nur bei Kind=Player relevant, beim Join aus Character.Yang
    /// geladen (siehe GameServer.CompleteJoin), von World.TryAttack bei Monstertod erhöht.</summary>
    public long Yang { get; set; }

    private Entity(int id, EntityKind kind, int? characterId, int mobId, int? spawnPointId, int respawnSeconds, string name, int x, int y, int movementSpeed,
        int wanderRadiusWire, int wanderIntervalMinMs, int wanderIntervalMaxMs,
        int maxHp, int attack, int defense, int meleeRangeWire, int attackCooldownMs, int yangMin, int yangMax)
    {
        Id = id;
        Kind = kind;
        CharacterId = characterId;
        MobId = mobId;
        SpawnPointId = spawnPointId;
        RespawnSeconds = respawnSeconds;
        Name = name;
        X = x;
        Y = y;
        MovementSpeed = movementSpeed;
        WanderAnchorX = x;
        WanderAnchorY = y;
        WanderRadiusWire = wanderRadiusWire;
        WanderIntervalMinMs = wanderIntervalMinMs;
        WanderIntervalMaxMs = wanderIntervalMaxMs;
        MaxHp = maxHp;
        CurrentHp = maxHp;
        Attack = attack;
        Defense = defense;
        MeleeRangeWire = meleeRangeWire;
        AttackCooldownMs = attackCooldownMs;
        YangMin = yangMin;
        YangMax = yangMax;
    }

    public Entity(int id, int characterId, string name, int x, int y,
        int maxHp = DefaultMaxHp, int attack = DefaultAttack, int defense = DefaultDefense,
        int meleeRangeWire = DefaultMeleeRangeWire, int attackCooldownMs = DefaultAttackCooldownMs)
        : this(id, EntityKind.Player, characterId, mobId: 0, spawnPointId: null, respawnSeconds: 0, name, x, y, movementSpeed: GameConstants.PlayerSpeed,
            wanderRadiusWire: 0, wanderIntervalMinMs: 0, wanderIntervalMaxMs: 0,
            maxHp, attack, defense, meleeRangeWire, attackCooldownMs, yangMin: 0, yangMax: 0)
    {
    }

    /// <summary>spawnPointId: null bedeutet "nicht an einen SpawnPoint aus spawns.json gebunden" (Dev-Tools-
    /// Debug-Spawn, siehe SpawnPointId-Kommentar) - reguläre, aus der SpawnCatalog erzeugte Monster übergeben
    /// hier immer die echte SpawnPoint.Id (siehe World.SpawnMonster/GameServer-Konstruktor).
    /// respawnSeconds: nur bei gesetzter spawnPointId aussagekräftig (siehe RespawnSeconds-Kommentar).</summary>
    public static Entity CreateMonster(int id, int mobId, int? spawnPointId, string name, int x, int y, int speedWire,
        int wanderRadiusWire, int wanderIntervalMinMs, int wanderIntervalMaxMs,
        int maxHp = DefaultMaxHp, int attack = DefaultAttack, int defense = DefaultDefense, int yangMin = 0, int yangMax = 0,
        int respawnSeconds = 0)
    {
        return new Entity(id, EntityKind.Monster, characterId: null, mobId, spawnPointId, respawnSeconds, name, x, y, movementSpeed: speedWire,
            wanderRadiusWire, wanderIntervalMinMs, wanderIntervalMaxMs,
            maxHp, attack, defense, meleeRangeWire: 0, attackCooldownMs: 0, yangMin, yangMax);
    }

    public void MoveTo(int targetX, int targetY, int speed)
    {
        TargetX = targetX;
        TargetY = targetY;
        Speed = speed;
        IsMoving = true;
    }

    /// <summary>Setzt die Position hart, ohne Bewegung (Respawn, M4a) - kein Interpolationsziel, der Client
    /// erhält dafür ein EntityStopPacket (siehe GameServer), keine neue Paketform nötig.</summary>
    public void Teleport(int x, int y)
    {
        X = x;
        Y = y;
        IsMoving = false;
    }

    /// <summary>Bricht eine laufende Bewegung sofort ab (z. B. Spielertod mitten in der Bewegung), ohne die
    /// Position zu verändern - der nächste Tick meldet sonst fälschlich "angekommen".</summary>
    public void StopMoving()
    {
        IsMoving = false;
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
