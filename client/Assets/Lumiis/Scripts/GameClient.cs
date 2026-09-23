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
        private LookCatalogClient _lookCatalog;
        private CachingSpriteProvider _spriteProvider;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private CharacterTestboard _testboard;
        private CharacterTestboard _sortingTestboard;

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

            if (_inGame) HandleClickInput();
            HandleZoomInput();
            HandleDebugToggleInput();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            HandleDebugHotkeys();
#endif
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
            const float w = 220f;
            Rect area = new Rect(Screen.width - w - 10f, 10f, w, 360f);
            GUILayout.BeginArea(area, GUI.skin.box);

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

            GUILayout.EndArea();
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

        /// <summary>M4a: Rechtsklick auf ein sichtbares Monster sendet AttackRequestPacket - Bounds-Check auf
        /// der MonsterView reicht (siehe MonsterView.ContainsWorldPoint), kein echtes Collider-System nötig.
        /// Nur Monster sind Ziele (Spieler-Views werden absichtlich nicht geprüft - PvP wird vom Server
        /// ohnehin abgelehnt, siehe CLAUDE.md Abschnitt "Kampf").</summary>
        private void HandleRightClickAttack()
        {
            Vector3 worldPos = ScreenToWorldPoint(Mouse.current.position.ReadValue());
            foreach (KeyValuePair<int, MonsterView> kvp in _monsterViews)
            {
                if (kvp.Value == null || !kvp.Value.ContainsWorldPoint(worldPos)) continue;
                _targetEntityId = kvp.Key;
                _netClient.SendAttack(kvp.Key);
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
                _world.HandleEntitySpawn(spawn.EntityId, spawn.Name, spawn.Kind, spawn.MobId, spawn.X, spawn.Y, spawn.Appearance);
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
                _yangTotal = yangChanged.NewTotal;
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
                default: return "Angriff fehlgeschlagen.";
            }
        }

        // ---- Views ----

        private void OnEntitySpawned(ClientEntity entity)
        {
            if (entity.Kind == EntityKind.Monster)
            {
                var monsterGo = new GameObject("Monster_" + entity.Id + "_" + entity.Name);
                var monsterView = monsterGo.AddComponent<MonsterView>();
                monsterView.Initialize(MonsterShapeFor(entity.MobId), MonsterColorFor(entity.MobId));
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

            string text = "Leben: " + ownHp + " | Ziel: " + targetText + " | Yang: " + _yangTotal;
            if (_isDead) text += " | Du bist gestorben, Respawn in Kürze...";
            else if (!string.IsNullOrEmpty(_lastAttackFailedMessage)) text += " | " + _lastAttackFailedMessage;

            GUI.Label(new Rect(10, 32, 700, 20), text);
        }

        /// <summary>Liste aller Entities, nach Entfernung zum lokalen Spieler sortiert, maximal MaxDebugEntityRows Zeilen.
        /// Die Gesamtzahl bleibt unabhängig davon in DrawStatusLine sichtbar.</summary>
        private void DrawEntityList()
        {
            ClientEntity local = null;
            if (_world.LocalEntityId >= 0) _world.Entities.TryGetValue(_world.LocalEntityId, out local);

            List<ClientEntity> sorted = new List<ClientEntity>(_world.Entities.Values);
            sorted.Sort((a, b) => DistanceToLocal(local, a).CompareTo(DistanceToLocal(local, b)));

            float y = 54f;
            int shown = 0;
            foreach (ClientEntity entity in sorted)
            {
                if (shown >= MaxDebugEntityRows) break;

                double distance = DistanceToLocal(local, entity);
                string kindLabel = entity.Kind == EntityKind.Monster ? "Monster" : "Spieler";
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

                // Eigener Name in Gelb statt Ebenen-Tönung - unterscheidet den lokalen Spieler zusätzlich zum Ring am Fuß.
                Color previousColor = GUI.color;
                GUI.color = entity.Id == _world.LocalEntityId ? Color.yellow : Color.white;
                GUI.Label(new Rect(screenPos.x - 40, guiY - 10, 80, 20), entity.Name);
                GUI.color = previousColor;
            }
        }
    }
}
