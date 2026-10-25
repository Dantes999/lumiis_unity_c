using Lumiis.Shared;
using UnityEngine;

namespace Lumiis.Client
{
    /// <summary>Prozedurale Idle-/Lauf-/Schlag-Animation für eine CharacterView: Atmen im Stand, Wippen beim
    /// Gehen, kurzer Schlagimpuls mit Waffen-Schweif. Das Waffen-Socket wird nicht mehr direkt rotiert,
    /// sondern über eine Frame-Data-Konfiguration (WeaponSocketFrameConfig) je State/Frame/Facing gesetzt.
    /// Rein kosmetisch, kein Gameplay-Zustand.</summary>
    public sealed class CharacterAnimator : MonoBehaviour
    {
        private const float IdleBreatheScale = 0.01f;
        private const float IdleBreatheSpeed = 1.5f;
        private const float WalkBobUnits = 0.05f;
        private const float AttackDurationSeconds = SwingMath.TotalSeconds;
        private const float AfterimageIntervalSeconds = 0.028f;
        private const float LungeUnits = 0.07f;

        private CharacterView _view;
        private WeaponSocketFrameConfig _frameConfig;
        private bool _ownsFrameConfig;

        private string _lastState;
        private int _lastFrame = -1;
        private WeaponFacing _lastFacing = (WeaponFacing)(-1);
        private float _attackStartTime = float.NegativeInfinity;
        private int _comboIndex;
        private float _aimDegrees = -90f;
        private float _pauseUntil = float.NegativeInfinity;
        private float _pausedElapsed;
        private float _nextAfterimageAt;
        private float _walkPhase;
        private bool _walking;
        private float _lastMovingAt = float.NegativeInfinity;

        public WeaponSocketFrameConfig FrameConfig => _frameConfig;

        public void Initialize(CharacterView view, WeaponSocketFrameConfig frameConfig = null)
        {
            _view = view;
            _frameConfig = frameConfig != null ? frameConfig : WeaponSocketFrameConfig.CreateDefault();
            _ownsFrameConfig = frameConfig == null;

            _lastState = null;
            _lastFrame = -1;
            ApplyFrame("idle", 0);
        }

        /// <summary>Startet einen Schwerthieb in Richtung aimDegrees (Welt, 0 = rechts, 90 = oben). Die Klinge fährt
        /// nach SwingMath durch den Bogen (Ausholen, Schlag, Nachschwung), mit Schweif und Nachbildern.</summary>
        public void PlayAttack(int comboIndex = 0, float aimDegrees = float.NaN)
        {
            _attackStartTime = Time.time;
            _pauseUntil = float.NegativeInfinity;
            _comboIndex = Mathf.Abs(comboIndex) % 3;
            if (!float.IsNaN(aimDegrees)) _aimDegrees = aimDegrees;
            else _aimDegrees = DefaultAimDegrees();
            _nextAfterimageAt = 0f;
            _lastState = null; // nächster Animate-Aufruf wendet sofort den Attack-Frame an
        }

        /// <summary>Treffer-Stopp: friert den laufenden Hieb kurz ein, damit der Kontakt spürbar "hängt".</summary>
        public void HitPause(float seconds)
        {
            float t = Time.time;
            float elapsed = t - _attackStartTime;
            if (elapsed < 0f || elapsed >= AttackDurationSeconds || t < _pauseUntil) return;
            _pausedElapsed = elapsed;
            _pauseUntil = t + seconds;
            _attackStartTime += seconds;
        }

        private float DefaultAimDegrees()
        {
            if (_view == null) return -90f;
            Facing facing = _view.CurrentFacing;
            if (facing.Direction == FacingDirection.Up) return 90f;
            if (facing.Direction == FacingDirection.Down) return -90f;
            return facing.FlipX ? 180f : 0f;
        }

        public bool IsAttackPlaying => Time.time - _attackStartTime < AttackDurationSeconds;

        private void OnDestroy()
        {
            if (_ownsFrameConfig && _frameConfig != null)
                Destroy(_frameConfig);
        }

