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
    /// Marker-Ring -1) - keine Y-Formel mehr, die den gültigen sortingOrder-Bereich (short) überschreiten könnte.
    ///
    /// Seit dem Socketing-Umbau hängt die Waffe als <c>Body -> WeaponSocket -> Weapon</c> unter dem Körper.
    /// CharacterAnimator schiebt pro Frame Hand-Offset, Rotation und Sortier-Offset in das Socket; die Waffe
    /// selbst bleibt ein einzelnes Sprite und muss nicht bildweise animiert werden.</summary>
    public sealed class CharacterView : MonoBehaviour
    {
        private static readonly CharacterSlot[] OrderDown = { CharacterSlot.Body, CharacterSlot.Armor, CharacterSlot.Shield, CharacterSlot.Weapon, CharacterSlot.Hair, CharacterSlot.Helmet };
        private static readonly CharacterSlot[] OrderUp = { CharacterSlot.Weapon, CharacterSlot.Shield, CharacterSlot.Body, CharacterSlot.Armor, CharacterSlot.Hair, CharacterSlot.Helmet };
        private static readonly CharacterSlot[] OrderSide = { CharacterSlot.Shield, CharacterSlot.Body, CharacterSlot.Armor, CharacterSlot.Weapon, CharacterSlot.Hair, CharacterSlot.Helmet };

        private static readonly Vector2 DefaultPivot = new Vector2(0.5f, 0f);

        private static readonly int ArmorPrimaryProperty = Shader.PropertyToID("_Color_ArmorPrimary");
        private static readonly int ArmorSecondaryProperty = Shader.PropertyToID("_Color_ArmorSecondary");
        private static readonly int ArmorTrimProperty = Shader.PropertyToID("_Color_ArmorTrim");

        private const string ArmorMaterialResourcePath = "Materials/CharacterArmor";
        private const int MaxUpgradeLevel = 9;

        public int EntityId { get; private set; }
        public string EntityName { get; private set; }
        public Transform VisualRoot { get; private set; }

        /// <summary>Basis-SpriteRenderer des Körpers - Elternknoten des Waffen-Sockets.</summary>
        public SpriteRenderer BodyRenderer => _bodyRenderer;

        /// <summary>Transform, an dem die Waffe sitzt (Position/Rotation kommen aus den Frame-Daten).</summary>
        public Transform WeaponSocket => _weaponSocket;

        public SpriteRenderer WeaponRenderer => _weaponRenderer;

        /// <summary>Aktuelle Blickrichtung (inkl. FlipX) - CharacterAnimator mappt sie auf WeaponFacing.</summary>
        public Facing CurrentFacing => _facing;

        public int WeaponUpgradeLevel => _weaponUpgradeLevel;

        public bool IsPlus7GlowActive => _plus7Glow != null && _plus7Glow.gameObject.activeSelf;
        public bool IsPlus9GlowActive => _plus9Glow != null && _plus9Glow.gameObject.activeSelf;
        public bool IsAttackTrailEmitting => _weaponTrail != null && _weaponTrail.emitting;
        public int TrailPositionCount => _weaponTrail != null ? _weaponTrail.positionCount : 0;

        private ISpriteProvider _spriteProvider;
        private LookCatalogClient _lookCatalog;
        private readonly Dictionary<CharacterSlot, SpriteRenderer> _renderers = new Dictionary<CharacterSlot, SpriteRenderer>();
        private Appearance _appearance;
        private Facing _facing = new Facing(FacingDirection.Down, false);

        private SpriteRenderer _bodyRenderer;
        private Transform _weaponSocket;
        private SpriteRenderer _weaponRenderer;

        // ---- Waffen-Socketing (Paper-Doll-Umbau) ----
        private bool _hasWeaponOverride;
        private Sprite _weaponOverrideSprite;
        private Vector2 _weaponPivotOffsetPixels;
        private int _weaponUpgradeLevel;

        // Vom CharacterAnimator gesetzte Frame-Pose (handOffset relativ zum Hand-Anker aus looks.json).
        private Vector2 _frameHandOffset;
        private float _frameHandRotation;
        private int _frameSortingOffset;
        private bool _framePoseInitialized;

        // ---- Rüstungs-Palette (MaterialPropertyBlock) ----
        private Material _armorMaterial;
        private MaterialPropertyBlock _armorPropertyBlock;

        // ---- Waffen-VFX ----
        private ParticleSystem _plus7Glow;
        private ParticleSystem _plus9Glow;
        private TrailRenderer _weaponTrail;
        private Transform _weaponTrailAnchor;
        private Material _weaponVfxMaterial;
        private Material _particleMaterial;
        private Texture2D _particleTexture;

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

            // Körper zuerst, danach alle anderen Ebenen. Die Waffe bekommt eine eigene Hierarchie unter dem
            // Körper: Body -> WeaponSocket -> Weapon (siehe Klassenkommentar/Aufgabenstellung).
            _bodyRenderer = CreateSlotRenderer(CharacterSlot.Body, VisualRoot);

            foreach (CharacterSlot slot in (CharacterSlot[])Enum.GetValues(typeof(CharacterSlot)))
            {
                if (slot == CharacterSlot.Body || slot == CharacterSlot.Weapon) continue;
                CreateSlotRenderer(slot, VisualRoot);
            }

            var weaponSocketGo = new GameObject("WeaponSocket");
            weaponSocketGo.transform.SetParent(_bodyRenderer.transform, false);
            _weaponSocket = weaponSocketGo.transform;

            _weaponRenderer = CreateSlotRenderer(CharacterSlot.Weapon, _weaponSocket);

            _armorMaterial = Resources.Load<Material>(ArmorMaterialResourcePath);
            _armorPropertyBlock = new MaterialPropertyBlock();
            if (_armorMaterial != null && _renderers.TryGetValue(CharacterSlot.Armor, out SpriteRenderer armorRenderer))
                armorRenderer.sharedMaterial = _armorMaterial;

            CreateWeaponVfx();
            ApplySortingOrder();
            RefreshWeaponSocketPose();
        }

        private SpriteRenderer CreateSlotRenderer(CharacterSlot slot, Transform parent)
        {
            var go = new GameObject(slot.ToString());
            go.transform.SetParent(parent, false);
            var renderer = go.AddComponent<SpriteRenderer>();
            _renderers[slot] = renderer;
            return renderer;
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
            RefreshWeaponSocketPose();
        }

        /// <summary>Waffe aus Item-Daten setzen (GameClient beim Ausrüsten oder Testboard). sprite darf null
        /// sein, wenn kein PNG unter Resources/Sprites/Items/ existiert - dann bleibt der generierte
        /// Platzhalter aus der aktuellen Appearance sichtbar. pivotOffset ist die Pixel-Korrektur relativ zum
        /// Griff, upgradeLevel steuert die +7/+9-VFX.</summary>
        public void SetWeapon(Sprite weaponSprite, Vector2 pivotOffset, int upgradeLevel)
        {
            _hasWeaponOverride = true;
            _weaponOverrideSprite = weaponSprite;
            _weaponPivotOffsetPixels = pivotOffset;
            _weaponUpgradeLevel = Mathf.Clamp(upgradeLevel, 0, MaxUpgradeLevel);

            if (weaponSprite != null)
            {
                _weaponRenderer.enabled = true;
                _weaponRenderer.sprite = weaponSprite;
                SetWeaponPixelOffset();
                UpdateTrailAnchor();
            }
            else
            {
                // Kein statisches Asset: Platzhalter der aktuellen Waffen-LookId zeichnen.
                _weaponRenderer.transform.localPosition = Vector3.zero;
                RefreshSlot(CharacterSlot.Weapon, _appearance.WeaponId, null);
            }

            UpdateUpgradeEffects();
            RefreshWeaponSocketPose();
        }

        /// <summary>Zurück auf die Appearance-Waffe (Platzhalter) - z. B. beim lokalen F7-Look-Cycler.</summary>
        public void ClearWeapon()
        {
            _hasWeaponOverride = false;
            _weaponOverrideSprite = null;
            _weaponPivotOffsetPixels = Vector2.zero;
            _weaponUpgradeLevel = 0;
            _weaponRenderer.transform.localPosition = Vector3.zero;
            RefreshSlot(CharacterSlot.Weapon, _appearance.WeaponId, null);
            UpdateUpgradeEffects();
            RefreshWeaponSocketPose();
        }

        /// <summary>Vom CharacterAnimator pro Frame aufgerufen: handOffset ist relativ zum Hand-Anker aus
        /// looks.json (wird bei FlipX gespiegelt), weaponSortingOrder ist relativ zur Körper-Ebene.</summary>
        public void ApplyWeaponSocketFrame(Vector2 handOffset, float handRotation, int weaponSortingOrder)
        {
            _frameHandOffset = handOffset;
            _frameHandRotation = handRotation;
            _frameSortingOffset = weaponSortingOrder;
            _framePoseInitialized = true;
            RefreshWeaponSocketPose();
        }

        /// <summary>TrailRenderer nur während der aktiven Schlagphase einschalten (CharacterAnimator). Beim
        /// Ausschalten wird nicht geleert, damit der vorhandene Schweif kurz ausklingen kann.</summary>
        public void SetAttackTrailActive(bool active)
        {
            if (_weaponTrail == null) return;
            if (active && !_weaponTrail.emitting)
                _weaponTrail.Clear();
            _weaponTrail.emitting = active;
        }

        /// <summary>Bounds-Check statt echtem Collider-System (M5b, Rechtsklick auf einen NPC - siehe
        /// GameClient.HandleRightClickAttack), analog zu MonsterView.ContainsWorldPoint - kombiniert die
        /// Bounds aller Ebenen-Renderer (Paper Doll hat anders als MonsterView keinen einzelnen Renderer).</summary>
        public bool ContainsWorldPoint(Vector3 worldPos)
        {
            SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>();
            if (renderers.Length == 0) return false;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            return bounds.Contains(worldPos);
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
            ApplyArmorColors(_appearance.ArmorId);

            RefreshSlot(CharacterSlot.Shield, _appearance.ShieldId, null);
            RefreshSlot(CharacterSlot.Helmet, _appearance.HelmetId, null);

            bool hairHidden = _appearance.HelmetId != 0 && IsHelmetHiding(_appearance.HelmetId);
            RefreshSlot(CharacterSlot.Hair, hairHidden ? 0 : _appearance.HairStyleId, ColorForHairId(_appearance.HairColorId));

            if (_hasWeaponOverride && _weaponOverrideSprite != null)
            {
                _weaponRenderer.enabled = true;
                _weaponRenderer.sprite = _weaponOverrideSprite;
                SetWeaponPixelOffset();
                UpdateTrailAnchor();
            }
            else
            {
                RefreshSlot(CharacterSlot.Weapon, _appearance.WeaponId, null);
            }

            UpdateUpgradeEffects();
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
            if (slot == CharacterSlot.Weapon) UpdateTrailAnchor();
        }

        private void ApplyFlip()
        {
            foreach (SpriteRenderer renderer in _renderers.Values)
                renderer.flipX = _facing.FlipX;

            RefreshWeaponSocketPose();
        }

        /// <summary>Setzt die Waffe an den Hand-Anker (gespiegelt bei FlipX, analog zu GetHeadWorldOffset) und
        /// addiert den Frame-Offset des Animators. Der Sprite-Pivot (ComputeWeaponPivot) sorgt dafür, dass
        /// exakt der Griff-Pixel dorthin fällt.</summary>
        private void RefreshWeaponSocketPose()
        {
            if (_weaponSocket == null) return;

            Vector2 anchor = GetHandAnchorLocal();
            Vector2 offset = _frameHandOffset;
            float rotation = _frameHandRotation;
            if (_facing.FlipX)
            {
                offset.x = -offset.x;
                rotation = -rotation;
            }

            _weaponSocket.localPosition = new Vector3(anchor.x + offset.x, anchor.y + offset.y, 0f);
            _weaponSocket.localEulerAngles = new Vector3(0f, 0f, rotation);

            if (_weaponRenderer == null || _bodyRenderer == null) return;

            int sortingOffset = _framePoseInitialized
                ? _frameSortingOffset
                : WeaponSocketFrameConfig.SortingOffsetFor(WeaponSocketFrameConfig.FromFacing(_facing));
            int weaponOrder = _bodyRenderer.sortingOrder + sortingOffset;
            _weaponRenderer.sortingOrder = weaponOrder;

            if (_weaponTrail != null) _weaponTrail.sortingOrder = _bodyRenderer.sortingOrder + 6;
            if (_plus7Glow != null) SetParticleSortingOrder(_plus7Glow, _bodyRenderer.sortingOrder + 6);
            if (_plus9Glow != null) SetParticleSortingOrder(_plus9Glow, _bodyRenderer.sortingOrder + 6);
        }

        private Vector2 GetHandAnchorLocal()
        {
            LookAnchorJson anchor = GetBodyAnchor(_facing.Direction);
            int ppu = _lookCatalog?.Ppu ?? 0;
            if (anchor?.hand != null && anchor.hand.Length >= 2 && ppu > 0)
            {
                float worldX = anchor.hand[0] / (float)ppu;
                float worldY = anchor.hand[1] / (float)ppu;
                if (_facing.FlipX) worldX = -worldX;
                return new Vector2(worldX, worldY);
            }
            return Vector2.zero;
        }

        private void SetWeaponPixelOffset()
        {
            int ppu = _lookCatalog?.Ppu ?? 0;
            if (ppu <= 0)
            {
                _weaponRenderer.transform.localPosition = Vector3.zero;
                return;
            }

            _weaponRenderer.transform.localPosition = new Vector3(
                _weaponPivotOffsetPixels.x / ppu,
                _weaponPivotOffsetPixels.y / ppu,
                0f);
        }

        /// <summary>Nur der feste Rang (0-5) innerhalb der Figur - welche Figur vor welcher liegt, entscheidet
        /// die SortingGroup zusammen mit dem Custom-Axis-Sort des Renderers, nicht dieser Wert. Die Waffe
        /// bekommt danach über das Socket einen relativen Offset, damit sie je Richtung vor/hinter dem Körper
        /// liegt (sprite-spec.md).</summary>
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

        /// <summary>Farb-Palette aus looks.json per MaterialPropertyBlock setzen. Der Block bricht kein
        /// Batching (kein Material-Instanzieren); ein späterer CharacterArmor-Shader liest die drei
        /// Properties. Solange kein Shader-Material vorhanden ist, dient primaryColor als sichtbarer
        /// SpriteRenderer-Farbton (nur für die Rüstung, nicht für den Körper).</summary>
        private void ApplyArmorColors(int armorId)
        {
            Color primary = Color.white;
            Color secondary = Color.white;
            Color accent = Color.white;

            if (_lookCatalog != null && _lookCatalog.TryGetArmorColors(armorId, out LookColorsJson colors))
            {
                primary = ParseColor(colors.primaryColor, Color.white);
                secondary = ParseColor(colors.secondaryColor, Color.white);
                accent = ParseColor(colors.accentColor, Color.white);
            }

            SetArmorProperties(_bodyRenderer, primary, secondary, accent);

            if (_renderers.TryGetValue(CharacterSlot.Armor, out SpriteRenderer armorRenderer))
            {
                SetArmorProperties(armorRenderer, primary, secondary, accent);
                armorRenderer.color = _armorMaterial != null ? Color.white : primary;
            }
        }

        private void SetArmorProperties(SpriteRenderer renderer, Color primary, Color secondary, Color accent)
        {
            if (renderer == null) return;
            if (_armorPropertyBlock == null) _armorPropertyBlock = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(_armorPropertyBlock);
            _armorPropertyBlock.SetColor(ArmorPrimaryProperty, primary);
            _armorPropertyBlock.SetColor(ArmorSecondaryProperty, secondary);
            _armorPropertyBlock.SetColor(ArmorTrimProperty, accent);
            renderer.SetPropertyBlock(_armorPropertyBlock);
        }

        private static Color ParseColor(string hex, Color fallback)
        {
            return !string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out Color color) ? color : fallback;
        }

        // ---- Waffen-VFX (Aufgabe 5) ----

        private void CreateWeaponVfx()
        {
            Shader spriteShader = Shader.Find("Sprites/Default");
            _weaponVfxMaterial = spriteShader != null ? new Material(spriteShader) : null;
            if (_weaponVfxMaterial != null)
                _weaponVfxMaterial.mainTexture = Texture2D.whiteTexture;

            _particleTexture = CreateSoftCircleTexture(32);
            _particleMaterial = spriteShader != null ? new Material(spriteShader) : null;
            if (_particleMaterial != null)
                _particleMaterial.mainTexture = _particleTexture;

            _plus7Glow = CreateGlow("Plus7Glow", new Color(1f, 0.92f, 0.45f, 1f), rateOverTime: 9f, startSize: 0.13f);
            _plus9Glow = CreateGlow("Plus9Glow", new Color(0.35f, 0.6f, 1f, 1f), rateOverTime: 22f, startSize: 0.18f);

            var trailAnchorGo = new GameObject("TrailAnchor");
            trailAnchorGo.transform.SetParent(_weaponRenderer.transform, false);
            _weaponTrailAnchor = trailAnchorGo.transform;

            var trail = trailAnchorGo.AddComponent<TrailRenderer>();
            trail.material = _weaponVfxMaterial;
            trail.time = 0.35f;
            trail.minVertexDistance = 0.02f;
            trail.startWidth = 0.24f;
            trail.endWidth = 0.02f;
            trail.startColor = new Color(0.75f, 0.92f, 1f, 0.95f);
            trail.endColor = new Color(0.75f, 0.92f, 1f, 0f);
            trail.emitting = false;
            trail.autodestruct = false;
            trail.textureMode = LineTextureMode.Stretch;
            trail.alignment = LineAlignment.View;
            _weaponTrail = trail;
            UpdateTrailAnchor();
        }

        private void UpdateTrailAnchor()
        {
            if (_weaponTrailAnchor == null) return;
            float length = _weaponRenderer != null && _weaponRenderer.sprite != null
                ? _weaponRenderer.sprite.bounds.max.y
                : 1.2f;
            _weaponTrailAnchor.localPosition = new Vector3(0f, Mathf.Max(0.5f, length * 0.88f), 0f);
        }

        private void OnDestroy()
        {
            if (_weaponVfxMaterial != null)
                Destroy(_weaponVfxMaterial);
            if (_particleMaterial != null)
                Destroy(_particleMaterial);
            if (_particleTexture != null)
                Destroy(_particleTexture);
        }

        private static Texture2D CreateSoftCircleTexture(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;

            float center = (size - 1) / 2f;
            float radius = size / 2f;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float alpha = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy) / radius);
                    alpha *= alpha;
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        private ParticleSystem CreateGlow(string name, Color color, float rateOverTime, float startSize)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_weaponSocket, false);

            var particles = go.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = particles.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.03f, 0.15f);
            main.startSize = new ParticleSystem.MinMaxCurve(startSize * 0.7f, startSize);
            main.startColor = new ParticleSystem.MinMaxGradient(color, new Color(color.r, color.g, color.b, 0.35f));
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 48;
            main.gravityModifier = 0f;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.enabled = true;
            emission.rateOverTime = rateOverTime;

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.16f;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = _particleMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;

            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            go.SetActive(false);
            return particles;
        }

        private void UpdateUpgradeEffects()
        {
            bool weaponVisible = _weaponRenderer != null && _weaponRenderer.enabled && _hasWeaponOverride;
            SetGlowActive(_plus7Glow, weaponVisible && _weaponUpgradeLevel >= 7);
            SetGlowActive(_plus9Glow, weaponVisible && _weaponUpgradeLevel >= 9);
        }

        private static void SetGlowActive(ParticleSystem particles, bool active)
        {
            if (particles == null) return;

            if (active)
            {
                if (!particles.gameObject.activeSelf)
                    particles.gameObject.SetActive(true);
                if (!particles.isPlaying)
                    particles.Play();
            }
            else
            {
                if (particles.isPlaying)
                    particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                if (particles.gameObject.activeSelf)
                    particles.gameObject.SetActive(false);
            }
        }

        private static void SetParticleSortingOrder(ParticleSystem particles, int sortingOrder)
        {
            ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
            if (renderer != null) renderer.sortingOrder = sortingOrder;
        }
    }
}
