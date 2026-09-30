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
        private LookCatalogClient _lookCatalog;
        private ItemCatalogClient _itemCatalog;
        private SkillCatalogClient _skillCatalog;
        private ClassCatalogClient _classCatalog;
        private CachingSpriteProvider _spriteProvider;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private CharacterTestboard _testboard;
        private CharacterTestboard _sortingTestboard;
        private Vector2 _debugPanelScroll;

        /// <summary>Bildschirmbereich des Debug-Test-Panels (siehe DrawDebugTestPanel) - wird dort jeden Frame
        /// aktualisiert. HandleClickInput prüft das, damit ein Klick auf einen Button im Panel nicht
        /// zusätzlich als Bewegungs-/Angriffsbefehl in die Welt durchschlägt.</summary>
        private Rect _debugPanelRect;

        // Dev-Tools-Debug-Spawn (siehe CLAUDE.md "Dev-Tools"): fest hinterlegte Mob-Typen statt eines
        // geladenen Katalogs - der Client kennt mobs.json bewusst nicht (siehe CLAUDE.md Abschnitt "Monster"),
        // dieselbe kleine, feste MobId-Zuordnung wie MonsterShapeFor/MonsterColorFor, nur um einen Anzeigenamen
        // ergänzt. Reine Dev-Bequemlichkeit, kein neuer Lade-Mechanismus für ein Dev-Only-Feature.
        private static readonly (int MobId, string Name)[] DebugMobTypes = { (1, "Wildhund"), (2, "Waldschrat") };
        private int _debugSpawnMobIndex;
