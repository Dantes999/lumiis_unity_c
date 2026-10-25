using System.Collections.Generic;
using Lumiis.Shared;
using Lumiis.Shared.Net;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Lumiis.Client
{
    /// <summary>Orchestriert NetClient, ClientWorld, Views, Eingabe, Kamera und die temporäre Debug-UI (IMGUI).
    /// Wird von Bootstrap erzeugt, keine Inspector-Verkabelung.</summary>
    public sealed class GameClient : MonoBehaviour
    {
        private NetClient _netClient;
        private Camera _camera;
        private readonly ClientWorld _world = new ClientWorld();
        private readonly Dictionary<int, CharacterView> _views = new Dictionary<int, CharacterView>();
        private readonly Dictionary<int, CharacterAnimator> _animators = new Dictionary<int, CharacterAnimator>();
        // Monster (seit M3c): eigene, viel einfachere View statt CharacterView (kein Paper-Doll, keine
        // Blickrichtung/Animation) - deshalb ein eigenes, kleineres Dictionary statt _views/_animators
        // mitzubenutzen (siehe MonsterView-Klassenkommentar).
        private readonly Dictionary<int, MonsterView> _monsterViews = new Dictionary<int, MonsterView>();
        private readonly Dictionary<int, GroundItemView> _groundItemViews = new Dictionary<int, GroundItemView>();

        /// <summary>M7-Chat: eingehende ChatMessagePacket für die HUD-/Chat-UI. Reines Anzeige-Event,
        /// der Client sendet nur ChatRequestPacket und berechnet keine Chat-Zustände.</summary>
        public event System.Action<ChatMessagePacket> ChatMessageReceived;

        /// <summary>M7-Party: voller Gruppenstand (Resync) für das HUD.</summary>
        public event System.Action<PartyChangedPacket> PartyChangedReceived;
        /// <summary>Gruppen-UI ohne Chat: Name des Einladenden (PartyInviteReceivedPacket).</summary>
        public event System.Action<string> PartyInviteReceived;

        /// <summary>Rechtsklick auf einen anderen Spieler: (EntityId, Name) für ein Kontextmenü im HUD
        /// (z. B. "In Gruppe einladen"). Reine UI-Anfrage, keine Spielaktion.</summary>
        public event System.Action<int, string> PlayerContextRequested;
        private Sprite _squareSprite;
        private Sprite _localMarkerSprite;
        private Sprite _monsterCircleSprite;
        private Sprite _monsterTriangleSprite;

        // ---- Kampf-Visualisierung (rein clientseitig/kosmetisch, siehe CLAUDE.md Abschnitt
        // "Kampf-Visualisierung") ----
        // Einheits-Ring (Durchmesser exakt 1 Weltenheit bei transform.localScale 1) - gemeinsame Sprite fuer
        // Reichweitenring/Radius-Blitz/Buff-Ringe, Groesse jeweils ueber localScale gesteuert statt einer
        // eigenen gebackenen Textur pro Verwendung (siehe RangeRingView/RadiusFlashView/BuffRingView).
        private Sprite _unitRingSprite;
        private RangeRingView _rangeRingView;
        private readonly Dictionary<BuffType, RangeRingView> _buffRingViews = new Dictionary<BuffType, RangeRingView>();
        private LookCatalogClient _lookCatalog;
        private ItemCatalogClient _itemCatalog;
        private ItemIconCatalog _iconCatalog;
        private SkillCatalogClient _skillCatalog;
        private ClassCatalogClient _classCatalog;
        private MapCatalogClient _mapCatalogClient;
        private UpgradeCatalogClient _upgradeCatalog;
        private GameObject _mapRoot;
        private CachingSpriteProvider _spriteProvider;
        private AudioService _audio;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private CharacterTestboard _testboard;
        private CharacterTestboard _sortingTestboard;
        private CharacterTestboard _weaponTestboard;
        private AssetTestLab _assetTestLab;
        private Vector2 _debugPanelScroll;

        /// <summary>Lokaler Test-Override für die Waffen-Aufwertungsstufe (+0..+9), nur Debug/Editor. Die
        /// echte Instanzstufe kommt erst mit M6c über das Inventar; bis dahin lässt sich das +7/+9-Glitzern
        /// damit direkt an der eigenen Figur prüfen.</summary>
        private int _debugWeaponUpgradeLevel;

        /// <summary>Bildschirmbereich des Debug-Test-Panels (siehe DrawDebugTestPanel) - wird dort jeden Frame
        /// aktualisiert. HandleClickInput prüft das, damit ein Klick auf einen Button im Panel nicht
        /// zusätzlich als Bewegungs-/Angriffsbefehl in die Welt durchschlägt.</summary>
        private Rect _debugPanelRect;

        // Dev-Tools-Debug-Spawn (siehe CLAUDE.md "Dev-Tools"): fest hinterlegte Mob-Typen statt eines
        // geladenen Katalogs - der Client kennt mobs.json bewusst nicht (siehe CLAUDE.md Abschnitt "Monster"),
        // dieselbe kleine, feste MobId-Zuordnung wie MonsterShapeFor/MonsterColorFor, nur um einen Anzeigenamen
        // ergänzt. Reine Dev-Bequemlichkeit, kein neuer Lade-Mechanismus für ein Dev-Only-Feature.
        private static readonly (int MobId, string Name)[] DebugMobTypes = { (1, "Wildhund"), (2, "Waldschrat"), (3, "Lumiis-Stein"), (4, "Waldboss"), (5, "Hungriger Wildhund") };
        private int _debugSpawnMobIndex;
        private int _debugMapIndex;
#endif

        // Login/Charakterauswahl übernimmt LoginController (UI Toolkit) - GameClient merkt sich nur, ob
        // der Join bereits geglückt ist (fürs Gating von Klick-Eingabe und Debug-Overlay).
        private bool _inGame;
        private string _pendingLoginName = "";
        private string _statusMessage = "";
        private bool _debugOverlayVisible;

        // escape-menu (siehe docs/tasks/escape-menu.md): von LoginController gesetzt, während das Menü
        // sichtbar ist - sperrt nur die eigentlichen Gameplay-Eingaben (Bewegungsklick, Angriff/Rechtsklick,
        // Skills 1-4), nicht Zoom/F1-Overlay-Umschaltung (keine Gameplay-Aktionen im engeren Sinn).
        private bool _gameplayInputLocked;

        /// <summary>M7-Chat: UI-Textfeld hat den Fokus - dann dürfen Tasten 1-4 keine Skills auslösen und
        /// Mausklicks nicht durch das Chat-Panel in die Welt gehen.</summary>
        private bool _chatInputFocused;
        private int _chatClosedAtFrame = -10;

        // Asset-Testlabor (nur Editor/Development): blendet Server-Figuren/Monster aus, sperrt Gameplay-Eingaben
        // und lässt GameClient.OnGUI das Debug-Overlay weglassen. Die UI selbst ist eine separate UI-Toolkit-
        // Wurzel in AssetTestLab.
        private bool _assetLabActive;
        private readonly List<GameObject> _assetLabHiddenObjects = new List<GameObject>();

        // ---- Kampf (M4a, Debug-Overlay-Text - siehe DrawStatusLine/DrawTargetLine) ----
        private int _targetEntityId = -1;
        private string _lastAttackFailedMessage = "";
        private bool _attackHeld;
        private float _nextAutoAttackAt;
        private int _comboIndex;
        private float _nextMoveSendAt;
        private bool _keyboardMoveSent;
        private Vector2 _aimDirection = Vector2.down;
        private long _yangTotal;
        private bool _isDead;

        // ---- Inventar/Ausrüstung (M5a, Debug-Panel-Stil - siehe DrawInventoryPanel/DrawEquipmentPanel) ----
        // Voller Resync bei Join und nach jeder Änderung (siehe InventorySyncPacket) - rein lokaler Cache für
        // die Debug-UI, keine eigene ClientWorld-Anbindung nötig (analog zu _yangTotal/_targetEntityId).
        // Seit M6c trägt jeder Slot zusätzlich seine Aufwertungsstufe (0-9).
        private readonly List<(int SlotIndex, int ItemId, int UpgradeLevel)> _inventorySlots = new List<(int, int, int)>();
        private int _equippedArmorItemId;
        private int _equippedHelmetItemId;
        private int _equippedWeaponItemId;
        private int _equippedShieldItemId;
        private int _equippedArmorUpgradeLevel;
        private int _equippedHelmetUpgradeLevel;
        private int _equippedWeaponUpgradeLevel;
        private int _equippedShieldUpgradeLevel;
        private string _lastInventoryActionFailedMessage = "";

        // ---- Aufwertung (M6c, Debug-Panel-Stil - siehe DrawEquipmentRow) ----
        // Index = (int)EquipmentSlot (1-4); Checkbox-Zustand je Slot. Reine Komfort-Vorprüfung, der Server
        // validiert Material/Rolle final (World.TryUpgrade).
        private readonly bool[] _useProtectionScroll = new bool[5];
        private string _lastUpgradeMessage = "";
        private string _lastUpgradeItemName = "Ausrüstung";

        // ---- Shop (M5b, Debug-Panel-Stil - siehe DrawShopPanel) ----
        // -1 = kein Shop offen. Sortiment kommt per ShopContentPacket (Resync bei jedem OpenShopRequest,
        // kein Delta - dieselbe Philosophie wie InventorySyncPacket).
        private int _shopOpenNpcEntityId = -1;
        private readonly List<(int ItemId, long BuyPrice)> _shopItems = new List<(int, long)>();
        private bool _debugSmithPreview;
        private string _lastShopActionFailedMessage = "";

        // ---- Fortschritt (M6a, Debug-Panel-Stil - siehe DrawCharacterPanel) ----
        // Level/XP/Werte/SP/Ausweichen leben in ClientWorld.CharacterStats (siehe dort) - hier nur die
        // beiden kurzlebigen Overlay-Meldungen (Level Up/Ausgewichen/Verfehlt) und der lokale Level-Cycler
        // fürs Dev-Tools-Debug-Panel.
        private string _lastLevelUpMessage = "";
        private string _lastDodgeMessage = "";
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private int _debugSetLevelValue = 1;
#endif

        // ---- Skills (M6b, Taste 1-4 - echte Spielsteuerung, siehe HandleSkillHotkeys/DrawSkillsLine) ----
        // Feste Zuordnung Taste 1-4 -> SkillId (siehe data/skills.json: 1=Wuchtschlag/SingleStrike,
        // 2=Kreisschlag/AreaAttack, 3=Kampfrausch/AttackBuff, 4=Hast/AttackSpeedBuff) - nur Wuchtschlag
        // braucht ein Ziel (RequiresTarget). Genau diese vier Skills existieren in M6b, kein Katalog-Cycler
        // wie beim Dev-Tools-Monster-Spawn nötig.
        private static readonly (int SkillId, bool RequiresTarget)[] SkillHotkeys =
        {
            (1, true), (2, false), (3, false), (4, false)
        };

        // Kampf-Visualisierung: dieselbe feste Zuordnung wie SkillHotkeys, nur für die zwei Skills, die
        // einen eigenen visuellen Trigger brauchen (Radius-Blitz bzw. optimistische Blickrichtung).
        private const int SingleStrikeSkillId = 1;
        private const int AreaAttackSkillId = 2;

        // Lokale, optimistische Cooldown-Anzeige (Time.time-basiert) - gesetzt beim Senden, nicht vom Server
        // bestätigt (der prüft ohnehin erneut serverautoritativ, siehe World.TryUseSkill). Aktive Buffs
        // dagegen server-autoritativ: RemainingMs aus ActiveBuffsChangedPacket, hier nur lokal mit
        // Time.deltaTime heruntergezählt (Projekt-Konvention: keine Client-Uhrensynchronisation).
        private readonly Dictionary<int, float> _skillCooldownReadyAtTime = new Dictionary<int, float>();
        private readonly Dictionary<BuffType, float> _activeBuffRemainingSeconds = new Dictionary<BuffType, float>();
        private string _lastSkillFailedMessage = "";

        private const float MinZoom = 3f;
        private const float MaxZoom = 300f;
        private const float ZoomStep = 1f;
        private const int MaxDebugEntityRows = 12;

        /// <summary>Von LoginController aufgerufen, bevor NetClient.Connect - der Server schickt den eigenen
        /// Namen nie zurück (JoinAcceptedPacket kennt kein Name-Feld), der Client muss ihn sich also selbst
        /// merken (jetzt: der in der Charakterauswahl gewählte Name, statt wie bisher das freie Namensfeld).</summary>
        public void PrepareJoin(string characterName)
        {
            _pendingLoginName = characterName;
        }

        /// <summary>Von LoginController aufgerufen, während das escape-menu geöffnet/geschlossen wird (siehe
        /// docs/tasks/escape-menu.md) - sperrt nur Bewegungsklick/Angriff/Rechtsklick und Skills 1-4 (siehe
        /// Update-Kommentar), nicht Zoom/F1.</summary>
        public void SetGameplayInputLocked(bool locked)
        {
            _gameplayInputLocked = locked;
            if (locked && _keyboardMoveSent)
            {
                _netClient?.SendStopMove();
                _keyboardMoveSent = false;
            }
        }

        /// <summary>Multi-Map: aktueller Kartenname/-Id für HUD/Debug-Anzeige (nur Anzeige, Server bleibt
        /// autoritativ).</summary>
        public string CurrentMapName
        {
            get { return _world.MapName; }
        }

        public string CurrentMapId
        {
            get { return _world.MapId; }
        }

        /// <summary>Kartenbild-Metadaten (Ursprung/Größe) für HUD-Minimap und Bodenanzeige.</summary>
        public bool TryGetCurrentMapVisual(out MapVisual visual)
        {
            visual = null;
            return _mapCatalogClient != null && _mapCatalogClient.TryGetVisual(_world.MapId, out visual);
        }

        /// <summary>Icon-Textur für HUD-Elemente (z. B. Skill-Symbole aus dem Lumiis-Icon-Manifest).</summary>
        public Texture2D GetIconTexture(string key)
        {
            return _iconCatalog != null ? _iconCatalog.GetTexture(key) : null;
        }

        /// <summary>Multi-Map-Debug-API (nur sinnvoll mit --allow-debug-commands): bittet den Server, den
        /// eigenen Charakter auf die Startposition der Karte zu setzen. Der Server bleibt autoritativ - ohne
        /// Flag passiert nichts (kein Fehlerpaket, wie die übrigen Dev-Befehle).</summary>
        public void DebugChangeMap(string mapId)
        {
            _netClient?.SendDebugChangeMap(mapId);
        }

        public void Initialize(NetClient netClient, Camera camera)
        {
            _netClient = netClient;
            _camera = camera;
            _squareSprite = PlaceholderSprites.CreateSquare();
            _localMarkerSprite = PlaceholderSprites.CreateRing();
            _monsterCircleSprite = PlaceholderSprites.CreateCircle();
            _monsterTriangleSprite = PlaceholderSprites.CreateTriangle();
            // pixelsPerUnit == pixelSize: Durchmesser exakt 1 Weltenheit bei transform.localScale 1 (siehe
            // RangeRingView-Klassenkommentar) - duenner Ring (innerRadiusFraction nah an 1).
            _unitRingSprite = PlaceholderSprites.CreateRing(pixelSize: 64, innerRadiusFraction: 0.85f, pixelsPerUnit: 64);

            try
            {
                _lookCatalog = LookCatalogClient.LoadFromStreamingAssets();
            }
            catch (System.Exception ex)
            {
                Debug.LogError("looks.json konnte nicht geladen werden (Assets/StreamingAssets/lumiis-data/looks.json). "
                    + "Wurde 'cd server && dotnet build' vor dem Öffnen von Unity ausgeführt? " + ex);
                throw;
            }
            _spriteProvider = new CachingSpriteProvider(new LumiisSpriteProvider(_lookCatalog.CanvasPx, _lookCatalog.Ppu));
            _audio = gameObject.AddComponent<AudioService>();
            _audio.Initialize();

            try
            {
                _itemCatalog = ItemCatalogClient.LoadFromStreamingAssets();
            }
            catch (System.Exception ex)
            {
                Debug.LogError("items.json konnte nicht geladen werden (Assets/StreamingAssets/lumiis-data/items.json). "
                    + "Wurde 'cd server && dotnet build' vor dem Öffnen von Unity ausgeführt? " + ex);
                throw;
            }

            _iconCatalog = ItemIconCatalog.LoadFromResources();

            try
            {
                _skillCatalog = SkillCatalogClient.LoadFromStreamingAssets();
            }
            catch (System.Exception ex)
            {
                Debug.LogError("skills.json konnte nicht geladen werden (Assets/StreamingAssets/lumiis-data/skills.json). "
                    + "Wurde 'cd server && dotnet build' vor dem Öffnen von Unity ausgeführt? " + ex);
                throw;
            }

            try
            {
                _classCatalog = ClassCatalogClient.LoadFromStreamingAssets();
            }
            catch (System.Exception ex)
            {
                Debug.LogError("classes.json konnte nicht geladen werden (Assets/StreamingAssets/lumiis-data/classes.json). "
                    + "Wurde 'cd server && dotnet build' vor dem Öffnen von Unity ausgeführt? " + ex);
                throw;
            }

            // Schmied-Fenster: Erfolgschancen nur für die Anzeige - fehlt die Kopie (alter Build), zeigt das
            // Fenster einfach keine Prozentwerte.
            try
            {
                _upgradeCatalog = UpgradeCatalogClient.LoadFromStreamingAssets();
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("upgrade.json nicht gefunden, Schmied zeigt keine Erfolgschancen: " + ex.Message);
            }

            // Multi-Map: Kartenname/Portale nur für die Anzeige - fehlt die Kopie (z. B. alter Build), läuft
            // der Client ohne Portalmarkierungen weiter statt am fehlenden Anzeige-Katalog zu scheitern.
            try
            {
                _mapCatalogClient = MapCatalogClient.LoadFromStreamingAssets();
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("maps/index.json konnte nicht geladen werden - Portale werden nicht angezeigt: " + ex.Message);
                _mapCatalogClient = null;
            }

            _world.EntitySpawned += OnEntitySpawned;
            _world.EntityDespawned += OnEntityDespawned;
            _world.Warning += OnWorldWarning;

            _netClient.Disconnected += OnNetDisconnected;
            _netClient.PacketReceived += OnPacketReceived;

            CreateFloorAndBoundary();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _assetTestLab = gameObject.AddComponent<AssetTestLab>();
            _assetTestLab.Initialize(this, _spriteProvider, _lookCatalog, _itemCatalog, _iconCatalog);
            gameObject.AddComponent<ScreenshotRequestWatcher>();
#endif
        }

        private void OnDestroy()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_assetTestLab != null) _assetTestLab.Close();
#endif
            _spriteProvider?.DestroyAll();
        }

        private void Update()
        {
            _world.Tick(Time.timeAsDouble);

            foreach (KeyValuePair<int, CharacterView> kvp in _views)
            {
                ClientEntity entity;
                if (_world.Entities.TryGetValue(kvp.Key, out entity))
                {
                    kvp.Value.SetWorldPosition(entity.X / 100.0, entity.Y / 100.0);
                    kvp.Value.SetFacing(entity.Facing);
                    if (_animators.TryGetValue(kvp.Key, out CharacterAnimator animator))
                        animator.Animate(entity.IsMoving);
                }
            }

            foreach (KeyValuePair<int, MonsterView> kvp in _monsterViews)
            {
                ClientEntity entity;
                if (_world.Entities.TryGetValue(kvp.Key, out entity))
                {
                    kvp.Value.SetWorldPosition(entity.X / 100.0, entity.Y / 100.0);
                    kvp.Value.SetPose(entity.Facing, entity.IsMoving, Time.time);
                    kvp.Value.SetHealth(entity.CurrentHp, entity.MaxHp);
                }
            }

            if (_world.LocalEntityId >= 0 && _world.Entities.TryGetValue(_world.LocalEntityId, out ClientEntity localForRing))
            {
                if (_rangeRingView != null)
                    _rangeRingView.SetWorldPosition(localForRing.X / 100.0, localForRing.Y / 100.0);
                foreach (RangeRingView buffRing in _buffRingViews.Values)
                    buffRing.SetWorldPosition(localForRing.X / 100.0, localForRing.Y / 100.0);
            }

            if (_inGame && !_gameplayInputLocked && !_assetLabActive && !_chatInputFocused)
            {
                UpdateMouseFacing();
                HandleKeyboardMovement();
                HandleKeyboardCombat();
                HandleClickInput();
                UpdateGroundItemHover();
                HandleSkillHotkeys();
            }
            HandleZoomInput();
            HandleDebugToggleInput();
            UpdateShopAutoClose();
            TickBuffCountdowns();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            HandleDebugHotkeys();
#endif
        }

        /// <summary>Reine Komfortfunktion (M5b) - schließt das offene Shop-Panel, sobald sich die eigene
        /// Figur weiter als NpcInteractRangeWire vom NPC entfernt oder der NPC/die eigene Figur nicht mehr
        /// bekannt ist (z. B. nach einem Despawn). Der Server prüft jede Shop-Aktion ohnehin serverautoritativ
        /// erneut (siehe World.CheckShopAccess) - das hier verhindert nur ein sichtbar totes Panel.</summary>
        private void UpdateShopAutoClose()
        {
            if (_shopOpenNpcEntityId < 0 || _debugSmithPreview) return;

            if (_world.LocalEntityId < 0
                || !_world.Entities.TryGetValue(_world.LocalEntityId, out ClientEntity local)
                || !_world.Entities.TryGetValue(_shopOpenNpcEntityId, out ClientEntity npc))
            {
                _shopOpenNpcEntityId = -1;
                return;
            }

            double dx = local.X - npc.X;
            double dy = local.Y - npc.Y;
            if (dx * dx + dy * dy > (double)GameConstants.NpcInteractRangeWire * GameConstants.NpcInteractRangeWire)
                _shopOpenNpcEntityId = -1;
        }

        private void HandleZoomInput()
        {
            if (Mouse.current == null) return;
            float scrollY = Mouse.current.scroll.ReadValue().y;
            if (scrollY == 0f) return;

            // Nur das Vorzeichen nutzen, da der Betrag je Plattform (Windows/Linux) unterschiedlich skaliert.
            float sign = Mathf.Sign(scrollY);
            _camera.orthographicSize = Mathf.Clamp(_camera.orthographicSize - sign * ZoomStep, MinZoom, MaxZoom);
        }

        private void HandleDebugToggleInput()
        {
            if (Keyboard.current == null) return;
            if (Keyboard.current.f1Key.wasPressedThisFrame)
                _debugOverlayVisible = !_debugOverlayVisible;
        }

        /// <summary>M6b: Taste 1-4 (echte Spielsteuerung, siehe SkillHotkeys). Wuchtschlag (Index 0) nutzt das
        /// aktuell per Rechtsklick anvisierte Ziel (_targetEntityId, -1 = keins -&gt; TargetEntityId 0, der
        /// Server lehnt dann mit TargetRequired ab - kein eigener Vorab-Check nötig, siehe
        /// docs/tasks/M6b-skills.md Abschnitt 6). Die anderen drei Skills brauchen kein Ziel.</summary>
        private void HandleSkillHotkeys()
        {
            if (Keyboard.current == null || _world.LocalEntityId < 0) return;

            if (Keyboard.current.digit1Key.wasPressedThisFrame) UseSkillHotkey(0);
            if (Keyboard.current.digit2Key.wasPressedThisFrame) UseSkillHotkey(1);
            if (Keyboard.current.digit3Key.wasPressedThisFrame) UseSkillHotkey(2);
            if (Keyboard.current.digit4Key.wasPressedThisFrame) UseSkillHotkey(3);
        }

        private void UseSkillHotkey(int hotkeyIndex)
        {
            (int skillId, bool requiresTarget) = SkillHotkeys[hotkeyIndex];
            int targetEntityId = requiresTarget && _targetEntityId >= 0 ? _targetEntityId : 0;
            _netClient.SendUseSkill(skillId, targetEntityId);
            PlayAttackAnimation(_world.LocalEntityId);
            _audio?.PlaySwing();

            // Optimistische lokale Cooldown-Anzeige (siehe SkillCooldownReadyAtTime-Kommentar) - nur wenn der
            // Skill laut letztem CharacterStatsPacket tatsächlich gelernt ist, sonst gäbe es einen falschen
            // Cooldown-Text für einen Skill, den der Server ohnehin mit NotLearned ablehnen wird.
            if (_skillCatalog != null && _world.CharacterStats.Skills.TryGetValue(skillId, out int level) && level > 0)
            {
                int cooldownMs = _skillCatalog.CooldownMsForLevel(skillId, level);
                if (cooldownMs > 0)
                    _skillCooldownReadyAtTime[skillId] = Time.time + cooldownMs / 1000f;
            }

            if (skillId == AreaAttackSkillId)
                TriggerAreaAttackFlash();

            // Kampf-Visualisierung Abschnitt 5: Wuchtschlag ist der einzige zielgerichtete Skill (siehe
            // SkillHotkeys) - sofort lokal zum Ziel drehen, analog zu HandleRightClickAttack.
            // Blickrichtung bleibt am Mauszeiger (siehe UpdateMouseFacing); kein Ziel-Override hier.
        }

        /// <summary>Kampf-Visualisierung Abschnitt 3: kurzzeitiger Radius-Ring beim Ausloesen von Kreisschlag -
        /// optimistisch beim SENDEN ausgeloest (nicht reaktiv am EntityDamaged/EntityDodged), weil diese
        /// Pakete keine Skill-Kennzeichnung tragen (keine Protokolländerung erlaubt) und ein Kreisschlag ohne
        /// Ziel im Radius ohnehin kein Paket erzeugen würde - reaktiv wäre also nicht zuverlässig möglich.</summary>
        private void TriggerAreaAttackFlash()
        {
            if (_world.LocalEntityId < 0 || !_world.Entities.TryGetValue(_world.LocalEntityId, out ClientEntity local)) return;

            int radiusWire = _skillCatalog != null ? _skillCatalog.RadiusWire(AreaAttackSkillId) : 0;
            if (radiusWire <= 0) return;

            var go = new GameObject("AreaAttackFlash");
            var flash = go.AddComponent<RadiusFlashView>();
            Vector3 worldPos = new Vector3((float)(local.X / 100.0), (float)(local.Y / 100.0), 0f);
            float diameter = 2f * radiusWire / 100f;
            flash.Initialize(_unitRingSprite, worldPos, diameter, new Color(1f, 0.6f, 0.15f, 0.5f), durationSeconds: 0.35f);
        }

        /// <summary>M6b: lokale Restzeit aktiver Buffs herunterzählen (siehe ActiveBuffsChangedPacket-Handling
        /// in OnPacketReceived, das die Startwerte setzt) - reine Anzeige, der Server ist weiterhin die
        /// einzige Autorität über den tatsächlichen Ablauf (siehe World.Tick/BuffsExpired).</summary>
        private void TickBuffCountdowns()
        {
            if (_activeBuffRemainingSeconds.Count == 0) return;

            List<BuffType> expired = null;
            List<BuffType> keys = new List<BuffType>(_activeBuffRemainingSeconds.Keys);
            foreach (BuffType type in keys)
            {
                float remaining = _activeBuffRemainingSeconds[type] - Time.deltaTime;
                if (remaining <= 0f)
                {
                    expired ??= new List<BuffType>();
                    expired.Add(type);
                }
                else
                {
                    _activeBuffRemainingSeconds[type] = remaining;
                }
            }

            if (expired == null) return;
            foreach (BuffType type in expired)
                _activeBuffRemainingSeconds.Remove(type);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void HandleDebugHotkeys()
        {
            if (Keyboard.current == null) return;

            if (Keyboard.current.f10Key.wasPressedThisFrame)
            {
                ToggleAssetLab();
                return;
            }
            if (Keyboard.current.f12Key.wasPressedThisFrame)
            {
                CaptureScreenshot();
                return;
            }
            if (_assetLabActive) return;

            if (Keyboard.current.f2Key.wasPressedThisFrame) ToggleTestboard();
            if (Keyboard.current.f9Key.wasPressedThisFrame) ToggleSortingTest();

            if (_world.LocalEntityId < 0) return;
            if (Keyboard.current.f3Key.wasPressedThisFrame) CycleLocalAppearanceSlot("Hair", true);
            if (Keyboard.current.f4Key.wasPressedThisFrame) CycleLocalHairColor(true);
            if (Keyboard.current.f5Key.wasPressedThisFrame) CycleLocalAppearanceSlot("Armor", true);
            if (Keyboard.current.f6Key.wasPressedThisFrame) CycleLocalAppearanceSlot("Helmet", true);
            if (Keyboard.current.f7Key.wasPressedThisFrame) CycleLocalAppearanceSlot("Weapon", true);
            if (Keyboard.current.f8Key.wasPressedThisFrame) CycleLocalAppearanceSlot("Shield", true);
        }

        private void ToggleTestboard()
        {
            if (_testboard == null)
            {
                var go = new GameObject("CharacterTestboard");
                go.transform.position = new Vector3(GameConstants.MapWidth / 100f + 3f, GameConstants.MapHeight / 200f, 0f);
                _testboard = go.AddComponent<CharacterTestboard>();

                List<int> bodyIds = new List<int>(_lookCatalog.GetIdsForSlot("Body"));
                int bodyId = bodyIds.Count > 0 ? bodyIds[0] : 1;
                _testboard.BuildArmorGrid(_spriteProvider, _lookCatalog, bodyId, _lookCatalog.GetIdsForSlot("Armor"));
                go.SetActive(false);
            }

            _testboard.gameObject.SetActive(!_testboard.gameObject.activeSelf);
        }

        private void ToggleSortingTest()
        {
            if (_sortingTestboard == null)
            {
                var go = new GameObject("SortingTestboard");
                go.transform.position = new Vector3(GameConstants.MapWidth / 100f + 3f, GameConstants.MapHeight / 200f - 8f, 0f);
                _sortingTestboard = go.AddComponent<CharacterTestboard>();

                List<int> bodyIds = new List<int>(_lookCatalog.GetIdsForSlot("Body"));
                int bodyId = bodyIds.Count > 0 ? bodyIds[0] : 1;
                _sortingTestboard.BuildSortingTest(_spriteProvider, _lookCatalog, bodyId, _lookCatalog.GetIdsForSlot("Armor"));
                go.SetActive(false);
            }

            _sortingTestboard.gameObject.SetActive(!_sortingTestboard.gameObject.activeSelf);
        }

        /// <summary>Waffen-Testfeld (+0..+9) inkl. Glitzern und Schlag-Trail. Nutzt die erste Waffe aus
        /// items.json (spriteKey/pivotOffset/upgradeLevel) und die erste Rüstung für die Farbpalette.</summary>
        private void ToggleWeaponTest()
        {
            if (_weaponTestboard == null)
            {
                var go = new GameObject("WeaponTestboard");
                go.transform.position = new Vector3(GameConstants.MapWidth / 100f + 3f, GameConstants.MapHeight / 200f - 16f, 0f);
                _weaponTestboard = go.AddComponent<CharacterTestboard>();

                List<int> bodyIds = new List<int>(_lookCatalog.GetIdsForSlot("Body"));
                int bodyId = bodyIds.Count > 0 ? bodyIds[0] : 1;
                List<int> armorIds = new List<int>(_lookCatalog.GetIdsForSlot("Armor"));
                int armorId = armorIds.Count > 0 ? armorIds[armorIds.Count - 1] : 0;
                _weaponTestboard.BuildWeaponUpgradeGrid(_spriteProvider, _lookCatalog, _itemCatalog, bodyId, armorId);
                go.SetActive(false);
            }

            _weaponTestboard.gameObject.SetActive(!_weaponTestboard.gameObject.activeSelf);
        }

        private void ToggleAssetLab()
        {
            if (_assetTestLab == null) return;
            _assetTestLab.Toggle();
        }

        /// <summary>Nur für PlayMode-/Screenshot-Tests: legt eine lokale Spielerfigur ohne Server an,
        /// damit das Asset-Testlabor geöffnet werden kann.</summary>
        public void DebugPrepareAssetLabLocalPlayer()
        {
            if (HasLocalPlayer) return;

            int bodyId = 1;
            foreach (int id in _lookCatalog.GetIdsForSlot("Body"))
            {
                bodyId = id;
                break;
            }

            _world.HandleJoinAccepted(9000, 43700, 49800, "Abenteurer",
                new Appearance { BodyId = bodyId, HairStyleId = 1, HairColorId = 1, ArmorId = 1, WeaponId = 2 },
                mapId: "map1", mapName: "Yongan");
            _world.ApplyCharacterStats(new CharacterStatsPacket
            {
                Level = 5,
                Xp = 140,
                XpToNext = 560,
                MaxHp = 220,
                CurrentHp = 184,
                MaxSp = 60,
                CurrentSp = 44,
                Attack = 22,
                Defense = 9,
                UnspentStatPoints = 3
            });
            _inGame = true;
            RebuildMapVisuals();
        }

        public void DebugOpenAssetLab()
        {
            _assetTestLab?.Open();
        }

        public void DebugCloseAssetLab()
        {
            _assetTestLab?.Close();
        }

        public void DebugSetAssetLabUpgrade(int level)
        {
            _assetTestLab?.DebugSetUpgradeLevel(level);
        }

        public void DebugPlayAssetLabAttack()
        {
            _assetTestLab?.DebugPlayAttack();
        }

        public bool DebugAssetLabTrailEmitting => _assetTestLab != null && _assetTestLab.DebugTrailEmitting;
        public int DebugAssetLabTrailPositionCount => _assetTestLab != null ? _assetTestLab.DebugTrailPositionCount : 0;

        public void DebugShowAssetLabGallery()
        {
            _assetTestLab?.DebugShowCharacterGallery();
        }

        public void DebugSetAssetLabUiVisible(bool visible)
        {
            _assetTestLab?.DebugSetUiVisible(visible);
        }

        public void DebugShowAssetLabIconGallery(string category, int page = 0)
        {
            _assetTestLab?.DebugShowIconGallery(category, page);
        }

        public void DebugShowAssetLabMapPreview()
        {
            _assetTestLab?.DebugShowMapPreview();
        }

        /// <summary>Screenshot-/Testhilfe: zeigt das Einladungs-Popup, als hätte der Server eine Einladung
        /// geschickt. Antworten gehen trotzdem normal an den Server (der sie ohne offene Einladung ignoriert).</summary>
        public void DebugShowPartyInvite(string inviterName)
        {
            PartyInviteReceived?.Invoke(inviterName);
        }

        /// <summary>Screenshot-/Testhilfe: öffnet das Schmied-Fenster ohne echten Schmied in der Nähe
        /// (Sortiment aus items.json: Schleifstein + Bewahrungsrolle). Aufwerten schickt trotzdem eine echte
        /// Anfrage, die der Server ohne Schmied mit NoBlacksmith ablehnt.</summary>
        public void DebugShowSmithPreview()
        {
            _debugSmithPreview = true;
            _shopOpenNpcEntityId = int.MaxValue - 1;
            _shopItems.Clear();
            if (_itemCatalog != null && _upgradeCatalog != null)
            {
                foreach (int itemId in new[] { _upgradeCatalog.WhetstoneItemId, _upgradeCatalog.ProtectionScrollItemId })
                    if (_itemCatalog.TryGet(itemId, out ItemEntryJson item)) _shopItems.Add((itemId, item.buyPrice));
            }
        }

        public void DebugShowPartyPreview()
        {
            if (_world.LocalEntityId < 0 || !_world.Entities.TryGetValue(_world.LocalEntityId, out ClientEntity local))
                return;

            var packet = new PartyChangedPacket { LeaderEntityId = local.Id };
            packet.Members.Add((local.Id, local.Name, _world.CharacterStats.Level, Mathf.Max(0, local.CurrentHp), Mathf.Max(0, local.MaxHp)));
            packet.MemberStatus.Add((true, PartyRole.Attacker));
            packet.Members.Add((local.Id + 1, "Bob", 12, 200, 240));
            packet.MemberStatus.Add((true, PartyRole.Defender));
            packet.Members.Add((0, "Clara", 9, 90, 180)); // offline: EntityId 0, letzter bekannter Stand
            packet.MemberStatus.Add((false, PartyRole.None));
            PartyChangedReceived?.Invoke(packet);
        }

        public void DebugHideAssetLabMapPreview()
        {
            _assetTestLab?.DebugHideMapPreview();
        }

        /// <summary>Schneller Screenshot aus dem laufenden Play-Modus (F12 oder Testlabor-Button) - kein
        /// Unity-Neustart nötig. Schreibt nach /tmp/lumiis-screenshot-&lt;Zeitstempel&gt;.png.</summary>
        public void CaptureScreenshot()
        {
            string path = "/tmp/lumiis-screenshot-" + System.DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png";
            ScreenCapture.CaptureScreenshot(path, 1);
            Debug.Log("Screenshot: " + path);
        }

        /// <summary>Zyklus 0 (keine Ebene) -> erste Katalog-Id -> ... -> letzte -> zurück zu 0 (und umgekehrt
        /// rückwärts). Rein lokal, wird nicht an den Server geschickt.</summary>
        private void CycleLocalAppearanceSlot(string slot, bool forward)
        {
            if (!_world.Entities.TryGetValue(_world.LocalEntityId, out ClientEntity local)) return;

            Appearance appearance = local.Appearance;
            switch (slot)
            {
                case "Hair": appearance.HairStyleId = CycleIdInSlot("Hair", appearance.HairStyleId, forward); break;
                case "Armor": appearance.ArmorId = CycleIdInSlot("Armor", appearance.ArmorId, forward); break;
                case "Helmet": appearance.HelmetId = CycleIdInSlot("Helmet", appearance.HelmetId, forward); break;
                case "Weapon": appearance.WeaponId = CycleIdInSlot("Weapon", appearance.WeaponId, forward); break;
                case "Shield": appearance.ShieldId = CycleIdInSlot("Shield", appearance.ShieldId, forward); break;
            }
            local.Appearance = appearance;

            if (_views.TryGetValue(local.Id, out CharacterView view))
            {
                // Rein lokaler Look-Test: die Item-Waffe (falls eine ausgerüstet ist) wieder durch den
                // Appearance-Platzhalter ersetzen, sonst bliebe SetWeapon als Override aktiv.
                if (slot == "Weapon") view.ClearWeapon();
                view.SetAppearance(appearance);
            }
        }

        private void CycleLocalHairColor(bool forward)
        {
            if (!_world.Entities.TryGetValue(_world.LocalEntityId, out ClientEntity local)) return;

            int colorCount = _lookCatalog.HairColors.Count;
            if (colorCount == 0) return;

            Appearance appearance = local.Appearance;
            int zeroBased = ((appearance.HairColorId - 1) + (forward ? 1 : -1) + colorCount) % colorCount;
            appearance.HairColorId = zeroBased + 1; // 1-basiert, siehe Appearance-Dokumentation
            local.Appearance = appearance;

            if (_views.TryGetValue(local.Id, out CharacterView view))
                view.SetAppearance(appearance);
        }

        private int CycleIdInSlot(string slot, int currentId, bool forward)
        {
            IReadOnlyList<int> ids = _lookCatalog.GetIdsForSlot(slot);
            if (ids.Count == 0) return 0;

            int currentIndex = -1;
            for (int i = 0; i < ids.Count; i++)
            {
                if (ids[i] == currentId) { currentIndex = i; break; }
            }

            if (forward)
            {
                int nextIndex = currentIndex + 1;
                return nextIndex >= ids.Count ? 0 : ids[nextIndex];
            }

            if (currentIndex < 0) return ids[ids.Count - 1]; // keine Ebene -> letzte Id
            int previousIndex = currentIndex - 1;
            return previousIndex < 0 ? 0 : ids[previousIndex]; // erste Id -> keine Ebene
        }

        // ---- Debug-Test-Panel (Buttons statt F-Tasten merken, F-Tasten bleiben zusätzlich nutzbar) ----

        private void DrawDebugTestPanel()
        {
            const float w = 240f;
            float h = Mathf.Min(700f, Screen.height - 20f);
            Rect area = new Rect(Screen.width - w - 10f, 10f, w, h);
            _debugPanelRect = area;
            GUILayout.BeginArea(area, GUI.skin.box);
            _debugPanelScroll = GUILayout.BeginScrollView(_debugPanelScroll);

            GUILayout.Label("Debug-Test (F-Tasten bleiben aktiv)");

            if (GUILayout.Button("Asset-Testlabor (F10)"))
                ToggleAssetLab();
            if (GUILayout.Button("Testboard: " + (_testboard != null && _testboard.gameObject.activeSelf ? "an" : "aus")))
                ToggleTestboard();
            if (GUILayout.Button("Sortier-Testfeld: " + (_sortingTestboard != null && _sortingTestboard.gameObject.activeSelf ? "an" : "aus")))
                ToggleSortingTest();
            if (GUILayout.Button("Waffen-Testfeld +0..+9: " + (_weaponTestboard != null && _weaponTestboard.gameObject.activeSelf ? "an" : "aus")))
                ToggleWeaponTest();

            DrawDebugMapRow();

            if (_world.LocalEntityId >= 0 && _world.Entities.TryGetValue(_world.LocalEntityId, out ClientEntity local))
            {
                Appearance appearance = local.Appearance;
                DrawCycleRow("Frisur", "Hair", appearance.HairStyleId);
                DrawHairColorRow(appearance.HairColorId);
                DrawCycleRow("Ruestung", "Armor", appearance.ArmorId);
                DrawCycleRow("Helm", "Helmet", appearance.HelmetId);
                DrawCycleRow("Waffe", "Weapon", appearance.WeaponId);
                DrawCycleRow("Schild", "Shield", appearance.ShieldId);
                DrawWeaponUpgradeTestRow();
            }

            DrawDebugSpawnRow();
            GUILayout.Space(6);
            if (GUILayout.Button("+1000 Yang"))
                _netClient.SendDebugGiveYang(1000);
            DrawCharacterPanel();
            DrawSkillsPanel();
            DrawEquipmentPanel();
            DrawInventoryPanel();
            DrawShopPanel();

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        /// <summary>M6a: Level/XP/Werte/SP/Ausweichen aus ClientWorld.CharacterStats (siehe dort) - eigenes
        /// Leben kommt bewusst aus ClientEntity (derselbe einzige Speicherort wie überall sonst, siehe
        /// ClientWorld.ApplyHp), nicht aus CharacterStats (das Paket trägt es nur redundant mit). "+"-Knöpfe
        /// neben ST/HT/DX/IQ nur aktiv, wenn UnspentStatPoints &gt; 0 - der Server prüft das ohnehin erneut
        /// (siehe World.TryAllocateStatPoint). Dazu ein Level-Cycler + "Level setzen"-Knopf (Dev-Tools, nur
        /// wirksam mit --allow-debug-commands am Server - der Client weiß nicht, ob die Option aktiv ist,
        /// siehe DrawDebugSpawnRow-Kommentar).</summary>
        private void DrawCharacterPanel()
        {
            GUILayout.Space(6);
            GUILayout.Label("Charakter");

            CharacterState stats = _world.CharacterStats;
            if (stats.Level <= 0)
            {
                GUILayout.Label("(noch kein Stand)");
            }
            else
            {
                string xpText = stats.XpToNext > 0 ? stats.Xp + "/" + (stats.Xp + stats.XpToNext) : "MAX";
                GUILayout.Label("Level " + stats.Level + " - XP " + xpText);

                string ownHp = "-";
                if (_world.LocalEntityId >= 0 && _world.Entities.TryGetValue(_world.LocalEntityId, out ClientEntity local) && local.CurrentHp >= 0)
                    ownHp = local.CurrentHp + "/" + local.MaxHp;
                GUILayout.Label("HP " + ownHp + "   SP " + stats.CurrentSp + "/" + stats.MaxSp);

                DrawStatRow("ST", StatKind.St, stats.St, stats.UnspentStatPoints);
                DrawStatRow("HT", StatKind.Ht, stats.Ht, stats.UnspentStatPoints);
                DrawStatRow("DX", StatKind.Dx, stats.Dx, stats.UnspentStatPoints);
                DrawStatRow("IQ", StatKind.Iq, stats.Iq, stats.UnspentStatPoints);

                GUILayout.Label("Statuspunkte: " + stats.UnspentStatPoints + "   Fertigkeitspunkte: " + stats.UnspentSkillPoints);
                GUILayout.Label("Angriff: " + stats.Attack + "   Verteidigung: " + stats.Defense
                    + "   Ausweichen: " + (stats.DodgeBp / 100.0).ToString("0.0") + "%");
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("<", GUILayout.Width(24)) && _debugSetLevelValue > 1) _debugSetLevelValue--;
            GUILayout.Label("Level " + _debugSetLevelValue, GUILayout.Width(70));
            if (GUILayout.Button(">", GUILayout.Width(24))) _debugSetLevelValue++;
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Level setzen (Dev-Tools)"))
                _netClient.SendDebugSetLevel(_debugSetLevelValue);
        }

        /// <summary>M6b: alle vier Skills mit aktueller Stufe (0-17) und "+"-Knopf (nur aktiv bei
        /// UnspentSkillPoints &gt; 0 UND Stufe &lt; 17) - analog zu DrawStatRow/TryAllocateStatPoint aus M6a.
        /// SkillId kommt aus SkillHotkeys (dieselbe feste 1-4-Zuordnung wie die echten Tasten), Stufe aus
        /// ClientWorld.CharacterStats.Skills (0 = kein Eintrag = nicht gelernt).</summary>
        private void DrawSkillsPanel()
        {
            GUILayout.Space(6);
            GUILayout.Label("Skills");

            CharacterState stats = _world.CharacterStats;
            foreach ((int skillId, bool _) in SkillHotkeys)
            {
                int level = stats.Skills.TryGetValue(skillId, out int lvl) ? lvl : 0;
                string name = _skillCatalog != null ? _skillCatalog.DisplayName(skillId) : "Skill " + skillId;

                GUILayout.BeginHorizontal();
                GUILayout.Label(name + ": Stufe " + level + "/" + SkillCatalogClient.MaxLevel, GUILayout.Width(190));
                GUI.enabled = stats.UnspentSkillPoints > 0 && level < SkillCatalogClient.MaxLevel;
                if (GUILayout.Button("+", GUILayout.Width(24)))
                    _netClient.SendAllocateSkillPoint(skillId);
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
        }

        private void DrawStatRow(string label, StatKind stat, int value, int unspentStatPoints)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label + ": " + value, GUILayout.Width(150));
            GUI.enabled = unspentStatPoints > 0;
            if (GUILayout.Button("+", GUILayout.Width(24)))
                _netClient.SendAllocateStatPoint(stat);
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }

        /// <summary>M5a/M6c: die vier Ausrüstungs-Slots mit aktuellem Item-Namen (+N), Ablegen-Knopf sowie
        /// (nur bei belegtem Slot und Stufe &lt; 9) einer "Aufwerten"-Aktion mit optionaler Bewahrungsrolle.
        /// Item-Namen kommen aus ItemCatalogClient, die Stufe aus dem InventorySyncPacket (Server autoritativ).</summary>
        private void DrawEquipmentPanel()
        {
            GUILayout.Space(6);
            GUILayout.Label("Ausrüstung");
            DrawEquipmentRow("Ruestung", EquipmentSlot.Armor, _equippedArmorItemId, _equippedArmorUpgradeLevel);
            DrawEquipmentRow("Helm", EquipmentSlot.Helmet, _equippedHelmetItemId, _equippedHelmetUpgradeLevel);
            DrawEquipmentRow("Waffe", EquipmentSlot.Weapon, _equippedWeaponItemId, _equippedWeaponUpgradeLevel);
            DrawEquipmentRow("Schild", EquipmentSlot.Shield, _equippedShieldItemId, _equippedShieldUpgradeLevel);
        }

        private void DrawEquipmentRow(string label, EquipmentSlot slot, int equippedItemId, int upgradeLevel)
        {
            GUILayout.BeginHorizontal();
            DrawItemIcon(equippedItemId);
            string itemName = equippedItemId == 0 ? "leer" : _itemCatalog.DisplayName(equippedItemId);
            string levelSuffix = equippedItemId != 0 && upgradeLevel > 0 ? " +" + upgradeLevel : "";
            GUILayout.Label(label + ": " + itemName + levelSuffix, GUILayout.Width(140));
            GUI.enabled = equippedItemId != 0;
            if (GUILayout.Button("Ablegen", GUILayout.Width(60)))
                _netClient.SendUnequipItem(slot);
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            if (equippedItemId != 0 && upgradeLevel < ItemEntryJson.MaxUpgradeLevel)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(28);
                bool hasProtectionScroll = HasProtectionScrollInInventory();
                GUI.enabled = hasProtectionScroll;
                int slotIndex = (int)slot;
                _useProtectionScroll[slotIndex] = GUILayout.Toggle(_useProtectionScroll[slotIndex],
                    hasProtectionScroll ? "Bewahrungsrolle" : "keine Bewahrungsrolle", GUILayout.Width(150));
                GUI.enabled = true;
                if (GUILayout.Button("Aufwerten", GUILayout.Width(80)))
                {
                    _lastUpgradeItemName = itemName;
                    _netClient.SendUpgradeItem(slot, _useProtectionScroll[slotIndex]);
                }
                GUILayout.EndHorizontal();
            }
        }

        /// <summary>M6c: rein clientseitige Vorprüfung für die Checkbox-Aktivierung - zählt die Rollen im
        /// lokalen Inventar-Cache (ItemType "ProtectionScroll" aus items.json). Der Server validiert final.</summary>
        private bool HasProtectionScrollInInventory()
        {
            foreach ((int SlotIndex, int ItemId, int UpgradeLevel) entry in _inventorySlots)
            {
                if (_itemCatalog.TryGet(entry.ItemId, out ItemEntryJson item) && item.itemType == "ProtectionScroll")
                    return true;
            }
            return false;
        }

        private void DrawItemIcon(int itemId)
        {
            Texture2D icon = itemId != 0 && _iconCatalog != null
                ? _iconCatalog.GetTextureForItem(itemId, _itemCatalog)
                : null;
            if (icon != null)
            {
                float width = Mathf.Clamp(24f * icon.width / icon.height, 8f, 24f);
                GUILayout.Box(icon, GUILayout.Width(width), GUILayout.Height(24));
            }
            else
                GUILayout.Space(26);
        }

        /// <summary>M5a: 20 Zeilen (Slot-Nummer + Item-Name oder "leer"). Klick auf einen belegten Slot löst
        /// die Kontextaktion je ItemType aus: Consumable -> benutzen, sonst (Ausrüstungs-Item) -> anlegen -
        /// der Server prüft ItemType/Slot-Passung ohnehin erneut (siehe World.TryEquip).</summary>
        private void DrawInventoryPanel()
        {
            GUILayout.Space(6);
            GUILayout.Label("Inventar");

            for (int slotIndex = 0; slotIndex < GameConstants.InventorySize; slotIndex++)
            {
                int itemId = FindItemInSlot(slotIndex);
                int upgradeLevel = FindUpgradeLevelInSlot(slotIndex);
                GUILayout.BeginHorizontal();
                DrawItemIcon(itemId);
                string levelSuffix = itemId != 0 && upgradeLevel > 0 ? " +" + upgradeLevel : "";
                string text = slotIndex + ": " + (itemId == 0 ? "leer" : _itemCatalog.DisplayName(itemId) + levelSuffix);
                GUI.enabled = itemId != 0;
                if (GUILayout.Button(text, GUILayout.Width(186)))
                    HandleInventorySlotClick(slotIndex, itemId);
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
        }

        private int FindItemInSlot(int slotIndex)
        {
            foreach ((int SlotIndex, int ItemId, int UpgradeLevel) entry in _inventorySlots)
            {
                if (entry.SlotIndex == slotIndex) return entry.ItemId;
            }
            return 0;
        }

        private int FindUpgradeLevelInSlot(int slotIndex)
        {
            foreach ((int SlotIndex, int ItemId, int UpgradeLevel) entry in _inventorySlots)
            {
                if (entry.SlotIndex == slotIndex) return entry.UpgradeLevel;
            }
            return 0;
        }

        private void HandleInventorySlotClick(int slotIndex, int itemId)
        {
            if (itemId == 0) return;
            if (!_itemCatalog.TryGet(itemId, out ItemEntryJson entry)) return;

            if (entry.itemType == "Consumable")
            {
                _netClient.SendUseConsumable(slotIndex);
                return;
            }

            if (entry.itemType == "Armor" || entry.itemType == "Helmet" || entry.itemType == "Weapon" || entry.itemType == "Shield")
            {
                _netClient.SendEquipItem(slotIndex);
                return;
            }

            // Material/Aufwertungsverbrauch: kein Anlegen/Benutzen ueber den Inventar-Klick; der Server
            // wuerde es ohnehin ablehnen. Kurze Rueckmeldung im Debug-Overlay.
            _lastInventoryActionFailedMessage = entry.name + " ist ein Material.";
        }

        /// <summary>M5b: nur sichtbar, solange ein Shop offen ist (Rechtsklick auf einen NPC, siehe
        /// HandleRightClickAttack) - Titel mit NPC-Name (aus ClientWorld, das Paket selbst trägt keinen
        /// Namen), Sortiment (Name/Preis/Kaufen-Knopf, aus ShopContentPacket), darunter die eigenen
        /// Inventar-Slots mit Verkaufspreis (nur SellPrice &gt; 0, lokal aus items.json/ItemCatalogClient -
        /// kein Paket trägt Verkaufspreise), aktueller Yang-Stand, Schließen-Knopf. Schließt sich zusätzlich
        /// automatisch bei zu großer Distanz (siehe UpdateShopAutoClose).</summary>
        private void DrawShopPanel()
        {
            if (_shopOpenNpcEntityId < 0) return;

            GUILayout.Space(6);
            string npcName = _world.Entities.TryGetValue(_shopOpenNpcEntityId, out ClientEntity npc) ? npc.Name : "Händler";
            GUILayout.Label("Shop: " + npcName);

            foreach ((int itemId, long buyPrice) in _shopItems)
            {
                GUILayout.BeginHorizontal();
                DrawItemIcon(itemId);
                GUILayout.Label(_itemCatalog.DisplayName(itemId) + " (" + buyPrice + " Yang)", GUILayout.Width(160));
                if (GUILayout.Button("Kaufen", GUILayout.Width(60)))
                    _netClient.SendBuyItem(_shopOpenNpcEntityId, itemId);
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(4);
            GUILayout.Label("Verkaufen (Yang: " + _yangTotal + ")");
            for (int slotIndex = 0; slotIndex < GameConstants.InventorySize; slotIndex++)
            {
                int itemId = FindItemInSlot(slotIndex);
                if (itemId == 0) continue;
                if (!_itemCatalog.TryGet(itemId, out ItemEntryJson entry) || entry.sellPrice <= 0) continue;

                GUILayout.BeginHorizontal();
                DrawItemIcon(itemId);
                GUILayout.Label(entry.name + " (" + entry.sellPrice + " Yang)", GUILayout.Width(160));
                if (GUILayout.Button("Verkaufen", GUILayout.Width(70)))
                    _netClient.SendSellItem(_shopOpenNpcEntityId, slotIndex);
                GUILayout.EndHorizontal();
            }

            if (GUILayout.Button("Schließen"))
                _shopOpenNpcEntityId = -1;
        }

        /// <summary>Dev-Tools (siehe CLAUDE.md "Dev-Tools"): Mob-Typ-Cycler + Spawnen-Knopf. Ohne
        /// --allow-debug-commands am Server hat der Knopf sichtbar keine Wirkung (Paket wird dort still
        /// verworfen, siehe GameServer.HandleDebugSpawnMonster) - der Client weiß nicht, ob die Option aktiv
        /// ist, und muss das auch nicht wissen.</summary>
        private void DrawDebugSpawnRow()
        {
            GUILayout.Space(6);
            GUILayout.Label("Monster spawnen (Dev-Tools)");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("<", GUILayout.Width(24))) CycleDebugSpawnMobIndex(-1);
            GUILayout.Label(DebugMobTypes[_debugSpawnMobIndex].Name, GUILayout.Width(94));
            if (GUILayout.Button(">", GUILayout.Width(24))) CycleDebugSpawnMobIndex(1);
            GUILayout.EndHorizontal();

            if (GUILayout.Button("Spawnen"))
                _netClient.SendDebugSpawnMonster(DebugMobTypes[_debugSpawnMobIndex].MobId);
        }

        private void CycleDebugSpawnMobIndex(int direction)
        {
            _debugSpawnMobIndex = (_debugSpawnMobIndex + direction + DebugMobTypes.Length) % DebugMobTypes.Length;
        }

        /// <summary>Multi-Map-Debug-API im Test-Panel: Karte wählen und per DebugChangeMapRequestPacket
        /// wechseln (nur mit --allow-debug-commands am Server wirksam). Kartenliste kommt aus
        /// StreamingAssets/maps/index.json, Fallback auf die beiden bekannten Karten.</summary>
        private void DrawDebugMapRow()
        {
            GUILayout.Space(6);
            GUILayout.Label("Karte wechseln (Dev-Tools)");
            IReadOnlyList<string> mapIds = _mapCatalogClient != null && _mapCatalogClient.MapIds.Count > 0
                ? _mapCatalogClient.MapIds
                : new[] { "village", "greenfield" };
            _debugMapIndex = Mathf.Clamp(_debugMapIndex, 0, mapIds.Count - 1);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("<", GUILayout.Width(24)))
                _debugMapIndex = (_debugMapIndex - 1 + mapIds.Count) % mapIds.Count;
            GUILayout.Label(mapIds[_debugMapIndex], GUILayout.Width(94));
            if (GUILayout.Button(">", GUILayout.Width(24)))
                _debugMapIndex = (_debugMapIndex + 1) % mapIds.Count;
            GUILayout.EndHorizontal();

            if (GUILayout.Button("Karte wechseln"))
            {
                string targetMapId = mapIds[_debugMapIndex];
                if (targetMapId == _world.MapId) _debugMapIndex = (_debugMapIndex + 1) % mapIds.Count;
                DebugChangeMap(targetMapId);
            }
        }

        private void DrawCycleRow(string label, string slot, int currentId)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(60));
            if (GUILayout.Button("<", GUILayout.Width(24))) CycleLocalAppearanceSlot(slot, false);
            GUILayout.Label(DisplayNameFor(slot, currentId), GUILayout.Width(70));
            if (GUILayout.Button(">", GUILayout.Width(24))) CycleLocalAppearanceSlot(slot, true);
            GUILayout.EndHorizontal();
        }

        private void DrawHairColorRow(int currentColorId)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Haarfarbe", GUILayout.Width(60));
            if (GUILayout.Button("<", GUILayout.Width(24))) CycleLocalHairColor(false);
            GUILayout.Label(DisplayHairColor(currentColorId), GUILayout.Width(70));
            if (GUILayout.Button(">", GUILayout.Width(24))) CycleLocalHairColor(true);
            GUILayout.EndHorizontal();
        }

        /// <summary>Debug-Override der Waffenstufe (+0..+9), damit das +7/+9-Glitzern und der Schlag-Trail
        /// ohne M6c-Upgrade-Serverlogik an der eigenen Figur geprüft werden können.</summary>
        private void DrawWeaponUpgradeTestRow()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Waffe +", GUILayout.Width(60));
            if (GUILayout.Button("<", GUILayout.Width(24)) && _debugWeaponUpgradeLevel > 0)
            {
                _debugWeaponUpgradeLevel--;
                ApplyLocalWeaponVisual();
            }
            GUILayout.Label(_debugWeaponUpgradeLevel.ToString(), GUILayout.Width(70));
            if (GUILayout.Button(">", GUILayout.Width(24)) && _debugWeaponUpgradeLevel < ItemEntryJson.MaxUpgradeLevel)
            {
                _debugWeaponUpgradeLevel++;
                ApplyLocalWeaponVisual();
            }
            GUILayout.EndHorizontal();
        }

        private string DisplayNameFor(string slot, int id)
        {
            if (id <= 0) return "Keine";
            return _lookCatalog.TryGetEntry(slot, id, out LookEntryJson entry) ? entry.name : "#" + id;
        }

        private string DisplayHairColor(int id)
        {
            IReadOnlyList<string> colors = _lookCatalog.HairColors;
            if (id <= 0 || id > colors.Count) return "-";
            return colors[id - 1];
        }
