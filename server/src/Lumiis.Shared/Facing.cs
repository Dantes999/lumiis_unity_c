namespace Lumiis.Shared
{
    public enum FacingDirection : byte
    {
        Down = 0,
        Up = 1,
        Side = 2
    }

    /// <summary>Blickrichtung einer Figur: eine der drei gezeichneten Richtungen plus Spiegelung (Left = Side + FlipX).</summary>
    public struct Facing
    {
        public FacingDirection Direction;
        public bool FlipX;

        public Facing(FacingDirection direction, bool flipX)
        {
            Direction = direction;
            FlipX = flipX;
        }
    }
}
