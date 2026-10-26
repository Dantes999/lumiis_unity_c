using System;
using System.Collections.Generic;
using Lumiis.Shared;
using Lumiis.Shared.Net;

namespace Lumiis.Server.Game;

/// <summary>Körperkollision zwischen Entities: jeder lebende Spieler und jedes Monster ist ein Kreis mit
/// GameConstants.BodyRadiusWire. Ein Schritt ist blockiert, wenn er in einen fremden Körper hinein- und dabei
/// näher heranführt; Wegbewegen bleibt immer erlaubt (sonst klebten überlappend gespawnte Körper fest).
/// Spieler untereinander blockieren sich nicht (wie in Metin2, sonst verstopfen Dorf und Portale).
/// Für tausende Monster je Karte liegen die Körper in einem groben Raster (Zellen von CellWire), die
/// Abfrage prüft nur die 3x3 Nachbarzellen. Das Raster enthält Referenzen, gelesen werden die aktuellen
/// Positionen - wer sich seit dem Aufbau bewegt hat, steht höchstens eine Zelle daneben.</summary>
public sealed class BodyGrid
{
    private const int CellWire = 400;
    private readonly Dictionary<long, List<Entity>> _cells = new();

    public static bool IsBody(Entity entity) =>
        !entity.IsDead && (entity.Kind == EntityKind.Player || entity.Kind == EntityKind.Monster);

    /// <summary>Mitglieder derselben Monstergruppe (groupId) blockieren sich nicht: sie laufen als Rudel in
    /// Formation (World.GroupFormationRadiusWire) und würden sich beim gemeinsamen Losgehen sonst gegenseitig
    /// stoppen.</summary>
    private static bool Collide(Entity a, Entity b) =>
        a.Id != b.Id && IsBody(b)
        && !(a.Kind == EntityKind.Player && b.Kind == EntityKind.Player)
        && !(a.GroupId != 0 && a.GroupId == b.GroupId && a.Kind == EntityKind.Monster && b.Kind == EntityKind.Monster);

    private static long Key(int cellX, int cellY) => ((long)cellX << 32) | (uint)cellY;

    public void Rebuild(IEnumerable<Entity> entities)
    {
        foreach (List<Entity> cell in _cells.Values) cell.Clear();
        foreach (Entity entity in entities)
        {
            if (!IsBody(entity)) continue;
            long key = Key(entity.IntX / CellWire, entity.IntY / CellWire);
            if (!_cells.TryGetValue(key, out List<Entity>? cell))
            {
                cell = new List<Entity>();
                _cells[key] = cell;
            }
            cell.Add(entity);
        }
    }

    /// <summary>true, wenn mover beim Schritt nach (nextX, nextY) in einen fremden Körper liefe.</summary>
    public bool Blocks(Entity mover, double nextX, double nextY)
    {
        if (!IsBody(mover)) return false;
        long minGap = 2L * GameConstants.BodyRadiusWire;
        long minGapSq = minGap * minGap;
        int cellX = (int)nextX / CellWire;
        int cellY = (int)nextY / CellWire;
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (!_cells.TryGetValue(Key(cellX + dx, cellY + dy), out List<Entity>? cell)) continue;
                foreach (Entity other in cell)
                {
                    if (!Collide(mover, other)) continue;
                    double nx = nextX - other.X, ny = nextY - other.Y;
                    double nextSq = nx * nx + ny * ny;
                    if (nextSq >= minGapSq) continue;
                    double cx = mover.X - other.X, cy = mover.Y - other.Y;
                    if (nextSq < cx * cx + cy * cy) return true;
                }
            }
        }
        return false;
    }
}
