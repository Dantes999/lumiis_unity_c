using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace Lumiis.Client
{
    // JsonUtility gleicht Feldnamen exakt (case-sensitive) ab - deshalb lowerCamelCase wie in props.json.
    [Serializable]
    public class TerrainPropJson
    {
        public string type = "";
        public string kind = "";
        public int tx;
        public int ty;
        public int w;
        public int h;
        public float v;
    }

    [Serializable]
    public class TerrainVillageJson
    {
        public float cx = -1f;
        public float cy = -1f;
        public float r;
    }

    [Serializable]
    public class TerrainPropsFileJson
    {
        public TerrainPropJson[] props = Array.Empty<TerrainPropJson>();
        public TerrainVillageJson village;
    }

    /// <summary>Zeigt den Boden einer Karte mit terrain.bin (siehe TerrainArt): ein grobes Bild (1 Bildpunkt je
    /// Kachel) liegt immer darunter, die feinen 512x512-Chunks werden um die Kamera auf Hintergrund-Threads gerechnet
    /// und auf dem Hauptthread hochgeladen (höchstens zwei je Frame, alte werden verworfen). Bäume, Häuser und
    /// Denkmäler sind eigene SpriteRenderer, die nach ihrem Fußpunkt mit den Figuren sortiert werden - wer dahinter
    /// steht, verschwindet dahinter. Reine Anzeige, die Kollision prüft weiterhin nur der Server.</summary>
    public sealed class TerrainRenderer
    {
        private const float MaxDetailOrthoSize = 28f;
        private const int MaxCachedChunks = 72;
        private const int MaxUploadsPerFrame = 2;
        private const float PixelsPerUnit = TerrainArt.TilePixels;

        private sealed class Chunk
        {
            public GameObject Go;
            public Texture2D Texture;
            public Sprite Sprite;
        }

        private readonly TerrainArt _art;
        private readonly TerrainMap _map;
        private readonly GameObject _root;
        private readonly Dictionary<int, Chunk> _chunks = new Dictionary<int, Chunk>();
        private readonly HashSet<int> _pending = new HashSet<int>();
        private readonly ConcurrentQueue<KeyValuePair<int, uint[]>> _done = new ConcurrentQueue<KeyValuePair<int, uint[]>>();
        private readonly ConcurrentQueue<string> _errors = new ConcurrentQueue<string>();
        private readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        private readonly int _maxInFlight;
        private int _inFlight;
        private bool _disposed;

        /// <summary>Quadratische Minimap (1 Bildpunkt je Kachel, Rand mit Waldfarbe aufgefüllt).</summary>
        public Texture2D MinimapTexture { get; private set; }

        /// <summary>Weltausschnitt der Minimap-Textur (quadratisch).</summary>
        public MapVisual MinimapVisual { get; private set; }

        private TerrainRenderer(TerrainMap map, TerrainPropsFileJson props, Transform parent)
        {
            _map = map;
            TerrainVillageJson village = props?.village;
            _art = village != null && village.cx >= 0
                ? new TerrainArt(map, village.cx, village.cy, village.r)
                : new TerrainArt(map);
            _root = new GameObject("Terrain");
            _root.transform.SetParent(parent, false);
            _maxInFlight = Mathf.Clamp(SystemInfo.processorCount - 1, 1, 3);

            CreateBackdrop();
            CreateLowRes();
            CreateProps(props);
        }

        /// <summary>Lädt terrain.bin/props.json der Karte aus StreamingAssets; null, wenn die Karte kein Gelände hat.</summary>
        public static TerrainRenderer TryCreate(string mapId, Transform parent)
        {
            if (string.IsNullOrEmpty(mapId)) return null;
            string dir = Path.Combine(Application.streamingAssetsPath, "lumiis-data", "maps", mapId);
            string terrainPath = Path.Combine(dir, "terrain.bin");
            if (!File.Exists(terrainPath)) return null;
            try
            {
                TerrainMap map = TerrainMap.Parse(File.ReadAllBytes(terrainPath));
                TerrainPropsFileJson props = null;
                string propsPath = Path.Combine(dir, "props.json");
                if (File.Exists(propsPath)) props = JsonUtility.FromJson<TerrainPropsFileJson>(File.ReadAllText(propsPath));
                return new TerrainRenderer(map, props, parent);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Gelände für " + mapId + " konnte nicht geladen werden: " + ex.Message);
                return null;
            }
        }

        private Texture2D NewTexture(int w, int h, string name)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = name
            };
            _owned.Add(tex);
            return tex;
        }

        private Sprite NewSprite(Texture2D tex, Vector2 pivot, float ppu)
        {
            Sprite s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), pivot, ppu, 0, SpriteMeshType.FullRect);
            _owned.Add(s);
            return s;
        }

        private GameObject NewRenderer(string name, Sprite sprite, Vector3 position, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);
            go.transform.position = position;
            var r = go.AddComponent<SpriteRenderer>();
            r.sprite = sprite;
            r.sortingOrder = sortingOrder;
            return go;
        }

        // Außerhalb der Kachelkarte: dichter Wald statt leerer Fläche
        private void CreateBackdrop()
        {
            TerrainArt.RockColor(out byte r, out byte g, out byte b);
            Texture2D tex = NewTexture(1, 1, "TerrainBackdrop");
            tex.SetPixel(0, 0, new Color32(r, g, b, 255));
            tex.Apply(false, true);
            Sprite sprite = NewSprite(tex, Vector2.zero, 1f);
            float size = Mathf.Max(GameConstantsWidth(), 1f);
            GameObject go = NewRenderer("TerrainBackdrop", sprite, new Vector3(-200f, -200f, 0f), -102);
            go.transform.localScale = new Vector3(size + 400f, size + 400f, 1f);
        }

        private static float GameConstantsWidth()
        {
            return Lumiis.Shared.GameConstants.MapWidth / 100f;
        }

        private void CreateLowRes()
        {
            int w = _map.Width, h = _map.Height;
            byte[] rgba = _art.PaintLowRes();
            Texture2D tex = NewTexture(w, h, "TerrainLowRes");
            tex.SetPixelData(FlipRows(rgba, w, h), 0);
            tex.Apply(false, false);
            NewRenderer("TerrainLowRes", NewSprite(tex, Vector2.zero, 1f), new Vector3(_map.OriginX, _map.OriginY, 0f), -101);

            // Minimap quadratisch (die HUD-Minimap ist rund und quadratisch skaliert)
            int side = Mathf.Max(w, h);
            TerrainArt.RockColor(out byte rr, out byte gg, out byte bb);
            var square = new byte[side * side * 4];
            for (int i = 0; i < side * side; i++)
            {
                square[i * 4] = rr; square[i * 4 + 1] = gg; square[i * 4 + 2] = bb; square[i * 4 + 3] = 255;
            }
            int offX = (side - w) / 2, offY = (side - h) / 2;
            for (int y = 0; y < h; y++)
                Buffer.BlockCopy(rgba, y * w * 4, square, ((y + offY) * side + offX) * 4, w * 4);
            Texture2D mini = NewTexture(side, side, "TerrainMinimap");
            mini.SetPixelData(FlipRows(square, side, side), 0);
            mini.Apply(false, false);
            MinimapTexture = mini;
            // Zeile 0 der Kachelkarte (Norden) liegt oben: offY Zeilen Rand oben, (side - h - offY) unten
            MinimapVisual = new MapVisual
            {
                OriginX = _map.OriginX - offX,
                OriginY = _map.OriginY - (side - h - offY),
                Width = side,
                Height = side,
                PixelsPerUnit = 1
            };
        }

        private static byte[] FlipRows(byte[] rgba, int w, int h)
        {
            var r = new byte[rgba.Length];
            for (int y = 0; y < h; y++) Buffer.BlockCopy(rgba, y * w * 4, r, (h - 1 - y) * w * 4, w * 4);
            return r;
        }

        private void CreateProps(TerrainPropsFileJson file)
        {
            var extra = new List<TerrainProp>();
            if (file?.props != null)
            {
                foreach (TerrainPropJson p in file.props)
                {
                    if (p == null || string.IsNullOrEmpty(p.type)) continue;
                    extra.Add(new TerrainProp
                    {
                        Type = p.type,
                        Kind = p.kind ?? "",
                        Tx = p.tx,
                        Ty = p.ty,
                        W = p.w > 0 ? p.w : 1,
                        H = p.h > 0 ? p.h : 1,
                        V = p.v
                    });
                }
            }

            var images = new Dictionary<string, KeyValuePair<PropImage, Sprite>>();
            var propsRoot = new GameObject("Props");
            propsRoot.transform.SetParent(_root.transform, false);
            foreach (TerrainProp p in _art.CollectProps(extra))
            {
                string key = TerrainArt.ImageKey(p);
                if (key.Length == 0) continue;
                if (!images.TryGetValue(key, out KeyValuePair<PropImage, Sprite> entry))
                {
                    PropImage img = TerrainArt.PaintProp(p);
                    if (img == null) continue;
                    Texture2D tex = NewTexture(img.Width, img.Height, "Prop_" + key);
                    tex.SetPixelData(FlipRows(img.Rgba, img.Width, img.Height), 0);
                    tex.Apply(false, true);
                    var pivot = new Vector2(img.Ox / (float)img.Width, (img.Height - img.Oy) / (float)img.Height);
                    entry = new KeyValuePair<PropImage, Sprite>(img, NewSprite(tex, pivot, PixelsPerUnit));
                    images[key] = entry;
                }

                TerrainArt.FootInFootprint(p, entry.Key, out double footX, out double footY);
                float left = _map.OriginX + p.Tx;
                float top = _map.OriginY + _map.Height - p.Ty;
                var go = new GameObject(p.Type);
                go.transform.SetParent(propsRoot.transform, false);
                go.transform.position = new Vector3(left + (float)footX / PixelsPerUnit, top - (float)footY / PixelsPerUnit, 0f);
                var r = go.AddComponent<SpriteRenderer>();
                r.sprite = entry.Value;
                r.spriteSortPoint = SpriteSortPoint.Pivot;
            }
        }

        /// <summary>Einmal pro Frame nach der Kamerabewegung: fertige Chunks hochladen, fehlende anstoßen.</summary>
        public void Tick(Camera camera)
        {
            if (_disposed || camera == null) return;

            while (_errors.TryDequeue(out string error)) Debug.LogWarning("Gelände-Chunk: " + error);

            int uploads = 0;
            while (uploads < MaxUploadsPerFrame && _done.TryDequeue(out KeyValuePair<int, uint[]> result))
            {
                _pending.Remove(result.Key);
                if (result.Value == null || _chunks.ContainsKey(result.Key)) continue;
                Upload(result.Key, result.Value);
                uploads++;
            }

            if (camera.orthographicSize > MaxDetailOrthoSize) return;

            Vector3 cam = camera.transform.position;
            float halfH = camera.orthographicSize + 4f;
            float halfW = halfH * Mathf.Max(0.5f, camera.aspect) + 4f;
            int cxMin = Mathf.FloorToInt((cam.x - halfW - _map.OriginX) / TerrainArt.ChunkTiles);
            int cxMax = Mathf.FloorToInt((cam.x + halfW - _map.OriginX) / TerrainArt.ChunkTiles);
            // Chunkzeile 0 liegt im Norden (oben)
            float top = _map.OriginY + _map.Height;
            int cyMin = Mathf.FloorToInt((top - (cam.y + halfH)) / TerrainArt.ChunkTiles);
            int cyMax = Mathf.FloorToInt((top - (cam.y - halfH)) / TerrainArt.ChunkTiles);
            cxMin = Mathf.Max(0, cxMin); cyMin = Mathf.Max(0, cyMin);
            cxMax = Mathf.Min(_art.ChunksX - 1, cxMax); cyMax = Mathf.Min(_art.ChunksY - 1, cyMax);

            // Nächstgelegene fehlende Chunks zuerst
            while (System.Threading.Volatile.Read(ref _inFlight) < _maxInFlight)
            {
                int bestKey = -1;
                float bestDist = float.MaxValue;
                for (int cy = cyMin; cy <= cyMax; cy++)
                    for (int cx = cxMin; cx <= cxMax; cx++)
                    {
                        int key = cy * _art.ChunksX + cx;
                        if (_chunks.ContainsKey(key) || _pending.Contains(key)) continue;
                        Vector2 c = ChunkCenter(cx, cy);
                        float d = (c.x - cam.x) * (c.x - cam.x) + (c.y - cam.y) * (c.y - cam.y);
                        if (d < bestDist) { bestDist = d; bestKey = key; }
                    }
                if (bestKey < 0) break;
                StartPaint(bestKey);
            }

            if (_chunks.Count > MaxCachedChunks) EvictFarthest(cam);
        }

        private Vector2 ChunkCenter(int cx, int cy)
        {
            float half = TerrainArt.ChunkTiles / 2f;
            return new Vector2(_map.OriginX + cx * TerrainArt.ChunkTiles + half,
                _map.OriginY + _map.Height - cy * TerrainArt.ChunkTiles - half);
        }

        private void StartPaint(int key)
        {
            _pending.Add(key);
            System.Threading.Interlocked.Increment(ref _inFlight);
            int cx = key % _art.ChunksX, cy = key / _art.ChunksX;
            TerrainArt art = _art;
            Task.Run(() =>
            {
                uint[] px = null;
                try
                {
                    px = art.PaintChunk(cx, cy, true);
                }
                catch (Exception ex)
                {
                    _errors.Enqueue(ex.Message);
                }
                _done.Enqueue(new KeyValuePair<int, uint[]>(key, px));
            }).ContinueWith(_ => System.Threading.Interlocked.Decrement(ref _inFlight));
        }

        private void Upload(int key, uint[] pixels)
        {
            int cx = key % _art.ChunksX, cy = key / _art.ChunksX;
            var tex = new Texture2D(TerrainArt.ChunkPixels, TerrainArt.ChunkPixels, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "TerrainChunk_" + cx + "_" + cy
            };
            tex.SetPixelData(pixels, 0);
            tex.Apply(false, true);
            Sprite sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), Vector2.zero, PixelsPerUnit, 0, SpriteMeshType.FullRect);
            float left = _map.OriginX + cx * TerrainArt.ChunkTiles;
            float bottom = _map.OriginY + _map.Height - (cy + 1) * TerrainArt.ChunkTiles;
            GameObject go = NewRenderer(tex.name, sprite, new Vector3(left, bottom, 0f), -100);
            _chunks[key] = new Chunk { Go = go, Texture = tex, Sprite = sprite };
        }

        private void EvictFarthest(Vector3 cam)
        {
            var order = new List<KeyValuePair<float, int>>();
            foreach (KeyValuePair<int, Chunk> kv in _chunks)
            {
                Vector2 c = ChunkCenter(kv.Key % _art.ChunksX, kv.Key / _art.ChunksX);
                order.Add(new KeyValuePair<float, int>((c.x - cam.x) * (c.x - cam.x) + (c.y - cam.y) * (c.y - cam.y), kv.Key));
            }
            order.Sort((a, b) => b.Key.CompareTo(a.Key));
            for (int i = 0; i < order.Count && _chunks.Count > MaxCachedChunks; i++)
            {
                Chunk chunk = _chunks[order[i].Value];
                DestroyChunk(chunk);
                _chunks.Remove(order[i].Value);
            }
        }

        private static void DestroyChunk(Chunk chunk)
        {
            if (chunk.Go != null) UnityEngine.Object.Destroy(chunk.Go);
            if (chunk.Sprite != null) UnityEngine.Object.Destroy(chunk.Sprite);
            if (chunk.Texture != null) UnityEngine.Object.Destroy(chunk.Texture);
        }

        /// <summary>Portal-Wirbel (3x3 Kacheln), eigene Textur dieses Renderers.</summary>
        public Sprite CreatePortalSprite(int seed)
        {
            PropImage img = TerrainArt.PaintPortal(96, seed * 2.1);
            Texture2D tex = NewTexture(img.Width, img.Height, "Portal_" + seed);
            tex.SetPixelData(FlipRows(img.Rgba, img.Width, img.Height), 0);
            tex.Apply(false, true);
            return NewSprite(tex, new Vector2(0.5f, 0.5f), PixelsPerUnit);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (Chunk chunk in _chunks.Values) DestroyChunk(chunk);
            _chunks.Clear();
            foreach (UnityEngine.Object o in _owned)
                if (o != null) UnityEngine.Object.Destroy(o);
            _owned.Clear();
            if (_root != null) UnityEngine.Object.Destroy(_root);
        }
    }
}