#endif

        // Login/Charakterauswahl übernimmt LoginController (UI Toolkit) - GameClient merkt sich nur, ob
        // der Join bereits geglückt ist (fürs Gating von Klick-Eingabe und Debug-Overlay).
        private bool _inGame;
        private string _pendingLoginName = "";
        private string _statusMessage = "";
        private bool _debugOverlayVisible = true;

        // ---- Kampf (M4a, Debug-Overlay-Text - siehe DrawStatusLine/DrawTargetLine) ----
        private int _targetEntityId = -1;
        private string _lastAttackFailedMessage = "";
        private long _yangTotal;
        private bool _isDead;

        // ---- Inventar/Ausrüstung (M5a, Debug-Panel-Stil - siehe DrawInventoryPanel/DrawEquipmentPanel) ----
        // Voller Resync bei Join und nach jeder Änderung (siehe InventorySyncPacket) - rein lokaler Cache für
        // die Debug-UI, keine eigene ClientWorld-Anbindung nötig (analog zu _yangTotal/_targetEntityId).
        private readonly List<(int SlotIndex, int ItemId)> _inventorySlots = new List<(int, int)>();
        private int _equippedArmorItemId;
        private int _equippedHelmetItemId;
        private int _equippedWeaponItemId;
        private int _equippedShieldItemId;
        private string _lastInventoryActionFailedMessage = "";

        // ---- Shop (M5b, Debug-Panel-Stil - siehe DrawShopPanel) ----
        // -1 = kein Shop offen. Sortiment kommt per ShopContentPacket (Resync bei jedem OpenShopRequest,
        // kein Delta - dieselbe Philosophie wie InventorySyncPacket).
        private int _shopOpenNpcEntityId = -1;
        private readonly List<(int ItemId, long BuyPrice)> _shopItems = new List<(int, long)>();
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
            _spriteProvider = new CachingSpriteProvider(new PlaceholderSpriteProvider(_lookCatalog.CanvasPx, _lookCatalog.Ppu));

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

            _world.EntitySpawned += OnEntitySpawned;
            _world.EntityDespawned += OnEntityDespawned;
            _world.Warning += OnWorldWarning;

            _netClient.Disconnected += OnNetDisconnected;
            _netClient.PacketReceived += OnPacketReceived;

            CreateFloorAndBoundary();
        }

        private void OnDestroy()
        {
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
                    kvp.Value.SetWorldPosition(entity.X / 100.0, entity.Y / 100.0);
            }

            if (_rangeRingView != null && _world.LocalEntityId >= 0 && _world.Entities.TryGetValue(_world.LocalEntityId, out ClientEntity localForRing))
                _rangeRingView.SetWorldPosition(localForRing.X / 100.0, localForRing.Y / 100.0);

            if (_inGame)
            {
                HandleClickInput();
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
            if (_shopOpenNpcEntityId < 0) return;

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
            if (skillId == SingleStrikeSkillId && targetEntityId != 0 && _world.LocalEntityId >= 0
                && _world.Entities.TryGetValue(targetEntityId, out ClientEntity skillTarget))
            {
                _world.SetFacingOverride(_world.LocalEntityId, skillTarget.IntX, skillTarget.IntY);
            }
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
                view.SetAppearance(appearance);
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

            if (GUILayout.Button("Testboard: " + (_testboard != null && _testboard.gameObject.activeSelf ? "an" : "aus")))
                ToggleTestboard();
            if (GUILayout.Button("Sortier-Testfeld: " + (_sortingTestboard != null && _sortingTestboard.gameObject.activeSelf ? "an" : "aus")))
                ToggleSortingTest();

            if (_world.LocalEntityId >= 0 && _world.Entities.TryGetValue(_world.LocalEntityId, out ClientEntity local))
            {
                Appearance appearance = local.Appearance;
                DrawCycleRow("Frisur", "Hair", appearance.HairStyleId);
                DrawHairColorRow(appearance.HairColorId);
                DrawCycleRow("Ruestung", "Armor", appearance.ArmorId);
                DrawCycleRow("Helm", "Helmet", appearance.HelmetId);
                DrawCycleRow("Waffe", "Weapon", appearance.WeaponId);
                DrawCycleRow("Schild", "Shield", appearance.ShieldId);
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

        /// <summary>M5a: die vier Ausrüstungs-Slots mit aktuellem Item-Namen (oder "leer") plus
        /// Ablegen-Knopf. Item-Namen kommen aus ItemCatalogClient (items.json, StreamingAssets), aufgelöst
        /// über die vom Server per InventorySyncPacket verschickten EquippedXItemId-Werte.</summary>
        private void DrawEquipmentPanel()
        {
            GUILayout.Space(6);
            GUILayout.Label("Ausrüstung");
            DrawEquipmentRow("Ruestung", EquipmentSlot.Armor, _equippedArmorItemId);
            DrawEquipmentRow("Helm", EquipmentSlot.Helmet, _equippedHelmetItemId);
            DrawEquipmentRow("Waffe", EquipmentSlot.Weapon, _equippedWeaponItemId);
            DrawEquipmentRow("Schild", EquipmentSlot.Shield, _equippedShieldItemId);
        }

        private void DrawEquipmentRow(string label, EquipmentSlot slot, int equippedItemId)
        {
            GUILayout.BeginHorizontal();
            string itemName = equippedItemId == 0 ? "leer" : _itemCatalog.DisplayName(equippedItemId);
            GUILayout.Label(label + ": " + itemName, GUILayout.Width(150));
            GUI.enabled = equippedItemId != 0;
            if (GUILayout.Button("Ablegen", GUILayout.Width(60)))
                _netClient.SendUnequipItem(slot);
            GUI.enabled = true;
            GUILayout.EndHorizontal();
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
                GUILayout.BeginHorizontal();
                string text = slotIndex + ": " + (itemId == 0 ? "leer" : _itemCatalog.DisplayName(itemId));
                GUI.enabled = itemId != 0;
                if (GUILayout.Button(text, GUILayout.Width(210)))
                    HandleInventorySlotClick(slotIndex, itemId);
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
        }

        private int FindItemInSlot(int slotIndex)
        {
            foreach ((int SlotIndex, int ItemId) entry in _inventorySlots)
            {
                if (entry.SlotIndex == slotIndex) return entry.ItemId;
            }
            return 0;
        }

        private void HandleInventorySlotClick(int slotIndex, int itemId)
        {
            if (itemId == 0) return;

            if (_itemCatalog.TryGet(itemId, out ItemEntryJson entry) && entry.itemType == "Consumable")
                _netClient.SendUseConsumable(slotIndex);
            else
                _netClient.SendEquipItem(slotIndex);
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
                GUILayout.Label(_itemCatalog.DisplayName(itemId) + " (" + buyPrice + " Yang)", GUILayout.Width(170));
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
                GUILayout.Label(entry.name + " (" + entry.sellPrice + " Yang)", GUILayout.Width(170));
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

        private void HandleClickInput()
        {
            if (Mouse.current == null) return;
            if (IsPointerOverDebugPanel()) return; // Klick auf einen Debug-Panel-Button soll die Figur nicht bewegen/angreifen lassen.

            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                Vector3 worldPos = ScreenToWorldPoint(Mouse.current.position.ReadValue());
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
                _targetEntityId = kvp.Key;
                _netClient.SendAttack(kvp.Key);
                // Kampf-Visualisierung Abschnitt 5: sofort lokal zum Ziel drehen, bevor die Serverantwort da
                // ist - kein dauerhafter Override, der naechste HandleEntityMove ueberschreibt das wieder.
                if (_world.LocalEntityId >= 0 && _world.Entities.TryGetValue(kvp.Key, out ClientEntity attackTarget))
                    _world.SetFacingOverride(_world.LocalEntityId, attackTarget.IntX, attackTarget.IntY);
                return;
            }

            foreach (KeyValuePair<int, CharacterView> kvp in _views)
            {
                if (kvp.Value == null || !kvp.Value.ContainsWorldPoint(worldPos)) continue;
                if (!_world.Entities.TryGetValue(kvp.Key, out ClientEntity entity) || entity.Kind != EntityKind.Npc) continue;

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
            _lastInventoryActionFailedMessage = "";
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
                _world.HandleJoinAccepted(accepted.EntityId, accepted.X, accepted.Y, _pendingLoginName, accepted.Appearance);
                _statusMessage = "Verbunden als " + _pendingLoginName;
                _inGame = true;
                return;
            }

            // JoinRejected: keine eigene Behandlung hier nötig - LoginController zeigt die Fehlermeldung im
            // UI, GameClient war zu diesem Zeitpunkt ohnehin nie InGame.

            EntitySpawnPacket spawn = packet as EntitySpawnPacket;
            if (spawn != null)
            {
                Debug.Log("EntitySpawn: Id=" + spawn.EntityId + " Kind=" + spawn.Kind + " Name=" + spawn.Name + " Pos=(" + (spawn.X / 100.0) + "," + (spawn.Y / 100.0) + ")");
                _world.HandleEntitySpawn(spawn.EntityId, spawn.Name, spawn.Kind, spawn.TypeId, spawn.X, spawn.Y, spawn.Appearance);
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
                _inventorySlots.Clear();
                _inventorySlots.AddRange(inventorySync.Slots);
                _equippedArmorItemId = inventorySync.EquippedArmorItemId;
                _equippedHelmetItemId = inventorySync.EquippedHelmetItemId;
                _equippedWeaponItemId = inventorySync.EquippedWeaponItemId;
                _equippedShieldItemId = inventorySync.EquippedShieldItemId;
                return;
            }

            InventoryActionFailedPacket inventoryFailed = packet as InventoryActionFailedPacket;
            if (inventoryFailed != null)
            {
                _lastInventoryActionFailedMessage = InventoryActionFailedReasonText(inventoryFailed.Reason);
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
                    _lastLevelUpMessage = "Level Up! Level " + characterStats.Level;
                return;
            }

            EntityDodgedPacket dodged = packet as EntityDodgedPacket;
            if (dodged != null)
            {
                if (dodged.TargetEntityId == _world.LocalEntityId) _lastDodgeMessage = "Ausgewichen!";
                else if (dodged.AttackerEntityId == _world.LocalEntityId) _lastDodgeMessage = "Verfehlt!";
                SpawnHitLine(dodged.AttackerEntityId, dodged.TargetEntityId, hit: false);
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

        private static readonly Color HitLineColor = new Color(0.9f, 0.15f, 0.15f, 0.9f);
        private static readonly Color DodgeLineColor = new Color(0.4f, 0.6f, 0.9f, 0.9f);

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
                default: return "Aktion fehlgeschlagen.";
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
            if (entity.Kind == EntityKind.Monster)
            {
                var monsterGo = new GameObject("Monster_" + entity.Id + "_" + entity.Name);
                var monsterView = monsterGo.AddComponent<MonsterView>();
                monsterView.Initialize(MonsterShapeFor(entity.TypeId), MonsterColorFor(entity.TypeId));
                monsterView.SetWorldPosition(entity.X / 100.0, entity.Y / 100.0);
                _monsterViews[entity.Id] = monsterView;
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

                // Kampf-Visualisierung Abschnitt 2: Reichweitenring, unparented (siehe RangeRingView-
                // Klassenkommentar) - Durchmesser = 2 * MeleeRangeWire (Radius), Wire -> Weltenheit /100.
                var rangeRingGo = new GameObject("RangeRing");
                _rangeRingView = rangeRingGo.AddComponent<RangeRingView>();
                float rangeDiameter = 2f * _classCatalog.MeleeRangeWire / 100f;
                _rangeRingView.Initialize(_unitRingSprite, rangeDiameter);
                _rangeRingView.SetWorldPosition(entity.X / 100.0, entity.Y / 100.0);
            }
        }

        // Platzhalter-Zuordnung Form/Farbe je MobId (siehe data/mobs.json: 1=Wildhund, 2=Waldschrat) - der
        // Client kennt mobs.json bewusst nicht (Appearance-Feld im Paket bleibt bei Monstern ohnehin ohne
        // Bedeutung, siehe EntitySpawnPacket-Kommentar), deshalb eine feste, kleine Zuordnung statt eines
        // eigenen Katalogs. Unbekannte MobIds fallen auf Kreis/Grau zurück.
        private Sprite MonsterShapeFor(int mobId) => mobId == 2 ? _monsterTriangleSprite : _monsterCircleSprite;

        private static Color MonsterColorFor(int mobId)
        {
            return mobId switch
            {
                1 => new Color(0.55f, 0.35f, 0.2f), // Wildhund: braun
                2 => new Color(0.3f, 0.5f, 0.25f),  // Waldschrat: grün
                _ => Color.gray
            };
        }

        private void OnEntityDespawned(int entityId)
        {
            if (entityId == _targetEntityId) _targetEntityId = -1;
            if (entityId == _shopOpenNpcEntityId) _shopOpenNpcEntityId = -1; // NPCs despawnen praktisch nie, defensiv trotzdem.

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
            foreach (CharacterView view in _views.Values)
                if (view != null) Destroy(view.gameObject);
            _views.Clear();
            _animators.Clear();

            foreach (MonsterView monsterView in _monsterViews.Values)
                if (monsterView != null) Destroy(monsterView.gameObject);
            _monsterViews.Clear();

            if (_rangeRingView != null) Destroy(_rangeRingView.gameObject);
            _rangeRingView = null;

            _world.Reset();
        }

        // ---- Boden & Kartenbegrenzung ----

        private void CreateFloorAndBoundary()
        {
            float mapUnitsX = GameConstants.MapWidth / 100f;
            float mapUnitsY = GameConstants.MapHeight / 100f;

            var floorGo = new GameObject("Floor");
            var floorRenderer = floorGo.AddComponent<SpriteRenderer>();
            floorRenderer.sprite = PlaceholderSprites.CreateCheckerTile();
            floorRenderer.drawMode = SpriteDrawMode.Tiled;
            floorRenderer.size = new Vector2(mapUnitsX, mapUnitsY);
            floorRenderer.sortingOrder = -100;
            floorGo.transform.position = new Vector3(mapUnitsX / 2f, mapUnitsY / 2f, 0f);

            Color borderColor = new Color(1f, 0.3f, 0.3f, 1f);
            float thickness = 0.3f;
            CreateBorderBar(borderColor, new Vector3(mapUnitsX / 2f, -thickness / 2f, 0f), new Vector3(mapUnitsX + thickness, thickness, 1f));
            CreateBorderBar(borderColor, new Vector3(mapUnitsX / 2f, mapUnitsY + thickness / 2f, 0f), new Vector3(mapUnitsX + thickness, thickness, 1f));
            CreateBorderBar(borderColor, new Vector3(-thickness / 2f, mapUnitsY / 2f, 0f), new Vector3(thickness, mapUnitsY + thickness, 1f));
            CreateBorderBar(borderColor, new Vector3(mapUnitsX + thickness / 2f, mapUnitsY / 2f, 0f), new Vector3(thickness, mapUnitsY + thickness, 1f));
        }

        private void CreateBorderBar(Color color, Vector3 position, Vector3 scale)
        {
            var go = new GameObject("MapBoundaryBar");
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = _squareSprite;
            renderer.color = color;
            renderer.sortingOrder = -50;
            go.transform.position = position;
            go.transform.localScale = scale;
        }

        // ---- Debug-UI (IMGUI) - Login/Charakterauswahl übernimmt LoginController (UI Toolkit, siehe dort). ----

        private void OnGUI()
        {
            if (!_inGame) return;

            if (_debugOverlayVisible)
            {
                DrawStatusLine();
                DrawCombatLine();
                DrawSkillsLine();
                DrawEntityList();
                DrawEntityLabels();
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            DrawDebugTestPanel();
#endif
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
