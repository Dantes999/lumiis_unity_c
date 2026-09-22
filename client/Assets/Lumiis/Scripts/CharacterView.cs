using System;
using System.Collections.Generic;
using Lumiis.Shared;
using Lumiis.Shared.Net;
using UnityEngine;
using UnityEngine.Rendering;

namespace Lumiis.Client
{
    /// <summary>Baut eine Figur aus Ebenen zusammen (Paper Doll). Ersetzt das einfarbige Quadrat der EntityView
    /// aus M1b für Spieler. Reihenfolge/Sortierung nach docs/sprite-spec.md.
    ///
    /// Sortierung zwischen Figuren läuft über eine SortingGroup am Figuren-Root (immer sortingOrder 0, Root-
    /// Position = Fußpunkt) plus Transparency Sort Mode "Custom Axis" (0,1,0) auf dem URP-2D-Renderer - dafür
    /// sorgt die kontinuierliche Y-Position der Gruppe, kein von Hand berechneter Wert. Innerhalb einer Figur
    /// sortieren die Kind-Renderer nur noch über ihren festen Slot-Rang (0-5 je Zeichenreihenfolge-Tabelle,
    /// Marker-Ring -1) - keine Y-Formel mehr, die den gültigen sortingOrder-Bereich (short) überschreiten könnte.</summary>
    public sealed class CharacterView : MonoBehaviour
    {
        private static readonly CharacterSlot[] OrderDown = { CharacterSlot.Body, CharacterSlot.Armor, CharacterSlot.Shield, CharacterSlot.Weapon, CharacterSlot.Hair, CharacterSlot.Helmet };
        private static readonly CharacterSlot[] OrderUp = { CharacterSlot.Weapon, CharacterSlot.Shield, CharacterSlot.Body, CharacterSlot.Armor, CharacterSlot.Hair, CharacterSlot.Helmet };
        private static readonly CharacterSlot[] OrderSide = { CharacterSlot.Shield, CharacterSlot.Body, CharacterSlot.Armor, CharacterSlot.Weapon, CharacterSlot.Hair, CharacterSlot.Helmet };

        private static readonly Vector2 DefaultPivot = new Vector2(0.5f, 0f);

        public int EntityId { get; private set; }
        public string EntityName { get; private set; }
        public Transform VisualRoot { get; private set; }

        private ISpriteProvider _spriteProvider;
        private LookCatalogClient _lookCatalog;
        private readonly Dictionary<CharacterSlot, SpriteRenderer> _renderers = new Dictionary<CharacterSlot, SpriteRenderer>();
        private Appearance _appearance;
        private Facing _facing = new Facing(FacingDirection.Down, false);

        public void Initialize(int entityId, string entityName, ISpriteProvider spriteProvider, LookCatalogClient lookCatalog)
        {
            EntityId = entityId;
            EntityName = entityName;
            _spriteProvider = spriteProvider;
            _lookCatalog = lookCatalog;

            SortingGroup sortingGroup = gameObject.AddComponent<SortingGroup>();
            sortingGroup.sortingOrder = 0;

            var visualRootGo = new GameObject("VisualRoot");
            visualRootGo.transform.SetParent(transform, false);
            VisualRoot = visualRootGo.transform;

            foreach (CharacterSlot slot in (CharacterSlot[])Enum.GetValues(typeof(CharacterSlot)))
            {
                var go = new GameObject(slot.ToString());
                go.transform.SetParent(VisualRoot, false);
                _renderers[slot] = go.AddComponent<SpriteRenderer>();
            }
        }

        public void SetWorldPosition(double worldX, double worldY)
        {
            transform.position = new Vector3((float)worldX, (float)worldY, 0f);
        }

        public Transform GetSlotTransform(CharacterSlot slot)
        {
            return _renderers[slot].transform;
        }

        public void SetAppearance(Appearance appearance)
        {
            _appearance = appearance;
            RefreshAllSlots();
        }

        public void SetFacing(Facing facing)
        {
            bool directionChanged = facing.Direction != _facing.Direction;
            _facing = facing;

            if (directionChanged)
                RefreshAllSlots();
            else
                ApplyFlip();

            ApplySortingOrder();
        }

        /// <summary>Weltraum-Offset zum Kopf-Anker (Namensschild), gespiegelt bei FlipX. Fällt auf eine feste
        /// Höhe zurück, wenn kein Anker im Katalog hinterlegt ist.</summary>
        public Vector3 GetHeadWorldOffset()
        {
            LookAnchorJson anchor = GetBodyAnchor(_facing.Direction);
            int ppu = _lookCatalog?.Ppu ?? 0;
            if (anchor?.head != null && anchor.head.Length >= 2 && ppu > 0)
            {
                float worldX = anchor.head[0] / (float)ppu;
                float worldY = anchor.head[1] / (float)ppu;
                if (_facing.FlipX) worldX = -worldX;
                return new Vector3(worldX, worldY, 0f);
            }
            return new Vector3(0f, 1.2f, 0f);
        }

