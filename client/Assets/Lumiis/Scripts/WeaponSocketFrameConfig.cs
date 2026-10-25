using System;
using System.Collections.Generic;
using Lumiis.Shared;
using UnityEngine;

namespace Lumiis.Client
{
    /// <summary>Vier Darstellungsrichtungen der Waffe - Left/Right entstehen beide aus dem Side-Sprite,
    /// unterscheiden sich aber in der Spiegelung und können deshalb eigene Hand-Offsets bekommen.</summary>
    public enum WeaponFacing
    {
        Down,
        Up,
        Left,
        Right
    }

    /// <summary>Ein Eintrag der Frame-Daten: Wo liegt die Hand (Offset relativ zum Hand-Anker aus looks.json),
    /// wie ist die Waffe gedreht und in welcher Ebene liegt sie relativ zum Körper?</summary>
    [Serializable]
    public struct WeaponSocketFrame
    {
        public string state;
        public int frame;
        public WeaponFacing facing;

        /// <summary>Zusätzlicher Offset in Weltenheiten relativ zum Hand-Anker (x wird bei Left gespiegelt).</summary>
        public Vector2 handOffset;

        /// <summary>Winkel in Grad um die Z-Achse.</summary>
        public float handRotation;

        /// <summary>Sortier-Offset relativ zur Körper-Ebene. Defaults folgen docs/sprite-spec.md:
        /// Up -2 (Waffe hinter Körper/Schild), Down +3 (vor dem Schild), Side +2.</summary>
        public int weaponSortingOrder;
    }

    /// <summary>Frame-Data-Dictionary für das Waffen-Socketing. Als ScriptableObject im Inspector editierbar
    /// (CreateAssetMenu), für den Platzhalter-Betrieb erzeugt <see cref="CreateDefault"/> dieselben Daten zur
    /// Laufzeit. CharacterAnimator fragt hier je (State, Frame, Facing) die Socket-Pose ab und schiebt sie in
    /// CharacterView; ohne Treffer bleibt die Waffe am Hand-Anker.</summary>
    [CreateAssetMenu(fileName = "WeaponSocketFrames", menuName = "Lumiis/Weapon Socket Frames")]
    public sealed class WeaponSocketFrameConfig : ScriptableObject
    {
        public const int SortingOffsetDown = 3;
        public const int SortingOffsetUp = -2;
        public const int SortingOffsetSide = 2;

        public WeaponSocketFrame[] frames = Array.Empty<WeaponSocketFrame>();

        public bool TryGet(string state, int frame, WeaponFacing facing, out WeaponSocketFrame result)
        {
            if (frames != null)
            {
                for (int i = 0; i < frames.Length; i++)
                {
                    WeaponSocketFrame candidate = frames[i];
                    if (candidate.frame == frame
                        && candidate.facing == facing
                        && string.Equals(candidate.state, state, StringComparison.Ordinal))
                    {
                        result = candidate;
                        return true;
                    }
                }
            }

            result = default;
            return false;
        }

        /// <summary>Platzhalter-Frame-Daten (Idle/Walk/Attack je Richtung) - reine Laufzeit-Erzeugung, damit
        /// das Projekt ohne vorbereitete Assets auskommt. Später kann ein Designer eine .asset-Datei mit
        /// echten Werten zuweisen, ohne Code zu ändern.</summary>
        public static WeaponSocketFrameConfig CreateDefault()
        {
            var config = CreateInstance<WeaponSocketFrameConfig>();
            var frames = new List<WeaponSocketFrame>();

            foreach (WeaponFacing facing in (WeaponFacing[])Enum.GetValues(typeof(WeaponFacing)))
            {
                int sorting = SortingOffsetFor(facing);

                // Ruhehaltung: die Klinge zeigt nie senkrecht nach oben (sah aus wie ein Schwert über dem Kopf),
                // sondern schräg nach vorn bzw. gesenkt. Lauf-Frames pendeln um diese Ruhehaltung.
                float rest = RestRotationFor(facing);
                Add(frames, "idle", 0, facing, Vector2.zero, rest, sorting);

                Add(frames, "walk", 0, facing, new Vector2(0f, 0f), rest, sorting);
                Add(frames, "walk", 1, facing, new Vector2(0.02f, 0.04f), rest - 12f, sorting);
                Add(frames, "walk", 2, facing, new Vector2(0f, 0.06f), rest, sorting);
                Add(frames, "walk", 3, facing, new Vector2(-0.01f, 0.03f), rest + 10f, sorting);

                Add(frames, "attack", 0, facing, new Vector2(0f, 0.02f), 10f, sorting);
                Add(frames, "attack", 1, facing, new Vector2(0.02f, 0.06f), -35f, sorting);
                Add(frames, "attack", 2, facing, new Vector2(0.04f, 0.02f), -65f, sorting);
                Add(frames, "attack", 3, facing, new Vector2(0.01f, -0.01f), -20f, sorting);
            }

            config.frames = frames.ToArray();
            return config;
        }

        /// <summary>Ruhewinkel der Waffe (Grad, gegen den Uhrzeigersinn, 0 = Klinge nach oben). Seite: schräg
        /// nach vorn (bei Left spiegelt CharacterView das Vorzeichen), Unten: gesenkt nach außen vor dem Körper,
        /// Oben: über die Schulter hinter dem Körper.</summary>
        public static float RestRotationFor(WeaponFacing facing)
        {
            switch (facing)
            {
                case WeaponFacing.Down: return -145f;
                case WeaponFacing.Up: return -30f;
                default: return -60f;
            }
        }

        public static int SortingOffsetFor(WeaponFacing facing)
        {
            switch (facing)
            {
                case WeaponFacing.Up: return SortingOffsetUp;
                case WeaponFacing.Down: return SortingOffsetDown;
                default: return SortingOffsetSide;
            }
        }

        /// <summary>Mappt die gemeinsame Facing-Struktur (Down/Up/Side + FlipX) auf die vier
        /// Waffen-Darstellungsrichtungen.</summary>
        public static WeaponFacing FromFacing(Facing facing)
        {
            if (facing.Direction == FacingDirection.Up) return WeaponFacing.Up;
            if (facing.Direction == FacingDirection.Down) return WeaponFacing.Down;
            return facing.FlipX ? WeaponFacing.Left : WeaponFacing.Right;
        }

        private static void Add(List<WeaponSocketFrame> frames, string state, int frame, WeaponFacing facing,
            Vector2 handOffset, float handRotation, int weaponSortingOrder)
        {
            frames.Add(new WeaponSocketFrame
            {
                state = state,
                frame = frame,
                facing = facing,
                handOffset = handOffset,
                handRotation = handRotation,
                weaponSortingOrder = weaponSortingOrder
            });
        }
    }
}
