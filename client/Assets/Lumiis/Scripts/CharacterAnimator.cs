using UnityEngine;

namespace Lumiis.Client
{
    /// <summary>Prozedurale Idle-/Lauf-Animation für eine CharacterView: Atmen im Stand, Wippen und
    /// Waffen-Schwung um den Hand-Anker beim Gehen. Rein kosmetisch, kein Gameplay-Zustand.</summary>
    public sealed class CharacterAnimator : MonoBehaviour
    {
        private const float IdleBreatheScale = 0.01f;
        private const float IdleBreatheSpeed = 1.5f;
        private const float WalkBobUnits = 0.05f;
        private const float WalkBobSpeed = 9f;
        private const float WeaponSwingDegrees = 25f;
        private const float WeaponSwingSpeed = 9f;

        private CharacterView _view;
        private Transform _weaponTransform;

        public void Initialize(CharacterView view)
        {
            _view = view;
            _weaponTransform = view.GetSlotTransform(CharacterSlot.Weapon);
        }

        public void Animate(bool isMoving)
        {
            if (_view == null || _view.VisualRoot == null) return;

            float t = Time.time;
            if (isMoving)
            {
                float bob = Mathf.Abs(Mathf.Sin(t * WalkBobSpeed)) * WalkBobUnits;
                _view.VisualRoot.localPosition = new Vector3(0f, bob, 0f);
                _view.VisualRoot.localScale = Vector3.one;

                if (_weaponTransform != null)
                {
                    float swing = Mathf.Sin(t * WeaponSwingSpeed) * WeaponSwingDegrees;
                    _weaponTransform.localRotation = Quaternion.Euler(0f, 0f, swing);
                }
            }
            else
            {
                float breathe = 1f + Mathf.Sin(t * IdleBreatheSpeed) * IdleBreatheScale;
                _view.VisualRoot.localPosition = Vector3.zero;
                _view.VisualRoot.localScale = new Vector3(breathe, breathe, 1f);

                if (_weaponTransform != null)
                    _weaponTransform.localRotation = Quaternion.identity;
            }
        }
    }
}