        private void RefreshAllSlots()
        {
            RefreshSlot(CharacterSlot.Body, _appearance.BodyId, null);
            RefreshSlot(CharacterSlot.Armor, _appearance.ArmorId, null);
            RefreshSlot(CharacterSlot.Shield, _appearance.ShieldId, null);
            RefreshSlot(CharacterSlot.Weapon, _appearance.WeaponId, null);
            RefreshSlot(CharacterSlot.Helmet, _appearance.HelmetId, null);

            bool hairHidden = _appearance.HelmetId != 0 && IsHelmetHiding(_appearance.HelmetId);
            RefreshSlot(CharacterSlot.Hair, hairHidden ? 0 : _appearance.HairStyleId, ColorForHairId(_appearance.HairColorId));

            ApplyFlip();
        }

        private void RefreshSlot(CharacterSlot slot, int lookId, Color? tint)
        {
            SpriteRenderer renderer = _renderers[slot];
            if (lookId <= 0)
            {
                renderer.enabled = false;
                renderer.sprite = null;
                return;
            }

            Vector2 pivot = slot == CharacterSlot.Weapon ? ComputeWeaponPivot(_facing.Direction) : DefaultPivot;
            renderer.enabled = true;
            renderer.sprite = _spriteProvider.GetSprite(slot, lookId, _facing.Direction, "idle", 0, pivot);
            renderer.color = tint ?? Color.white;
        }

        private void ApplyFlip()
        {
            foreach (SpriteRenderer renderer in _renderers.Values)
                renderer.flipX = _facing.FlipX;

            PositionWeapon();
        }

        /// <summary>Verschiebt den Waffen-Slot-Transform an die Weltraum-Position des Hand-Ankers (gespiegelt bei
        /// FlipX, analog zu GetHeadWorldOffset). Der Sprite-Pivot (siehe ComputeWeaponPivot) sorgt dafür, dass
        /// exakt der Griff-Pixel dorthin fällt - ohne diese Verschiebung würde der Anker-Höhenbeitrag durch den
        /// Pivot rechnerisch wieder aufgehoben und der Griff landete am Fußpunkt statt auf Handhöhe.</summary>
        private void PositionWeapon()
        {
            Transform weaponTransform = _renderers[CharacterSlot.Weapon].transform;
            LookAnchorJson anchor = GetBodyAnchor(_facing.Direction);
            int ppu = _lookCatalog?.Ppu ?? 0;
            if (anchor?.hand != null && anchor.hand.Length >= 2 && ppu > 0)
            {
                float worldX = anchor.hand[0] / (float)ppu;
                float worldY = anchor.hand[1] / (float)ppu;
                if (_facing.FlipX) worldX = -worldX;
                weaponTransform.localPosition = new Vector3(worldX, worldY, 0f);
            }
            else
            {
                weaponTransform.localPosition = Vector3.zero;
            }
        }

        /// <summary>Nur der feste Rang (0-5) innerhalb der Figur - welche Figur vor welcher liegt, entscheidet
        /// die SortingGroup zusammen mit dem Custom-Axis-Sort des Renderers, nicht dieser Wert.</summary>
        private void ApplySortingOrder()
        {
            CharacterSlot[] order = OrderForDirection(_facing.Direction);
            for (int rank = 0; rank < order.Length; rank++)
            {
                if (_renderers.TryGetValue(order[rank], out SpriteRenderer renderer))
                    renderer.sortingOrder = rank;
            }
        }

        private static CharacterSlot[] OrderForDirection(FacingDirection direction)
        {
            switch (direction)
            {
                case FacingDirection.Up: return OrderUp;
                case FacingDirection.Side: return OrderSide;
                default: return OrderDown;
            }
        }

        private bool IsHelmetHiding(int helmetId)
        {
            return _lookCatalog != null && _lookCatalog.TryGetEntry("Helmet", helmetId, out LookEntryJson entry) && entry.hidesHair;
        }

        private Color ColorForHairId(int hairColorId)
        {
            if (_lookCatalog == null || hairColorId <= 0) return Color.white;
            int index = hairColorId - 1; // 1-basiert, siehe Appearance-Dokumentation (0 = keine Ebene)
            IReadOnlyList<string> colors = _lookCatalog.HairColors;
            if (index < 0 || index >= colors.Count) return Color.white;
            return ColorUtility.TryParseHtmlString(colors[index], out Color color) ? color : Color.white;
        }

        private LookAnchorJson GetBodyAnchor(FacingDirection direction)
        {
            if (_lookCatalog == null || !_lookCatalog.TryGetEntry("Body", _appearance.BodyId, out LookEntryJson bodyEntry) || bodyEntry.anchors == null)
                return null;

            switch (direction)
            {
                case FacingDirection.Up: return bodyEntry.anchors.up;
                case FacingDirection.Side: return bodyEntry.anchors.side;
                default: return bodyEntry.anchors.down;
            }
        }

        private Vector2 ComputeWeaponPivot(FacingDirection direction)
        {
            LookAnchorJson anchor = GetBodyAnchor(direction);
            int canvasPx = _lookCatalog?.CanvasPx ?? 0;
            if (anchor?.hand == null || anchor.hand.Length < 2 || canvasPx <= 0)
                return DefaultPivot;

            float absoluteX = canvasPx / 2f + anchor.hand[0];
            float absoluteY = anchor.hand[1];
            return new Vector2(absoluteX / canvasPx, absoluteY / canvasPx);
        }
    }
}
