using Lumiis.Shared.Net;

namespace Lumiis.Server.Game;

/// <summary>Eine bewegliche Entity in der Welt (in M1 nur Spieler). Reine Logik, kein Netzwerk.</summary>
public sealed class Entity
{
    public int Id { get; }
    public string Name { get; }

    /// <summary>Wird von GameServer direkt nach dem Join gesetzt (LookCatalog.CreateAppearanceForName).</summary>
    public Appearance Appearance { get; set; }

    public double X { get; private set; }
    public double Y { get; private set; }

    public bool IsMoving { get; private set; }
    public int TargetX { get; private set; }
    public int TargetY { get; private set; }
    public int Speed { get; private set; }

    public int IntX => (int)System.Math.Round(X);
    public int IntY => (int)System.Math.Round(Y);

    public Entity(int id, string name, int x, int y)
    {
        Id = id;
        Name = name;
        X = x;
        Y = y;
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
