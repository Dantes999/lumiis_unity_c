using System.Collections.Generic;
using Lumiis.Shared;
using UnityEngine;
using UnityEngine.UIElements;

namespace Lumiis.Client
{
    /// <summary>Weltbezogene HUD-Ebene (UI Toolkit, gleiche Skalierung/Lesbarkeit wie das übrige HUD):
    /// Namensschilder über Spielern, NPCs und Monstern, schwebende Schadenszahlen bzw. "Verfehlt"/
    /// "Ausgewichen" sowie der Todesbildschirm mit Countdown. Rein kosmetisch - liest nur den bestehenden
    /// GameClient-/ClientWorld-Zustand, keine eigene Spielwahrheit.</summary>
    public sealed class HudWorldOverlay
    {
        private const float FloatDuration = 0.9f;
        private const float FloatRisePx = 34f;
        private const int MaxFloatingTexts = 40;

        private sealed class FloatingText
        {
            public Label Label;
            public int EntityId;
            public Vector3 LastWorld;
            public float StartTime;
            public float OffsetX;
        }

        private readonly GameClient _gameClient;
        private readonly VisualElement _nameLayer;
        private readonly VisualElement _floatLayer;
        private readonly VisualElement _deathScreen;
        private readonly Label _deathCountdown;
        private readonly Dictionary<int, Label> _nameplates = new Dictionary<int, Label>();
        private readonly List<FloatingText> _floating = new List<FloatingText>();
        private readonly HashSet<int> _seen = new HashSet<int>();
        private readonly List<int> _stale = new List<int>();
        private int _floatCounter;

        public HudWorldOverlay(GameClient gameClient, VisualElement root)
        {
            _gameClient = gameClient;

            _nameLayer = CreateLayer("hud-world-names");
            _floatLayer = CreateLayer("hud-world-floating");
            // Hinter allen HUD-Elementen (Fenster/Taskleiste verdecken Namensschilder, nicht umgekehrt).
            root.Insert(0, _floatLayer);
            root.Insert(0, _nameLayer);

            _deathScreen = new VisualElement { name = "hud-death-screen", pickingMode = PickingMode.Ignore };
            _deathScreen.AddToClassList("hud-death-screen");
            var panel = new VisualElement { pickingMode = PickingMode.Ignore };
            panel.AddToClassList("hud-death-panel");
            var title = new Label("Du bist gestorben") { pickingMode = PickingMode.Ignore };
            title.AddToClassList("hud-death-title");
            _deathCountdown = new Label("") { pickingMode = PickingMode.Ignore };
            _deathCountdown.AddToClassList("hud-death-countdown");
            panel.Add(title);
            panel.Add(_deathCountdown);
            _deathScreen.Add(panel);
            _deathScreen.style.display = DisplayStyle.None;
            root.Add(_deathScreen);

            _gameClient.DamageShown += OnDamageShown;
            _gameClient.DodgeShown += OnDodgeShown;
        }

        private static VisualElement CreateLayer(string name)
        {
            var layer = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
            layer.AddToClassList("hud-world-layer");
            return layer;
        }

        public void Dispose()
        {
            _gameClient.DamageShown -= OnDamageShown;
            _gameClient.DodgeShown -= OnDodgeShown;
        }

        /// <summary>Alles ausblenden (nicht im Spiel, Asset-Labor) - Namensschilder werden beim nächsten
        /// Update neu angelegt.</summary>
        public void Clear()
        {
            foreach (Label label in _nameplates.Values) label.RemoveFromHierarchy();
            _nameplates.Clear();
            foreach (FloatingText text in _floating) text.Label.RemoveFromHierarchy();
            _floating.Clear();
            _deathScreen.style.display = DisplayStyle.None;
        }

        public void Update()
        {
            Camera camera = _gameClient.WorldCamera;
            IPanel panel = _nameLayer.panel;
            if (camera == null || panel == null) return;

            UpdateNameplates(camera, panel);
            UpdateFloatingTexts(camera, panel);
            UpdateDeathScreen();
        }

        // ---- Namensschilder ----

        private void UpdateNameplates(Camera camera, IPanel panel)
        {
            ClientWorld world = _gameClient.World;
            _seen.Clear();
            foreach (int id in _gameClient.OverheadEntityIds)
            {
                if (!world.Entities.TryGetValue(id, out ClientEntity entity) || string.IsNullOrEmpty(entity.Name)) continue;
                if (!_gameClient.TryGetOverheadWorldPosition(id, out Vector3 worldPos)) continue;
                if (!TryWorldToPanel(camera, panel, worldPos, out Vector2 panelPos)) continue;

                _seen.Add(id);
                if (!_nameplates.TryGetValue(id, out Label label))
                {
                    label = new Label { pickingMode = PickingMode.Ignore };
                    label.AddToClassList("hud-nameplate");
                    label.AddToClassList(NameplateClass(entity, world.LocalEntityId));
                    label.style.translate = new Translate(Length.Percent(-50), Length.Percent(-100));
                    _nameLayer.Add(label);
                    _nameplates.Add(id, label);
                }

                string text = entity.Name;
                if (label.text != text) label.text = text;
                bool dead = entity.CurrentHp == 0;
                label.EnableInClassList("hud-nameplate-target", id == _gameClient.TargetEntityId);
                label.EnableInClassList("hud-nameplate-dead", dead);
                label.style.left = panelPos.x;
                label.style.top = panelPos.y;
            }

            _stale.Clear();
            foreach (int id in _nameplates.Keys)
                if (!_seen.Contains(id)) _stale.Add(id);
            foreach (int id in _stale)
            {
                _nameplates[id].RemoveFromHierarchy();
                _nameplates.Remove(id);
            }
        }