#endif

        private void LateUpdate()
        {
            CharacterView localView;
            if (_world.LocalEntityId >= 0 && _views.TryGetValue(_world.LocalEntityId, out localView))
            {
                Vector3 target = localView.transform.position;
                Vector3 camPos = _camera.transform.position;
                _camera.transform.position = new Vector3(target.x, target.y, camPos.z);
            }
        }

        /// <summary>Blickrichtung folgt dem Mauszeiger, nicht der Laufrichtung. So kann man weglaufen und
        /// gleichzeitig nach hinten schlagen; der Server braucht dafür keine Änderung, weil Angriffe über
        /// Ziel-EntityIds laufen und nur die lokale Darstellung/der Schlagbogen die Richtung nutzt.</summary>
        private void UpdateMouseFacing()
        {
            if (Mouse.current == null || _world.LocalEntityId < 0) return;
            if (IsPointerOverDebugPanel()) return;

            Vector3 mouseWorld = ScreenToWorldPoint(Mouse.current.position.ReadValue());
            Vector3 localWorld = LocalPlayerWorldPosition;
            Vector2 direction = new Vector2(mouseWorld.x - localWorld.x, mouseWorld.y - localWorld.y);
            if (direction.sqrMagnitude < 0.0001f) return;

            _aimDirection = direction.normalized;
            _world.SetFacingOverride(_world.LocalEntityId,
                Mathf.RoundToInt(mouseWorld.x * 100f),
                Mathf.RoundToInt(mouseWorld.y * 100f));
        }

        /// <summary>WASD/Pfeiltasten: hält die Figur in die gewünschte Richtung. Der Client sendet in
        /// kurzen Abständen ein neues MoveTo-Ziel; die eigentliche Bewegung/Kollision bleibt serverautoritativ
        /// (ClientWorld interpoliert die eingehenden EntityMove-Pakete).</summary>
        private void HandleKeyboardMovement()
        {
            if (Keyboard.current == null || _world.LocalEntityId < 0) return;

            Vector2 direction = Vector2.zero;
            if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) direction.y += 1f;
            if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) direction.y -= 1f;
            if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) direction.x -= 1f;
            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) direction.x += 1f;

            if (direction.sqrMagnitude <= 0f)
            {
                if (_keyboardMoveSent)
                {
                    _keyboardMoveSent = false;
                    _netClient.SendStopMove();
                }
                return;
            }

            _keyboardMoveSent = true;
            if (Time.time < _nextMoveSendAt) return;
            _nextMoveSendAt = Time.time + 0.10f;
            direction.Normalize();

            Vector3 position = LocalPlayerWorldPosition;
            // Etwas weiter voraus als ein Server-Tick, damit ein einzelnes verzögertes MoveTo nicht sofort
            // zum Stehen kommt; der Server klemmt/kollidiert weiterhin autoritativ.
            Vector3 target = position + new Vector3(direction.x, direction.y, 0f) * 4f;
            int wireX = Mathf.RoundToInt(target.x * 100f);
            int wireY = Mathf.RoundToInt(target.y * 100f);
            _netClient.SendMoveTo(wireX, wireY);
        }

        /// <summary>Leertaste: ein Schlag; gedrückt halten schlägt automatisch im Takt weiter. Der lokale
        /// Kombo-Zähler (links/rechts/links) ist rein kosmetisch, der Server prüft jeden Treffer selbst.</summary>
        private void HandleKeyboardCombat()
        {
            if (Keyboard.current == null || _world.LocalEntityId < 0) return;

            bool space = Keyboard.current.spaceKey.isPressed;
            if (Keyboard.current.spaceKey.wasPressedThisFrame && !_attackHeld)
            {
                _attackHeld = true;
                _nextAutoAttackAt = Time.time;
            }
            if (!space)
            {
                _attackHeld = false;
                return;
            }

            if (!_attackHeld || Time.time < _nextAutoAttackAt) return;
            _nextAutoAttackAt = Time.time + 0.82f;

            int targetId = FindAutoAttackTarget();
            if (targetId <= 0)
            {
                // Auch ohne Ziel immer schlagen: Animation, Sound und Kombo laufen lokal weiter. Ein
                // AttackRequest an den Server wäre ohne Ziel nur ein garantiertes AttackFailed.
                PerformSwingOnly();
                return;
            }
            _lastAttackFailedMessage = "";
            PerformAttack(targetId);
        }

        private int FindAutoAttackTarget()
        {
            if (_world.LocalEntityId < 0 || !_world.Entities.TryGetValue(_world.LocalEntityId, out ClientEntity local))
                return -1;

            float maxDistance = _classCatalog != null ? _classCatalog.MeleeRangeWire / 100f + 0.7f : 2.2f;

            if (_targetEntityId >= 0
                && _world.Entities.TryGetValue(_targetEntityId, out ClientEntity selected)
                && selected.Kind == EntityKind.Monster
                && selected.CurrentHp != 0
                && Distance(local, selected) <= maxDistance)
            {
                Vector2 toSelected = new Vector2(
                    (float)((selected.X - local.X) / 100.0),
                    (float)((selected.Y - local.Y) / 100.0));
                if (toSelected.sqrMagnitude < 0.0001f
                    || Vector2.Dot(_aimDirection, toSelected.normalized) >= 0.15f)
                    return _targetEntityId;
            }

            // Auto-Target bevorzugt Ziele in Blickrichtung (Mauszeiger), damit der sichtbare Schlagbogen
            // und der tatsächlich getroffene Gegner zusammenpassen. Bei Maus nach hinten werden auch Ziele
            // hinter dem Charakter gefunden.
            int bestId = -1;
            float bestScore = float.MinValue;
            foreach (KeyValuePair<int, ClientEntity> kvp in _world.Entities)
            {
                ClientEntity entity = kvp.Value;
                if (entity.Kind != EntityKind.Monster || entity.CurrentHp == 0) continue;
                float distance = Distance(local, entity);
                if (distance > maxDistance) continue;

                Vector2 toTarget = new Vector2(
                    (float)((entity.X - local.X) / 100.0),
                    (float)((entity.Y - local.Y) / 100.0));
                float facing = toTarget.sqrMagnitude > 0.0001f
                    ? Vector2.Dot(_aimDirection, toTarget.normalized)
                    : 0f;
                if (facing < 0.15f) continue; // nicht in Blickrichtung

                float score = facing * 2f - distance * 0.15f;
                if (score <= bestScore) continue;
                bestScore = score;
                bestId = kvp.Key;
            }
            return bestId;
        }

        private static float Distance(ClientEntity a, ClientEntity b)
        {
            float dx = (float)((a.X - b.X) / 100.0);
            float dy = (float)((a.Y - b.Y) / 100.0);
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        private void PerformSwingOnly()
        {
            PlayAttackAnimation(_world.LocalEntityId, _comboIndex);
            _audio?.PlaySwing();
            SpawnMeleeArc();
            _comboIndex = (_comboIndex + 1) % 3;
        }

        private void SpawnMeleeArc()
        {
            if (_world.LocalEntityId < 0 || !_world.Entities.TryGetValue(_world.LocalEntityId, out ClientEntity local))
                return;

            float radius = _classCatalog != null ? _classCatalog.MeleeRangeWire / 100f : 1.5f;
            Vector3 origin = new Vector3((float)(local.X / 100.0), (float)(local.Y / 100.0), 0f);
            var arcGo = new GameObject("MeleeArc");
            var arc = arcGo.AddComponent<MeleeArcView>();
            arc.Initialize(origin, _aimDirection, radius, new Color(1f, 0.88f, 0.35f, 0.65f), 0.2f);
        }

        private void SpawnTargetHitbox(int targetId)
        {
            if (_unitRingSprite == null || !_world.Entities.TryGetValue(targetId, out ClientEntity target))
                return;

            var go = new GameObject("TargetHitbox");
            var ring = go.AddComponent<RadiusFlashView>();
            ring.Initialize(_unitRingSprite,
                new Vector3((float)(target.X / 100.0), (float)(target.Y / 100.0), 0f),
                1.15f, new Color(1f, 0.25f, 0.2f, 0.55f), 0.22f);
        }

        private void PerformAttack(int targetId)
        {
            if (targetId <= 0) return;
            _targetEntityId = targetId;
            _netClient.SendAttack(targetId);
            PlayAttackAnimation(_world.LocalEntityId, _comboIndex);
            _audio?.PlaySwing();
            SpawnMeleeArc();
            SpawnTargetHitbox(targetId);
            _comboIndex = (_comboIndex + 1) % 3;
        }

        /// <summary>Hebt das Boden-Item unter dem Mauszeiger hervor (nur Optik, Aufheben bleibt Linksklick).</summary>
        private void UpdateGroundItemHover()
        {
            if (Mouse.current == null || _groundItemViews.Count == 0) return;
            bool overUi = UiPointerHitTest != null && UiPointerHitTest(Mouse.current.position.ReadValue());
            Vector3 worldPos = ScreenToWorldPoint(Mouse.current.position.ReadValue());
            bool found = false;
            foreach (GroundItemView view in _groundItemViews.Values)
            {
                if (view == null) continue;
                bool hover = !overUi && !found && view.ContainsWorldPoint(worldPos);
                if (hover) found = true;
                view.SetHighlighted(hover);
            }
        }

        private void HandleClickInput()
        {
            if (Mouse.current == null) return;
            if (IsPointerOverDebugPanel()) return; // Klick auf einen Debug-Panel-Button soll die Figur nicht bewegen/angreifen lassen.
            if (UiPointerHitTest != null && UiPointerHitTest(Mouse.current.position.ReadValue())) return; // Klick auf HUD/Fenster.

            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                Vector3 worldPos = ScreenToWorldPoint(Mouse.current.position.ReadValue());
                foreach (KeyValuePair<int, GroundItemView> kvp in _groundItemViews)
                {
                    if (kvp.Value != null && kvp.Value.ContainsWorldPoint(worldPos))
                    {
                        _netClient.SendPickupItem(kvp.Key);
                        return;
                    }
                }

                int wireX = Mathf.RoundToInt(worldPos.x * 100f);
                int wireY = Mathf.RoundToInt(worldPos.y * 100f);
                _netClient.SendMoveTo(wireX, wireY); // Klemmung an Kartengrenzen macht der Server.
            }

            if (Mouse.current.rightButton.wasPressedThisFrame)
                HandleRightClickAttack();
        }

        /// <summary>Wandelt die Input-System-Mausposition (Ursprung unten links) in GUI-Koordinaten (Ursprung
        /// oben links) um und prüft sie gegen das zuletzt gezeichnete Debug-Panel-Rechteck - nur im Editor/
        /// Development-Build vorhanden (das Panel existiert nur dort, siehe DrawDebugTestPanel), in Release-
        /// Builds also immer false.</summary>
        private bool IsPointerOverDebugPanel()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Vector2 mousePos = Mouse.current.position.ReadValue();
            Vector2 guiPos = new Vector2(mousePos.x, Screen.height - mousePos.y);
            return _debugPanelRect.Contains(guiPos);
