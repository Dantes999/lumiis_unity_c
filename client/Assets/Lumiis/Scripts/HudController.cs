using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Lumiis.Shared;
using Lumiis.Shared.Net;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Lumiis.Client
{
    /// <summary>Spiel-HUD im klassischen MMO-Stil (Hud.uxml): Taskleiste mit HP-/SP-Kugeln, Schnellleiste
    /// (Skills 1-4, Heiltrank 5), EXP-Kugeln und Menüknöpfen, runde Minimap, Zielanzeige, Chat sowie die
    /// Fenster Inventar (I), Charakter (C) und Shop. Liest ausschließlich den bestehenden GameClient-/
    /// ClientWorld-Zustand und schickt nur Absichten über GameClient - keine eigene Spielwahrheit.</summary>
    public sealed class HudController : MonoBehaviour
    {
        private static readonly int[] SkillIds = { 1, 2, 3, 4 };
        private static readonly string[] SkillIconKeys = { "book_bash", "book_sword_spin", "book_berserk", "book_fast_attack" };
        private const int MaxSkillLevel = 17;
        private const int MaxChatLines = 60;
        private const float OrbInnerSize = 84f;
        private const float ExpOrbSize = 18f;
        private const float MinimapSize = 148f;
        private const float MinimapZoom = 3f;
        private const int InventoryColumns = 5;

        private static readonly ChatChannel[] ChatChannelCycle = { ChatChannel.Map, ChatChannel.Global, ChatChannel.Whisper };

        /// <summary>Knopf "System" in der Taskleiste - LoginController öffnet daraufhin das Systemmenü.</summary>
        public event Action SystemMenuRequested;

        private GameClient _gameClient;
        private UIDocument _uiDocument;
        private PanelSettings _panelSettings;
        private VisualElement _root;

        // ---- Spieler/Ziel/Meldung ----
        private Label _playerName;
        private Label _playerLevel;
        private Label _buffs;
        private Label _hpText;
        private Label _spText;
        private VisualElement _hpFill;
        private VisualElement _spFill;
        private readonly VisualElement[] _expOrbFills = new VisualElement[4];
        private Label _xpText;
        private Label _yang;
        private VisualElement _targetRoot;
        private Label _targetName;
        private Label _targetHpText;
        private VisualElement _targetHpFill;
        private Label _message;

        // ---- Schnellleiste ----
        private readonly VisualElement[] _quickslots = new VisualElement[8];
        private readonly Label[] _skillLevels = new Label[4];
        private readonly Label[] _skillCooldowns = new Label[4];
        private Image _potionIcon;
        private Label _potionCount;

        // ---- Minimap ----
        private VisualElement _minimapMap;
        private Label _minimapTitle;
        private Label _minimapCoords;
        private string _minimapMapId = "";

        // ---- Gruppe ----
        private VisualElement _partyRoot;
        private VisualElement _partyMembers;

        // ---- Chat ----
        private VisualElement _chatInputRow;
        private ScrollView _chatLog;
        private TextField _chatInput;
        private TextField _chatTarget;
        private Button _chatChannelButton;
        private ChatChannel _channel = ChatChannel.Map;
        private int _chatLines;
        private bool _chatOpen;
        private int _chatOpenedAtFrame = -10;

        // ---- Fenster ----
        private VisualElement _inventoryWindow;
        private VisualElement _characterWindow;
        private VisualElement _shopWindow;
        private readonly List<VisualElement> _openWindows = new List<VisualElement>();

        private sealed class ItemSlotView
        {
            public VisualElement Root;
            public Image Icon;
            public Label Text;
            public Label Badge;
        }

        private readonly ItemSlotView[] _inventorySlots = new ItemSlotView[GameConstants.InventorySize];
        private readonly Dictionary<EquipmentSlot, ItemSlotView> _equipSlots = new Dictionary<EquipmentSlot, ItemSlotView>();
        private Toggle _protectToggle;
        private Label _inventoryHint;
        private Label _inventoryYang;
        private int _upgradeMaterialSlot = -1;

        private VisualElement _shopGrid;
        private Label _shopTitle;
        private string _shopSignature = "";
        private int _lastShopNpcEntityId = -1;

        private Label _charLevel;
        private Label _charXp;
        private Label _charHp;
        private Label _charSp;
        private Label _charAttack;
        private Label _charDefense;
        private Label _charDodge;
        private Label _charStatHeader;
        private Label _charSkillHeader;
        private readonly Dictionary<StatKind, Label> _statValues = new Dictionary<StatKind, Label>();
        private readonly Dictionary<StatKind, Button> _statButtons = new Dictionary<StatKind, Button>();
        private readonly Label[] _skillRowValues = new Label[4];
        private readonly Label[] _skillRowNames = new Label[4];
        private readonly Button[] _skillRowButtons = new Button[4];

        // ---- Tooltip / Fenster ziehen ----
        private VisualElement _tooltip;
        private Label _tooltipTitle;
        private Label _tooltipBody;
        private VisualElement _tooltipOwner;
        private VisualElement _dragWindow;
        private Vector2 _dragStartPointer;
        private Vector2 _dragStartWindow;

        public void Initialize(GameClient gameClient)
        {
            _gameClient = gameClient;
            _gameClient.ChatMessageReceived += OnChatMessage;
            _gameClient.PartyChangedReceived += OnPartyChanged;
            _gameClient.UiPointerHitTest = IsPointerOverUi;
            StartCoroutine(BuildUiNextFrame());
        }

        /// <summary>Schließt das oberste offene Fenster (Escape). false = keins offen.</summary>
        public bool CloseTopWindow()
        {
            if (_chatOpen)
            {
                CloseChat();
                return true;
            }
            if (_openWindows.Count == 0) return false;
            VisualElement top = _openWindows[_openWindows.Count - 1];
            SetWindowOpen(top, false);
            return true;
        }

        // ================= Aufbau =================

        private IEnumerator BuildUiNextFrame()
        {
            yield return null;

            var go = new GameObject("HudUI");
            go.transform.SetParent(transform, false);
            go.SetActive(false);

            _uiDocument = go.AddComponent<UIDocument>();
            _panelSettings = UiSkin.CreatePanelSettings(0f);
            _uiDocument.panelSettings = _panelSettings;
            go.SetActive(true);

            VisualTreeAsset asset = Resources.Load<VisualTreeAsset>("UI/Hud");
            if (asset == null)
            {
                Debug.LogError("Hud.uxml fehlt unter Assets/Resources/UI/.");
                yield break;
            }

            _root = asset.CloneTree();
            UiSkin.StretchToFill(_root);
            _root.style.display = DisplayStyle.None;
            _uiDocument.rootVisualElement.pickingMode = PickingMode.Ignore;
            _uiDocument.rootVisualElement.Add(_root);

            BindPlayer();
            BindQuickslots();
            BindMinimap();
            BindParty();
            BindChat();
            BindWindows();
            BindTooltip();

            _root.Q<Button>("hud-btn-character").clicked += () => ToggleWindow(_characterWindow);
            _root.Q<Button>("hud-btn-inventory").clicked += () => ToggleWindow(_inventoryWindow);
            _root.Q<Button>("hud-btn-system").clicked += () => SystemMenuRequested?.Invoke();

            AddChatLine("System", "Willkommen in Lumiis! Enter öffnet den Chat, I das Inventar, C den Charakter.", ChatChannel.System);
        }

        private void BindPlayer()
        {
            _playerName = _root.Q<Label>("hud-player-name");
            _playerLevel = _root.Q<Label>("hud-player-level");
            _buffs = _root.Q<Label>("hud-buffs");
            _hpText = _root.Q<Label>("hud-hp-text");
            _spText = _root.Q<Label>("hud-sp-text");
            _hpFill = _root.Q<VisualElement>("hud-hp-fill");
            _spFill = _root.Q<VisualElement>("hud-sp-fill");
            _xpText = _root.Q<Label>("hud-xp-text");
            _yang = _root.Q<Label>("hud-yang");
            for (int i = 0; i < _expOrbFills.Length; i++)
            {
                VisualElement fill = _root.Q<VisualElement>("hud-exp-orb-" + (i + 1) + "-fill");
                var image = new VisualElement { pickingMode = PickingMode.Ignore };
                image.AddToClassList("hud-exp-orb-fill-image");
                fill.Add(image);
                _expOrbFills[i] = fill;
            }

            _targetRoot = _root.Q<VisualElement>("hud-target");
            _targetName = _root.Q<Label>("hud-target-name");
            _targetHpText = _root.Q<Label>("hud-target-hp-text");
            _targetHpFill = _root.Q<VisualElement>("hud-target-hp-fill");
            _message = _root.Q<Label>("hud-message");
        }

        private void BindQuickslots()
        {
            for (int i = 0; i < _quickslots.Length; i++)
                _quickslots[i] = _root.Q<VisualElement>("hud-quickslot-" + (i + 1));

            for (int i = 0; i < SkillIds.Length; i++)
            {
                int index = i;
                _skillLevels[i] = _root.Q<Label>("hud-skill-" + (i + 1));
                _skillCooldowns[i] = _root.Q<Label>("hud-skill-" + (i + 1) + "-cd");
                VisualElement iconHost = _root.Q<VisualElement>("hud-skill-" + (i + 1) + "-icon");
                Texture2D texture = _gameClient.GetIconTexture(SkillIconKeys[i]);
                if (texture != null && iconHost != null)
                    iconHost.Add(CreateIcon(texture, 32f));

                _quickslots[i].RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button == 0) _gameClient.UseSkillSlot(index);
                });
                RegisterTooltip(_quickslots[i], () => SkillTooltip(index));
            }

            // Platz 5: erster Heiltrank aus dem Inventar.
            VisualElement potionHost = _root.Q<VisualElement>("hud-quickslot-5-icon");
            _potionIcon = CreateIcon(null, 32f);
            potionHost.Add(_potionIcon);
            _potionCount = _root.Q<Label>("hud-quickslot-5-count");
            _quickslots[4].RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == 0) _gameClient.UseFirstConsumable();
            });
            RegisterTooltip(_quickslots[4], () =>
            {
                int slot = _gameClient.FindFirstConsumableSlot(out int count);
                if (slot < 0) return ("Heiltrank", "Kein Heiltrank im Inventar.\nTaste 5 benutzt den ersten Heiltrank.");
                return (ItemName(ItemInSlot(slot)), count + "x im Inventar\nTaste 5 oder Klick: benutzen");
            });
        }

        private void BindMinimap()
        {
            _minimapMap = _root.Q<VisualElement>("hud-minimap-map");
            _minimapTitle = _root.Q<Label>("hud-minimap-title");
            _minimapCoords = _root.Q<Label>("hud-minimap-coords");
        }

        private void BindParty()
        {
            _partyRoot = _root.Q<VisualElement>("hud-party");
            _partyMembers = _root.Q<VisualElement>("hud-party-members");
            _partyRoot.style.display = DisplayStyle.None;
            _root.Q<Button>("hud-party-leave").clicked += () => _gameClient.SendChat(ChatChannel.Map, "", "/leave");
        }

        private void BindChat()
        {
            _chatLog = _root.Q<ScrollView>("hud-chat-log");
            _chatLog.pickingMode = PickingMode.Ignore;
            _chatLog.contentContainer.pickingMode = PickingMode.Ignore;
            _chatLog.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            _chatLog.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            _chatInputRow = _root.Q<VisualElement>("hud-chat-input-row");
            _chatInput = _root.Q<TextField>("hud-chat-input");
            _chatTarget = _root.Q<TextField>("hud-chat-target");
            _chatChannelButton = _root.Q<Button>("hud-chat-channel");

            // Chatfeld ist nur fokussierbar, solange es per Enter geöffnet ist - WASD/Leertaste landen sonst
            // nie versehentlich im Textfeld.
            _chatInput.focusable = false;
            _chatTarget.focusable = false;
            _chatInputRow.style.display = DisplayStyle.None;
            _gameClient.SetChatInputFocused(false);

            _chatInput.RegisterCallback<KeyDownEvent>(OnChatKeyDown, TrickleDown.TrickleDown);
            _chatTarget.RegisterCallback<KeyDownEvent>(OnChatKeyDown, TrickleDown.TrickleDown);
            _root.Q<Button>("hud-chat-send").clicked += SubmitChat;
            _chatChannelButton.clicked += CycleChannel;
            UpdateChannelUi();
        }

        private void BindWindows()
        {
            _inventoryWindow = _root.Q<VisualElement>("hud-inventory-window");
            _characterWindow = _root.Q<VisualElement>("hud-character-window");
            _shopWindow = _root.Q<VisualElement>("hud-shop-window");

            SetupWindow(_inventoryWindow, "hud-inventory-titlebar", "hud-inventory-close", null);
            SetupWindow(_characterWindow, "hud-character-titlebar", "hud-character-close", null);
            SetupWindow(_shopWindow, "hud-shop-titlebar", "hud-shop-close", () => _gameClient.CloseShop());

            BuildInventoryWindow();
            BuildCharacterWindow();
            _shopGrid = _root.Q<VisualElement>("hud-shop-grid");
            _shopTitle = _root.Q<Label>("hud-shop-title");
        }

        private void BindTooltip()
        {
            _tooltip = _root.Q<VisualElement>("hud-tooltip");
            _tooltipTitle = _root.Q<Label>("hud-tooltip-title");
            _tooltipBody = _root.Q<Label>("hud-tooltip-body");
            _tooltip.BringToFront();
        }

        private static Image CreateIcon(Texture2D texture, float size)
        {
            var icon = new Image
            {
                image = texture,
                scaleMode = ScaleMode.ScaleToFit,
                pickingMode = PickingMode.Ignore
            };
            icon.style.width = size;
            icon.style.height = size;
            return icon;
        }

        // ================= Fenster: allgemein =================

        private void SetupWindow(VisualElement window, string titlebarName, string closeName, Action onClose)
        {
            window.style.display = DisplayStyle.None;
            window.RegisterCallback<PointerDownEvent>(_ => BringWindowToFront(window), TrickleDown.TrickleDown);

            Button close = window.Q<Button>(closeName);
            close.clicked += () =>
            {
                SetWindowOpen(window, false);
                onClose?.Invoke();
            };

            VisualElement titlebar = window.Q<VisualElement>(titlebarName);
            titlebar.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0 || evt.target is Button) return; // Schließen-Knopf nicht als Ziehen werten.
                _dragWindow = window;
                _dragStartPointer = evt.position;
                _dragStartWindow = new Vector2(window.layout.x, window.layout.y);
                window.style.left = _dragStartWindow.x;
                window.style.top = _dragStartWindow.y;
                window.style.right = StyleKeyword.Auto;
                titlebar.CapturePointer(evt.pointerId);
            });
            titlebar.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (_dragWindow != window || !titlebar.HasPointerCapture(evt.pointerId)) return;
                Vector2 delta = (Vector2)evt.position - _dragStartPointer;
                float maxX = Mathf.Max(0f, _root.layout.width - window.layout.width);
                float maxY = Mathf.Max(0f, _root.layout.height - 40f);
                window.style.left = Mathf.Clamp(_dragStartWindow.x + delta.x, 0f, maxX);
                window.style.top = Mathf.Clamp(_dragStartWindow.y + delta.y, 0f, maxY);
            });
            titlebar.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (titlebar.HasPointerCapture(evt.pointerId)) titlebar.ReleasePointer(evt.pointerId);
                _dragWindow = null;
            });
        }

        private void ToggleWindow(VisualElement window)
        {
            if (window == null) return;
            bool open = window.resolvedStyle.display != DisplayStyle.None && _openWindows.Contains(window);
            SetWindowOpen(window, !open);
            if (open && window == _shopWindow) _gameClient.CloseShop();
        }

        private void SetWindowOpen(VisualElement window, bool open)
        {
            window.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            _openWindows.Remove(window);
            if (open)
            {
                _openWindows.Add(window);
                window.BringToFront();
                _tooltip.BringToFront();
            }
            else
            {
                if (window == _shopWindow) _gameClient.CloseShop();
                if (window == _inventoryWindow) CancelUpgradeMode();
                HideTooltip();
            }
        }

        private void BringWindowToFront(VisualElement window)
        {
            if (!_openWindows.Remove(window)) return;
            _openWindows.Add(window);
            window.BringToFront();
            _tooltip.BringToFront();
        }

        // ================= Inventar =================

        private void BuildInventoryWindow()
        {
            VisualElement equipArea = _root.Q<VisualElement>("hud-equip-area");
            AddEquipSlot(equipArea, EquipmentSlot.Helmet, 50f, 6f, true);
            AddEquipSlot(equipArea, EquipmentSlot.Weapon, 10f, 50f, false);
            AddEquipSlot(equipArea, EquipmentSlot.Armor, 50f, 50f, true);
            AddEquipSlot(equipArea, EquipmentSlot.Shield, 90f, 50f, false);

            _protectToggle = _root.Q<Toggle>("hud-protect-toggle");
            _inventoryHint = _root.Q<Label>("hud-inventory-hint");
            _inventoryYang = _root.Q<Label>("hud-inventory-yang");

            VisualElement grid = _root.Q<VisualElement>("hud-inventory-grid");
            grid.style.width = InventoryColumns * 40f;
            grid.style.alignSelf = Align.Center;
            for (int i = 0; i < _inventorySlots.Length; i++)
            {
                int slotIndex = i;
                ItemSlotView view = CreateItemSlot();
                grid.Add(view.Root);
                _inventorySlots[i] = view;
                view.Root.RegisterCallback<PointerDownEvent>(evt => OnInventorySlotPointerDown(slotIndex, evt.button));
                RegisterTooltip(view.Root, () => InventoryTooltip(slotIndex));
            }
        }

        private void AddEquipSlot(VisualElement area, EquipmentSlot slot, float leftPercent, float top, bool centered)
        {
            ItemSlotView view = CreateItemSlot();
            view.Root.AddToClassList("hud-equip-slot");
            view.Root.style.left = Length.Percent(leftPercent);
            view.Root.style.marginLeft = -19f;
            view.Root.style.top = top;
            area.Add(view.Root);
            _equipSlots[slot] = view;

            view.Root.RegisterCallback<PointerDownEvent>(evt => OnEquipSlotPointerDown(slot, evt.button));
            RegisterTooltip(view.Root, () => EquipTooltip(slot));
        }

        private static ItemSlotView CreateItemSlot()
        {
            var view = new ItemSlotView { Root = new VisualElement() };
            view.Root.AddToClassList("hud-item-slot");
            view.Icon = CreateIcon(null, 30f);
            view.Icon.AddToClassList("hud-item-icon");
            view.Text = new Label { pickingMode = PickingMode.Ignore };
            view.Text.AddToClassList("hud-item-text");
            view.Badge = new Label { pickingMode = PickingMode.Ignore };
            view.Badge.AddToClassList("hud-item-badge");
            view.Root.Add(view.Icon);
            view.Root.Add(view.Text);
            view.Root.Add(view.Badge);
            return view;
        }

        private void OnInventorySlotPointerDown(int slotIndex, int button)
        {
            int itemId = ItemInSlot(slotIndex);
            if (itemId == 0) return;
            ItemEntryJson item = null;
            _gameClient.ItemCatalog?.TryGet(itemId, out item);

            if (button == 1 && _gameClient.ShopNpcEntityId >= 0)
            {
                _gameClient.SellInventorySlot(slotIndex);
                return;
            }

            if (item != null && item.itemType == "UpgradeMaterial")
            {
                _upgradeMaterialSlot = _upgradeMaterialSlot == slotIndex ? -1 : slotIndex;
                return;
            }

            if (item != null && item.itemType == "ProtectionScroll")
            {
                if (_protectToggle != null) _protectToggle.value = true;
                return;
            }

            _gameClient.UseInventorySlot(slotIndex);
        }

        private void OnEquipSlotPointerDown(EquipmentSlot slot, int button)
        {
            if (_gameClient.EquippedItemId(slot) == 0) return;

            if (button == 0 && _upgradeMaterialSlot >= 0)
            {
                bool protect = _protectToggle != null && _protectToggle.value && _gameClient.HasProtectionScroll;
                _gameClient.UpgradeEquippedItem(slot, protect);
                CancelUpgradeMode();
                return;
            }

            if (button == 1) _gameClient.UnequipItem(slot);
        }

        private void CancelUpgradeMode()
        {
            _upgradeMaterialSlot = -1;
        }

        private void RefreshInventoryWindow()
        {
            if (_inventoryWindow.resolvedStyle.display == DisplayStyle.None) return;

            // Material-Slot kann sich durch einen Sync geleert/verschoben haben.
            if (_upgradeMaterialSlot >= 0)
            {
                ItemEntryJson material = null;
                int materialId = ItemInSlot(_upgradeMaterialSlot);
                if (materialId == 0 || _gameClient.ItemCatalog == null
                    || !_gameClient.ItemCatalog.TryGet(materialId, out material) || material.itemType != "UpgradeMaterial")
                    _upgradeMaterialSlot = -1;
            }

            for (int i = 0; i < _inventorySlots.Length; i++)
            {
                int itemId = ItemInSlot(i);
                ApplySlot(_inventorySlots[i], itemId, UpgradeInSlot(i));
                _inventorySlots[i].Root.EnableInClassList("hud-item-slot-selected", i == _upgradeMaterialSlot);
            }

            foreach (KeyValuePair<EquipmentSlot, ItemSlotView> kvp in _equipSlots)
                ApplySlot(kvp.Value, _gameClient.EquippedItemId(kvp.Key), _gameClient.EquippedUpgradeLevel(kvp.Key));

            bool hasScroll = _gameClient.HasProtectionScroll;
            _protectToggle.SetEnabled(hasScroll);
            if (!hasScroll) _protectToggle.value = false;

            if (_upgradeMaterialSlot >= 0)
            {
                _inventoryHint.text = "Aufwerten: Klicke auf ein angelegtes Ausrüstungsteil. Erneuter Klick auf den Schleifstein bricht ab.";
                _inventoryHint.AddToClassList("hud-window-hint-active");
            }
            else
            {
                _inventoryHint.text = _gameClient.ShopNpcEntityId >= 0
                    ? "Rechtsklick auf einen Gegenstand: verkaufen."
                    : "Klick: benutzen/anlegen · Rechtsklick auf Ausrüstung: ablegen · Schleifstein anklicken: aufwerten";
                _inventoryHint.RemoveFromClassList("hud-window-hint-active");
            }

            _inventoryYang.text = FormatYang(_gameClient.YangTotal);
        }

        private void ApplySlot(ItemSlotView view, int itemId, int upgradeLevel)
        {
            if (itemId == 0)
            {
                view.Icon.image = null;
                view.Text.text = "";
                view.Badge.text = "";
                return;
            }

            Texture2D icon = _gameClient.GetItemIcon(itemId);
            view.Icon.image = icon;
            view.Icon.style.display = icon != null ? DisplayStyle.Flex : DisplayStyle.None;
            view.Text.text = icon != null ? "" : ItemName(itemId);
            view.Badge.text = upgradeLevel > 0 ? "+" + upgradeLevel : "";
        }

        private int ItemInSlot(int slotIndex)
        {
            foreach ((int SlotIndex, int ItemId, int UpgradeLevel) entry in _gameClient.InventorySlots)
            {
                if (entry.SlotIndex == slotIndex) return entry.ItemId;
            }
            return 0;
        }

        private int UpgradeInSlot(int slotIndex)
        {
            foreach ((int SlotIndex, int ItemId, int UpgradeLevel) entry in _gameClient.InventorySlots)
            {
                if (entry.SlotIndex == slotIndex) return entry.UpgradeLevel;
            }
            return 0;
        }

        private string ItemName(int itemId)
        {
            return _gameClient.ItemCatalog != null ? _gameClient.ItemCatalog.DisplayName(itemId) : "Gegenstand " + itemId;
        }

        // ================= Charakter =================

        private void BuildCharacterWindow()
        {
            VisualElement body = _root.Q<VisualElement>("hud-character-body");

            body.Add(Section("Allgemein"));
            _charLevel = AddStatRow(body, "Stufe");
            _charXp = AddStatRow(body, "Erfahrung");
            _charHp = AddStatRow(body, "Lebenspunkte");
            _charSp = AddStatRow(body, "Mana (SP)");
            _charAttack = AddStatRow(body, "Angriff");
            _charDefense = AddStatRow(body, "Verteidigung");
            _charDodge = AddStatRow(body, "Ausweichen");

            _charStatHeader = Section("Statuspunkte");
            body.Add(_charStatHeader);
            AddAllocatableStat(body, "Stärke (ST)", StatKind.St);
            AddAllocatableStat(body, "Vitalität (HT)", StatKind.Ht);
            AddAllocatableStat(body, "Geschick (DX)", StatKind.Dx);
            AddAllocatableStat(body, "Intelligenz (IQ)", StatKind.Iq);

            _charSkillHeader = Section("Fertigkeiten");
            body.Add(_charSkillHeader);
            for (int i = 0; i < SkillIds.Length; i++)
            {
                int skillId = SkillIds[i];
                var row = new VisualElement();
                row.AddToClassList("hud-stat-row");
                _skillRowNames[i] = new Label(_gameClient.SkillDisplayName(skillId));
                _skillRowNames[i].AddToClassList("hud-stat-name");
                _skillRowValues[i] = new Label("-");
                _skillRowValues[i].AddToClassList("hud-stat-value");
                _skillRowButtons[i] = new Button(() => _gameClient.AllocateSkillPoint(skillId)) { text = "+" };
                _skillRowButtons[i].AddToClassList("hud-plus-button");
                row.Add(_skillRowNames[i]);
                row.Add(_skillRowValues[i]);
                row.Add(_skillRowButtons[i]);
                body.Add(row);
            }
        }

        private static Label Section(string text)
        {
            var label = new Label(text);
            label.AddToClassList("m2-section");
            return label;
        }

        private static Label AddStatRow(VisualElement body, string name)
        {
            var row = new VisualElement();
            row.AddToClassList("hud-stat-row");
            var nameLabel = new Label(name);
            nameLabel.AddToClassList("hud-stat-name");
            var value = new Label("-");
            value.AddToClassList("hud-stat-value");
            row.Add(nameLabel);
            row.Add(value);
            body.Add(row);
            return value;
        }

        private void AddAllocatableStat(VisualElement body, string name, StatKind stat)
        {
            var row = new VisualElement();
            row.AddToClassList("hud-stat-row");
            var nameLabel = new Label(name);
            nameLabel.AddToClassList("hud-stat-name");
            var value = new Label("-");
            value.AddToClassList("hud-stat-value");
            var button = new Button(() => _gameClient.AllocateStatPoint(stat)) { text = "+" };
            button.AddToClassList("hud-plus-button");
            row.Add(nameLabel);
            row.Add(value);
            row.Add(button);
            body.Add(row);
            _statValues[stat] = value;
            _statButtons[stat] = button;
        }

        private void RefreshCharacterWindow()
        {
            if (_characterWindow.resolvedStyle.display == DisplayStyle.None) return;

            CharacterState stats = _gameClient.World.CharacterStats;
            ClientEntity local = LocalEntity();

            _charLevel.text = stats.Level > 0 ? stats.Level.ToString() : "-";
            _charXp.text = stats.XpToNext > 0 ? stats.Xp + " / " + (stats.Xp + stats.XpToNext) : "MAX";
            _charHp.text = local != null && local.MaxHp > 0 ? Mathf.Max(0, local.CurrentHp) + " / " + local.MaxHp : "-";
            _charSp.text = stats.MaxSp > 0 ? stats.CurrentSp + " / " + stats.MaxSp : "-";
            _charAttack.text = stats.Attack.ToString();
            _charDefense.text = stats.Defense.ToString();
            _charDodge.text = (stats.DodgeBp / 100f).ToString("0.#", CultureInfo.InvariantCulture) + " %";

            _charStatHeader.text = "Statuspunkte" + (stats.UnspentStatPoints > 0 ? "  (" + stats.UnspentStatPoints + " frei)" : "");
            SetStat(StatKind.St, stats.St, stats.UnspentStatPoints);
            SetStat(StatKind.Ht, stats.Ht, stats.UnspentStatPoints);
            SetStat(StatKind.Dx, stats.Dx, stats.UnspentStatPoints);
            SetStat(StatKind.Iq, stats.Iq, stats.UnspentStatPoints);

            _charSkillHeader.text = "Fertigkeiten" + (stats.UnspentSkillPoints > 0 ? "  (" + stats.UnspentSkillPoints + " frei)" : "");
            for (int i = 0; i < SkillIds.Length; i++)
            {
                int level = SkillLevel(SkillIds[i]);
                _skillRowValues[i].text = level > 0 ? "Stufe " + level : "nicht gelernt";
                bool canRaise = stats.UnspentSkillPoints > 0 && level < MaxSkillLevel;
                _skillRowButtons[i].SetEnabled(canRaise);
                _skillRowButtons[i].style.visibility = canRaise ? Visibility.Visible : Visibility.Hidden;
            }
        }

        private void SetStat(StatKind stat, int value, int unspent)
        {
            _statValues[stat].text = value.ToString();
            _statButtons[stat].SetEnabled(unspent > 0);
            _statButtons[stat].style.visibility = unspent > 0 ? Visibility.Visible : Visibility.Hidden;
        }

        // ================= Shop =================

        private void RefreshShopWindow()
        {
            int npcId = _gameClient.ShopNpcEntityId;
            bool shopOpen = npcId >= 0;
            bool windowOpen = _openWindows.Contains(_shopWindow);

            if (shopOpen && npcId != _lastShopNpcEntityId)
            {
                // Neuer Shop (Rechtsklick auf einen Händler): Shop- und Inventarfenster öffnen.
                SetWindowOpen(_shopWindow, true);
                if (!_openWindows.Contains(_inventoryWindow)) SetWindowOpen(_inventoryWindow, true);
                windowOpen = true;
            }
            else if (!shopOpen && windowOpen)
            {
                _shopWindow.style.display = DisplayStyle.None;
                _openWindows.Remove(_shopWindow);
                windowOpen = false;
            }
            _lastShopNpcEntityId = npcId;
            if (!windowOpen) return;

            _shopTitle.text = _gameClient.ShopNpcName;

            string signature = "";
            foreach ((int ItemId, long BuyPrice) entry in _gameClient.ShopItems)
                signature += entry.ItemId + ":" + entry.BuyPrice + ";";
            if (signature == _shopSignature) return;
            _shopSignature = signature;

            _shopGrid.Clear();
            _shopGrid.style.width = InventoryColumns * 40f;
            _shopGrid.style.alignSelf = Align.Center;
            foreach ((int ItemId, long BuyPrice) entry in _gameClient.ShopItems)
            {
                int itemId = entry.ItemId;
                long price = entry.BuyPrice;
                ItemSlotView view = CreateItemSlot();
                ApplySlot(view, itemId, 0);
                view.Root.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button == 0) _gameClient.BuyShopItem(itemId);
                });
                RegisterTooltip(view.Root, () => (ItemName(itemId), ItemDescription(itemId, 0) + "\nPreis: " + FormatYang(price) + "\nKlick: kaufen"));
                _shopGrid.Add(view.Root);
            }
        }

        // ================= Tooltip =================

        private void RegisterTooltip(VisualElement element, Func<(string Title, string Body)> content)
        {
            element.RegisterCallback<PointerEnterEvent>(_ =>
            {
                (string title, string body) = content();
                if (string.IsNullOrEmpty(title))
                {
                    HideTooltip();
                    return;
                }
                _tooltipOwner = element;
                _tooltipTitle.text = title;
                _tooltipBody.text = body ?? "";
                _tooltipBody.style.display = string.IsNullOrEmpty(body) ? DisplayStyle.None : DisplayStyle.Flex;
                _tooltip.style.display = DisplayStyle.Flex;
                _tooltip.BringToFront();
                PositionTooltip(element);
            });
            element.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                if (_tooltipOwner == element) HideTooltip();
            });
        }

        private void PositionTooltip(VisualElement owner)
        {
            Rect bounds = owner.worldBound;
            Vector2 local = _root.WorldToLocal(new Vector2(bounds.xMax + 6f, bounds.yMin));
            float rootWidth = _root.layout.width;
            float rootHeight = _root.layout.height;
            if (local.x + 220f > rootWidth)
                local.x = _root.WorldToLocal(new Vector2(bounds.xMin, bounds.yMin)).x - 226f;
            if (local.y + 90f > rootHeight)
                local.y = Mathf.Max(0f, rootHeight - 100f);
            _tooltip.style.left = Mathf.Max(0f, local.x);
            _tooltip.style.top = Mathf.Max(0f, local.y);
        }

        private void HideTooltip()
        {
            _tooltipOwner = null;
            if (_tooltip != null) _tooltip.style.display = DisplayStyle.None;
        }

        private (string, string) InventoryTooltip(int slotIndex)
        {
            int itemId = ItemInSlot(slotIndex);
            if (itemId == 0) return (null, null);
            int upgrade = UpgradeInSlot(slotIndex);
            string body = ItemDescription(itemId, upgrade);
            if (_gameClient.ItemCatalog != null && _gameClient.ItemCatalog.TryGet(itemId, out ItemEntryJson item))
            {
                if (_gameClient.ShopNpcEntityId >= 0)
                    body += item.sellPrice > 0 ? "\nRechtsklick: verkaufen für " + FormatYang(item.sellPrice) : "\nNicht verkäuflich";
                else if (item.itemType == "UpgradeMaterial")
                    body += "\nKlick: Aufwerten starten";
                else if (item.itemType == "Consumable")
                    body += "\nKlick: benutzen";
                else if (item.itemType != "ProtectionScroll")
                    body += "\nKlick: anlegen";
            }
            return (ItemName(itemId) + (upgrade > 0 ? " +" + upgrade : ""), body);
        }

        private (string, string) EquipTooltip(EquipmentSlot slot)
        {
            int itemId = _gameClient.EquippedItemId(slot);
            if (itemId == 0) return (SlotName(slot), "leer");
            int upgrade = _gameClient.EquippedUpgradeLevel(slot);
            string body = ItemDescription(itemId, upgrade) + "\nRechtsklick: ablegen";
            if (upgrade < ItemEntryJson.MaxUpgradeLevel) body += "\nAufwerten: Schleifstein anklicken, dann hier klicken";
            return (ItemName(itemId) + (upgrade > 0 ? " +" + upgrade : ""), body);
        }

        private string ItemDescription(int itemId, int upgradeLevel)
        {
            if (_gameClient.ItemCatalog == null || !_gameClient.ItemCatalog.TryGet(itemId, out ItemEntryJson item))
                return "";

            string text = ItemTypeName(item.itemType);
            int upgradeBonus = item.upgradeBonusPerLevel * upgradeLevel;
            if (item.itemType == "Weapon")
                text += "\nAngriff +" + (item.attackBonus + upgradeBonus);
            else if (item.attackBonus > 0)
                text += "\nAngriff +" + item.attackBonus;
            if (item.itemType == "Armor" || item.itemType == "Helmet" || item.itemType == "Shield")
                text += "\nVerteidigung +" + (item.defenseBonus + upgradeBonus);
            else if (item.defenseBonus > 0)
                text += "\nVerteidigung +" + item.defenseBonus;
            if (item.healAmount > 0)
                text += "\nHeilt " + item.healAmount + " HP";
            return text;
        }

        private (string, string) SkillTooltip(int index)
        {
            int skillId = SkillIds[index];
            int level = SkillLevel(skillId);
            string body = level > 0 ? "Stufe " + level + " / " + MaxSkillLevel : "Noch nicht gelernt (Charakterfenster: C)";
            body += "\nTaste " + (index + 1) + " oder Klick: einsetzen";
            if (skillId == 1) body += "\nBraucht ein Ziel (Rechtsklick auf ein Monster)";
            return (_gameClient.SkillDisplayName(skillId), body);
        }

        private static string ItemTypeName(string itemType)
        {
            switch (itemType)
            {
                case "Weapon": return "Waffe";
                case "Armor": return "Rüstung";
                case "Helmet": return "Helm";
                case "Shield": return "Schild";
                case "Consumable": return "Verbrauchsgegenstand";
                case "UpgradeMaterial": return "Aufwertungsmaterial";
                case "ProtectionScroll": return "Schriftrolle";
                default: return itemType;
            }
        }

        private static string SlotName(EquipmentSlot slot)
        {
            switch (slot)
            {
                case EquipmentSlot.Weapon: return "Waffe";
                case EquipmentSlot.Armor: return "Rüstung";
                case EquipmentSlot.Helmet: return "Helm";
                case EquipmentSlot.Shield: return "Schild";
                default: return slot.ToString();
            }
        }

        // ================= Update =================

        private void Update()
        {
            if (_root == null) return;

            bool visible = _gameClient != null && _gameClient.IsInGame && !_gameClient.IsAssetLabActive;
            _root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!visible)
            {
                if (_chatOpen) CloseChat();
                if (_partyRoot != null) _partyRoot.style.display = DisplayStyle.None;
                HideTooltip();
                return;
            }

            HandleHotkeys();
            RefreshPlayer();
            RefreshTarget();
            RefreshQuickslots();
            RefreshMinimap();
            RefreshShopWindow();
            RefreshInventoryWindow();
            RefreshCharacterWindow();
            _message.text = _gameClient.CurrentHudMessage ?? "";

            // Tooltip verschwindet, wenn sein Element ausgeblendet wurde (z. B. Fenster geschlossen).
            if (_tooltipOwner != null && _tooltipOwner.resolvedStyle.display == DisplayStyle.None)
                HideTooltip();
        }

        private void HandleHotkeys()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || _chatOpen || _gameClient.IsChatInputFocused) return;

            bool enter = keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame;
            if (enter && !_gameClient.ChatClosedRecently)
            {
                OpenChat();
                return;
            }

            if (keyboard.iKey.wasPressedThisFrame) ToggleWindow(_inventoryWindow);
            if (keyboard.cKey.wasPressedThisFrame) ToggleWindow(_characterWindow);
            if (keyboard.digit5Key.wasPressedThisFrame) _gameClient.UseFirstConsumable();
        }

        private void RefreshPlayer()
        {
            CharacterState stats = _gameClient.World.CharacterStats;
            ClientEntity local = LocalEntity();

            _playerName.text = local != null ? local.Name : "Spieler";
            _playerLevel.text = "Lv. " + (stats.Level > 0 ? stats.Level.ToString() : "-") + "   ·   " + _gameClient.CurrentMapName;

            int hp = local != null ? Mathf.Max(0, local.CurrentHp) : 0;
            int maxHp = local != null ? Mathf.Max(0, local.MaxHp) : 0;
            SetOrb(_hpFill, hp, maxHp);
            _hpText.text = maxHp > 0 ? hp + "/" + maxHp : "-";

            SetOrb(_spFill, stats.CurrentSp, stats.MaxSp);
            _spText.text = stats.MaxSp > 0 ? stats.CurrentSp + "/" + stats.MaxSp : "-";

            long xpTotal = stats.Xp + stats.XpToNext;
            float xpFraction = stats.XpToNext <= 0 ? 1f : (xpTotal > 0 ? (float)stats.Xp / xpTotal : 0f);
            for (int i = 0; i < _expOrbFills.Length; i++)
            {
                float part = Mathf.Clamp01(xpFraction * _expOrbFills.Length - i);
                _expOrbFills[i].style.height = ExpOrbSize * part;
            }
            _xpText.text = stats.XpToNext > 0
                ? (xpFraction * 100f).ToString("0.0", CultureInfo.InvariantCulture) + " %"
                : "MAX";

            _yang.text = FormatYang(_gameClient.YangTotal);

            string buffText = "";
            foreach (KeyValuePair<BuffType, float> kvp in _gameClient.ActiveBuffSeconds)
            {
                if (buffText.Length > 0) buffText += "   ";
                buffText += BuffName(kvp.Key) + " " + kvp.Value.ToString("0", CultureInfo.InvariantCulture) + "s";
            }
            _buffs.text = buffText;
        }

        private void RefreshTarget()
        {
            int targetId = _gameClient.TargetEntityId;
            if (targetId < 0 || !_gameClient.World.Entities.TryGetValue(targetId, out ClientEntity target))
            {
                _targetRoot.style.display = DisplayStyle.None;
                return;
            }

            _targetRoot.style.display = DisplayStyle.Flex;
            _targetName.text = target.Name;
            int hp = Mathf.Max(0, target.CurrentHp);
            int maxHp = Mathf.Max(0, target.MaxHp);
            float fraction = maxHp <= 0 ? 1f : Mathf.Clamp01((float)hp / maxHp);
            _targetHpFill.style.width = Length.Percent(fraction * 100f);
            _targetHpText.text = maxHp > 0 ? hp + " / " + maxHp : "";
        }

        private void RefreshQuickslots()
        {
            for (int i = 0; i < SkillIds.Length; i++)
            {
                int level = SkillLevel(SkillIds[i]);
                _skillLevels[i].text = level > 0 ? level.ToString() : "";
                _quickslots[i].EnableInClassList("hud-quickslot-locked", level <= 0);

                float remaining = _gameClient.SkillCooldownRemaining(SkillIds[i]);
                bool cooling = remaining > 0f;
                _skillCooldowns[i].text = cooling ? remaining.ToString(remaining < 10f ? "0.0" : "0", CultureInfo.InvariantCulture) : "";
                _skillCooldowns[i].EnableInClassList("hud-quickslot-cooling", cooling);
            }

            int potionSlot = _gameClient.FindFirstConsumableSlot(out int potionCount);
            _potionIcon.image = potionSlot >= 0 ? _gameClient.GetItemIcon(ItemInSlot(potionSlot)) : null;
            _potionCount.text = potionCount > 0 ? potionCount.ToString() : "";
            _quickslots[4].EnableInClassList("hud-quickslot-locked", potionCount <= 0);
        }

        private void RefreshMinimap()
        {
            if (!string.Equals(_minimapMapId, _gameClient.CurrentMapId, StringComparison.Ordinal))
            {
                _minimapMapId = _gameClient.CurrentMapId;
                Sprite mapSprite = Resources.Load<Sprite>("Maps/" + _minimapMapId);
                Texture2D mapTexture = mapSprite != null ? mapSprite.texture : Resources.Load<Texture2D>("Maps/" + _minimapMapId);
                _minimapMap.style.backgroundImage = mapTexture != null ? new StyleBackground(mapTexture) : new StyleBackground(StyleKeyword.None);
                _minimapTitle.text = _gameClient.CurrentMapName;
            }

            Vector3 position = _gameClient.LocalPlayerWorldPosition;
            _minimapCoords.text = Mathf.RoundToInt(position.x) + ", " + Mathf.RoundToInt(position.y);

            // Kartenausschnitt um die eigene Figur: Karte ist MinimapZoom-mal so groß wie der Kreis und wird
            // so verschoben, dass die Spielerposition in der Kreismitte (Punkt) liegt.
            float mapSize = MinimapSize * MinimapZoom;
            _minimapMap.style.width = mapSize;
            _minimapMap.style.height = mapSize;
            if (!_gameClient.TryGetCurrentMapVisual(out MapVisual visual) || visual.Width <= 0 || visual.Height <= 0)
            {
                _minimapMap.style.left = (MinimapSize - mapSize) / 2f;
                _minimapMap.style.top = (MinimapSize - mapSize) / 2f;
                return;
            }

            float x = Mathf.Clamp01((position.x - visual.OriginX) / visual.Width);
            float y = Mathf.Clamp01(1f - (position.y - visual.OriginY) / visual.Height);
            _minimapMap.style.left = MinimapSize / 2f - x * mapSize;
            _minimapMap.style.top = MinimapSize / 2f - y * mapSize;
        }

        private static void SetOrb(VisualElement fill, float current, float max)
        {
            float fraction = max <= 0f ? 0f : Mathf.Clamp01(current / max);
            fill.style.height = OrbInnerSize * fraction;
        }

        private ClientEntity LocalEntity()
        {
            ClientWorld world = _gameClient.World;
            if (world.LocalEntityId >= 0 && world.Entities.TryGetValue(world.LocalEntityId, out ClientEntity local))
                return local;
            return null;
        }

        private int SkillLevel(int skillId)
        {
            return _gameClient.World.CharacterStats.Skills.TryGetValue(skillId, out int level) ? level : 0;
        }

        private static string FormatYang(long yang)
        {
            // Tausendertrennzeichen mit Punkt wie im Deutschen, unabhängig von der Systemkultur.
            return yang.ToString("N0", CultureInfo.InvariantCulture).Replace(',', '.') + " Yang";
        }

        // ================= Chat =================

        private void OpenChat()
        {
            _chatOpen = true;
            _chatOpenedAtFrame = Time.frameCount;
            _chatInputRow.style.display = DisplayStyle.Flex;
            _chatInput.focusable = true;
            _chatTarget.focusable = true;
            _gameClient.SetChatInputFocused(true);
            _chatInput.Focus();
        }

        private void CloseChat()
        {
            if (!_chatOpen) return;
            _chatOpen = false;
            _chatInput.Blur();
            _chatTarget.Blur();
            _chatInput.focusable = false;
            _chatTarget.focusable = false;
            _chatInputRow.style.display = DisplayStyle.None;
            _gameClient.SetChatInputFocused(false);
        }

        private void OnChatKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
            {
                evt.StopPropagation();
                // Dieselbe Enter-Taste, die den Chat geöffnet hat, soll ihn nicht sofort wieder schließen.
                if (Time.frameCount - _chatOpenedAtFrame <= 1) return;
                SubmitChat();
                CloseChat();
            }
            else if (evt.keyCode == KeyCode.Escape)
            {
                evt.StopPropagation();
                CloseChat();
            }
        }

        private void SubmitChat()
        {
            string text = (_chatInput.value ?? "").Trim();
            if (text.Length == 0) return;

            string target = _channel == ChatChannel.Whisper ? (_chatTarget.value ?? "").Trim() : "";
            if (_channel == ChatChannel.Whisper && target.Length == 0)
            {
                AddChatLine("System", "Bitte einen Empfänger für das Flüstern eintragen.", ChatChannel.System);
                return;
            }

            _gameClient.SendChat(_channel, target, text);
            _chatInput.value = "";
        }

        private void CycleChannel()
        {
            int index = Array.IndexOf(ChatChannelCycle, _channel);
            _channel = ChatChannelCycle[(index + 1) % ChatChannelCycle.Length];
            UpdateChannelUi();
            if (_chatOpen) _chatInput.Focus();
        }

        private void UpdateChannelUi()
        {
            _chatChannelButton.text = ChannelName(_channel);
            _chatTarget.style.display = _channel == ChatChannel.Whisper ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void OnChatMessage(ChatMessagePacket packet)
        {
            AddChatLine(packet.SenderName, packet.Text, packet.Channel);
        }

        private void AddChatLine(string sender, string text, ChatChannel channel)
        {
            if (_chatLog == null) return;

            var line = new Label(ChannelPrefix(channel) + sender + ": " + text) { pickingMode = PickingMode.Ignore };
            line.AddToClassList("hud-chat-line");
            if (channel == ChatChannel.System) line.AddToClassList("hud-chat-system");
            else if (channel == ChatChannel.Whisper) line.AddToClassList("hud-chat-whisper");
            else if (channel == ChatChannel.Global) line.AddToClassList("hud-chat-global");

            _chatLog.Add(line);
            _chatLines++;
            while (_chatLines > MaxChatLines && _chatLog.contentContainer.childCount > 0)
            {
                _chatLog.contentContainer.RemoveAt(0);
                _chatLines--;
            }

            _chatLog.schedule.Execute(() => _chatLog.ScrollTo(line));
        }

        private void OnPartyChanged(PartyChangedPacket packet)
        {
            if (_partyRoot == null) return;
            _partyMembers.Clear();
            _partyRoot.style.display = packet.Members.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;

            foreach ((int entityId, string name, int level, int hp, int maxHp) in packet.Members)
            {
                string prefix = entityId == packet.LeaderEntityId ? "★ " : "· ";
                var label = new Label(prefix + name + "  Lv " + level + "  HP " + hp + "/" + maxHp);
                label.AddToClassList("hud-party-member");
                _partyMembers.Add(label);
            }
        }

        // ================= Klick-Abschirmung =================

        /// <summary>true, wenn die Mausposition (Input-System-Bildschirmkoordinaten, Ursprung unten links)
        /// über einem sichtbaren Element mit Klasse "ui-block" liegt - dann soll der Klick die Figur nicht
        /// bewegen oder angreifen (siehe GameClient.HandleClickInput).</summary>
        private bool IsPointerOverUi(Vector2 screenPosition)
        {
            if (_root == null || _root.resolvedStyle.display == DisplayStyle.None) return false;
            IPanel panel = _root.panel;
            if (panel == null) return false;

            Vector2 panelPosition = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screenPosition.x, Screen.height - screenPosition.y));
            VisualElement picked = panel.Pick(panelPosition);
            for (VisualElement element = picked; element != null; element = element.parent)
            {
                if (element.ClassListContains("ui-block")) return true;
            }
            return false;
        }

        // ================= Texte =================

        private static string ChannelPrefix(ChatChannel channel)
        {
            switch (channel)
            {
                case ChatChannel.Global: return "[Global] ";
                case ChatChannel.Whisper: return "[Flüstern] ";
                case ChatChannel.System: return "";
                default: return "";
            }
        }

        private static string ChannelName(ChatChannel channel)
        {
            switch (channel)
            {
                case ChatChannel.Global: return "Global";
                case ChatChannel.Whisper: return "Flüstern";
                case ChatChannel.System: return "System";
                default: return "Karte";
            }
        }

        private static string BuffName(BuffType type)
        {
            switch (type)
            {
                case BuffType.AttackBuff: return "Kampfrausch";
                case BuffType.AttackSpeedBuff: return "Hast";
                default: return type.ToString();
            }
        }

        private void OnDestroy()
        {
            if (_gameClient != null)
            {
                _gameClient.ChatMessageReceived -= OnChatMessage;
                _gameClient.PartyChangedReceived -= OnPartyChanged;
                _gameClient.UiPointerHitTest = null;
            }
            if (_panelSettings != null)
                Destroy(_panelSettings);
        }
    }
}