        public void Animate(bool isMoving, int speedWire = 0)
        {
            if (_view == null || _view.VisualRoot == null) return;

            float t = Time.time;
            if (isMoving) _lastMovingAt = t;
            bool walking = isMoving || t - _lastMovingAt < WalkCycle.StopGraceSeconds;
            if (walking)
            {
                // Losgehen beginnt immer mit einem Schritt, nicht an einer zufälligen Stelle des Zyklus.
                _walkPhase = _walking ? WalkCycle.Advance(_walkPhase, Time.deltaTime, speedWire) : 0f;
            }
            _walking = walking;
            float attackElapsed = t - _attackStartTime;
            bool attacking = t < _pauseUntil || (attackElapsed >= 0f && attackElapsed < AttackDurationSeconds);

            string state;
            int frame;
            if (attacking)
            {
                float elapsed = t < _pauseUntil ? _pausedElapsed : attackElapsed;
                state = "attack";
                frame = elapsed < SwingMath.WindupSeconds ? 0
                    : elapsed < SwingMath.WindupSeconds + SwingMath.StrikeSeconds ? 1 : 2;

                bool striking = SwingMath.IsStriking(elapsed);
                float bladeDegrees = _aimDegrees + SwingMath.BladeOffsetDegrees(elapsed, _comboIndex);
                _view.SetSwingPose(true, bladeDegrees, striking ? 1.18f : 1f);

                // Körper geht in den Schlag mit: kurzer Ausfallschritt in Zielrichtung, Neigung mit der Klinge.
                float aimRad = _aimDegrees * Mathf.Deg2Rad;
                float lunge = striking ? LungeUnits : elapsed < SwingMath.WindupSeconds ? -LungeUnits * 0.4f : LungeUnits * 0.5f;
                _view.VisualRoot.localPosition = new Vector3(Mathf.Cos(aimRad) * lunge, Mathf.Sin(aimRad) * lunge * 0.6f, 0f);
                _view.VisualRoot.localScale = Vector3.one;
                float lean = Mathf.Clamp(SwingMath.BladeOffsetDegrees(elapsed, _comboIndex) * 0.06f, -6f, 6f);
                _view.VisualRoot.localRotation = Quaternion.Euler(0f, 0f, lean);

                if (striking && t >= _pauseUntil)
                {
                    _view.AddSweepSample();
                    if (t >= _nextAfterimageAt)
                    {
                        _nextAfterimageAt = t + AfterimageIntervalSeconds;
                        _view.SpawnWeaponAfterimage(0.45f);
                    }
                }
                _view.SetAttackTrailActive(striking);
            }
            else if (walking)
            {
                state = "walk";
                frame = WalkCycle.Frame(_walkPhase);

                float bob = WalkCycle.Bob(_walkPhase) * WalkBobUnits;
                _view.VisualRoot.localPosition = new Vector3(0f, bob, 0f);
                _view.VisualRoot.localScale = Vector3.one;
                _view.VisualRoot.localRotation = Quaternion.identity;
            }
            else
            {
                state = "idle";
                frame = 0;

                float breathe = 1f + Mathf.Sin(t * IdleBreatheSpeed) * IdleBreatheScale;
                _view.VisualRoot.localPosition = Vector3.zero;
                _view.VisualRoot.localScale = new Vector3(breathe, breathe, 1f);
                _view.VisualRoot.localRotation = Quaternion.identity;
            }

            if (!attacking)
            {
                _view.SetSwingPose(false, 0f);
                _view.SetAttackTrailActive(false);
            }
            _view.SetAnimationFrame(state, frame);
            ApplyFrame(state, frame);
        }

        private void ApplyFrame(string state, int frame)
        {
            if (_view == null) return;

            WeaponFacing facing = WeaponSocketFrameConfig.FromFacing(_view.CurrentFacing);
            if (state == _lastState && frame == _lastFrame && facing == _lastFacing) return;

            _lastState = state;
            _lastFrame = frame;
            _lastFacing = facing;

            if (_frameConfig != null && _frameConfig.TryGet(state, frame, facing, out WeaponSocketFrame entry))
            {
                _view.ApplyWeaponSocketFrame(entry.handOffset, entry.handRotation, entry.weaponSortingOrder);
            }
            else
            {
                _view.ApplyWeaponSocketFrame(Vector2.zero, 0f, WeaponSocketFrameConfig.SortingOffsetFor(facing));
            }
        }
    }
}