#else
            return false;
#endif
        }

        /// <summary>M4a: Rechtsklick auf ein sichtbares Monster sendet AttackRequestPacket - Bounds-Check auf
        /// der MonsterView reicht (siehe MonsterView.ContainsWorldPoint), kein echtes Collider-System nötig.
        /// Seit M5b zusätzlich: Rechtsklick auf einen NPC (Kind=Npc, dieselbe CharacterView wie Spieler, siehe
        /// OnEntitySpawned) öffnet dessen Shop (OpenShopRequestPacket). Andere Spieler bleiben absichtlich
        /// ungeprüft - PvP wird vom Server ohnehin abgelehnt, siehe CLAUDE.md Abschnitt "Kampf".</summary>
        private void HandleRightClickAttack()
        {
            Vector3 worldPos = ScreenToWorldPoint(Mouse.current.position.ReadValue());

            foreach (KeyValuePair<int, MonsterView> kvp in _monsterViews)
            {
                if (kvp.Value == null || !kvp.Value.ContainsWorldPoint(worldPos)) continue;
                PerformAttack(kvp.Key);
                return;
            }

            foreach (KeyValuePair<int, CharacterView> kvp in _views)
            {
                if (kvp.Value == null || !kvp.Value.ContainsWorldPoint(worldPos)) continue;
                if (!_world.Entities.TryGetValue(kvp.Key, out ClientEntity entity)) continue;
                if (entity.Kind == EntityKind.Player)
                {
                    if (kvp.Key != _world.LocalEntityId) PlayerContextRequested?.Invoke(kvp.Key, entity.Name);
                    return;
                }
                if (entity.Kind != EntityKind.Npc) continue;

                _shopOpenNpcEntityId = kvp.Key;
                _shopItems.Clear();
                _netClient.SendOpenShop(kvp.Key);
                return;
            }
        }

        private Vector3 ScreenToWorldPoint(Vector2 screenPos)
        {
            // z = Abstand Kamera -> Weltebene z=0 (Kamera steht bei negativem z, siehe Bootstrap).
            float distanceToWorldPlane = -_camera.transform.position.z;
            return _camera.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, distanceToWorldPlane));
        }

        // ---- Netzwerk-Events ----

        private void OnNetDisconnected(string reason)
        {
            CleanupEntities();
            _inGame = false;
            _targetEntityId = -1;
            _lastAttackFailedMessage = "";
            _yangTotal = 0;
            _isDead = false;
            _inventorySlots.Clear();
            _equippedArmorItemId = 0;
            _equippedHelmetItemId = 0;
            _equippedWeaponItemId = 0;
            _equippedShieldItemId = 0;
            _equippedArmorUpgradeLevel = 0;
            _equippedHelmetUpgradeLevel = 0;
            _equippedWeaponUpgradeLevel = 0;
            _equippedShieldUpgradeLevel = 0;
            _lastInventoryActionFailedMessage = "";
            _lastUpgradeMessage = "";
            _lastUpgradeItemName = "Ausrüstung";
            for (int i = 0; i < _useProtectionScroll.Length; i++) _useProtectionScroll[i] = false;
            _shopOpenNpcEntityId = -1;
            _shopItems.Clear();
            _lastShopActionFailedMessage = "";
            _lastLevelUpMessage = "";
            _lastDodgeMessage = "";
            _skillCooldownReadyAtTime.Clear();
            _activeBuffRemainingSeconds.Clear();
            _lastSkillFailedMessage = "";
        }

        private void OnPacketReceived(IPacket packet)
        {
            JoinAcceptedPacket accepted = packet as JoinAcceptedPacket;
            if (accepted != null)
            {
                _world.Reset();
                _world.HandleJoinAccepted(accepted.EntityId, accepted.X, accepted.Y, _pendingLoginName, accepted.Appearance,
                    accepted.MapId, accepted.MapName, accepted.MapWidth, accepted.MapHeight);
                RebuildMapVisuals();
                _statusMessage = "Verbunden als " + _pendingLoginName + " (" + _world.MapName + ")";
                _inGame = true;
                return;
            }

            MapChangedPacket mapChanged = packet as MapChangedPacket;
            if (mapChanged != null)
            {
                // Reihenfolge: Views der alten Karte zerstören, Entity-Zustand leeren (behält Level/XP/
                // Inventar), lokale Figur an der Zielposition neu anlegen, Boden/Portale der neuen Karte
                // bauen. Fremde Entities folgen über EntitySpawn (AOI-Diff des Servers).
                CleanupMapViews();
                _world.HandleMapChanged(mapChanged.EntityId, mapChanged.X, mapChanged.Y,
                    mapChanged.MapId, mapChanged.MapName, mapChanged.MapWidth, mapChanged.MapHeight);
                RebuildMapVisuals();
                _statusMessage = "Karte: " + _world.MapName;
                return;
            }

            // JoinRejected: keine eigene Behandlung hier nötig - LoginController zeigt die Fehlermeldung im
            // UI, GameClient war zu diesem Zeitpunkt ohnehin nie InGame.

            EntitySpawnPacket spawn = packet as EntitySpawnPacket;
            if (spawn != null)
            {
                Debug.Log("EntitySpawn: Id=" + spawn.EntityId + " Kind=" + spawn.Kind + " Name=" + spawn.Name + " Pos=(" + (spawn.X / 100.0) + "," + (spawn.Y / 100.0) + ")");
                _world.HandleEntitySpawn(spawn.EntityId, spawn.Name, spawn.Kind, spawn.TypeId, spawn.X, spawn.Y, spawn.Appearance,
                    spawn.ItemUpgradeLevel, spawn.NpcService);
                return;
            }

            EntityDespawnPacket despawn = packet as EntityDespawnPacket;
            if (despawn != null)
            {
                ClientEntity existing;
                if (_world.Entities.TryGetValue(despawn.EntityId, out existing))
                    Debug.Log("EntityDespawn: Id=" + despawn.EntityId + " Name=" + existing.Name + " Pos=(" + (existing.X / 100.0) + "," + (existing.Y / 100.0) + ")");
                else
                    Debug.Log("EntityDespawn: Id=" + despawn.EntityId + " (unbekannt)");
                _world.HandleEntityDespawn(despawn.EntityId);
                return;
            }

            EntityMovePacket move = packet as EntityMovePacket;
            if (move != null)
            {
                _world.HandleEntityMove(move.EntityId, move.StartX, move.StartY, move.TargetX, move.TargetY, move.Speed, Time.timeAsDouble);
                return;
            }

            EntityStopPacket stop = packet as EntityStopPacket;
            if (stop != null)
            {
                _world.HandleEntityStop(stop.EntityId, stop.X, stop.Y);
                // Eine dead player kann sich nicht bewegen - jeder Stop nach dem eigenen Tod ist also der
                // Respawn (Kartenmitte, siehe World.Tick/GameServer). Kein eigenes "Respawned"-Paket nötig,
                // siehe CLAUDE.md Abschnitt "Kampf".
                if (_isDead && stop.EntityId == _world.LocalEntityId) _isDead = false;
                return;
            }

            EntityDamagedPacket damaged = packet as EntityDamagedPacket;
            if (damaged != null)
            {
                _world.HandleEntityDamaged(damaged.TargetEntityId, damaged.TargetHpAfter, damaged.TargetMaxHp);
                SpawnHitLine(damaged.AttackerEntityId, damaged.TargetEntityId, hit: true);
                PlayAttackAnimation(damaged.AttackerEntityId);
                if (damaged.TargetEntityId == _world.LocalEntityId) _audio?.PlayHurt();
                else _audio?.PlayHit();
                return;
            }

            EntityHpChangedPacket hpChanged = packet as EntityHpChangedPacket;
            if (hpChanged != null)
            {
                _world.HandleEntityHpChanged(hpChanged.EntityId, hpChanged.CurrentHp, hpChanged.MaxHp);
                return;
            }

            EntityDiedPacket died = packet as EntityDiedPacket;
            if (died != null)
            {
                _world.HandleEntityDied(died.EntityId);
                if (died.EntityId == _world.LocalEntityId) _isDead = true;
                _audio?.PlayKill();
                return;
            }

            AttackFailedPacket attackFailed = packet as AttackFailedPacket;
            if (attackFailed != null)
            {
                _lastAttackFailedMessage = AttackFailedReasonText(attackFailed.Reason);
                return;
            }

            YangChangedPacket yangChanged = packet as YangChangedPacket;
            if (yangChanged != null)
            {
                ApplyYang(yangChanged.NewTotal);
                return;
            }

            InventorySyncPacket inventorySync = packet as InventorySyncPacket;
            if (inventorySync != null)
            {
                int previousInventoryCount = _inventorySlots.Count;
                _inventorySlots.Clear();
                _inventorySlots.AddRange(inventorySync.Slots);
                _equippedArmorItemId = inventorySync.EquippedArmorItemId;
                _equippedHelmetItemId = inventorySync.EquippedHelmetItemId;
                _equippedWeaponItemId = inventorySync.EquippedWeaponItemId;
                _equippedShieldItemId = inventorySync.EquippedShieldItemId;
                _equippedArmorUpgradeLevel = inventorySync.EquippedArmorUpgradeLevel;
                _equippedHelmetUpgradeLevel = inventorySync.EquippedHelmetUpgradeLevel;
                _equippedWeaponUpgradeLevel = inventorySync.EquippedWeaponUpgradeLevel;
                _equippedShieldUpgradeLevel = inventorySync.EquippedShieldUpgradeLevel;
                if (_inventorySlots.Count > previousInventoryCount) _audio?.PlayLoot();
                ApplyLocalWeaponVisual();
                return;
            }

            InventoryActionFailedPacket inventoryFailed = packet as InventoryActionFailedPacket;
            if (inventoryFailed != null)
            {
                _lastInventoryActionFailedMessage = InventoryActionFailedReasonText(inventoryFailed.Reason);
                return;
            }

            UpgradeItemResultPacket upgradeResult = packet as UpgradeItemResultPacket;
            if (upgradeResult != null)
            {
                _lastInventoryActionFailedMessage = "";
                if (upgradeResult.Success)
                    _lastUpgradeMessage = _lastUpgradeItemName + " ist jetzt +" + upgradeResult.NewUpgradeLevel + "!";
                else if (upgradeResult.Destroyed)
                    _lastUpgradeMessage = "Aufwertung fehlgeschlagen - " + _lastUpgradeItemName + " wurde zerstört!";
                else
                    _lastUpgradeMessage = "Aufwertung fehlgeschlagen, durch die Bewahrungsrolle gerettet.";
                return;
            }

            UpgradeActionFailedPacket upgradeFailed = packet as UpgradeActionFailedPacket;
            if (upgradeFailed != null)
            {
                _lastUpgradeMessage = UpgradeActionFailedReasonText(upgradeFailed.Reason);
                return;
            }

            AppearanceChangedPacket appearanceChanged = packet as AppearanceChangedPacket;
            if (appearanceChanged != null)
            {
                if (_world.Entities.TryGetValue(appearanceChanged.EntityId, out ClientEntity entity))
                    entity.Appearance = appearanceChanged.Appearance;
                if (_views.TryGetValue(appearanceChanged.EntityId, out CharacterView view))
                    view.SetAppearance(appearanceChanged.Appearance);
                return;
            }

            ShopContentPacket shopContent = packet as ShopContentPacket;
            if (shopContent != null)
            {
                if (shopContent.NpcEntityId == _shopOpenNpcEntityId)
                {
                    _shopItems.Clear();
                    _shopItems.AddRange(shopContent.Items);
                }
                return;
            }

            ShopActionFailedPacket shopFailed = packet as ShopActionFailedPacket;
            if (shopFailed != null)
            {
                _lastShopActionFailedMessage = ShopActionFailedReasonText(shopFailed.Reason);
                return;
            }

            CharacterStatsPacket characterStats = packet as CharacterStatsPacket;
            if (characterStats != null)
            {
                // "Level Up!" nur bei einer tatsächlichen Steigerung erkannt (aufsteigender Level) - der
                // Sentinel-Wert 0 (ClientWorld.Reset, "noch kein Paket erhalten") verhindert eine
                // Fehlmeldung beim allerersten CharacterStatsPacket nach dem Join.
                int previousLevel = _world.CharacterStats.Level;
                _world.ApplyCharacterStats(characterStats);
                if (previousLevel > 0 && characterStats.Level > previousLevel)
                {
                    _lastLevelUpMessage = "Level Up! Level " + characterStats.Level;
                    _audio?.PlayLevelUp();
                }
                return;
            }

            EntityDodgedPacket dodged = packet as EntityDodgedPacket;
            if (dodged != null)
            {
                if (dodged.TargetEntityId == _world.LocalEntityId) _lastDodgeMessage = "Ausgewichen!";
                else if (dodged.AttackerEntityId == _world.LocalEntityId) _lastDodgeMessage = "Verfehlt!";
                SpawnHitLine(dodged.AttackerEntityId, dodged.TargetEntityId, hit: false);
                PlayAttackAnimation(dodged.AttackerEntityId);
                return;
            }

            UseSkillFailedPacket skillFailed = packet as UseSkillFailedPacket;
            if (skillFailed != null)
            {
                _lastSkillFailedMessage = UseSkillFailedReasonText(skillFailed.Reason);
                return;
            }

            ActiveBuffsChangedPacket activeBuffs = packet as ActiveBuffsChangedPacket;
            if (activeBuffs != null)
            {
                _activeBuffRemainingSeconds.Clear();
                foreach ((BuffType type, long remainingMs) in activeBuffs.Buffs)
                    _activeBuffRemainingSeconds[type] = remainingMs / 1000f;
                SyncBuffRings(activeBuffs.Buffs);
                return;
            }

            ChatMessagePacket chat = packet as ChatMessagePacket;
            if (chat != null)
            {
                ChatMessageReceived?.Invoke(chat);
                return;
            }

            PartyInviteReceivedPacket partyInvite = packet as PartyInviteReceivedPacket;
            if (partyInvite != null)
            {
                PartyInviteReceived?.Invoke(partyInvite.InviterName);
                return;
            }

            PartyChangedPacket party = packet as PartyChangedPacket;
            if (party != null)
            {
                PartyChangedReceived?.Invoke(party);
                return;
            }
        }

        /// <summary>Kampf-Visualisierung Abschnitt 6: voller Resync wie das Paket selbst (kein Delta) - legt
        /// fehlende Ringe fuer noch aktive Buffs an, entfernt Ringe fuer nicht mehr gelistete Buffs. "Ring
        /// verschwindet exakt wenn ActiveBuffsChanged den Buff nicht mehr listet" (Aufgabenstellung) ist damit
        /// woertlich erfuellt - kein eigener Timer, ausschliesslich paketgetrieben. ActiveBuffsChangedPacket
        /// geht laut Protokoll nur an den Besitzer (siehe CLAUDE.md "Skills") - Buff-Ringe sind deshalb
        /// strukturell auf den lokalen Spieler beschraenkt, keine Protokolländerung noetig/moeglich.</summary>
        private void SyncBuffRings(List<(BuffType Type, long RemainingMs)> activeBuffs)
        {
            var stillActive = new HashSet<BuffType>();
            foreach ((BuffType type, long _) in activeBuffs) stillActive.Add(type);

            List<BuffType> toRemove = null;
            foreach (BuffType existing in _buffRingViews.Keys)
            {
                if (stillActive.Contains(existing)) continue;
                toRemove ??= new List<BuffType>();
                toRemove.Add(existing);
            }
            if (toRemove != null)
            {
                foreach (BuffType type in toRemove)
                {
                    Destroy(_buffRingViews[type].gameObject);
                    _buffRingViews.Remove(type);
                }
            }

            foreach (BuffType type in stillActive)
            {
                if (_buffRingViews.ContainsKey(type)) continue;
                if (_world.LocalEntityId < 0 || !_world.Entities.TryGetValue(_world.LocalEntityId, out ClientEntity local)) continue;

                (Color color, float diameter) = type == BuffType.AttackBuff
                    ? (KampfrauschRingColor, KampfrauschRingDiameter)
                    : (HastRingColor, HastRingDiameter);

                var go = new GameObject("BuffRing_" + type);
                var ring = go.AddComponent<RangeRingView>();
                ring.Initialize(_unitRingSprite, diameter, color, BuffRingSortingOrder);
                ring.SetWorldPosition(local.X / 100.0, local.Y / 100.0);
                _buffRingViews[type] = ring;
            }
        }

        /// <summary>Einziger Schreibpfad für den bekannten Yang-Stand (Bugfix - vorher wurde YangChangedPacket
        /// inline verarbeitet, kein zweiter Codepfad aktuell vorhanden, aber wie bei ClientWorld.ApplyHp bewusst
        /// als eigene Methode, damit das so bleibt).</summary>
        private void ApplyYang(long newTotal)
        {
            _yangTotal = newTotal;
        }

        // ---- Kampf-Visualisierung (rein clientseitig/kosmetisch, siehe CLAUDE.md Abschnitt
        // "Kampf-Visualisierung") ----

        private const int RangeRingSortingOrder = -40;
        private static readonly Color RangeRingColor = new Color(1f, 1f, 1f, 0.25f);

        private static readonly Color HitLineColor = new Color(0.9f, 0.15f, 0.15f, 0.9f);
        private static readonly Color DodgeLineColor = new Color(0.4f, 0.6f, 0.9f, 0.9f);

        // Abschnitt 6: Kampfrausch (AttackBuff) rot-orange mit kleinerem Durchmesser, Hast (AttackSpeedBuff)
        // gelb mit groesserem Durchmesser - konzentrisch, ueberschneiden sich dadurch nie (keine
        // gegenseitige Verdeckung, auch wenn beide gleichzeitig aktiv sind).
        private const int BuffRingSortingOrder = -30;
        private static readonly Color KampfrauschRingColor = new Color(0.9f, 0.35f, 0.1f, 0.6f);
        private static readonly Color HastRingColor = new Color(0.95f, 0.85f, 0.1f, 0.6f);
        private const float KampfrauschRingDiameter = 2.6f;
        private const float HastRingDiameter = 3.4f;

        /// <summary>Abschnitt 4: nur wenn BEIDE beteiligten Entities aktuell bekannte Positionen haben (auch
        /// wenn keine davon der lokale Spieler ist, z. B. Monster gegen anderen Spieler) - sonst kein Effekt,
        /// keine Fehlermeldung (analog zu ClientWorld.HandleEntityDamaged: unbekannte Id ist kein Fehler, nur
        /// z. B. bereits despawnt). Treffer rot, Ausweichen blau-grau.</summary>
        private void SpawnHitLine(int attackerEntityId, int targetEntityId, bool hit)
        {
            if (!_world.Entities.TryGetValue(attackerEntityId, out ClientEntity attacker)) return;
            if (!_world.Entities.TryGetValue(targetEntityId, out ClientEntity target)) return;

            Vector3 from = new Vector3((float)(attacker.X / 100.0), (float)(attacker.Y / 100.0), 0f);
            Vector3 to = new Vector3((float)(target.X / 100.0), (float)(target.Y / 100.0), 0f);

            var go = new GameObject("HitLine");
            var line = go.AddComponent<HitLineView>();
            line.Initialize(from, to, hit ? HitLineColor : DodgeLineColor, durationSeconds: 0.2f);

            // Abschnitt 5 (optionale Erweiterung "ohne grossen Aufwand"): auch fuer fremde Angreifer
            // (Monster gegen Spieler, anderer Spieler gegen Monster) reaktiv zum Ziel drehen - beide
            // Positionen sind hier ohnehin schon bekannt. Fuer den lokalen Spieler redundant zur
            // optimistischen Drehung beim Senden (HandleRightClickAttack/UseSkillHotkey), aber harmlos.
            _world.SetFacingOverride(attackerEntityId, target.IntX, target.IntY);
        }

        private static string InventoryActionFailedReasonText(InventoryActionFailedReason reason)
        {
            switch (reason)
            {
                case InventoryActionFailedReason.InvalidSlot: return "Ungültiger Inventarplatz.";
                case InventoryActionFailedReason.EmptySlot: return "Inventarplatz ist leer.";
                case InventoryActionFailedReason.WrongItemType: return "Dieser Gegenstand kann nicht angelegt werden.";
                case InventoryActionFailedReason.NotConsumable: return "Dieser Gegenstand kann nicht benutzt werden.";
                case InventoryActionFailedReason.InventoryFull: return "Inventar ist voll.";
                case InventoryActionFailedReason.TargetAtFullHp: return "Leben bereits voll.";
                case InventoryActionFailedReason.GroundItemNotFound: return "Der Gegenstand liegt nicht mehr da.";
                case InventoryActionFailedReason.PickupOutOfRange: return "Zu weit entfernt zum Aufheben.";
                case InventoryActionFailedReason.PickupNotOwner: return "Dieser Gegenstand gehört noch einem anderen Spieler.";
                default: return "Aktion fehlgeschlagen.";
            }
        }

        private static string UpgradeActionFailedReasonText(UpgradeActionFailedReason reason)
        {
            switch (reason)
            {
                case UpgradeActionFailedReason.SlotEmpty: return "Dieser Ausrüstungsplatz ist leer.";
                case UpgradeActionFailedReason.AlreadyMaxLevel: return "Maximale Aufwertungsstufe (+9) erreicht.";
                case UpgradeActionFailedReason.NoMaterial: return "Kein Schleifstein im Inventar.";
                case UpgradeActionFailedReason.NoProtectionScroll: return "Keine Bewahrungsrolle im Inventar.";
                case UpgradeActionFailedReason.NoBlacksmith: return "Aufwerten geht nur beim Schmied.";
                default: return "Aufwertung nicht möglich.";
            }
        }

        private static string AttackFailedReasonText(AttackFailedReason reason)
        {
            switch (reason)
            {
                case AttackFailedReason.TargetNotFound: return "Ziel nicht gefunden.";
                case AttackFailedReason.TargetAlreadyDead: return "Ziel ist bereits tot.";
                case AttackFailedReason.OutOfRange: return "Ziel außer Reichweite.";
                case AttackFailedReason.OnCooldown: return "Angriff noch im Abklingen.";
                case AttackFailedReason.TargetIsPlayer: return "Spieler können nicht angegriffen werden.";
                case AttackFailedReason.TargetNotAttackable: return "Dieses Ziel kann nicht angegriffen werden.";
                default: return "Angriff fehlgeschlagen.";
            }
        }

        private static string UseSkillFailedReasonText(UseSkillFailedReason reason)
        {
            switch (reason)
            {
                case UseSkillFailedReason.NotLearned: return "Skill nicht erlernt.";
                case UseSkillFailedReason.NotEnoughSp: return "Nicht genug SP.";
                case UseSkillFailedReason.OnCooldown: return "Skill noch im Abklingen.";
                case UseSkillFailedReason.TargetRequired: return "Ziel erforderlich.";
                case UseSkillFailedReason.TargetNotFound: return "Ziel nicht gefunden.";
                case UseSkillFailedReason.TargetAlreadyDead: return "Ziel ist bereits tot.";
                case UseSkillFailedReason.OutOfRange: return "Ziel außer Reichweite.";
                case UseSkillFailedReason.TargetIsPlayer: return "Spieler können nicht angegriffen werden.";
                case UseSkillFailedReason.TargetNotAttackable: return "Dieses Ziel kann nicht angegriffen werden.";
                default: return "Skill fehlgeschlagen.";
            }
        }

        private static string BuffDisplayName(BuffType type)
        {
            switch (type)
            {
                case BuffType.AttackBuff: return "Kampfrausch";
                case BuffType.AttackSpeedBuff: return "Hast";
                default: return type.ToString();
            }
        }

        private static string ShopActionFailedReasonText(ShopActionFailedReason reason)
        {
            switch (reason)
            {
                case ShopActionFailedReason.NpcNotFound: return "Händler nicht gefunden.";
                case ShopActionFailedReason.NotAShop: return "Dieser NPC hat kein Sortiment.";
                case ShopActionFailedReason.OutOfRange: return "Zu weit vom Händler entfernt.";
                case ShopActionFailedReason.ItemNotInStock: return "Gegenstand nicht im Sortiment.";
                case ShopActionFailedReason.NotEnoughYang: return "Nicht genug Yang.";
                case ShopActionFailedReason.InventoryFull: return "Inventar ist voll.";
                case ShopActionFailedReason.InvalidSlot: return "Ungültiger Inventarplatz.";
                case ShopActionFailedReason.EmptySlot: return "Inventarplatz ist leer.";
                case ShopActionFailedReason.ItemNotSellable: return "Dieser Gegenstand kann nicht verkauft werden.";
                case ShopActionFailedReason.YangCapReached: return "Yang-Höchststand erreicht.";
                default: return "Shop-Aktion fehlgeschlagen.";
            }
        }

        // ---- Views ----

        private void OnEntitySpawned(ClientEntity entity)
        {
            if (entity.Kind == EntityKind.GroundItem)
            {
                var groundGo = new GameObject("GroundItem_" + entity.Id + "_" + entity.Name);
                var groundView = groundGo.AddComponent<GroundItemView>();
                Texture2D icon = _iconCatalog != null ? _iconCatalog.GetTextureForItem(entity.TypeId, _itemCatalog) : null;
                Sprite iconSprite = icon != null
                    ? Sprite.Create(icon, new Rect(0, 0, icon.width, icon.height), new Vector2(0.5f, 0.5f), 32f)
                    : _squareSprite;
                string groundName = _itemCatalog != null && _itemCatalog.TryGet(entity.TypeId, out ItemEntryJson groundItem)
                    ? groundItem.name
                    : entity.Name;
                groundView.Initialize(iconSprite, groundName, entity.ItemUpgradeLevel);
                groundView.SetWorldPosition(entity.X / 100.0, entity.Y / 100.0);
                _groundItemViews[entity.Id] = groundView;
                if (_assetLabActive) HideForAssetLab(groundGo);
                return;
            }

            if (entity.Kind == EntityKind.Monster)
            {
                var monsterGo = new GameObject("Monster_" + entity.Id + "_" + entity.Name);
                var monsterView = monsterGo.AddComponent<MonsterView>();
                string artKey = MonsterArt.ResourceForMob(entity.TypeId);
                Sprite[] frames = artKey != null ? MonsterArt.LoadFrames(artKey) : null;
                if (frames != null)
                    monsterView.Initialize(frames, MonsterColorFor(entity.TypeId), MonsterScaleFor(entity.TypeId));
                else
                    monsterView.Initialize(MonsterShapeFor(entity.TypeId), MonsterColorFor(entity.TypeId), MonsterScaleFor(entity.TypeId));
                monsterView.SetWorldPosition(entity.X / 100.0, entity.Y / 100.0);
                _monsterViews[entity.Id] = monsterView;
                if (_assetLabActive) HideForAssetLab(monsterGo);
                return;
            }

            var go = new GameObject("Entity_" + entity.Id + "_" + entity.Name);
            var view = go.AddComponent<CharacterView>();
            view.Initialize(entity.Id, entity.Name, _spriteProvider, _lookCatalog);
            view.SetAppearance(entity.Appearance);
            view.SetFacing(entity.Facing);
            view.SetWorldPosition(entity.X / 100.0, entity.Y / 100.0);
            _views[entity.Id] = view;

            var animator = go.AddComponent<CharacterAnimator>();
            animator.Initialize(view);
            _animators[entity.Id] = animator;

            if (entity.Id == _world.LocalEntityId)
            {
                var markerGo = new GameObject("LocalPlayerMarker");
                markerGo.transform.SetParent(go.transform, false);
                LocalPlayerMarker marker = markerGo.AddComponent<LocalPlayerMarker>();
                marker.Initialize(_localMarkerSprite);

                // Kein Reichweitenring mehr im regulären Spiel - der wirkte wie ein Debug-Overlay. Der
                // Ring bleibt nur als internes Kampf-Visual (RadiusFlash/Buff-Ringe) verfügbar.
                ApplyLocalWeaponVisual();
            }

            if (_assetLabActive)
            {
                HideForAssetLab(go);
                if (entity.Id == _world.LocalEntityId && _rangeRingView != null)
                    HideForAssetLab(_rangeRingView.gameObject);
            }
        }

        /// <summary>Waffen-Rendering aus items.json: lädt das statische PNG (falls vorhanden) und übergibt
        /// Pivot-Korrektur plus Aufwertungsstufe an CharacterView. Ohne Sprite-Key/Asset bleibt der
        /// Appearance-Platzhalter sichtbar. Wird nach jedem InventorySync aufgerufen, weil der Server die
        /// ausgerüstete Waffen-ItemId dort mitschickt.</summary>
        private void ApplyLocalWeaponVisual()
        {
            if (_world.LocalEntityId < 0 || !_views.TryGetValue(_world.LocalEntityId, out CharacterView view)) return;

            if (_equippedWeaponItemId == 0 || _itemCatalog == null || !_itemCatalog.TryGet(_equippedWeaponItemId, out ItemEntryJson entry))
            {
                view.ClearWeapon();
                return;
            }

            Sprite weaponSprite = _spriteProvider != null ? _spriteProvider.LoadWeaponSprite(entry.spriteKey) : null;
            Vector2 pivotOffset = entry.pivotOffset != null
                ? new Vector2(entry.pivotOffset.x, entry.pivotOffset.y)
                : Vector2.zero;

            int upgradeLevel = _equippedWeaponUpgradeLevel;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            upgradeLevel = _debugWeaponUpgradeLevel;
#endif
            view.SetWeapon(weaponSprite, pivotOffset, upgradeLevel);
        }

        private void PlayAttackAnimation(int entityId, int comboIndex = 0)
        {
            if (entityId <= 0) return;
            if (_animators.TryGetValue(entityId, out CharacterAnimator animator) && animator != null)
                animator.PlayAttack(comboIndex);
        }

        public bool HasLocalPlayer => _world.LocalEntityId >= 0 && _world.Entities.ContainsKey(_world.LocalEntityId);

        // ---- HUD-/Chat-Daten (M7): nur lesende Sicht auf den bestehenden Client-Zustand ----
        public ClientWorld World => _world;
        public bool IsInGame => _inGame;
        public bool IsAssetLabActive => _assetLabActive;
        public int TargetEntityId => _targetEntityId;
        public long YangTotal => _yangTotal;
        public bool IsDead => _isDead;

        public string CurrentHudMessage
        {
            get
            {
                if (_isDead) return "Du bist gestorben, Respawn in Kürze ...";
                if (!string.IsNullOrEmpty(_lastLevelUpMessage)) return _lastLevelUpMessage;
                if (!string.IsNullOrEmpty(_lastDodgeMessage)) return _lastDodgeMessage;
                if (!string.IsNullOrEmpty(_lastAttackFailedMessage)) return _lastAttackFailedMessage;
                if (!string.IsNullOrEmpty(_lastSkillFailedMessage)) return _lastSkillFailedMessage;
                if (!string.IsNullOrEmpty(_lastUpgradeMessage)) return _lastUpgradeMessage;
                if (!string.IsNullOrEmpty(_lastInventoryActionFailedMessage)) return _lastInventoryActionFailedMessage;
                if (!string.IsNullOrEmpty(_lastShopActionFailedMessage)) return _lastShopActionFailedMessage;
                return "";
            }
        }

        public string SkillDisplayName(int skillId)
        {
            return _skillCatalog != null ? _skillCatalog.DisplayName(skillId) : "Skill " + skillId;
        }

        public float SkillCooldownRemaining(int skillId)
        {
            if (!_skillCooldownReadyAtTime.TryGetValue(skillId, out float readyAt)) return 0f;
            return Mathf.Max(0f, readyAt - Time.time);
        }

        public IReadOnlyDictionary<BuffType, float> ActiveBuffSeconds => _activeBuffRemainingSeconds;

        public void SetChatInputFocused(bool focused)
        {
            if (_chatInputFocused && !focused) _chatClosedAtFrame = Time.frameCount;
            _chatInputFocused = focused;
            if (focused && _keyboardMoveSent)
            {
                _netClient?.SendStopMove();
                _keyboardMoveSent = false;
            }
        }

        public bool IsChatInputFocused => _chatInputFocused;

        // ---- Spiel-UI (Inventar-/Charakter-/Shop-Fenster, Schnellleiste): lesende Sicht auf den lokalen
        // Cache plus dünne Absichts-Methoden. Der Server bleibt für jede Aktion die einzige Autorität. ----

        /// <summary>Vom HUD gesetzt: true, wenn die Bildschirmposition (Input-System-Koordinaten, Ursprung
        /// unten links) über einem klickbaren UI-Element liegt - dann bewegt/greift ein Klick nicht.</summary>
        public System.Func<Vector2, bool> UiPointerHitTest { get; set; }

        /// <summary>Wahr im Frame, in dem das Chatfeld geschlossen wurde (und im Folgeframe) - verhindert,
        /// dass dieselbe Escape-/Enter-Taste danach noch das Systemmenü bzw. erneut den Chat öffnet.</summary>
        public bool ChatClosedRecently => Time.frameCount - _chatClosedAtFrame <= 1;

        public IReadOnlyList<(int SlotIndex, int ItemId, int UpgradeLevel)> InventorySlots => _inventorySlots;
        public ItemCatalogClient ItemCatalog => _itemCatalog;

        public int EquippedItemId(EquipmentSlot slot)
        {
            switch (slot)
            {
                case EquipmentSlot.Armor: return _equippedArmorItemId;
                case EquipmentSlot.Helmet: return _equippedHelmetItemId;
                case EquipmentSlot.Weapon: return _equippedWeaponItemId;
                case EquipmentSlot.Shield: return _equippedShieldItemId;
                default: return 0;
            }
        }

        public int EquippedUpgradeLevel(EquipmentSlot slot)
        {
            switch (slot)
            {
                case EquipmentSlot.Armor: return _equippedArmorUpgradeLevel;
                case EquipmentSlot.Helmet: return _equippedHelmetUpgradeLevel;
                case EquipmentSlot.Weapon: return _equippedWeaponUpgradeLevel;
                case EquipmentSlot.Shield: return _equippedShieldUpgradeLevel;
                default: return 0;
            }
        }

        public Texture2D GetItemIcon(int itemId)
        {
            if (itemId == 0 || _iconCatalog == null || _itemCatalog == null) return null;
            return _iconCatalog.GetTextureForItem(itemId, _itemCatalog);
        }

        public bool HasProtectionScroll => _itemCatalog != null && HasProtectionScrollInInventory();

        /// <summary>Kontextaktion eines Inventarplatzes (benutzen/anlegen) - dieselbe Logik wie im Debug-Panel.</summary>
        public void UseInventorySlot(int slotIndex)
        {
            if (_itemCatalog == null) return;
            HandleInventorySlotClick(slotIndex, FindItemInSlot(slotIndex));
        }

        public void UnequipItem(EquipmentSlot slot)
        {
            if (EquippedItemId(slot) == 0) return;
            _netClient?.SendUnequipItem(slot);
        }

        public void UpgradeEquippedItem(EquipmentSlot slot, bool useProtectionScroll)
        {
            int itemId = EquippedItemId(slot);
            if (itemId == 0) return;
            _lastUpgradeItemName = _itemCatalog != null ? _itemCatalog.DisplayName(itemId) : "Ausrüstung";
            _netClient?.SendUpgradeItem(slot, useProtectionScroll);
        }

        /// <summary>Benutzt den ersten Verbrauchsgegenstand (Heiltrank) im Inventar - Schnellleisten-Taste 5.</summary>
        public void UseFirstConsumable()
        {
            int slotIndex = FindFirstConsumableSlot(out _);
            if (slotIndex >= 0) _netClient?.SendUseConsumable(slotIndex);
        }

        /// <summary>Erster Inventarplatz mit einem Consumable (-1 = keiner) plus Gesamtanzahl solcher Plätze.</summary>
        public int FindFirstConsumableSlot(out int count)
        {
            count = 0;
            int first = -1;
            if (_itemCatalog == null) return -1;
            foreach ((int SlotIndex, int ItemId, int UpgradeLevel) entry in _inventorySlots)
            {
                if (!_itemCatalog.TryGet(entry.ItemId, out ItemEntryJson item) || item.itemType != "Consumable") continue;
                count++;
                if (first < 0 || entry.SlotIndex < first) first = entry.SlotIndex;
            }
            return first;
        }

        public int ShopNpcEntityId => _shopOpenNpcEntityId;
        public IReadOnlyList<(int ItemId, long BuyPrice)> ShopItems => _shopItems;

        /// <summary>Dienst des NPCs, dessen Shop gerade offen ist (Händler/Schmied), None ohne offenen Shop.</summary>
        public NpcService ShopNpcService =>
            _debugSmithPreview && _shopOpenNpcEntityId >= 0 ? NpcService.Blacksmith
            : _shopOpenNpcEntityId >= 0 && _world.Entities.TryGetValue(_shopOpenNpcEntityId, out ClientEntity npc)
                ? npc.NpcService
                : NpcService.None;

        public UpgradeCatalogClient UpgradeCatalog => _upgradeCatalog;

        /// <summary>Letzte Aufwertungsmeldung (Erfolg/Fehlschlag/Ablehnungsgrund) fürs Schmied-Fenster.</summary>
        public string LastUpgradeMessage => _lastUpgradeMessage;

        public string ShopNpcName
        {
            get
            {
                return _shopOpenNpcEntityId >= 0 && _world.Entities.TryGetValue(_shopOpenNpcEntityId, out ClientEntity npc)
                    ? npc.Name
                    : ShopNpcService == NpcService.Blacksmith ? "Schmied" : "Händler";
            }
        }

        public void BuyShopItem(int itemId)
        {
            if (_shopOpenNpcEntityId < 0) return;
            _netClient?.SendBuyItem(_shopOpenNpcEntityId, itemId);
        }

        public void SellInventorySlot(int slotIndex)
        {
            if (_shopOpenNpcEntityId < 0 || FindItemInSlot(slotIndex) == 0) return;
            _netClient?.SendSellItem(_shopOpenNpcEntityId, slotIndex);
        }

        public void CloseShop()
        {
            _shopOpenNpcEntityId = -1;
            _debugSmithPreview = false;
        }

        public void AllocateStatPoint(StatKind stat)
        {
            _netClient?.SendAllocateStatPoint(stat);
        }

        public void AllocateSkillPoint(int skillId)
        {
            _netClient?.SendAllocateSkillPoint(skillId);
        }

        /// <summary>Klick auf einen Skill der Schnellleiste - wie Taste 1-4.</summary>
        public void UseSkillSlot(int hotkeyIndex)
        {
            if (!_inGame || _gameplayInputLocked || _world.LocalEntityId < 0) return;
            if (hotkeyIndex < 0 || hotkeyIndex >= SkillHotkeys.Length) return;
            UseSkillHotkey(hotkeyIndex);
        }

        public void SendChat(ChatChannel channel, string targetName, string text)
        {
            _netClient?.SendChat(channel, targetName, text);
        }

        public void PickupItem(int groundItemEntityId) => _netClient?.SendPickupItem(groundItemEntityId);
        public void PartyInvite(string targetName) => _netClient?.SendPartyInvite(targetName);
        public void PartyRespond(bool accept) => _netClient?.SendPartyRespond(accept);
        public void PartyLeave() => _netClient?.SendPartyLeave();
        public void PartyKick(string targetName) => _netClient?.SendPartyKick(targetName);
        public void PartyPromote(string targetName) => _netClient?.SendPartyPromote(targetName);
        public void PartySetRole(string targetName, PartyRole role) => _netClient?.SendPartySetRole(targetName, role);
        public void RequestPartySnapshot() => _netClient?.RequestPartySnapshot();

        public Vector3 LocalPlayerWorldPosition
        {
            get
            {
                if (_world.LocalEntityId >= 0 && _world.Entities.TryGetValue(_world.LocalEntityId, out ClientEntity local))
                    return new Vector3((float)(local.X / 100.0), (float)(local.Y / 100.0), 0f);
                return Vector3.zero;
            }
        }

        /// <summary>Räumt für das Asset-Testlabor die Szene auf: Server-Figuren, Monster, Ringe und die alten
        /// Debug-Testboards werden ausgeblendet (nicht zerstört), damit beim Schließen alles unverändert
        /// zurückkommt. Neue Entities, die während des Labors spawnen, werden in OnEntitySpawned ebenfalls
        /// versteckt.</summary>
        public void SetAssetLabMode(bool active)
        {
            if (active)
            {
                _assetLabActive = true;

                HideForAssetLab(_rangeRingView != null ? _rangeRingView.gameObject : null);
                foreach (RangeRingView buffRing in _buffRingViews.Values)
                    HideForAssetLab(buffRing != null ? buffRing.gameObject : null);
                foreach (CharacterView view in _views.Values)
                    HideForAssetLab(view != null ? view.gameObject : null);
                foreach (MonsterView monsterView in _monsterViews.Values)
                    HideForAssetLab(monsterView != null ? monsterView.gameObject : null);
                foreach (GroundItemView groundItemView in _groundItemViews.Values)
                    HideForAssetLab(groundItemView != null ? groundItemView.gameObject : null);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                HideForAssetLab(_testboard != null ? _testboard.gameObject : null);
                HideForAssetLab(_sortingTestboard != null ? _sortingTestboard.gameObject : null);
                HideForAssetLab(_weaponTestboard != null ? _weaponTestboard.gameObject : null);
#endif
            }
            else
            {
                _assetLabActive = false;
                foreach (GameObject go in _assetLabHiddenObjects)
                {
                    if (go != null)
                        go.SetActive(true);
                }
                _assetLabHiddenObjects.Clear();
            }
        }

        private void HideForAssetLab(GameObject go)
        {
            if (go == null || !go.activeSelf) return;
            go.SetActive(false);
            _assetLabHiddenObjects.Add(go);
        }

        // Platzhalter-Zuordnung Form/Farbe je MobId (siehe data/mobs.json: 1=Wildhund, 2=Waldschrat) - der
        // Client kennt mobs.json bewusst nicht (Appearance-Feld im Paket bleibt bei Monstern ohnehin ohne
        // Bedeutung, siehe EntitySpawnPacket-Kommentar), deshalb eine feste, kleine Zuordnung statt eines
        // eigenen Katalogs. Unbekannte MobIds fallen auf Kreis/Grau zurück.
        private Sprite MonsterShapeFor(int mobId)
        {
            switch (mobId)
            {
                case 2: return _monsterTriangleSprite; // Waldschrat
                case 3: return _squareSprite;           // Lumiis-Stein
                case 4: return _monsterTriangleSprite;  // Boss
                default: return _monsterCircleSprite;
            }
        }

        private static Color MonsterColorFor(int mobId)
        {
            return mobId switch
            {
                1 => Color.white,                    // Wildhund: Original-Sheet
                2 => new Color(0.9f, 1f, 0.85f),     // Waldschrat/Boar: leicht grünlicher Stich
                3 => new Color(0.35f, 0.45f, 0.8f), // Lumiis-Stein: blau
                4 => new Color(0.6f, 0.15f, 0.15f), // Boss: dunkelrot
                5 => new Color(0.95f, 0.62f, 0.55f), // Hungriger Wildhund: rötlich
                _ => Color.white
            };
        }

        private static float MonsterScaleFor(int mobId)
        {
            switch (mobId)
            {
                case 2: return 1.1f;
                case 3: return 1.15f;
                case 4: return 1.8f;
                case 5: return 1.05f;
                default: return 1f;
            }
        }

        private void OnEntityDespawned(int entityId)
        {
            if (entityId == _targetEntityId) _targetEntityId = -1;
            if (entityId == _shopOpenNpcEntityId) _shopOpenNpcEntityId = -1; // NPCs despawnen praktisch nie, defensiv trotzdem.

            if (_groundItemViews.TryGetValue(entityId, out GroundItemView groundItemView))
            {
                if (groundItemView != null) Destroy(groundItemView.gameObject);
                _groundItemViews.Remove(entityId);
            }

            if (_monsterViews.TryGetValue(entityId, out MonsterView monsterView))
            {
                if (monsterView != null) Destroy(monsterView.gameObject);
                _monsterViews.Remove(entityId);
            }

            if (_views.TryGetValue(entityId, out CharacterView view))
            {
                if (view != null) Destroy(view.gameObject);
                _views.Remove(entityId);
            }
            _animators.Remove(entityId);
        }

        private void OnWorldWarning(string message)
        {
            Debug.LogWarning(message);
        }

        private void CleanupEntities()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_assetTestLab != null) _assetTestLab.Close();
