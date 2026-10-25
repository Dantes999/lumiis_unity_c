using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Lumiis.Client
{
    [Serializable]
    public class MapIndexEntryJson
    {
        public string id = "";
        public string name = "";
    }

    [Serializable]
    public class MapIndexJson
    {
        public string startMapId = "";
        public MapIndexEntryJson[] maps = Array.Empty<MapIndexEntryJson>();
    }

    [Serializable]
    public class MapFileJson
    {
        public string name = "";
        public int startX;
        public int startY;
        public int visualOriginX;
        public int visualOriginY;
        public int visualWidth;
        public int visualHeight;
        public int visualPixelsPerUnit;
        public bool terrain;
    }

    /// <summary>Optionales Kartenbild (Resources/Maps/&lt;mapId&gt;). Herkunft/Breite/Höhe stehen in map.json;
    /// fehlt das Bild, fällt der Client auf den alten Platzhalterboden zurück.</summary>
    public sealed class MapVisual
    {
        public int OriginX;
        public int OriginY;
        public int Width;
        public int Height;
        public int PixelsPerUnit;
    }

    [Serializable]
    public class PortalJson
    {
        public int id;
        public int x1;
        public int y1;
        public int x2;
        public int y2;
        public string targetMapId = "";
    }

    [Serializable]
    public class PortalsJson
    {
        public PortalJson[] portals = Array.Empty<PortalJson>();
    }

    /// <summary>Multi-Map-Clientdaten (nur Anzeige): Kartenname und Portale aus StreamingAssets
    /// (lumiis-data/maps, vom Lumiis.Shared-Build dorthin kopiert). Der Client berechnet daraus KEINE
    /// Spielwahrheit - der Server entscheidet autoritativ über den Wechsel; die Rechtecke dienen nur der
    /// sichtbaren Portalmarkierung.</summary>
    public sealed class MapCatalogClient
    {
        private readonly Dictionary<string, string> _names = new Dictionary<string, string>();
        private readonly Dictionary<string, List<PortalJson>> _portals = new Dictionary<string, List<PortalJson>>();
        private readonly Dictionary<string, MapVisual> _visuals = new Dictionary<string, MapVisual>();
        private readonly List<string> _mapIds = new List<string>();
        private readonly HashSet<string> _terrainMaps = new HashSet<string>();

        public string StartMapId { get; private set; } = "village";
        public IReadOnlyList<string> MapIds => _mapIds;

        public static MapCatalogClient LoadFromStreamingAssets()
        {
            return new MapCatalogClient(Path.Combine(Application.streamingAssetsPath, "lumiis-data", "maps"));
        }

        public MapCatalogClient(string mapsDirectory)
        {
            string indexPath = Path.Combine(mapsDirectory, "index.json");
            MapIndexJson index = JsonUtility.FromJson<MapIndexJson>(File.ReadAllText(indexPath)) ?? new MapIndexJson();
            StartMapId = string.IsNullOrEmpty(index.startMapId) ? "village" : index.startMapId;

            foreach (MapIndexEntryJson entry in index.maps)
            {
                if (entry == null || string.IsNullOrEmpty(entry.id)) continue;
                _mapIds.Add(entry.id);
                _names[entry.id] = string.IsNullOrEmpty(entry.name) ? entry.id : entry.name;

                string mapJsonPath = Path.Combine(mapsDirectory, entry.id, "map.json");
                if (File.Exists(mapJsonPath))
                {
                    MapFileJson mapFile = JsonUtility.FromJson<MapFileJson>(File.ReadAllText(mapJsonPath));
                    if (mapFile != null)
                    {
                        if (!string.IsNullOrEmpty(mapFile.name)) _names[entry.id] = mapFile.name;
                        if (mapFile.terrain) _terrainMaps.Add(entry.id);
                        if (mapFile.visualWidth > 0 && mapFile.visualHeight > 0)
                        {
                            _visuals[entry.id] = new MapVisual
                            {
                                OriginX = mapFile.visualOriginX,
                                OriginY = mapFile.visualOriginY,
                                Width = mapFile.visualWidth,
                                Height = mapFile.visualHeight,
                                PixelsPerUnit = mapFile.visualPixelsPerUnit > 0 ? mapFile.visualPixelsPerUnit : 32
                            };
                        }
                    }
                }

                string portalsPath = Path.Combine(mapsDirectory, entry.id, "portals.json");
                var portals = new List<PortalJson>();
                if (File.Exists(portalsPath))
                {
                    PortalsJson data = JsonUtility.FromJson<PortalsJson>(File.ReadAllText(portalsPath));
                    if (data?.portals != null) portals.AddRange(data.portals);
                }
                _portals[entry.id] = portals;
            }
        }

        public string NameFor(string mapId)
        {
            return _names.TryGetValue(mapId, out string? name) ? name : mapId;
        }

        public IReadOnlyList<PortalJson> PortalsFor(string mapId)
        {
            return _portals.TryGetValue(mapId, out List<PortalJson>? portals)
                ? portals
                : (IReadOnlyList<PortalJson>)Array.Empty<PortalJson>();
        }

        /// <summary>Karte hat terrain.bin/props.json (Boden wird zur Laufzeit gezeichnet, siehe TerrainRenderer).</summary>
        public bool HasTerrain(string mapId)
        {
            return _terrainMaps.Contains(mapId);
        }

        public bool TryGetVisual(string mapId, out MapVisual visual)
        {
            return _visuals.TryGetValue(mapId, out visual);
        }
    }
}
