#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using Lumiis.Shared;
using Lumiis.Shared.Net;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace Lumiis.Client
{
    /// <summary>Editor-/Development-Testlabor (F10): blendet Server-Figuren, Monster, Testboards und das
    /// IMGUI-Debug-Overlay aus, baut eine lokale Vorschaufigur plus drei Trainingspuppen auf und stellt eine
    /// echte UI-Toolkit-Oberfläche für Waffen, Ruestungs-Looks, Upgrade-Stufen, Blickrichtungen und
    /// Idle-/Lauf-/Schlag-Animation bereit. Rein clientseitig, keine Server-/Protokollaenderung.</summary>
    public sealed class AssetTestLab : MonoBehaviour
    {
        private const string UiResource = "UI/AssetTestLab";
        private const int MaxUpgradeLevel = 9;

        private GameClient _gameClient;
        private ISpriteProvider _sprites;
        private LookCatalogClient _lookCatalog;
        private ItemCatalogClient _itemCatalog;
        private ItemIconCatalog _iconCatalog;

        private UIDocument _uiDocument;
        private PanelSettings _panelSettings;
        private VisualElement _root;
        private bool _uiReady;
        private bool _pendingOpen;
        private bool _isOpen;

        private Label _weaponLabel;
        private Label _weaponSourceLabel;
        private Label _upgradeLabel;
        private Label _armorLabel;
        private Label _helmetLabel;
        private Label _shieldLabel;
        private Label _hairLabel;
        private Label _hairColorLabel;
        private Label _motionLabel;
        private VisualElement _iconGrid;
        private Label _iconCategoryLabel;
        private Label _iconPageLabel;
        private readonly List<string> _iconCategories = new List<string>();
        private readonly List<IconEntryJson> _iconFiltered = new List<IconEntryJson>();
        private int _iconCategoryIndex;
        private int _iconPage;
        private const int IconPageSize = 64;

        private readonly List<ItemEntryJson> _weapons = new List<ItemEntryJson>();
        private readonly List<int> _armorIds = new List<int>();
        private readonly List<int> _helmetIds = new List<int>();
        private readonly List<int> _shieldIds = new List<int>();
        private readonly List<int> _hairIds = new List<int>();
        private int _weaponIndex;
        private int _upgradeLevel;
        private int _armorIndex;
        private int _helmetIndex;
        private int _shieldIndex;
        private int _hairIndex;
        private int _hairColorIndex = 1;

        private Appearance _appearance;
        private Facing _facing = new Facing(FacingDirection.Down, false);
        private bool _previewWalking;
        private float _previewPhase;

        private GameObject _labRoot;
        private CharacterView _previewView;
        private CharacterAnimator _previewAnimator;
        private CharacterSheetGallery _characterGallery;
        private GameObject _characterGalleryRoot;
        private bool _galleryVisible;
        private Vector3 _previewPosition;

        private Sprite _dummyPostSprite;
        private Sprite _dummyBodySprite;
        private Sprite _dummyHeadSprite;
        private Sprite _dummyArmSprite;

        public bool IsOpen => _isOpen;

        public void Initialize(GameClient gameClient, ISpriteProvider sprites, LookCatalogClient lookCatalog, ItemCatalogClient itemCatalog, ItemIconCatalog iconCatalog)
        {
            _gameClient = gameClient;
            _sprites = sprites;
            _lookCatalog = lookCatalog;
            _itemCatalog = itemCatalog;
            _iconCatalog = iconCatalog;

            BuildLists();
            ResetAppearance();
            StartCoroutine(BuildUiNextFrame());
        }

        private void BuildLists()
        {
            _weapons.Clear();
            if (_itemCatalog != null)
            {
                foreach (ItemEntryJson entry in _itemCatalog.Entries)
                {
                    if (string.Equals(entry.itemType, "Weapon", System.StringComparison.OrdinalIgnoreCase))
                        _weapons.Add(entry);
                }
            }

            FillLookList("Armor", _armorIds);
            FillLookList("Helmet", _helmetIds);
            FillLookList("Shield", _shieldIds);
            FillLookList("Hair", _hairIds);

            _iconCategories.Clear();
            _iconCategories.Add("Alle");
            if (_iconCatalog != null)
            {
                foreach (string category in _iconCatalog.Categories())
                {
                    if (!string.IsNullOrEmpty(category))
                        _iconCategories.Add(category);
                }
            }
        }

        private void FillLookList(string slot, List<int> target)
        {
            target.Clear();
            target.Add(0); // 0 = Ebene aus
            if (_lookCatalog == null) return;
            foreach (int id in _lookCatalog.GetIdsForSlot(slot))
                target.Add(id);
        }

        private void ResetAppearance()
        {
            _weaponIndex = 0;
            _upgradeLevel = 0;
            _armorIndex = _armorIds.Count > 1 ? 1 : 0;
            _helmetIndex = 0;
            _shieldIndex = _shieldIds.Count > 1 ? 1 : 0;
            _hairIndex = _hairIds.Count > 1 ? 1 : 0;
            _hairColorIndex = 1;
            _facing = new Facing(FacingDirection.Down, false);
            _previewWalking = false;
            _previewPhase = 0f;

            _appearance = new Appearance
            {
                BodyId = FirstLookId("Body"),
                HairStyleId = _hairIndex < _hairIds.Count ? _hairIds[_hairIndex] : 0,
                HairColorId = _hairColorIndex,
                ArmorId = _armorIndex < _armorIds.Count ? _armorIds[_armorIndex] : 0,
                HelmetId = _helmetIndex < _helmetIds.Count ? _helmetIds[_helmetIndex] : 0,
                ShieldId = _shieldIndex < _shieldIds.Count ? _shieldIds[_shieldIndex] : 0,
                WeaponId = CurrentWeapon != null ? CurrentWeapon.lookId : 0
            };
        }

        private int FirstLookId(string slot)
        {
            if (_lookCatalog == null) return 1;
            foreach (int id in _lookCatalog.GetIdsForSlot(slot))
                return id;
            return 1;
        }

        private ItemEntryJson CurrentWeapon => _weapons.Count > 0 ? _weapons[(_weaponIndex % _weapons.Count + _weapons.Count) % _weapons.Count] : null;

        // ---- UI ----

        private IEnumerator BuildUiNextFrame()
        {
            yield return null;

            var uiGo = new GameObject("AssetTestLabUI");
            uiGo.transform.SetParent(transform, false);
            uiGo.SetActive(false);

            _uiDocument = uiGo.AddComponent<UIDocument>();
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _panelSettings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("UI/DefaultRuntimeTheme");
            _uiDocument.panelSettings = _panelSettings;
            uiGo.SetActive(true);

            VisualTreeAsset asset = Resources.Load<VisualTreeAsset>(UiResource);
            if (asset == null)
            {
                Debug.LogError("AssetTestLab.uxml fehlt unter Assets/Resources/UI/.");
                yield break;
            }

            _root = asset.CloneTree();
            _uiDocument.rootVisualElement.Add(_root);
            _root.style.position = Position.Absolute;
            _root.style.left = 0;
            _root.style.top = 0;
            _root.style.right = 0;
            _root.style.bottom = 0;
            _root.style.display = DisplayStyle.None;

            _weaponLabel = _root.Q<Label>("asset-lab-weapon-label");
            _weaponSourceLabel = _root.Q<Label>("asset-lab-weapon-source");
            _upgradeLabel = _root.Q<Label>("asset-lab-upgrade-label");
            _armorLabel = _root.Q<Label>("asset-lab-armor-label");
            _helmetLabel = _root.Q<Label>("asset-lab-helmet-label");
            _shieldLabel = _root.Q<Label>("asset-lab-shield-label");
            _hairLabel = _root.Q<Label>("asset-lab-hair-label");
            _hairColorLabel = _root.Q<Label>("asset-lab-haircolor-label");
            _motionLabel = _root.Q<Label>("asset-lab-motion-label");
            _iconGrid = _root.Q<VisualElement>("asset-lab-icon-grid");
            _iconCategoryLabel = _root.Q<Label>("asset-lab-icon-category-label");
            _iconPageLabel = _root.Q<Label>("asset-lab-icon-page-label");

            WireCycle("weapon", ChangeWeapon);
            WireCycle("upgrade", ChangeUpgrade);
            WireCycle("armor", ChangeArmor);
            WireCycle("helmet", ChangeHelmet);
            WireCycle("shield", ChangeShield);
            WireCycle("hair", ChangeHair);
            WireCycle("haircolor", ChangeHairColor);

            _root.Q<Button>("asset-lab-facing-down").clicked += () => SetFacing(new Facing(FacingDirection.Down, false));
            _root.Q<Button>("asset-lab-facing-up").clicked += () => SetFacing(new Facing(FacingDirection.Up, false));
            _root.Q<Button>("asset-lab-facing-left").clicked += () => SetFacing(new Facing(FacingDirection.Side, true));
            _root.Q<Button>("asset-lab-facing-right").clicked += () => SetFacing(new Facing(FacingDirection.Side, false));
            _root.Q<Button>("asset-lab-anim-idle").clicked += () => SetWalking(false);
            _root.Q<Button>("asset-lab-anim-walk").clicked += () => SetWalking(true);
            _root.Q<Button>("asset-lab-anim-attack").clicked += PlayAttack;
            _root.Q<Button>("asset-lab-dummy-reset").clicked += RebuildDummies;
            _root.Q<Button>("asset-lab-reset").clicked += ResetLab;
            _root.Q<Button>("asset-lab-gallery").clicked += ToggleCharacterGallery;
            _root.Q<Button>("asset-lab-screenshot").clicked += () => _gameClient.CaptureScreenshot();
            _root.Q<Button>("asset-lab-icon-category-prev").clicked += () => ChangeIconCategory(-1);
            _root.Q<Button>("asset-lab-icon-category-next").clicked += () => ChangeIconCategory(1);
            _root.Q<Button>("asset-lab-icon-page-prev").clicked += () => ChangeIconPage(-1);
            _root.Q<Button>("asset-lab-icon-page-next").clicked += () => ChangeIconPage(1);
            _root.Q<Button>("asset-lab-close").clicked += Close;

            _uiReady = true;
            RefreshLabels();
            RefreshIconGallery();

            if (_pendingOpen)
            {
                _pendingOpen = false;
                Open();
            }
        }

        private void WireCycle(string prefix, System.Action<int> change)
        {
            Button previous = _root.Q<Button>("asset-lab-" + prefix + "-prev");
            Button next = _root.Q<Button>("asset-lab-" + prefix + "-next");
            if (previous != null) previous.clicked += () => change(-1);
            if (next != null) next.clicked += () => change(1);
        }

        public void Toggle()
        {
            if (_isOpen) Close();
            else Open();
        }

        public void Open()
        {
            if (!_uiReady)
            {
                _pendingOpen = true;
                return;
            }
            if (_isOpen) return;
            if (!_gameClient.HasLocalPlayer)
            {
                Debug.LogWarning("Asset-Testlabor: erst nach dem Join verfügbar.");
                return;
            }

            _isOpen = true;
            _root.style.display = DisplayStyle.Flex;
            _gameClient.SetAssetLabMode(true);

            _previewPosition = _gameClient.LocalPlayerWorldPosition;
            _labRoot = new GameObject("AssetLabScene");
            _galleryVisible = false;
            _characterGalleryRoot = null;
            _characterGallery = null;
            CreatePreview();
            CreateDummies();
            RefreshLabels();
        }

        public void Close()
        {
            if (!_isOpen) return;
            _isOpen = false;

            if (_labRoot != null)
                Destroy(_labRoot);
            _labRoot = null;
            _previewView = null;
            _previewAnimator = null;
            _characterGallery = null;
            _characterGalleryRoot = null;
            _galleryVisible = false;

            if (_root != null)
                _root.style.display = DisplayStyle.None;

            _gameClient.SetAssetLabMode(false);
        }

        private void OnDestroy()
        {
            if (_isOpen)
                _gameClient?.SetAssetLabMode(false);

            if (_panelSettings != null)
                Destroy(_panelSettings);
            DestroySprite(ref _dummyPostSprite);
            DestroySprite(ref _dummyBodySprite);
            DestroySprite(ref _dummyHeadSprite);
            DestroySprite(ref _dummyArmSprite);
        }

        private static void DestroySprite(ref Sprite sprite)
        {
            if (sprite == null) return;
            Texture2D texture = sprite.texture;
            Destroy(sprite);
            if (texture != null) Destroy(texture);
            sprite = null;
        }

        // ---- Vorschau-Figur ----

        private void CreatePreview()
        {
            var go = new GameObject("AssetLabPreview");
            go.transform.SetParent(_labRoot.transform, false);

            _previewView = go.AddComponent<CharacterView>();
            _previewView.Initialize(-2, "AssetLabPreview", _sprites, _lookCatalog);
            _previewView.SetFacing(_facing);
            _previewView.SetWorldPosition(_previewPosition.x, _previewPosition.y);

            _previewAnimator = go.AddComponent<CharacterAnimator>();
            _previewAnimator.Initialize(_previewView);

            ApplyAppearance();
        }

        private void Update()
        {
            if (!_isOpen || _previewView == null || _previewAnimator == null) return;

            if (_previewWalking)
            {
                _previewPhase += Time.deltaTime * 1.8f;
                float offset = Mathf.Sin(_previewPhase) * 1.2f;
                _previewView.SetWorldPosition(_previewPosition.x + offset, _previewPosition.y);
            }
            else
            {
                _previewView.SetWorldPosition(_previewPosition.x, _previewPosition.y);
            }

            _previewAnimator.Animate(_previewWalking);
        }

        private void ApplyAppearance()
        {
            if (_previewView == null) return;
            _previewView.SetAppearance(_appearance);
            _previewView.SetFacing(_facing);
            ApplyWeapon();
            RefreshLabels();
        }

        private void ApplyWeapon()
        {
            if (_previewView == null) return;

            ItemEntryJson weapon = CurrentWeapon;
            if (weapon == null)
            {
                _previewView.ClearWeapon();
                if (_weaponSourceLabel != null) _weaponSourceLabel.text = "Keine Waffe im ItemCatalog.";
                return;
            }

            _appearance.WeaponId = weapon.lookId;
            Sprite sprite = _sprites != null ? _sprites.LoadWeaponSprite(weapon.spriteKey) : null;
            Vector2 pivotOffset = weapon.pivotOffset != null
                ? new Vector2(weapon.pivotOffset.x, weapon.pivotOffset.y)
                : Vector2.zero;

            _previewView.SetWeapon(sprite, pivotOffset, _upgradeLevel);

            if (_weaponSourceLabel != null)
            {
                string path = "Resources/Sprites/Items/" + weapon.spriteKey + ".png";
                _weaponSourceLabel.text = sprite != null
                    ? "PNG geladen: " + path
                    : "PNG fehlt (" + path + ") - Platzhalter wird gezeichnet.";
            }
        }

        private void ChangeWeapon(int direction)
        {
            if (_weapons.Count == 0) return;
            _weaponIndex = (_weaponIndex + direction + _weapons.Count) % _weapons.Count;
            ApplyAppearance();
        }

        private void ChangeUpgrade(int direction)
        {
            _upgradeLevel = Mathf.Clamp(_upgradeLevel + direction, 0, MaxUpgradeLevel);
            ApplyWeapon();
            RefreshLabels();
        }

        private void ChangeArmor(int direction)
        {
            _armorIndex = Wrap(_armorIndex + direction, _armorIds.Count);
            _appearance.ArmorId = _armorIds[_armorIndex];
            ApplyAppearance();
        }

        private void ChangeHelmet(int direction)
        {
            _helmetIndex = Wrap(_helmetIndex + direction, _helmetIds.Count);
            _appearance.HelmetId = _helmetIds[_helmetIndex];
            ApplyAppearance();
        }

        private void ChangeShield(int direction)
        {
            _shieldIndex = Wrap(_shieldIndex + direction, _shieldIds.Count);
            _appearance.ShieldId = _shieldIds[_shieldIndex];
            ApplyAppearance();
        }

        private void ChangeHair(int direction)
        {
            _hairIndex = Wrap(_hairIndex + direction, _hairIds.Count);
            _appearance.HairStyleId = _hairIds[_hairIndex];
            ApplyAppearance();
        }

        private void ChangeHairColor(int direction)
        {
            int count = _lookCatalog != null ? _lookCatalog.HairColors.Count : 0;
            if (count == 0) return;
            _hairColorIndex = ((_hairColorIndex - 1 + direction + count) % count) + 1;
            _appearance.HairColorId = _hairColorIndex;
            ApplyAppearance();
        }

        private static int Wrap(int value, int count)
        {
            if (count <= 0) return 0;
            return (value % count + count) % count;
        }

        private void SetFacing(Facing facing)
        {
            _facing = facing;
            if (_previewView != null) _previewView.SetFacing(facing);
        }

        private void SetWalking(bool walking)
        {
            _previewWalking = walking;
            if (_motionLabel != null) _motionLabel.text = walking ? "Lauf-Demo aktiv" : "Idle";
        }

        private void PlayAttack()
        {
            if (_previewAnimator != null) _previewAnimator.PlayAttack();
            if (_motionLabel != null) _motionLabel.text = "Angriff";
        }

        /// <summary>Nur für Screenshot-/PlayMode-Tests: Stufe und Animation direkt setzen, ohne UI-Klicks.</summary>
        public void DebugSetUpgradeLevel(int level)
        {
            _upgradeLevel = Mathf.Clamp(level, 0, MaxUpgradeLevel);
            ApplyWeapon();
            RefreshLabels();
        }

        public bool DebugTrailEmitting => _previewView != null && _previewView.IsAttackTrailEmitting;
        public int DebugTrailPositionCount => _previewView != null ? _previewView.TrailPositionCount : 0;

        public void DebugPlayAttack()
        {
            PlayAttack();
        }

        public void DebugSetWalking(bool walking)
        {
            SetWalking(walking);
        }

        private void ResetLab()
        {
            ResetAppearance();
            ApplyAppearance();
            SetWalking(false);
        }

        private void RefreshLabels()
        {
            ItemEntryJson weapon = CurrentWeapon;
            if (_weaponLabel != null)
                _weaponLabel.text = weapon != null ? weapon.name + " (Look " + weapon.lookId + ")" : "Keine";
            if (_upgradeLabel != null) _upgradeLabel.text = "+" + _upgradeLevel;
            if (_armorLabel != null) _armorLabel.text = LookName("Armor", _appearance.ArmorId);
            if (_helmetLabel != null) _helmetLabel.text = LookName("Helmet", _appearance.HelmetId);
            if (_shieldLabel != null) _shieldLabel.text = LookName("Shield", _appearance.ShieldId);
            if (_hairLabel != null) _hairLabel.text = LookName("Hair", _appearance.HairStyleId);
            if (_hairColorLabel != null) _hairColorLabel.text = HairColorName(_appearance.HairColorId);
        }

        private void ChangeIconCategory(int direction)
        {
            if (_iconCategories.Count == 0) return;
            _iconCategoryIndex = (_iconCategoryIndex + direction + _iconCategories.Count) % _iconCategories.Count;
            _iconPage = 0;
            RefreshIconGallery();
        }

        private void ChangeIconPage(int direction)
        {
            int pageCount = PageCount();
            if (pageCount <= 1) return;
            _iconPage = (_iconPage + direction + pageCount) % pageCount;
            RefreshIconGallery();
        }

        private int PageCount()
        {
            FilterIcons();
            return Mathf.Max(1, (_iconFiltered.Count + IconPageSize - 1) / IconPageSize);
        }

        private void FilterIcons()
        {
            _iconFiltered.Clear();
            if (_iconCatalog == null) return;

            string category = _iconCategories.Count > 0 ? _iconCategories[_iconCategoryIndex] : "Alle";
            foreach (IconEntryJson entry in _iconCatalog.All)
            {
                if (category == "Alle" || string.Equals(entry.category, category, System.StringComparison.OrdinalIgnoreCase))
                    _iconFiltered.Add(entry);
            }
        }

        private void RefreshIconGallery()
        {
            if (_iconGrid == null) return;

            FilterIcons();
            int pageCount = Mathf.Max(1, (_iconFiltered.Count + IconPageSize - 1) / IconPageSize);
            _iconPage = Mathf.Clamp(_iconPage, 0, pageCount - 1);

            _iconGrid.Clear();
            int start = _iconPage * IconPageSize;
            int end = Mathf.Min(start + IconPageSize, _iconFiltered.Count);
            for (int i = start; i < end; i++)
            {
                IconEntryJson entry = _iconFiltered[i];
                var cell = new VisualElement();
                cell.AddToClassList("asset-lab-icon-cell");
                cell.tooltip = entry.key + " (" + entry.category + ")";
                var image = new Image
                {
                    image = _iconCatalog.GetTexture(entry.key),
                    scaleMode = ScaleMode.ScaleToFit
                };
                cell.Add(image);
                _iconGrid.Add(cell);
            }

            if (_iconCategoryLabel != null)
                _iconCategoryLabel.text = _iconCategories.Count > 0 ? _iconCategories[_iconCategoryIndex] : "Alle";
            if (_iconPageLabel != null)
                _iconPageLabel.text = (_iconPage + 1) + "/" + pageCount + " (" + _iconFiltered.Count + ")";
        }

        private string LookName(string slot, int id)
        {
            if (id <= 0) return "Keine";
            return _lookCatalog != null && _lookCatalog.TryGetEntry(slot, id, out LookEntryJson entry) ? entry.name : "#" + id;
        }

        private string HairColorName(int id)
        {
            if (_lookCatalog == null || id <= 0 || id > _lookCatalog.HairColors.Count) return "-";
            return _lookCatalog.HairColors[id - 1];
        }

        // ---- Trainingspuppen ----

        private void RebuildDummies()
        {
            if (_labRoot == null) return;
            foreach (Transform child in _labRoot.transform)
            {
                if (child.name.StartsWith("Trainingspuppe"))
                    Destroy(child.gameObject);
            }
            CreateDummies();
        }

        private void ToggleCharacterGallery()
        {
            if (_galleryVisible) HideCharacterGallery();
            else ShowCharacterGallery();
        }

        private void ShowCharacterGallery()
        {
            if (_galleryVisible || _labRoot == null) return;
            _galleryVisible = true;

            if (_previewView != null) _previewView.gameObject.SetActive(false);
            foreach (Transform child in _labRoot.transform)
            {
                if (child.name.StartsWith("Trainingspuppe"))
                    child.gameObject.SetActive(false);
            }

            _characterGalleryRoot = new GameObject("CharacterSheetGallery");
            _characterGalleryRoot.transform.SetParent(_labRoot.transform, false);
            _characterGallery = _characterGalleryRoot.AddComponent<CharacterSheetGallery>();
            _characterGallery.Build(_previewPosition);

            Button button = _root != null ? _root.Q<Button>("asset-lab-gallery") : null;
            if (button != null) button.text = "Charakter-Galerie ausblenden";
        }

        private void HideCharacterGallery()
        {
            if (!_galleryVisible) return;
            _galleryVisible = false;

            if (_characterGalleryRoot != null)
                Destroy(_characterGalleryRoot);
            _characterGalleryRoot = null;
            _characterGallery = null;

            if (_previewView != null) _previewView.gameObject.SetActive(true);
            if (_labRoot != null)
            {
                foreach (Transform child in _labRoot.transform)
                {
                    if (child.name.StartsWith("Trainingspuppe"))
                        child.gameObject.SetActive(true);
                }
            }

            Button button = _root != null ? _root.Q<Button>("asset-lab-gallery") : null;
            if (button != null) button.text = "Charakter-Galerie zeigen";
        }

        public void DebugShowCharacterGallery()
        {
            ShowCharacterGallery();
        }

        public void DebugSetUiVisible(bool visible)
        {
            if (_root != null)
                _root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void DebugShowIconGallery(string category, int page = 0)
        {
            if (_iconCategories.Count == 0) return;
            int index = _iconCategories.FindIndex(c => string.Equals(c, category, System.StringComparison.OrdinalIgnoreCase));
            _iconCategoryIndex = index >= 0 ? index : 0;
            _iconPage = Mathf.Max(0, page);
            RefreshIconGallery();

            ScrollView scroll = _root != null ? _root.Q<ScrollView>() : null;
            if (scroll != null) scroll.scrollOffset = new Vector2(0f, 500f);
        }

        private void CreateDummies()
        {
            if (_labRoot == null) return;
            EnsureDummySprites();

            CreateDummy(_previewPosition + new Vector3(-2.2f, 0f, 0f), "Trainingspuppe Links");
            CreateDummy(_previewPosition + new Vector3(2.2f, 0f, 0f), "Trainingspuppe Rechts");
            CreateDummy(_previewPosition + new Vector3(0f, 2.2f, 0f), "Trainingspuppe Oben");
        }

        private void EnsureDummySprites()
        {
            if (_dummyPostSprite == null) _dummyPostSprite = PlaceholderSprites.CreateSquare();
            if (_dummyBodySprite == null) _dummyBodySprite = PlaceholderSprites.CreateCircle(32, 32);
            if (_dummyHeadSprite == null) _dummyHeadSprite = PlaceholderSprites.CreateCircle(32, 32);
            if (_dummyArmSprite == null) _dummyArmSprite = PlaceholderSprites.CreateSquare();
        }

        private void CreateDummy(Vector3 position, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_labRoot.transform, false);
            go.transform.position = position;

            var sortingGroup = go.AddComponent<SortingGroup>();
            sortingGroup.sortingOrder = 0;

            AddDummyPart(go.transform, "Posten", _dummyPostSprite, new Color(0.42f, 0.29f, 0.17f), new Vector3(0f, 0.45f, 0f), new Vector3(0.12f, 0.9f, 1f), 0);
            AddDummyPart(go.transform, "Arme", _dummyArmSprite, new Color(0.5f, 0.35f, 0.2f), new Vector3(0f, 0.78f, 0f), new Vector3(0.85f, 0.08f, 1f), 1);
            AddDummyPart(go.transform, "Körper", _dummyBodySprite, new Color(0.78f, 0.67f, 0.38f), new Vector3(0f, 0.72f, 0f), new Vector3(0.42f, 0.55f, 1f), 2);
            AddDummyPart(go.transform, "Kopf", _dummyHeadSprite, new Color(0.88f, 0.77f, 0.45f), new Vector3(0f, 1.1f, 0f), new Vector3(0.32f, 0.32f, 1f), 3);

            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(go.transform, false);
            labelGo.transform.localPosition = new Vector3(0f, 1.42f, 0f);
            var label = labelGo.AddComponent<TextMesh>();
            label.text = "Trainingspuppe";
            label.characterSize = 0.09f;
            label.fontSize = 40;
            label.anchor = TextAnchor.MiddleCenter;
            label.color = new Color(0.9f, 0.9f, 0.85f);

            MeshRenderer labelRenderer = labelGo.GetComponent<MeshRenderer>();
            if (labelRenderer != null) labelRenderer.sortingOrder = 100;
        }

        private static void AddDummyPart(Transform parent, string name, Sprite sprite, Color color, Vector3 localPosition, Vector3 localScale, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = localScale;
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            renderer.sortingOrder = sortingOrder;
        }
    }
}
#endif