#endif

            CleanupMapViews(includeBuffRings: true);
            _world.Reset();
        }

        /// <summary>Multi-Map: räumt nur die Welt-Views auf (Figuren/Monster/Reichweitenring), ohne
        /// ClientWorld-Reset - beim Kartenwechsel bleiben Fortschritt/Inventar und aktive Buff-Ringe erhalten.</summary>
        private void CleanupMapViews(bool includeBuffRings = false)
        {
            foreach (CharacterView view in _views.Values)
                if (view != null) Destroy(view.gameObject);
            _views.Clear();
            _animators.Clear();

            foreach (MonsterView monsterView in _monsterViews.Values)
                if (monsterView != null) Destroy(monsterView.gameObject);
            _monsterViews.Clear();

            foreach (GroundItemView groundItemView in _groundItemViews.Values)
                if (groundItemView != null) Destroy(groundItemView.gameObject);
            _groundItemViews.Clear();

            if (_rangeRingView != null) Destroy(_rangeRingView.gameObject);
            _rangeRingView = null;

            if (includeBuffRings)
            {
                foreach (RangeRingView buffRing in _buffRingViews.Values)
                    Destroy(buffRing.gameObject);
                _buffRingViews.Clear();
            }

            _targetEntityId = -1;
            _shopOpenNpcEntityId = -1;
            _shopItems.Clear();
        }

        // ---- Boden & Kartenbegrenzung ----

        /// <summary>Multi-Map: baut Boden/Ränder/Portalmarkierungen der aktuellen Karte neu (alte Objekte
        /// werden verworfen). Maße kommen aus ClientWorld (JoinAccepted/MapChanged).</summary>
        private void RebuildMapVisuals()
        {
            CreateFloorAndBoundary();
            CreatePortalViews();
        }

        private void CreateFloorAndBoundary()
        {
            if (_mapRoot != null) Destroy(_mapRoot);
            _mapRoot = new GameObject("MapRoot");

            float mapUnitsX = _world.MapWidth / 100f;
            float mapUnitsY = _world.MapHeight / 100f;

            // Echte, aus den Referenz-Tiles gebackene Karte (Resources/Maps/<mapId>). Die Metadaten
            // (Ursprung/Größe/PixelsPerUnit) stehen in data/maps/<mapId>/map.json. Fehlt das Bild, bleibt ein
            // ruhiger Grasboden statt des alten Debug-Schachbretts.
            MapVisual visual = null;
            bool hasVisual = _mapCatalogClient != null
                && _mapCatalogClient.TryGetVisual(_world.MapId, out visual);
            if (hasVisual)
            {
                Sprite mapSprite = Resources.Load<Sprite>("Maps/" + _world.MapId);
                if (mapSprite != null)
                {
                    var floorGo = new GameObject("Floor_" + _world.MapId);
                    floorGo.transform.SetParent(_mapRoot.transform, false);
                    var floorRenderer = floorGo.AddComponent<SpriteRenderer>();
                    floorRenderer.sprite = mapSprite;
                    floorRenderer.sortingOrder = -100;
                    floorGo.transform.position = new Vector3(
                        visual.OriginX + visual.Width / 2f,
                        visual.OriginY + visual.Height / 2f,
                        0f);
                    return;
                }
            }

            var fallback = new GameObject("FloorFallback");
            fallback.transform.SetParent(_mapRoot.transform, false);
            var fallbackRenderer = fallback.AddComponent<SpriteRenderer>();
            fallbackRenderer.sprite = PlaceholderSprites.CreateSquare();
            fallbackRenderer.color = new Color(0.16f, 0.24f, 0.15f, 1f);
            fallbackRenderer.sortingOrder = -100;
            fallback.transform.localScale = new Vector3(mapUnitsX, mapUnitsY, 1f);
            fallback.transform.position = new Vector3(mapUnitsX / 2f, mapUnitsY / 2f, 0f);
        }

        /// <summary>Zeichnet die Portale der aktuellen Karte als halbtransparente Rechtecke (rein optisch,
        /// Daten aus StreamingAssets/lumiis-data/maps). Ohne MapCatalogClient bleibt die Karte portal-frei
        /// sichtbar - der Server wechselt trotzdem korrekt.</summary>
        private void CreatePortalViews()
        {
            if (_mapRoot == null || _mapCatalogClient == null) return;
            Color portalColor = new Color(0.2f, 0.8f, 1f, 0.35f);
            foreach (PortalJson portal in _mapCatalogClient.PortalsFor(_world.MapId))
            {
                var go = new GameObject("Portal_" + portal.id + "_" + portal.targetMapId);
                go.transform.SetParent(_mapRoot.transform, false);
                var renderer = go.AddComponent<SpriteRenderer>();
                renderer.sprite = _squareSprite;
                renderer.color = portalColor;
                renderer.sortingOrder = -60;
                float width = Mathf.Max(0.1f, (portal.x2 - portal.x1) / 100f);
                float height = Mathf.Max(0.1f, (portal.y2 - portal.y1) / 100f);
                go.transform.localScale = new Vector3(width, height, 1f);
                go.transform.position = new Vector3((portal.x1 + portal.x2) / 200f, (portal.y1 + portal.y2) / 200f, 0f);
            }
        }

        // ---- Debug-UI (IMGUI) - Login/Charakterauswahl übernimmt LoginController (UI Toolkit, siehe dort). ----

        private void OnGUI()
        {
            if (!_inGame) return;
            if (_assetLabActive) return; // Im Testlabor übernimmt die UI-Toolkit-Oberfläche.

            if (_debugOverlayVisible)
            {
                DrawStatusLine();
                DrawCombatLine();
                DrawSkillsLine();
                DrawEntityList();
                DrawEntityLabels();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                DrawDebugTestPanel();
#endif
            }
        }

        private void DrawStatusLine()
        {
            string posText = "-";
            ClientEntity local;
            if (_world.LocalEntityId >= 0 && _world.Entities.TryGetValue(_world.LocalEntityId, out local))
                posText = local.IntX + ", " + local.IntY;

            string text = _statusMessage + " | Pos (Wire): " + posText + " | Entities: " + _world.Entities.Count
                + " | Ping: " + _netClient.Ping + " ms" + " | Sprites im Cache: " + _spriteProvider.Count + " | F1: Overlay";
            GUI.Label(new Rect(10, 10, 700, 20), text);
        }

        /// <summary>M4a: eigenes Leben, Ziel (Name+Leben, falls anvisiert), Yang-Betrag, ggf. eine kurze
        /// Fehlermeldung oder die Todesmeldung - alles reiner Debug-Text (siehe CLAUDE.md Abschnitt "Kampf",
        /// "KEINE echten Health-Bars").</summary>
        private void DrawCombatLine()
        {
            string ownHp = "-";
            if (_world.LocalEntityId >= 0 && _world.Entities.TryGetValue(_world.LocalEntityId, out ClientEntity local) && local.CurrentHp >= 0)
                ownHp = local.CurrentHp + "/" + local.MaxHp;

            string targetText = "-";
            if (_targetEntityId >= 0 && _world.Entities.TryGetValue(_targetEntityId, out ClientEntity target))
                targetText = target.Name + " (" + (target.CurrentHp >= 0 ? target.CurrentHp + "/" + target.MaxHp : "-") + ")";

            string text = "Leben: " + ownHp + " | Ziel: " + targetText + " | Yang: " + _yangTotal
                + " | Level: " + (_world.CharacterStats.Level > 0 ? _world.CharacterStats.Level.ToString() : "-");
            if (_isDead) text += " | Du bist gestorben, Respawn in Kürze...";
            else if (!string.IsNullOrEmpty(_lastAttackFailedMessage)) text += " | " + _lastAttackFailedMessage;
            if (!string.IsNullOrEmpty(_lastInventoryActionFailedMessage)) text += " | " + _lastInventoryActionFailedMessage;
            if (!string.IsNullOrEmpty(_lastUpgradeMessage)) text += " | " + _lastUpgradeMessage;
            if (!string.IsNullOrEmpty(_lastShopActionFailedMessage)) text += " | " + _lastShopActionFailedMessage;
            if (!string.IsNullOrEmpty(_lastLevelUpMessage)) text += " | " + _lastLevelUpMessage;
            if (!string.IsNullOrEmpty(_lastDodgeMessage)) text += " | " + _lastDodgeMessage;

            GUI.Label(new Rect(10, 32, 700, 20), text);
        }

        /// <summary>M6b: Tastenbelegung 1-4 mit Skill-Namen, dahinter (falls aktiv) lokale Cooldown-Restzeit
        /// bzw. aktive Buffs (server-autoritative RemainingMs, siehe TickBuffCountdowns) sowie die letzte
        /// UseSkillFailedPacket-Meldung. Echte Spielsteuerung, deshalb ungated (kein #if UNITY_EDITOR/
        /// DEVELOPMENT_BUILD, anders als das Debug-Test-Panel).</summary>
        private void DrawSkillsLine()
        {
            string text = "";
            foreach ((int skillId, bool _) in SkillHotkeys)
            {
                string name = _skillCatalog != null ? _skillCatalog.DisplayName(skillId) : "Skill " + skillId;
                string cooldownSuffix = "";
                if (_skillCooldownReadyAtTime.TryGetValue(skillId, out float readyAt))
                {
                    float remaining = readyAt - Time.time;
                    if (remaining > 0f) cooldownSuffix = " (" + remaining.ToString("0.0") + "s)";
                }
                text += skillId + ":" + name + cooldownSuffix + "   ";
            }

            foreach (KeyValuePair<BuffType, float> kvp in _activeBuffRemainingSeconds)
                text += BuffDisplayName(kvp.Key) + ": " + kvp.Value.ToString("0.0") + "s   ";

            if (!string.IsNullOrEmpty(_lastSkillFailedMessage)) text += "| " + _lastSkillFailedMessage;

            GUI.Label(new Rect(10, 54, 760, 20), text);
        }

        /// <summary>Liste aller Entities, nach Entfernung zum lokalen Spieler sortiert, maximal MaxDebugEntityRows Zeilen.
        /// Die Gesamtzahl bleibt unabhängig davon in DrawStatusLine sichtbar.</summary>
        private void DrawEntityList()
        {
            ClientEntity local = null;
            if (_world.LocalEntityId >= 0) _world.Entities.TryGetValue(_world.LocalEntityId, out local);

            List<ClientEntity> sorted = new List<ClientEntity>(_world.Entities.Values);
            sorted.Sort((a, b) => DistanceToLocal(local, a).CompareTo(DistanceToLocal(local, b)));

            float y = 76f;
            int shown = 0;
            foreach (ClientEntity entity in sorted)
            {
                if (shown >= MaxDebugEntityRows) break;

                double distance = DistanceToLocal(local, entity);
                string kindLabel = entity.Kind switch { EntityKind.Monster => "Monster", EntityKind.Npc => "NPC", _ => "Spieler" };
                string line = "#" + entity.Id + " [" + kindLabel + "] " + entity.Name
                    + "  (" + (entity.X / 100.0).ToString("0.0") + ", " + (entity.Y / 100.0).ToString("0.0") + ")"
                    + "  " + distance.ToString("0.0") + " Einh.";
                GUI.Label(new Rect(10, y, 400, 18), line);
                y += 18f;
                shown++;
            }
        }

        private static double DistanceToLocal(ClientEntity local, ClientEntity entity)
        {
            if (local == null) return 0.0;
            double dx = entity.X - local.X;
            double dy = entity.Y - local.Y;
            return System.Math.Sqrt(dx * dx + dy * dy) / 100.0;
        }

        private void DrawEntityLabels()
        {
            foreach (KeyValuePair<int, CharacterView> kvp in _views)
            {
                ClientEntity entity;
                if (!_world.Entities.TryGetValue(kvp.Key, out entity)) continue;

                Vector3 basePos = new Vector3((float)(entity.X / 100.0), (float)(entity.Y / 100.0), 0f);
                Vector3 worldPos = basePos + kvp.Value.GetHeadWorldOffset();
                Vector3 screenPos = _camera.WorldToScreenPoint(worldPos);
                if (screenPos.z < 0) continue; // hinter der Kamera

                float guiY = Screen.height - screenPos.y; // GUI-Koordinaten sind Y-invertiert gegenüber Screen-Koordinaten

                // Eigener Name in Gelb statt Ebenen-Tönung - unterscheidet den lokalen Spieler zusätzlich zum
                // Ring am Fuß. NPCs (M5b) in Grün, um sie auf einen Blick von anderen Spielern zu unterscheiden.
                Color previousColor = GUI.color;
                if (entity.Id == _world.LocalEntityId) GUI.color = Color.yellow;
                else if (entity.Kind == EntityKind.Npc) GUI.color = Color.green;
                else GUI.color = Color.white;
                GUI.Label(new Rect(screenPos.x - 40, guiY - 10, 80, 20), entity.Name);
                GUI.color = previousColor;
            }
        }
    }
}