        private static string NameplateClass(ClientEntity entity, int localEntityId)
        {
            if (entity.Id == localEntityId) return "hud-nameplate-self";
            switch (entity.Kind)
            {
                case EntityKind.Npc: return "hud-nameplate-npc";
                case EntityKind.Monster: return "hud-nameplate-monster";
                default: return "hud-nameplate-player";
            }
        }

        // ---- Schadenszahlen ----

        private void OnDamageShown(int targetId, int damage, int attackerId)
        {
            int local = _gameClient.World.LocalEntityId;
            if (targetId == local) Spawn(targetId, "-" + damage, "hud-float-taken");
            else if (attackerId == local) Spawn(targetId, damage.ToString(), "hud-float-dealt");
            else Spawn(targetId, damage.ToString(), "hud-float-other");
        }

        private void OnDodgeShown(int targetId, int attackerId)
        {
            int local = _gameClient.World.LocalEntityId;
            if (targetId == local) Spawn(targetId, "Ausgewichen", "hud-float-dodge");
            else if (attackerId == local) Spawn(targetId, "Verfehlt", "hud-float-miss");
        }

        private void Spawn(int entityId, string text, string cssClass)
        {
            if (!_gameClient.TryGetOverheadWorldPosition(entityId, out Vector3 world)) return;
            if (_floating.Count >= MaxFloatingTexts)
            {
                _floating[0].Label.RemoveFromHierarchy();
                _floating.RemoveAt(0);
            }

            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("hud-float");
            label.AddToClassList(cssClass);
            label.style.translate = new Translate(Length.Percent(-50), Length.Percent(-100));
            _floatLayer.Add(label);
            // Aufeinanderfolgende Zahlen leicht versetzt, damit Mehrfachtreffer nicht übereinander liegen.
            float offsetX = ((_floatCounter++ % 3) - 1) * 14f;
            _floating.Add(new FloatingText { Label = label, EntityId = entityId, LastWorld = world, StartTime = Time.time, OffsetX = offsetX });
        }

        private void UpdateFloatingTexts(Camera camera, IPanel panel)
        {
            for (int i = _floating.Count - 1; i >= 0; i--)
            {
                FloatingText text = _floating[i];
                float t = (Time.time - text.StartTime) / FloatDuration;
                if (t >= 1f)
                {
                    text.Label.RemoveFromHierarchy();
                    _floating.RemoveAt(i);
                    continue;
                }

                // Folgt dem Ziel, solange es existiert; danach bleibt die Zahl an der letzten Stelle stehen.
                if (_gameClient.TryGetOverheadWorldPosition(text.EntityId, out Vector3 world)) text.LastWorld = world;
                if (!TryWorldToPanel(camera, panel, text.LastWorld, out Vector2 panelPos))
                {
                    text.Label.style.display = DisplayStyle.None;
                    continue;
                }

                text.Label.style.display = DisplayStyle.Flex;
                float ease = 1f - (1f - t) * (1f - t);
                text.Label.style.left = panelPos.x + text.OffsetX;
                text.Label.style.top = panelPos.y - 16f - ease * FloatRisePx;
                text.Label.style.opacity = t < 0.55f ? 1f : 1f - (t - 0.55f) / 0.45f;
                float scale = t < 0.12f ? 1.35f - t * 2.9f : 1f;
                text.Label.style.scale = new Scale(new Vector3(scale, scale, 1f));
            }
        }

        // ---- Todesbildschirm ----

        private void UpdateDeathScreen()
        {
            bool dead = _gameClient.IsDead;
            _deathScreen.style.display = dead ? DisplayStyle.Flex : DisplayStyle.None;
            if (!dead) return;

            float remaining = _gameClient.RespawnRemainingSeconds;
            _deathCountdown.text = remaining > 0.05f
                ? "Wiederbelebung in " + Mathf.CeilToInt(remaining) + " ..."
                : "Wiederbelebung ...";
        }

        private static bool TryWorldToPanel(Camera camera, IPanel panel, Vector3 world, out Vector2 panelPos)
        {
            Vector3 screen = camera.WorldToScreenPoint(world);
            panelPos = Vector2.zero;
            if (screen.z < 0f) return false;
            if (screen.x < -50f || screen.y < -50f || screen.x > Screen.width + 50f || screen.y > Screen.height + 50f) return false;
            panelPos = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screen.x, Screen.height - screen.y));
            return true;
        }
    }
}
