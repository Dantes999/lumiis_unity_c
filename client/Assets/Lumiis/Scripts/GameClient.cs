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
        private enum UiState
        {
            Login,
            Connecting,
            InGame
        }

        private NetClient _netClient;
        private Camera _camera;
        private readonly ClientWorld _world = new ClientWorld();
        private readonly Dictionary<int, EntityView> _views = new Dictionary<int, EntityView>();
        private Sprite _squareSprite;

        private UiState _uiState = UiState.Login;
        private string _host = "127.0.0.1";
        private string _portText = "7777";
        private string _name = "";
        private string _pendingLoginName = "";
        private string _statusMessage = "";

        private static readonly Color LocalPlayerColor = new Color(1f, 0.85f, 0.2f);

        public void Initialize(NetClient netClient, Camera camera)
        {
            _netClient = netClient;
            _camera = camera;
            _squareSprite = PlaceholderSprites.CreateSquare();

            _world.EntitySpawned += OnEntitySpawned;
            _world.EntityDespawned += OnEntityDespawned;
            _world.Warning += OnWorldWarning;

            _netClient.Connected += OnNetConnected;
            _netClient.Disconnected += OnNetDisconnected;
            _netClient.PacketReceived += OnPacketReceived;

            CreateFloorAndBoundary();
        }

        private void Update()
        {
            _world.Tick(Time.timeAsDouble);

            foreach (KeyValuePair<int, EntityView> kvp in _views)
            {
                ClientEntity entity;
                if (_world.Entities.TryGetValue(kvp.Key, out entity))
                    kvp.Value.SetWorldPosition(entity.X / 100.0, entity.Y / 100.0);
            }

            if (_uiState == UiState.InGame) HandleClickInput();
        }

        private void LateUpdate()
        {
            EntityView localView;
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
            if (!Mouse.current.leftButton.wasPressedThisFrame) return;

            Vector2 screenPos = Mouse.current.position.ReadValue();
            // z = Abstand Kamera -> Weltebene z=0 (Kamera steht bei negativem z, siehe Bootstrap).
            float distanceToWorldPlane = -_camera.transform.position.z;
            Vector3 worldPos = _camera.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, distanceToWorldPlane));

            int wireX = Mathf.RoundToInt(worldPos.x * 100f);
            int wireY = Mathf.RoundToInt(worldPos.y * 100f);
            _netClient.SendMoveTo(wireX, wireY); // Klemmung an Kartengrenzen macht der Server.
        }

        // ---- Netzwerk-Events ----

        private void OnNetConnected()
        {
            _statusMessage = "Verbunden, sende JoinRequest ...";
        }

        private void OnNetDisconnected(string reason)
        {
            _statusMessage = _uiState == UiState.InGame
                ? "Getrennt (" + reason + ")"
                : "Verbindung fehlgeschlagen (" + reason + ")";

            CleanupEntities();
            _uiState = UiState.Login;
        }

        private void OnPacketReceived(IPacket packet)
        {
            JoinAcceptedPacket accepted = packet as JoinAcceptedPacket;
            if (accepted != null)
            {
                _world.Reset();
                _world.HandleJoinAccepted(accepted.EntityId, accepted.X, accepted.Y, _pendingLoginName);
                _statusMessage = "Verbunden als " + _pendingLoginName;
                _uiState = UiState.InGame;
                return;
            }

            JoinRejectedPacket rejected = packet as JoinRejectedPacket;
            if (rejected != null)
            {
                _statusMessage = "JoinRejected: " + DescribeReason(rejected.Reason);
                return;
            }

            EntitySpawnPacket spawn = packet as EntitySpawnPacket;
            if (spawn != null)
            {
                _world.HandleEntitySpawn(spawn.EntityId, spawn.Name, spawn.X, spawn.Y);
                return;
            }

            EntityDespawnPacket despawn = packet as EntityDespawnPacket;
            if (despawn != null)
            {
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
            }
        }

        private static string DescribeReason(JoinRejectReason reason)
        {
            switch (reason)
            {
                case JoinRejectReason.VersionMismatch: return "Protokollversion stimmt nicht überein";
                case JoinRejectReason.InvalidName: return "Ungültiger Name";
                case JoinRejectReason.NameTaken: return "Name bereits vergeben";
                case JoinRejectReason.ServerFull: return "Server voll";
                default: return reason.ToString();
            }
        }

        // ---- Views ----

        private void OnEntitySpawned(ClientEntity entity)
        {
            var go = new GameObject("Entity_" + entity.Id + "_" + entity.Name);
            var view = go.AddComponent<EntityView>();
            bool isLocal = entity.Id == _world.LocalEntityId;
            Color color = isLocal ? LocalPlayerColor : ColorForEntity(entity.Id);
            view.Initialize(entity.Id, entity.Name, color, _squareSprite);
            view.SetWorldPosition(entity.X / 100.0, entity.Y / 100.0);
            _views[entity.Id] = view;
        }

        private void OnEntityDespawned(int entityId)
        {
            EntityView view;
            if (_views.TryGetValue(entityId, out view))
            {
                if (view != null) Destroy(view.gameObject);
                _views.Remove(entityId);
            }
        }

        private void OnWorldWarning(string message)
        {
            Debug.LogWarning(message);
        }

        private void CleanupEntities()
        {
            foreach (EntityView view in _views.Values)
                if (view != null) Destroy(view.gameObject);
            _views.Clear();
            _world.Reset();
        }

        private static Color ColorForEntity(int entityId)
        {
            float hue = (entityId * 0.618033988749895f) % 1f; // Goldener Schnitt verteilt Farben gut auseinander
            return Color.HSVToRGB(hue, 0.65f, 0.95f);
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
            floorRenderer.sortingOrder = -10;
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
            renderer.sortingOrder = -5;
            go.transform.position = position;
            go.transform.localScale = scale;
        }

        // ---- Debug-UI (IMGUI, temporär - wird in M2 durch UI Toolkit ersetzt) ----

        private void OnGUI()
        {
            if (_uiState == UiState.InGame)
            {
                DrawStatusLine();
                DrawEntityLabels();
            }
            else
            {
                DrawLoginWindow();
            }
        }

        private void DrawLoginWindow()
        {
            const float w = 260f, h = 190f;
            Rect rect = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            GUI.Box(rect, "Lumiis - Login");

            float y = rect.y + 25f;
            GUI.Label(new Rect(rect.x + 10, y, 60, 20), "Host:");
            _host = GUI.TextField(new Rect(rect.x + 80, y, 160, 20), _host);
            y += 25f;
            GUI.Label(new Rect(rect.x + 10, y, 60, 20), "Port:");
            _portText = GUI.TextField(new Rect(rect.x + 80, y, 160, 20), _portText);
            y += 25f;
            GUI.Label(new Rect(rect.x + 10, y, 60, 20), "Name:");
            _name = GUI.TextField(new Rect(rect.x + 80, y, 160, 20), _name);
            y += 30f;

            if (GUI.Button(new Rect(rect.x + 10, y, 150, 25), "Verbinden"))
            {
                TryConnect();
            }
            y += 30f;
            GUI.Label(new Rect(rect.x + 10, y, w - 20, 40), _statusMessage);
        }

        private void TryConnect()
        {
            int port;
            if (!int.TryParse(_portText, out port))
            {
                _statusMessage = "Ungültiger Port";
                return;
            }

            if (!NameRules.IsValid(_name))
            {
                _statusMessage = "Ungültiger Name (" + NameRules.MinLength + "-" + NameRules.MaxLength + " Zeichen, nur ASCII-Buchstaben/Ziffern/_)";
                return;
            }

            _pendingLoginName = _name;
            bool started = _netClient.Connect(_host, port, _name);
            _uiState = started ? UiState.Connecting : UiState.Login;
            _statusMessage = started ? "Verbinde ..." : "Verbindung fehlgeschlagen";
        }

        private void DrawStatusLine()
        {
            string posText = "-";
            ClientEntity local;
            if (_world.LocalEntityId >= 0 && _world.Entities.TryGetValue(_world.LocalEntityId, out local))
                posText = local.IntX + ", " + local.IntY;

            string text = _statusMessage + " | Pos (Wire): " + posText + " | Entities: " + _world.Entities.Count + " | Ping: " + _netClient.Ping + " ms";
            GUI.Label(new Rect(10, 10, 700, 20), text);
        }

        private void DrawEntityLabels()
        {
            foreach (KeyValuePair<int, EntityView> kvp in _views)
            {
                ClientEntity entity;
                if (!_world.Entities.TryGetValue(kvp.Key, out entity)) continue;

                Vector3 worldPos = new Vector3((float)(entity.X / 100.0), (float)(entity.Y / 100.0) + 0.6f, 0f);
                Vector3 screenPos = _camera.WorldToScreenPoint(worldPos);
                if (screenPos.z < 0) continue; // hinter der Kamera

                float guiY = Screen.height - screenPos.y; // GUI-Koordinaten sind Y-invertiert gegenüber Screen-Koordinaten
                GUI.Label(new Rect(screenPos.x - 40, guiY - 10, 80, 20), entity.Name);
            }
        }
    }
}
