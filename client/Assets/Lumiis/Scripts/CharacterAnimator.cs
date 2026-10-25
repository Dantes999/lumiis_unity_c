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
        private const float WalkBobSpeed = 9f;
        private const float AttackDurationSeconds = 0.28f;

        // Nur die mittleren zwei von vier Schlagframes zeigen den TrailRenderer (aktive Schlagphase).
        private const int AttackTrailFirstFrame = 1;
        private const int AttackTrailLastFrame = 2;

        private CharacterView _view;
        private WeaponSocketFrameConfig _frameConfig;
        private bool _ownsFrameConfig;

        private string _lastState;
        private int _lastFrame = -1;
        private WeaponFacing _lastFacing = (WeaponFacing)(-1);
        private float _attackStartTime = float.NegativeInfinity;
        private int _comboIndex;

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

        /// <summary>Startet einen Schlagimpuls (lokaler Angriff oder eingehendes EntityDamaged/EntityDodged
        /// für diese Entity). Der Trail wird nur während der aktiven Schlagframes emittiert.</summary>
        public void PlayAttack(int comboIndex = 0)
        {
            _attackStartTime = Time.time;
            _comboIndex = Mathf.Abs(comboIndex) % 3;
            _lastState = null; // nächster Animate-Aufruf wendet sofort den Attack-Frame an
        }

        public bool IsAttackPlaying => Time.time - _attackStartTime < AttackDurationSeconds;

        private void OnDestroy()
        {
            if (_ownsFrameConfig && _frameConfig != null)
                Destroy(_frameConfig);
        }

        public void Animate(bool isMoving)
        {
            if (_view == null || _view.VisualRoot == null) return;

            float t = Time.time;
            float attackElapsed = t - _attackStartTime;
            bool attacking = attackElapsed >= 0f && attackElapsed < AttackDurationSeconds;

            string state;
            int frame;
            if (attacking)
            {
                state = "attack";
                frame = Mathf.Clamp(Mathf.FloorToInt(attackElapsed / AttackDurationSeconds * 4f), 0, 3);
                _view.VisualRoot.localPosition = Vector3.zero;
                _view.VisualRoot.localScale = Vector3.one;
                // Kleine links/rechts/links-Neigung als sichtbarer Schlag-Kombo.
                float lean = _comboIndex == 1 ? 7f : _comboIndex == 2 ? -4f : -7f;
                _view.VisualRoot.localRotation = Quaternion.Euler(0f, 0f, lean);
            }
            else if (isMoving)
            {
                state = "walk";
                frame = Mathf.FloorToInt(Mathf.Abs(Mathf.Sin(t * WalkBobSpeed)) * 4f) % 4;

                float bob = Mathf.Abs(Mathf.Sin(t * WalkBobSpeed)) * WalkBobUnits;
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

            ApplyFrame(state, frame);

            bool trailActive = attacking && frame >= AttackTrailFirstFrame && frame <= AttackTrailLastFrame;
            _view.SetAttackTrailActive(trailActive);
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
