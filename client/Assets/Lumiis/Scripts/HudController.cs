using System.Collections;
using System.Collections.Generic;
using Lumiis.Shared.Net;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Lumiis.Client
{
    /// <summary>M7-HUD: dauerhaftes UI-Toolkit-Overlay für Spielerwerte, Ziel, Skillleiste, Buffs und Chat.
    /// Liest ausschließlich den bestehenden GameClient-/ClientWorld-Zustand und sendet Chat-Absichten -
    /// keine eigene Spielwahrheit, keine Prediction.</summary>
    public sealed class HudController : MonoBehaviour
    {
        private static readonly int[] SkillIds = { 1, 2, 3, 4 };
        private const int MaxChatLines = 60;

        private GameClient _gameClient;
        private UIDocument _uiDocument;
        private PanelSettings _panelSettings;
        private VisualElement _root;

        private Label _playerName;
        private Label _mapName;
        private Label _hpText;
        private Label _spText;
        private Label _xpText;
        private Label _yang;
        private VisualElement _hpFill;
        private VisualElement _spFill;
        private VisualElement _xpFill;
        private VisualElement _targetRoot;
        private Label _targetName;
        private Label _targetHpText;
        private VisualElement _targetHpFill;
        private Label _message;
        private Label _buffs;
        private VisualElement _partyRoot;
        private VisualElement _partyMembers;
        private readonly Label[] _skillNames = new Label[4];
        private readonly Label[] _skillCooldowns = new Label[4];

        private ScrollView _chatLog;
        private TextField _chatInput;
        private TextField _chatTarget;
        private Label _chatChannelLabel;
        private ChatChannel _channel = ChatChannel.Map;
        private int _chatLines;
        private bool _chatFocused;

        public void Initialize(GameClient gameClient)
        {
            _gameClient = gameClient;
            _gameClient.ChatMessageReceived += OnChatMessage;
            _gameClient.PartyChangedReceived += OnPartyChanged;
            StartCoroutine(BuildUiNextFrame());
        }

        private IEnumerator BuildUiNextFrame()
        {
            yield return null;

            var go = new GameObject("HudUI");
            go.transform.SetParent(transform, false);
            go.SetActive(false);

            _uiDocument = go.AddComponent<UIDocument>();
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _panelSettings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("UI/DefaultRuntimeTheme");
            _uiDocument.panelSettings = _panelSettings;
            go.SetActive(true);

            VisualTreeAsset asset = Resources.Load<VisualTreeAsset>("UI/Hud");
            if (asset == null)
            {
                Debug.LogError("Hud.uxml fehlt unter Assets/Resources/UI/.");
                yield break;
            }

            _root = asset.CloneTree();
            _root.style.position = Position.Absolute;
            _root.style.left = 0;
            _root.style.top = 0;
            _root.style.right = 0;
            _root.style.bottom = 0;
            _root.style.display = DisplayStyle.None;
            _uiDocument.rootVisualElement.Add(_root);

            _playerName = _root.Q<Label>("hud-player-name");
            _mapName = _root.Q<Label>("hud-map-name");
            _hpText = _root.Q<Label>("hud-hp-text");
            _spText = _root.Q<Label>("hud-sp-text");
            _xpText = _root.Q<Label>("hud-xp-text");
            _yang = _root.Q<Label>("hud-yang");
            _hpFill = _root.Q<VisualElement>("hud-hp-fill");
            _spFill = _root.Q<VisualElement>("hud-sp-fill");
            _xpFill = _root.Q<VisualElement>("hud-xp-fill");
            _targetRoot = _root.Q<VisualElement>("hud-target");
            _targetName = _root.Q<Label>("hud-target-name");
            _targetHpText = _root.Q<Label>("hud-target-hp-text");
            _targetHpFill = _root.Q<VisualElement>("hud-target-hp-fill");
            _message = _root.Q<Label>("hud-message");
            _buffs = _root.Q<Label>("hud-buffs");
            _partyRoot = _root.Q<VisualElement>("hud-party");
            _partyMembers = _root.Q<VisualElement>("hud-party-members");
            _partyRoot.style.display = DisplayStyle.None;
            _root.Q<Button>("hud-party-leave").clicked += () =>
                _gameClient.SendChat(ChatChannel.Map, "", "/leave");

            _skillNames[0] = _root.Q<Label>("hud-skill-1");
            _skillNames[1] = _root.Q<Label>("hud-skill-2");
            _skillNames[2] = _root.Q<Label>("hud-skill-3");
            _skillNames[3] = _root.Q<Label>("hud-skill-4");
            _skillCooldowns[0] = _root.Q<Label>("hud-skill-1-cd");
            _skillCooldowns[1] = _root.Q<Label>("hud-skill-2-cd");
            _skillCooldowns[2] = _root.Q<Label>("hud-skill-3-cd");
            _skillCooldowns[3] = _root.Q<Label>("hud-skill-4-cd");

            _chatLog = _root.Q<ScrollView>("hud-chat-log");
            _chatInput = _root.Q<TextField>("hud-chat-input");
            _chatTarget = _root.Q<TextField>("hud-chat-target");
            _chatChannelLabel = _root.Q<Label>("hud-chat-channel-label");
            _chatTarget.style.display = DisplayStyle.None;

            _chatInput.RegisterCallback<KeyDownEvent>(OnChatKeyDown);
            _chatInput.RegisterCallback<FocusInEvent>(_ =>
            {
                _chatFocused = true;
                _gameClient.SetChatInputFocused(true);
            });
            _chatInput.RegisterCallback<FocusOutEvent>(_ =>
            {
                _chatFocused = false;
                _gameClient.SetChatInputFocused(false);
            });
            _root.Q<Button>("hud-chat-send").clicked += SendChat;
            _root.Q<Button>("hud-chat-map").clicked += () => SetChannel(ChatChannel.Map);
            _root.Q<Button>("hud-chat-global").clicked += () => SetChannel(ChatChannel.Global);
            _root.Q<Button>("hud-chat-whisper").clicked += () => SetChannel(ChatChannel.Whisper);

            AddChatLine("System", "Willkommen. Enter öffnet den Chat.", ChatChannel.System);
        }

        private void Update()
        {
            if (_root == null) return;

            bool visible = _gameClient != null && _gameClient.IsInGame && !_gameClient.IsAssetLabActive;
            _root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!visible)
            {
                if (_chatInput != null && _chatFocused) _chatInput.Blur();
                if (_partyRoot != null) _partyRoot.style.display = DisplayStyle.None;
                return;
            }

            if (Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame
                && !_chatFocused)
            {
                _chatInput.Focus();
            }

            RefreshPlayer();
            RefreshTarget();
            RefreshSkills();
            RefreshMessage();
        }

        private void RefreshPlayer()
        {
            ClientWorld world = _gameClient.World;
            CharacterState stats = world.CharacterStats;

            ClientEntity local = null;
            if (world.LocalEntityId >= 0)
                world.Entities.TryGetValue(world.LocalEntityId, out local);

            _playerName.text = (local != null ? local.Name : "Spieler") + "  ·  Level " + (stats.Level > 0 ? stats.Level.ToString() : "-");
            if (_mapName != null)
                _mapName.text = "Karte: " + _gameClient.CurrentMapName;

            int hp = local != null ? Mathf.Max(0, local.CurrentHp) : 0;
            int maxHp = local != null ? Mathf.Max(0, local.MaxHp) : 0;
            SetBar(_hpFill, hp, maxHp);
            _hpText.text = maxHp > 0 ? hp + "/" + maxHp : "-";

            SetBar(_spFill, stats.CurrentSp, stats.MaxSp);
            _spText.text = stats.MaxSp > 0 ? stats.CurrentSp + "/" + stats.MaxSp : "-";

            long xpTotal = stats.Xp + stats.XpToNext;
            SetBar(_xpFill, xpTotal > 0 ? (float)stats.Xp : 0f, xpTotal > 0 ? (float)xpTotal : 1f);
            _xpText.text = stats.XpToNext > 0 ? stats.Xp + "/" + xpTotal : "MAX";

            _yang.text = "Yang: " + _gameClient.YangTotal;
        }

        private void RefreshTarget()
        {
            ClientWorld world = _gameClient.World;
            int targetId = _gameClient.TargetEntityId;
            if (targetId < 0 || !world.Entities.TryGetValue(targetId, out ClientEntity target))
            {
                _targetRoot.style.display = DisplayStyle.None;
                return;
            }

            _targetRoot.style.display = DisplayStyle.Flex;
            _targetName.text = target.Name;
            int hp = Mathf.Max(0, target.CurrentHp);
            int maxHp = Mathf.Max(0, target.MaxHp);
            SetBar(_targetHpFill, hp, maxHp);
            _targetHpText.text = maxHp > 0 ? hp + "/" + maxHp : "-";
        }

        private void RefreshSkills()
        {
            for (int i = 0; i < SkillIds.Length; i++)
            {
                int skillId = SkillIds[i];
                int level = _gameClient.World.CharacterStats.Skills.TryGetValue(skillId, out int lvl) ? lvl : 0;
                _skillNames[i].text = _gameClient.SkillDisplayName(skillId) + (level > 0 ? " · " + level : " · –");

                float remaining = _gameClient.SkillCooldownRemaining(skillId);
                _skillCooldowns[i].text = remaining > 0f ? remaining.ToString("0.0") + "s" : "";
            }

            string buffText = "";
            foreach (KeyValuePair<BuffType, float> kvp in _gameClient.ActiveBuffSeconds)
            {
                if (buffText.Length > 0) buffText += "   ";
                buffText += BuffName(kvp.Key) + " " + kvp.Value.ToString("0.0") + "s";
            }
            _buffs.text = buffText;
        }

        private void RefreshMessage()
        {
            string text = _gameClient.CurrentHudMessage;
            _message.text = text ?? "";
        }

        private static void SetBar(VisualElement fill, float current, float max)
        {
            float fraction = max <= 0f ? 0f : Mathf.Clamp01(current / max);
            fill.style.width = Length.Percent(fraction * 100f);
        }

        private void SetChannel(ChatChannel channel)
        {
            _channel = channel;
            _chatChannelLabel.text = "Kanal: " + ChannelName(channel);
            _chatTarget.style.display = channel == ChatChannel.Whisper ? DisplayStyle.Flex : DisplayStyle.None;
            _chatInput.Focus();
        }

        private void OnChatKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
            {
                SendChat();
                evt.StopPropagation();
            }
            else if (evt.keyCode == KeyCode.Escape)
            {
                _chatInput.Blur();
                evt.StopPropagation();
            }
        }

        private void SendChat()
        {
            string text = (_chatInput.value ?? "").Trim();
            if (text.Length == 0) return;

            string target = _channel == ChatChannel.Whisper ? (_chatTarget.value ?? "").Trim() : "";
            if (_channel == ChatChannel.Whisper && target.Length == 0)
            {
                _chatTarget.Focus();
                return;
            }

            _gameClient.SendChat(_channel, target, text);
            _chatInput.value = "";
            _chatInput.Focus();
        }

        private void OnChatMessage(ChatMessagePacket packet)
        {
            AddChatLine(packet.SenderName, packet.Text, packet.Channel);
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
                label.AddToClassList("hud-chat-line");
                _partyMembers.Add(label);
            }
        }

        private void AddChatLine(string sender, string text, ChatChannel channel)
        {
            if (_chatLog == null) return;

            var line = new Label(ChannelPrefix(channel) + sender + ": " + text);
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

            _chatLog.ScrollTo(line);
        }

        private static string ChannelPrefix(ChatChannel channel)
        {
            switch (channel)
            {
                case ChatChannel.Global: return "[Global] ";
                case ChatChannel.Whisper: return "[Flüstern] ";
                case ChatChannel.System: return "";
                default: return "[Karte] ";
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
            }
            if (_panelSettings != null)
                Destroy(_panelSettings);
        }
    }
}
