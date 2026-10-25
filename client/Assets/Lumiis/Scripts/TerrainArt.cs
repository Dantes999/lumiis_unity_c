using System;
using System.Collections.Generic;
using System.IO;

namespace Lumiis.Client
{
    /// <summary>Kachelkarte aus data/maps/&lt;map&gt;/terrain.bin ("LTR1", u16 Breite, u16 Höhe, i16 originX,
    /// i16 originY, dann Breite*Höhe Kachel-Bytes, Zeile 0 = Norden). Eine Kachel = eine Zelle = eine
    /// Unity-Einheit; Kachel (tx, ty) liegt bei Zelle (originX + tx, originY + Höhe - 1 - ty).</summary>
    public sealed class TerrainMap
    {
        public const byte Grass = 0, Flowers = 1, Path = 2, Water = 3, Sand = 4, Tree = 5, Rock = 6, Stone = 7,
            Wall = 8, Bridge = 9, Portal = 10;

        public int Width { get; }
        public int Height { get; }
        public int OriginX { get; }
        public int OriginY { get; }
        public byte[] Tiles { get; }

        public TerrainMap(int width, int height, int originX, int originY, byte[] tiles)
        {
            if (width <= 0 || height <= 0 || tiles == null || tiles.Length != width * height)
                throw new ArgumentException("terrain: Maße passen nicht zu den Kacheldaten.");
            Width = width;
            Height = height;
            OriginX = originX;
            OriginY = originY;
            Tiles = tiles;
        }

        public static TerrainMap Parse(byte[] data)
        {
            if (data == null || data.Length < 12 || data[0] != 'L' || data[1] != 'T' || data[2] != 'R' || data[3] != '1')
                throw new InvalidDataException("terrain.bin: Kopf 'LTR1' fehlt.");
            int w = data[4] | (data[5] << 8);
            int h = data[6] | (data[7] << 8);
            int ox = (short)(data[8] | (data[9] << 8));
            int oy = (short)(data[10] | (data[11] << 8));
            if (data.Length != 12 + w * h) throw new InvalidDataException("terrain.bin: falsche Länge.");
            var tiles = new byte[w * h];
            Buffer.BlockCopy(data, 12, tiles, 0, tiles.Length);
            return new TerrainMap(w, h, ox, oy, tiles);
        }

        public byte At(int tx, int ty)
        {
            return tx >= 0 && ty >= 0 && tx < Width && ty < Height ? Tiles[ty * Width + tx] : Rock;
        }
    }

    /// <summary>Deko-Eintrag aus props.json (Kachelkoordinaten, Zeile 0 = Norden).</summary>
    public sealed class TerrainProp
    {
        public string Type = "";
        public string Kind = "";
        public int Tx, Ty, W = 1, H = 1;
        public double V;
    }

    /// <summary>Gezeichnetes Deko-Bild: RGBA-Bildpunkte (Zeile 0 = oben) und Fußpunkt (Ox, Oy) im Bild.</summary>
    public sealed class PropImage
    {
        public int Width, Height, Ox, Oy;
        public byte[] Rgba = Array.Empty<byte>();
    }

    /// <summary>Zeichnet den Boden einer Karte im Pixel-Stil der Figuren: 32 Bildpunkte je Kachel, Ufer/Wege/Felsen
    /// aus den Nachbarkacheln gemischt (runde Kanten statt Kacheln), Felswände nach Süden mit Schatten, Wald auf den
    /// Bergen, Brücken und Blumen kachelgenau darüber. Deko (Bäume, Häuser, Denkmäler, Laternen, Brunnen) entsteht als
    /// eigene Bilder. Alles fest aus den Kacheln berechnet (gleiche Karte = gleiches Bild), ohne UnityEngine, damit es
    /// auf einem Hintergrund-Thread laufen und per dotnet test geprüft werden kann. Portiert aus dem eigenen
    /// lumiis-2d-Kartenstil (client/mapart.js).</summary>
    public sealed class TerrainArt
    {
        public const int TilePixels = 32;
        public const int ChunkTiles = 16;
        public const int ChunkPixels = ChunkTiles * TilePixels;
        private const int TP = TilePixels;
        private const int Face = 22; // Höhe der Felswand in Bildpunkten
        private const int Margin = Face + 8; // Rand um den Chunk für Nachbar-Abfragen

        private const byte CWater = 0, CSand = 1, CGrass = 2, CDirt = 3, CStone = 4, CRock = 5;
        private static readonly byte[] Order = { CRock, CStone, CDirt, CGrass, CSand, CWater };
        private static readonly double[] Edge = { 0.4, 0.5, 0.45, 0.5, 0.18, 0.55 };

        // ---------- Farben (Wiese, Shinsoo) ----------
        private struct C3
        {
            public int R, G, B;
            public C3(int r, int g, int b) { R = r; G = g; B = b; }
        }

        private static C3 Hex(string s)
        {
            return new C3(Convert.ToInt32(s.Substring(1, 2), 16), Convert.ToInt32(s.Substring(3, 2), 16), Convert.ToInt32(s.Substring(5, 2), 16));
        }

        private static C3[] Pal(params string[] hex)
        {
            var r = new C3[hex.Length];
            for (int i = 0; i < hex.Length; i++) r[i] = Hex(hex[i]);
            return r;
        }

        private static readonly C3[] GrassP = Pal("#2c4a22", "#3a5f29", "#47722f", "#558436", "#67963e", "#7ea64a", "#9cba5c");
        private static readonly C3[] DirtP = Pal("#5e4029", "#7a5636", "#946c44", "#a98052", "#bd9663", "#d1ae7c");
        private static readonly C3[] SandP = Pal("#9c8559", "#b49d6c", "#c8b37f", "#d9c693", "#e7d8aa");
        private static readonly C3[] WaterP = Pal("#2e5462", "#356170", "#3e6e7c", "#4a7c88", "#5c8c95", "#76a3a6", "#aacbc2", "#dcece4");
        private static readonly C3[] PlazaP = Pal("#8f7c5c", "#a99472", "#bba684", "#cbb794", "#d9c7a6");
        private static readonly C3[] StoneP = Pal("#6a645b", "#8a8477", "#a09a8b", "#b3ad9d", "#c4bead", "#d6d0c0");
        private static readonly C3[] RockP = Pal("#3e3a37", "#55504a", "#6b655d", "#837c71", "#9b9486", "#b2ab9b", "#c9c2b2");
        private static readonly C3[] MossP = Pal("#2a3f22", "#36502a", "#446432");
        private static readonly C3[] ForestP = Pal("#16261a", "#1e3422", "#28452a", "#335634", "#40683c");

        // ---------- Rauschen (fest, ohne Zufall) ----------
        private static readonly float[] HashTable = BuildHashTable();
        private static readonly double[] Bayer = BuildBayer();

        private static float[] BuildHashTable()
        {
            var t = new float[1 << 20];
            for (int i = 0; i < t.Length; i++) t[i] = (float)HashSlow(i & 1023, i >> 10);
            return t;
        }

        private static double[] BuildBayer()
        {
            int[] m = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
            var r = new double[16];
            for (int i = 0; i < 16; i++) r[i] = (m[i] + 0.5) / 16 - 0.5;
            return r;
        }

        private static double HashSlow(int x, int y)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263;
                h = (h ^ (int)((uint)h >> 13)) * 1274126177;
                return (uint)(h ^ (int)((uint)h >> 16)) / 4294967296.0;
            }
        }

        private static double H2(int x, int y)
        {
            return HashTable[((y & 1023) << 10) | (x & 1023)];
        }

        private static double VNoise(double x, double y)
        {
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
            double fx = x - x0, fy = y - y0;
            double sx = fx * fx * (3 - 2 * fx), sy = fy * fy * (3 - 2 * fy);
            double a = H2(x0, y0), b = H2(x0 + 1, y0), c = H2(x0, y0 + 1), d = H2(x0 + 1, y0 + 1);
            return (a + (b - a) * sx) * (1 - sy) + (c + (d - c) * sx) * sy;
        }

        private static int JsRound(double v)
        {
            return (int)Math.Floor(v + 0.5);
        }

        private static double BayerAt(int x, int y)
        {
            return Bayer[(y & 3) * 4 + (x & 3)];
        }

        private static C3 Ramp(C3[] pal, int start, int count, double v, int x, int y, double spread = 1)
        {
            int i = JsRound(v * (count - 1) + BayerAt(x, y) * spread);
            if (i < 0) i = 0;
            else if (i >= count) i = count - 1;
            return pal[start + i];
        }

        private static double Clamp01(double v)
        {
            return v < 0 ? 0 : v > 1 ? 1 : v;
        }

        // ---------- Vorberechnung je Karte ----------
        private readonly TerrainMap _map;
        private readonly int _w, _h;
        private readonly byte[] _cls;   // (w+2)*(h+2), Rand = Fels
        private readonly byte[] _uni;   // 3x3-Nachbarschaft gleich
        private readonly byte[] _rock2;
        private readonly byte[] _depth; // Felstiefe 0..8
        private readonly bool _hasVillage;
        private readonly double _villageCx, _villageCy, _plazaR;

        public TerrainMap Map => _map;
        public int ChunksX => (_w + ChunkTiles - 1) / ChunkTiles;
        public int ChunksY => (_h + ChunkTiles - 1) / ChunkTiles;

        /// <param name="villageCx">Mitte des Dorfplatzes in Kacheln (Steinkreis), negativ = keiner.</param>
        public TerrainArt(TerrainMap map, double villageCx = -1, double villageCy = -1, double villageRadiusTiles = 0)
        {
            _map = map;
            _w = map.Width;
            _h = map.Height;
            int W2 = _w + 2;
            _cls = new byte[W2 * (_h + 2)];
            for (int y = -1; y <= _h; y++)
                for (int x = -1; x <= _w; x++)
                    _cls[(y + 1) * W2 + x + 1] = TileClass(x, y);

            _uni = new byte[_w * _h];
            for (int y = 0; y < _h; y++)
                for (int x = 0; x < _w; x++)
                {
                    byte c0 = _cls[(y + 1) * W2 + x + 1];
                    bool ok = true;
                    for (int dy = -1; dy <= 1 && ok; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                            if (_cls[(y + 1 + dy) * W2 + x + 1 + dx] != c0) { ok = false; break; }
                    _uni[y * _w + x] = (byte)(ok ? 1 : 0);
                }

            _rock2 = new byte[_w * _h];
            for (int y = 0; y < _h; y++)
                for (int x = 0; x < _w; x++)
                    _rock2[y * _w + x] = (byte)(y + 2 < _h + 1 && _cls[(y + 3) * W2 + x + 1] == CRock ? 1 : 0);

            _depth = new byte[_w * _h];
            for (int i = 0; i < _depth.Length; i++) _depth[i] = 255;
            var q = new List<int>();
            for (int i = 0; i < _w * _h; i++)
                if (_cls[(i / _w + 1) * W2 + i % _w + 1] != CRock) { _depth[i] = 0; q.Add(i); }
            for (int qi = 0; qi < q.Count; qi++)
            {
                int i = q[qi], x = i % _w, y = i / _w, d = _depth[i];
                if (d >= 8) continue;
                TryDepth(x + 1, y, d, q);
                TryDepth(x - 1, y, d, q);
                TryDepth(x, y + 1, d, q);
                TryDepth(x, y - 1, d, q);
            }
            for (int i = 0; i < _depth.Length; i++) if (_depth[i] == 255) _depth[i] = 8;

            _hasVillage = villageCx >= 0 && villageCy >= 0;
            _villageCx = villageCx;
            _villageCy = villageCy;
            _plazaR = (villageRadiusTiles > 0 ? villageRadiusTiles * 6.3 / 13 : 6.3) * TP;
        }

        private void TryDepth(int x, int y, int d, List<int> q)
        {
            if (x < 0 || y < 0 || x >= _w || y >= _h) return;
            int j = y * _w + x;
            if (_depth[j] > d + 1) { _depth[j] = (byte)(d + 1); q.Add(j); }
        }

        private byte TileClass(int x, int y)
        {
            if (x < 0 || y < 0 || x >= _w || y >= _h) return CRock;
            switch (_map.Tiles[y * _w + x])
            {
                case TerrainMap.Water:
                case TerrainMap.Bridge: return CWater;
                case TerrainMap.Sand: return CSand;
                case TerrainMap.Path:
                case TerrainMap.Portal: return CDirt;
                case TerrainMap.Stone: return CStone;
                case TerrainMap.Rock: return CRock;
                case TerrainMap.Wall: return WallGround(x, y); // unter Häusern: Pflaster neben Pflaster, sonst Weg
                default: return CGrass;
            }
        }

        private byte WallGround(int x, int y)
        {
            for (int r = 1; r <= 2; r++)
            {
                if (_map.At(x + r, y) == TerrainMap.Stone || _map.At(x - r, y) == TerrainMap.Stone
                    || _map.At(x, y + r) == TerrainMap.Stone || _map.At(x, y - r) == TerrainMap.Stone)
                    return CStone;
            }
            return CDirt;
        }

        /// <summary>Grobes Bild, 1 Bildpunkt je Kachel, RGBA (Zeile 0 = Norden) - Ersatz, solange ein Chunk noch
        /// gerechnet wird, und Minimap.</summary>
        public byte[] PaintLowRes()
        {
            C3[] col = { WaterP[2], SandP[2], GrassP[3], DirtP[3], PlazaP[2], RockP[3] };
            var rgba = new byte[_w * _h * 4];
            for (int y = 0; y < _h; y++)
                for (int x = 0; x < _w; x++)
                {
                    C3 c = col[_cls[(y + 1) * (_w + 2) + x + 1]];
                    if (_map.Tiles[y * _w + x] == TerrainMap.Rock && _depth[y * _w + x] >= 4) c = ForestP[2];
                    int o = (y * _w + x) * 4;
                    rgba[o] = (byte)c.R; rgba[o + 1] = (byte)c.G; rgba[o + 2] = (byte)c.B; rgba[o + 3] = 255;
                }
            return rgba;
        }

        /// <summary>Durchschnittsfarbe des Felsens - für den Rand außerhalb der Kachelkarte.</summary>
        public static void RockColor(out byte r, out byte g, out byte b)
        {
            r = (byte)ForestP[1].R; g = (byte)ForestP[1].G; b = (byte)ForestP[1].B;
        }

        // Klasse an einem Bildpunkt
        private byte ClassAt(int px0, int py0)
        {
            int tx = FloorDiv(px0, TP), ty = FloorDiv(py0, TP);
            if (tx >= 0 && ty >= 0 && tx < _w && ty < _h && _uni[ty * _w + tx] == 1) return _cls[(ty + 1) * (_w + 2) + tx + 1];
            double px = px0 + (VNoise(px0 / 46.0, py0 / 46.0) - 0.5) * 22, py = py0 + (VNoise(px0 / 46.0 + 71, py0 / 46.0 + 29) - 0.5) * 22;
            double fx = (px + 0.5) / TP - 0.5, fy = (py + 0.5) / TP - 0.5;
            int x0 = (int)Math.Floor(fx), y0 = (int)Math.Floor(fy);
            double ax = fx - x0, ay = fy - y0;
            byte c00 = ClsSafe(x0, y0), c10 = ClsSafe(x0 + 1, y0), c01 = ClsSafe(x0, y0 + 1), c11 = ClsSafe(x0 + 1, y0 + 1);
            if (c00 == c10 && c00 == c01 && c00 == c11) return c00;
            double w00 = (1 - ax) * (1 - ay), w10 = ax * (1 - ay), w01 = (1 - ax) * ay, w11 = ax * ay;
            double n = VNoise(px0 / 13.0, py0 / 13.0) * 0.65 + VNoise(px0 / 5.0 + 31, py0 / 5.0 + 17) * 0.35;
            byte best = c00;
            double bw = -1;
            for (int k = 0; k < Order.Length; k++)
            {
                byte c = Order[k];
                double wgt = (c00 == c ? w00 : 0) + (c10 == c ? w10 : 0) + (c01 == c ? w01 : 0) + (c11 == c ? w11 : 0);
                if (wgt <= 0) continue;
                if (wgt > 0.5 + (n - 0.5) * Edge[c]) return c;
                if (wgt > bw) { bw = wgt; best = c; }
            }
            return best;
        }

        private byte ClsSafe(int tx, int ty)
        {
            if (tx < -1 || ty < -1 || tx > _w || ty > _h) return CRock;
            return _cls[(ty + 1) * (_w + 2) + tx + 1];
        }

        private static int FloorDiv(int a, int b)
        {
            int q = a / b;
            if ((a % b != 0) && ((a < 0) != (b < 0))) q--;
            return q;
        }

        private double DepthAt(int px, int py)
        {
            double fx = Math.Min(_w - 1.001, Math.Max(0, (px + 0.5) / TP - 0.5)), fy = Math.Min(_h - 1.001, Math.Max(0, (py + 0.5) / TP - 0.5));
            int x0 = (int)Math.Floor(fx), y0 = (int)Math.Floor(fy);
            double ax = fx - x0, ay = fy - y0;
            int i = y0 * _w + x0;
            int x1 = x0 + 1 < _w ? 1 : 0, y1 = y0 + 1 < _h ? _w : 0;
            return (_depth[i] * (1 - ax) + _depth[i + x1] * ax) * (1 - ay) + (_depth[i + y1] * (1 - ax) + _depth[i + y1 + x1] * ax) * ay;
        }

        // ---------- Chunk ----------

        /// <summary>Zeichnet Chunk (cx, cy) (cy = 0 im Norden) als RGBA32-Bildpunkte (0xAABBGGRR, also Bytes R,G,B,A).
        /// Mit flipRows liegt Zeile 0 im Süden (Unity-Texturen haben den Ursprung unten links).</summary>
        public uint[] PaintChunk(int cx, int cy, bool flipRows)
        {
            const int S = ChunkPixels;
            int ox = cx * S, oy = cy * S;
            int SW = S + 2 * Margin;
            var K = new byte[SW * SW];
            for (int y = 0; y < SW; y++)
            {
                for (int x = 0; x < SW;)
                {
                    int px = ox + x - Margin, py = oy + y - Margin;
                    int tx = FloorDiv(px, TP), ty = FloorDiv(py, TP);
                    if (tx >= 0 && ty >= 0 && tx < _w && ty < _h && _uni[ty * _w + tx] == 1)
                    {
                        int run = Math.Min(SW - x, (tx + 1) * TP - px);
                        byte c = _cls[(ty + 1) * (_w + 2) + tx + 1];
                        for (int i = 0; i < run; i++) K[y * SW + x + i] = c;
                        x += run;
                    }
                    else
                    {
                        K[y * SW + x] = ClassAt(px, py);
                        x++;
                    }
                }
            }

            var ctx = new Ctx(K, SW);
            var canvas = new PixelCanvas(S, S);
            for (int y = 0; y < S; y++)
            {
                for (int x = 0; x < S; x++)
                {
                    int px = ox + x, py = oy + y;
                    byte cl = ctx.K(x, y);
                    int tpx = px / TP, tpy = py / TP;
                    bool inside = tpx < _w && tpy < _h;
                    int ti = inside ? tpy * _w + tpx : 0;
                    bool U = inside && _uni[ti] == 1;
                    C3 col;
                    if (cl == CGrass) col = GrassPx(U, ctx, x, y, px, py);
                    else if (cl == CDirt) col = DirtPx(U, ctx, x, y, px, py);
                    else if (cl == CStone) col = StonePx(U, ctx, x, y, px, py);
                    else if (cl == CSand) col = SandPx(U, ctx, x, y, px, py);
                    else if (cl == CWater) col = WaterPx(U, ctx, x, y, px, py);
                    else col = RockPx(U && _rock2[ti] == 1, ctx, x, y, px, py);
                    if (cl != CRock && !U)
                    {
                        double s = 0;
                        for (int i = 1; i <= 7; i++)
                            if (ctx.K(x - (i >> 2), y - i) == CRock) { s = 1 - (i - 1) / 8.0; break; }
                        if (s > 0)
                        {
                            double f = 1 - 0.32 * s;
                            col = new C3((int)(col.R * f), (int)(col.G * f), (int)(col.B * f));
                        }
                    }
                    canvas.Set(x, y, col.R, col.G, col.B);
                }
            }

            // Kachelgenaue Auflagen (mit einer Kachel Rand, damit Wald und Geländer nicht an Chunkgrenzen abreißen)
            for (int ty = cy * ChunkTiles - 1; ty <= (cy + 1) * ChunkTiles; ty++)
            {
                for (int tx = cx * ChunkTiles - 1; tx <= (cx + 1) * ChunkTiles; tx++)
                {
                    if (tx < 0 || ty < 0 || tx >= _w || ty >= _h) continue;
                    byte t = _map.Tiles[ty * _w + tx];
                    int dx = (tx - cx * ChunkTiles) * TP, dy = (ty - cy * ChunkTiles) * TP;
                    if (t == TerrainMap.Bridge) BridgeTile(canvas, tx, ty, dx, dy);
                    else if (t == TerrainMap.Flowers) FlowersTile(canvas, tx, ty, dx, dy);
                    else if (t == TerrainMap.Rock && _depth[ty * _w + tx] >= 4
                             && VNoise((tx * TP + 16) / 30.0 + 7, (ty * TP + 16) / 30.0 + 3) > 0.6)
                        ForestClump(canvas, tx, ty, dx, dy);
                }
            }

            return canvas.ToPacked(flipRows);
        }

        private sealed class Ctx
        {
            private readonly byte[] _k;
            private readonly int _sw;

            public Ctx(byte[] k, int sw) { _k = k; _sw = sw; }

            public byte K(int x, int y)
            {
                int xx = x + Margin, yy = y + Margin;
                if (xx < 0 || yy < 0 || xx >= _sw || yy >= _sw) return CRock;
                return _k[yy * _sw + xx];
            }

            public int EdgeTo(int x, int y, byte cls, int r = 1)
            {
                for (int i = 1; i <= r; i++)
                    if (K(x + i, y) == cls || K(x - i, y) == cls || K(x, y + i) == cls || K(x, y - i) == cls) return i;
                return 0;
            }
        }

        private static C3 GrassPx(bool U, Ctx k, int x, int y, int px, int py)
        {
            if (!U && (k.EdgeTo(x, y, CDirt) > 0 || k.EdgeTo(x, y, CStone) > 0)) return GrassP[1];
            double big = VNoise(px / 70.0, py / 70.0), mid = VNoise(px / 18.0 + 5, py / 18.0 + 9);
            double v = 0.32 + big * 0.38 + mid * 0.22;
            double hs = H2(px, py);
            if (H2(px, py - 1) < 0.03) return GrassP[2];
            if (hs < 0.03) return big > 0.5 ? GrassP[6] : GrassP[5];
            if (hs > 0.988) return GrassP[1];
            if (!U && (k.EdgeTo(x, y, CSand, 3) > 0 || k.EdgeTo(x, y, CWater, 3) > 0)) v += 0.08;
            return Ramp(GrassP, 2, 4, Math.Min(1, v), px, py);
        }

        private static C3 DirtPx(bool U, Ctx k, int x, int y, int px, int py)
        {
            if (!U && k.EdgeTo(x, y, CGrass) > 0) return DirtP[1];
            double hs = H2(px + 7, py + 3);
            if (hs < 0.012) return DirtP[5];
            if (H2(px + 7, py + 2) < 0.012) return DirtP[0];
            double v = 0.35 + VNoise(px / 22.0, py / 22.0) * 0.4 + VNoise(px / 6.0, py / 6.0) * 0.15;
            int e = U ? 0 : k.EdgeTo(x, y, CGrass, 4);
            return Ramp(DirtP, 1, 4, Math.Min(1, v + (e > 0 ? -0.12 * (5 - e) / 4 : 0.05)), px, py);
        }

        private C3 StonePx(bool U, Ctx k, int x, int y, int px, int py)
        {
            if (!U && (k.EdgeTo(x, y, CGrass) > 0 || k.EdgeTo(x, y, CDirt) > 0)) return StoneP[0];
            if (_hasVillage)
            {
                // Dorfplatz: in der Mitte ein runder Platz aus Steinringen, darum festgetretene helle Erde
                double dx = px + 0.5 - _villageCx * TP, dy = py + 0.5 - _villageCy * TP, r = Math.Sqrt(dx * dx + dy * dy);
                double R = _plazaR;
                if (r >= R + 5)
                {
                    if (r < R + 8) return StoneP[1];
                    if (H2(px + 1, py + 9) < 0.01) return PlazaP[4];
                    if (H2(px + 1, py + 8) < 0.01) return PlazaP[0];
                    return Ramp(PlazaP, 1, 4, 0.3 + VNoise(px / 24.0, py / 24.0) * 0.45 + VNoise(px / 7.0, py / 7.0) * 0.15, px, py);
                }
                if (r >= R) return r < R + 1 ? StoneP[0] : StoneP[r < R + 3 ? 4 : 2];
                int ring = (int)Math.Floor(r / 13);
                double rr = r - ring * 13;
                int segs = Math.Max(6, JsRound(2 * Math.PI * (ring + 0.5) * 13 / 19));
                double a = (Math.Atan2(dy, dx) / (2 * Math.PI) + 1 + H2(ring, 7) / segs) * segs;
                int seg = (int)Math.Floor(a);
                double fa = (a - seg) * (2 * Math.PI * r / segs);
                if (rr < 1 || fa < 1) return StoneP[0];
                double tone = H2(seg + 31, ring);
                if (rr < 2.2 || fa < 2.2) return StoneP[tone > 0.5 ? 5 : 4];
                if (rr > 11.5) return StoneP[1];
                return Ramp(StoneP, 2, 3, 0.25 + tone * 0.5 + VNoise(px / 9.0, py / 9.0) * 0.15, px, py, 0.7);
            }
            // Platten in versetzten Reihen
            int row = (int)Math.Floor(py / 12.0), ry = py - row * 12;
            int off = (int)Math.Floor(H2(row, 91) * 16);
            int col = (int)Math.Floor((px + off) / 17.0), rx = px + off - col * 17;
            if (ry == 11 || rx == 16) return StoneP[0];
            double t2 = H2(col, row);
            if (ry == 0 || rx == 0) return StoneP[t2 > 0.5 ? 5 : 4];
            if (ry == 10 || rx == 15) return StoneP[1];
            return Ramp(StoneP, 2, 3, 0.3 + t2 * 0.45 + VNoise(px / 9.0, py / 9.0) * 0.15, px, py, 0.7);
        }

        private static C3 SandPx(bool U, Ctx k, int x, int y, int px, int py)
        {
            int wet = U ? 0 : k.EdgeTo(x, y, CWater, 4);
            if (wet == 1) return SandP[0];
            if (!U && k.EdgeTo(x, y, CGrass) > 0) return GrassP[3];
            double hs = H2(px + 3, py + 5);
            if (hs < 0.015) return SandP[0];
            if (hs > 0.985) return SandP[4];
            double v = 0.45 + VNoise(px / 26.0, py / 26.0) * 0.35 + VNoise(px / 7.0, py / 7.0) * 0.12;
            if (wet > 0) v -= 0.25;
            return Ramp(SandP, 1, 4, Clamp01(v), px, py);
        }

        private static C3 WaterPx(bool U, Ctx k, int x, int y, int px, int py)
        {
            int e = 0;
            for (int i = 1; i <= 10 && e == 0 && !U; i++)
                if (k.K(x + i, y) != CWater || k.K(x - i, y) != CWater || k.K(x, y + i) != CWater || k.K(x, y - i) != CWater) e = i;
            if (e == 1) return WaterP[7];
            if (e == 2) return WaterP[6];
            double depth = e > 0 ? e / 10.0 : 1;
            double v = 0.15 + (1 - depth) * 0.55 + VNoise(px / 40.0, py / 40.0) * 0.2;
            double wv = VNoise(px / 11.0, py / 2.6);
            if (wv > 0.9 && H2(px >> 2, py) > 0.45) return WaterP[5];
            if (wv > 0.86) return WaterP[4];
            return Ramp(WaterP, 0, 5, Math.Min(1, v), px, py);
        }

        private C3 RockPx(bool U, Ctx k, int x, int y, int px, int py)
        {
            int f = 0;
            for (int i = 1; i <= Face && !U; i++) if (k.K(x, y + i) != CRock) { f = i; break; }
            double dep = DepthAt(px, py);
            if (f > 0)
            {
                // Felswand: senkrechte Rillen, unten dunkler, oben eine helle Kante
                double t = f / (double)Face;
                if (f == Face || (f > Face - 2 && k.K(x, y - 1) == CRock && k.K(x, y - 2) == CRock && H2(px, 5) > 0.2)) return RockP[5];
                double groove = H2(px >> 1, (int)Math.Floor(py / 7.0) + (px & 1));
                double v = 0.15 + t * 0.45 + (groove - 0.5) * 0.25;
                if (H2(px, py) < 0.04) v -= 0.2;
                if (f <= 2) return RockP[0];
                return Ramp(RockP, 0, 5, Clamp01(v), px, py);
            }
            if (!U && (k.K(x, y - 1) != CRock || k.K(x - 1, y) != CRock || k.K(x + 1, y) != CRock)) return RockP[2];
            double n2 = VNoise(px / 5.0 + 9, py / 5.0 + 4);
            if (dep >= 4 && VNoise(px / 30.0 + 7, py / 30.0 + 3) > 0.6) return Ramp(MossP, 0, 3, n2, px, py);
            const double S = 34;
            double wx = px + (VNoise(px / 20.0, py / 20.0) - 0.5) * 16, wy = py + (VNoise(px / 20.0 + 40, py / 20.0) - 0.5) * 16;
            int gx = (int)Math.Floor(wx / S), gy = (int)Math.Floor(wy / S);
            double d1 = 1e9, d2 = 1e9, bx = 0, by = 0, bid = 0;
            for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++)
                {
                    double cxp = (gx + i + 0.15 + H2(gx + i, gy + j) * 0.7) * S, cyp = (gy + j + 0.15 + H2(gy + j + 77, gx + i) * 0.7) * S;
                    double ddx = wx - cxp, ddy = (wy - cyp) * 1.3;
                    double dd = ddx * ddx + ddy * ddy;
                    if (dd < d1) { d2 = d1; d1 = dd; bx = cxp; by = cyp; bid = H2(gx + i + 5, gy + j + 9); }
                    else if (dd < d2) d2 = dd;
                }
            double edge = Math.Sqrt(d2) - Math.Sqrt(d1);
            if (edge < 1.1 && bid > 0.35) return RockP[1];
            double lx = (wx - bx) / S, ly = (wy - by) / S;
            double vv = 0.52 - (lx + ly * 1.2) * 0.6 + (bid - 0.5) * 0.2 + (n2 - 0.5) * 0.18 - Math.Min(dep, 6) * 0.025;
            if (edge < 3) vv -= 0.12;
            return Ramp(RockP, 1, 6, Clamp01(vv), px, py);
        }

        // ---------- Kachelgenaue Auflagen ----------

        private static readonly C3[] BridgeWood = Pal("#5a3a20", "#7a5230", "#946840", "#ae8254");

        private static bool Walk(byte t)
        {
            return t == TerrainMap.Bridge || t == TerrainMap.Path || t == TerrainMap.Stone || t == TerrainMap.Portal;
        }

        private void BridgeTile(PixelCanvas g, int tx, int ty, int dx, int dy)
        {
            C3[] wood = BridgeWood;
            bool horiz = (Walk(_map.At(tx - 1, ty)) || Walk(_map.At(tx + 1, ty)))
                         && !(_map.At(tx, ty - 1) == TerrainMap.Bridge && _map.At(tx, ty + 1) == TerrainMap.Bridge && _map.At(tx - 1, ty) != TerrainMap.Bridge);
            g.Fill(dx, dy, TP, TP, wood[1]);
            if (horiz)
            {
                for (int x = 0; x < TP; x += 6) { g.Fill(dx + x, dy, 1, TP, wood[0]); g.Fill(dx + x + 1, dy, 1, TP, wood[3]); }
                for (int x = 2; x < TP; x += 6) if (H2(tx * 32 + x, ty) < 0.5) g.Fill(dx + x, dy + 2, 3, TP - 4, wood[2]);
                if (!Walk(_map.At(tx, ty - 1)))
                {
                    g.Fill(dx, dy, TP, 3, wood[0]); g.Fill(dx, dy, TP, 1, wood[3]);
                    for (int x = 2; x < TP; x += 10) g.Fill(dx + x, dy - 4, 3, 7, wood[0]);
                }
                if (!Walk(_map.At(tx, ty + 1)))
                {
                    g.Fill(dx, dy + TP - 3, TP, 3, wood[0]); g.Fill(dx, dy + TP - 3, TP, 1, wood[3]);
                    for (int x = 2; x < TP; x += 10) g.Fill(dx + x, dy + TP - 6, 3, 6, wood[0]);
                }
            }
            else
            {
                for (int y = 0; y < TP; y += 6) { g.Fill(dx, dy + y, TP, 1, wood[0]); g.Fill(dx, dy + y + 1, TP, 1, wood[3]); }
                for (int y = 2; y < TP; y += 6) if (H2(tx, ty * 32 + y) < 0.5) g.Fill(dx + 2, dy + y, TP - 4, 3, wood[2]);
                if (!Walk(_map.At(tx - 1, ty)))
                {
                    g.Fill(dx, dy, 3, TP, wood[0]); g.Fill(dx, dy, 1, TP, wood[3]);
                    for (int y = 2; y < TP; y += 10) g.Fill(dx, dy + y, 4, 3, wood[0]);
                }
                if (!Walk(_map.At(tx + 1, ty)))
                {
                    g.Fill(dx + TP - 3, dy, 3, TP, wood[0]); g.Fill(dx + TP - 1, dy, 1, TP, wood[3]);
                    for (int y = 2; y < TP; y += 10) g.Fill(dx + TP - 4, dy + y, 4, 3, wood[0]);
                }
            }
        }

        private static readonly C3[][] FlowerColors =
        {
            Pal("#f4e9f0", "#c9a4c0"), Pal("#f6d65a", "#c89a2a"), Pal("#ef8fa8", "#b8506c"), Pal("#ffffff", "#b8c4cc")
        };

        private static readonly C3 Stem = Hex("#3a5f29");

        private static void FlowersTile(PixelCanvas g, int tx, int ty, int dx, int dy)
        {
            int n = 3 + (int)Math.Floor(H2(tx, ty) * 4);
            for (int i = 0; i < n; i++)
            {
                int x = dx + 3 + (int)Math.Floor(H2(tx * 9 + i, ty) * 24), y = dy + 4 + (int)Math.Floor(H2(tx, ty * 9 + i) * 22);
                C3[] ab = FlowerColors[(int)Math.Floor(H2(tx + i, ty + 3) * FlowerColors.Length)];
                g.Fill(x + 1, y + 2, 1, 3, Stem);
                g.Fill(x, y, 3, 1, ab[0]); g.Fill(x + 1, y - 1, 1, 3, ab[0]); g.Fill(x + 1, y, 1, 1, ab[1]);
            }
        }

        // Wald auf den Bergen (von oben gesehen): dichte runde Kronen in dunklem Grün
        private static void ForestClump(PixelCanvas g, int tx, int ty, int dx, int dy)
        {
            C3[] pal = ForestP;
            for (int i = 0; i < 6; i++)
            {
                int cx = dx + 2 + (int)Math.Floor(H2(tx * 5 + i, ty * 3) * 28), cy = dy + 2 + (int)Math.Floor(H2(tx * 3, ty * 5 + i) * 28);
                int r = 9 + (int)Math.Floor(H2(tx + i * 11, ty + 5) * 5);
                // Kronen haben eigene Weltkoordinaten für das Rauschen, damit sie an Chunkgrenzen gleich aussehen
                int wcx = tx * TP + (cx - dx), wcy = ty * TP + (cy - dy);
                for (int y = -r - 1; y <= r + 1; y++)
                    for (int x = -r - 1; x <= r + 1; x++)
                    {
                        double q = (x * x + y * y) / (double)(r * r);
                        if (q > 1.18) continue;
                        C3 c;
                        if (q > 1) c = pal[0];
                        else
                        {
                            double v = 0.55 - (x + y) / (r * 2.6) + (H2(wcx + x, wcy + y) - 0.5) * 0.2;
                            int idx = JsRound(v * 4 + BayerAt(wcx + x, wcy + y) * 0.6);
                            c = pal[Math.Max(1, Math.Min(4, idx))];
                        }
                        g.Set(cx + x, cy + y, c.R, c.G, c.B);
                    }
            }
        }

        /// <summary>Wirbel für Portale (Breite = size Bildpunkte, mittig), RGBA mit Transparenz.</summary>
        public static PropImage PaintPortal(int size, double rot)
        {
            var g = new PixelCanvas(size, size);
            double c = (size - 1) / 2.0, scale = size / 32.0;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    double ax = x - c, ay = y - c, r = Math.Sqrt(ax * ax + ay * ay) / 2 / scale;
                    double arm = Math.Sin(3 * (Math.Atan2(ay, ax) + rot) + r * 1.15);
                    if (r < 1.6) g.Blend(x, y, 255, 255, 255, 1);
                    else if (arm > 0.4 && r < 7.6)
                    {
                        if (r < 3) g.Blend(x, y, 0xe0, 0xd8, 0xff, 1);
                        else if (r < 5.2) g.Blend(x, y, 0xa8, 0x90, 0xff, 1);
                        else g.Blend(x, y, 0x7a, 0x68, 0xf0, 1);
                    }
                    else
                    {
                        double a = 0.18 + 0.5 * Math.Max(0, 1 - r / 9);
                        if (r < 9.5) g.Blend(x, y, 96, 72, 236, a);
                    }
                }
            return g.ToImage(size / 2, size / 2);
        }

        // ---------- Deko-Bilder ----------

        /// <summary>Bäume aus den TREE-Kacheln plus die Einträge aus props.json.</summary>
        public List<TerrainProp> CollectProps(IEnumerable<TerrainProp> extra)
        {
            var list = new List<TerrainProp>();
            for (int y = 0; y < _h; y++)
                for (int x = 0; x < _w; x++)
                    if (_map.Tiles[y * _w + x] == TerrainMap.Tree)
                        list.Add(new TerrainProp { Type = "tree", Tx = x, Ty = y, V = H2(x * 7 + 3, y * 13 + 1) });
            if (extra != null) list.AddRange(extra);
            return list;
        }

        /// <summary>Schlüssel für das gemeinsame Bild einer Deko (gleiche Bäume/Häuser teilen sich ein Bild).</summary>
        public static string ImageKey(TerrainProp p)
        {
            switch (p.Type)
            {
                case "tree":
                    {
                        string kind = TreeKind(p.V);
                        return "tree|" + kind + (int)Math.Floor(p.V * 997 % 3);
                    }
                case "house": return "house|" + p.Kind + "|" + p.W + "|" + p.H;
                case "lantern":
                case "well":
                case "monument": return p.Type;
                default: return "";
            }
        }

        private static string TreeKind(double v)
        {
            return v < 0.62 ? "green" : v < 0.86 ? "dark" : v < 0.95 ? "autumn" : "red";
        }

        public static PropImage PaintProp(TerrainProp p)
        {
            switch (p.Type)
            {
                case "tree":
                    {
                        string kind = TreeKind(p.V);
                        int n = (int)Math.Floor(p.V * 997 % 3);
                        return PaintTree(kind, n + 1 + kind.Length * 11);
                    }
                case "house": return PaintHouse(p);
                case "lantern": return PaintLantern();
                case "well": return PaintWell();
                case "monument": return PaintMonument();
                default: return null;
            }
        }

        /// <summary>Fußpunkt der Deko in Kachel-Bildpunkten relativ zur linken oberen Ecke ihrer Grundfläche (wie
        /// PropArt.draw im Vorbild): liefert, wo der Bildpunkt (Ox, Oy) des Bildes liegt.</summary>
        public static void FootInFootprint(TerrainProp p, PropImage img, out double footX, out double footY)
        {
            double footLine = p.H * TP;
            if (p.Type == "tree") { footX = TP / 2.0; footY = footLine - 6; }
            else if (p.Type == "house") { footX = 0; footY = footLine; }
            else { footX = p.W * TP / 2.0; footY = footLine; }
        }

        private static readonly Dictionary<string, string[]> TreePals = new Dictionary<string, string[]>
        {
            { "green", new[] { "#22381c", "#2e4b22", "#3c5f2a", "#4d7432", "#62893c", "#7da04a" } },
            { "dark", new[] { "#1b2e1e", "#233d26", "#2e4e2e", "#3b6136", "#4c7640", "#62894c" } },
            { "autumn", new[] { "#5a2a14", "#7a3a18", "#9a4e1c", "#bc6a24", "#d88a34", "#ecae52" } },
            { "red", new[] { "#4a1a16", "#6a2420", "#8a3026", "#ab4230", "#c85a3a", "#e08050" } },
        };

        /// <summary>Baum: Krone aus Blättertrauben mit Licht von links oben, Stamm, Schatten. Fuß bei (44, 102).</summary>
        public static PropImage PaintTree(string kind, int seed)
        {
            const int W = 88, H = 108, ox = 44, oy = 102;
            var g = new PixelCanvas(W, H);
            C3[] pal = Pal(TreePals[kind]);
            g.FillEllipse(ox + 3, oy - 1, 26, 7, 30, 20, 30, 0.28);
            C3[] tr = Pal("#3a2618", "#5a3c24", "#76502e", "#8e6638");
            for (int y = oy - 34; y < oy; y++)
            {
                int flare = y > oy - 6 ? (y - (oy - 6)) >> 1 : 0;
                for (int x = ox - 5 - flare; x <= ox + 4 + flare; x++)
                {
                    double t = (x - (ox - 5 - flare)) / (double)(9 + 2 * flare);
                    C3 c = t < 0.25 ? tr[3] : t < 0.6 ? tr[2] : t < 0.85 ? tr[1] : tr[0];
                    g.Set(x, y, c.R, c.G, c.B);
                }
            }
            Func<int, double> rng = i => H2(seed * 977 + i * 13, i * 7 + 3);
            double[][] baseBlobs =
            {
                new double[] { ox, oy - 58, 30 }, new double[] { ox - 18, oy - 46, 20 }, new double[] { ox + 18, oy - 45, 21 },
                new double[] { ox - 6, oy - 76, 19 }, new double[] { ox + 12, oy - 70, 18 }
            };
            var blobs = new double[baseBlobs.Length][];
            for (int i = 0; i < baseBlobs.Length; i++)
                blobs[i] = new[]
                {
                    baseBlobs[i][0] + (rng(i) - 0.5) * 6, baseBlobs[i][1] + (rng(i + 9) - 0.5) * 6,
                    baseBlobs[i][2] * (0.9 + rng(i + 20) * 0.15)
                };
            Func<int, int, bool> inside = (x, y) =>
            {
                foreach (double[] b in blobs)
                {
                    double ddx = x - b[0], ddy = (y - b[1]) * 1.08;
                    if (ddx * ddx + ddy * ddy <= b[2] * b[2]) return true;
                }
                return false;
            };
            var tufts = new List<double[]>();
            for (int i = 0; i < 26; i++)
            {
                double a = rng(i + 40) * Math.PI * 2;
                double[] b = blobs[(int)Math.Floor(rng(i + 70) * blobs.Length)];
                double rr = Math.Sqrt(rng(i + 90)) * b[2] * 0.85;
                tufts.Add(new[] { b[0] + Math.Cos(a) * rr, b[1] + Math.Sin(a) * rr, 7 + rng(i + 110) * 5 });
            }
            tufts.Sort((p, q) => p[1].CompareTo(q[1]));
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    if (!inside(x, y)) continue;
                    const int cy0 = oy - 60;
                    double v = 0.55 - (y - cy0) / 70.0 - (x - ox) / 140.0;
                    foreach (double[] t in tufts)
                    {
                        double ddx = x - t[0], ddy = y - t[1], q = (ddx * ddx + ddy * ddy) / (t[2] * t[2]);
                        if (q < 1) v += (ddy < 0 ? 0.16 : -0.1) * (1 - q) + (ddx < 0 ? 0.05 : -0.04);
                        else if (q < 1.25 && ddy > 0) v -= 0.12;
                    }
                    v += (H2(x + seed, y) - 0.5) * 0.12;
                    int i = Math.Max(0, Math.Min(pal.Length - 1, JsRound(v * (pal.Length - 1) + BayerAt(x, y) * 0.6 + 1.2)));
                    g.Set(x, y, pal[i].R, pal[i].G, pal[i].B);
                }
            for (int i = 0; i < 10; i++)
            {
                double[] t = tufts[(int)Math.Floor(rng(i + 150) * tufts.Count)];
                int x = JsRound(t[0] - t[2] * 0.4), y = JsRound(t[1] - t[2] * 0.55);
                if (inside(x, y)) g.Fill(x, y, 2, 1, pal[5]);
            }
            g.Outline();
            return g.ToImage(ox, oy);
        }

        private static readonly C3[] RoofTile = Pal("#3c1410", "#5c1e16", "#7c2a1e", "#9a3826", "#b84a32", "#d06848");
        private static readonly C3 RoofRidge = Hex("#2e1410");
        private static readonly C3 RoofCap = Hex("#ece2d0");

        /// <summary>Haus im koreanischen Stil (Hanok) mit Shinsoo-Dach: Walmdach mit geschwungener Traufe und
        /// hochgezogenen Ecken, weißer First, Holzpfosten, Gitterfenster, Steinsockel mit Stufe.</summary>
        public static PropImage PaintHouse(TerrainProp p)
        {
            int fw = p.W * TP, fh = p.H * TP;
            const int ov = 14;
            int W = fw + 2 * ov;
            int wallH = JsRound(fh * 0.4) + 4;
            int span = JsRound(fh * 0.42) + 18;
            const int ridgeY = 14;
            int eaveY = ridgeY + span;
            int H = eaveY + wallH - 6;
            var g = new PixelCanvas(W, H);
            C3[] R = RoofTile;
            double cx = W / 2.0;
            int baseY = H, wallTop = baseY - wallH;
            g.FillA(ov + 6, baseY - 5, fw, 5, 30, 20, 30, 0.28);
            g.Fill(ov - 2, baseY - 9, fw + 4, 9, Hex("#5e5850")); g.Fill(ov - 2, baseY - 9, fw + 4, 2, Hex("#8a8477"));
            for (int x = ov + 4; x < ov + fw; x += 12) g.Fill(x, baseY - 7, 1, 7, Hex("#4e4943"));
            int posts = Math.Max(3, JsRound(p.W * 1.1) + 1);
            double step = fw / (double)(posts - 1);
            g.Fill(ov, wallTop, fw, wallH - 9, Hex("#eee6d2"));
            g.Fill(ov, baseY - 15, fw, 6, Hex("#d6ccb4"));
            int door = (posts - 1) / 2;
            for (int i = 0; i < posts - 1; i++)
            {
                int x0 = JsRound(ov + i * step) + 4, x1 = JsRound(ov + (i + 1) * step) - 3;
                if (i == door)
                {
                    g.Fill(x0, wallTop + 8, x1 - x0, wallH - 17, Hex("#4a2416"));
                    int hw = (x1 - x0 - 3) >> 1;
                    foreach (int xx in new[] { x0 + 1, x0 + 2 + hw })
                    {
                        g.Fill(xx, wallTop + 9, hw, wallH - 19, Hex("#f0e6cc"));
                        for (int x = xx + 2; x < xx + hw - 1; x += 3) g.Fill(x, wallTop + 9, 1, wallH - 19, Hex("#8a5634"));
                        for (int y = wallTop + 11; y < baseY - 10; y += 4) g.Fill(xx, y, hw, 1, Hex("#8a5634"));
                    }
                    g.Fill(x0 - 2, baseY - 4, x1 - x0 + 4, 4, Hex("#8a8477")); g.Fill(x0 - 2, baseY - 4, x1 - x0 + 4, 1, Hex("#a8a294"));
                }
                else if (wallH > 30)
                {
                    int wy = wallTop + 9, wh = Math.Min(15, wallH - 24);
                    g.Fill(x0 + 1, wy, x1 - x0 - 2, wh, Hex("#4a2a1a"));
                    g.Fill(x0 + 2, wy + 1, x1 - x0 - 4, wh - 2, Hex("#f6eedc"));
                    for (int x = x0 + 4; x < x1 - 2; x += 3) g.Fill(x, wy + 1, 1, wh - 2, Hex("#9a6a44"));
                    for (int y = wy + 3; y < wy + wh - 1; y += 3) g.Fill(x0 + 2, y, x1 - x0 - 4, 1, Hex("#9a6a44"));
                }
            }
            for (int i = 0; i < posts; i++)
            {
                int x = Math.Min(ov + fw - 4, JsRound(ov + i * step) - 2);
                g.Fill(x, wallTop, 4, wallH - 9, Hex("#4a1c12")); g.Fill(x + 1, wallTop, 1, wallH - 9, Hex("#7e3a26"));
            }
            g.FillA(ov, wallTop, fw, 7, 40, 20, 20, 0.45);
            int ridgeHalf = JsRound(fw * 0.32);
            const int lift = 11;
            Func<double, double> eaveAt = x => eaveY - lift * Math.Pow(Math.Min(1, Math.Abs((x - cx) / (W / 2.0))), 2.6);
            Func<double, double> outerAt = t => ridgeHalf + 6 + (W / 2.0 - ridgeHalf - 6) * Math.Pow(t, 0.75);
            Func<double, double> hipAt = t => ridgeHalf + (W / 2.0 - 10 - ridgeHalf) * Math.Pow(t, 1.25);
            int cols = JsRound(fw / 5.0);
            for (int y = ridgeY - 1; y <= eaveY + 1; y++)
            {
                double t = Math.Max(0, (y - ridgeY) / (double)span);
                double o = outerAt(t), hp = hipAt(t);
                for (int x = (int)Math.Floor(cx - o); x <= (int)Math.Ceiling(cx + o); x++)
                {
                    if (y > eaveAt(x)) continue;
                    double ddx = x - cx, ad = Math.Abs(ddx);
                    double v;
                    if (ad <= hp)
                    {
                        double u = (ddx / Math.Max(1, hp) + 1) / 2;
                        double cu = u * cols;
                        double f = cu - Math.Floor(cu);
                        v = 0.5 + t * 0.12;
                        if (f < 0.2) v -= 0.28;
                        else if (f < 0.45) v += 0.14;
                        if ((y - ridgeY) % 7 == 0) v -= 0.08;
                    }
                    else
                    {
                        v = ddx < 0 ? 0.68 : 0.22;
                        if ((y - ridgeY) % 5 == 0) v -= 0.1;
                    }
                    int i = Math.Max(0, Math.Min(R.Length - 1, JsRound(v * (R.Length - 1) + BayerAt(x, y) * 0.5)));
                    g.Fill(x, y, 1, 1, R[i]);
                }
            }
            foreach (int sg in new[] { -1, 1 })
            {
                for (int i = 0; i <= 60; i++)
                {
                    double t = i / 60.0;
                    int y = JsRound(ridgeY + t * span), x = JsRound(cx + sg * hipAt(t));
                    if (y > eaveAt(x) + 1) continue;
                    g.Fill(x - 1, y - 1, 3, 3, RoofRidge);
                    if (i % 5 == 0) g.Fill(x, y - 1, 1, 1, RoofCap);
                }
            }
            for (int x = (int)Math.Floor(cx - outerAt(1)); x <= (int)Math.Ceiling(cx + outerAt(1)); x++)
            {
                int y = JsRound(eaveAt(x));
                g.Fill(x, y, 1, 3, R[0]);
                if (((x - JsRound(cx)) % 5 + 5) % 5 == 0) g.Fill(x - 1, y, 2, 2, RoofCap);
            }
            int fl = JsRound(cx - ridgeHalf - 4), fr = JsRound(cx + ridgeHalf + 4);
            for (int x = fl; x <= fr; x++)
            {
                int e = Math.Max(0, Math.Max(fl + 8 - x, x - (fr - 8)));
                int yy = ridgeY - 3 - JsRound(e * 0.6);
                g.Fill(x, yy, 1, 7 + JsRound(e * 0.6), RoofRidge);
                g.Fill(x, yy + 2, 1, 2, RoofCap);
            }
            if (p.Kind == "hall")
            {
                int dx0 = JsRound(ov + door * step), dx1 = JsRound(ov + (door + 1) * step);
                g.Fill(dx0 + 2, wallTop + 2, dx1 - dx0 - 4, 6, Hex("#3a1a10")); g.Fill(dx0 + 5, wallTop + 3, dx1 - dx0 - 10, 4, Hex("#d8b050"));
            }
            g.Outline();
            return g.ToImage(ov, H);
        }

        private static readonly C3[] StoneGrey = Pal("#5a554e", "#7a746a", "#9a9486", "#bab3a4", "#d0cabb");

        public static PropImage PaintLantern()
        {
            const int W = 22, H = 52;
            var g = new PixelCanvas(W, H);
            C3[] s = StoneGrey;
            g.FillEllipse(11, 49, 9, 3, 30, 20, 30, 0.3);
            g.Fill(4, 44, 14, 6, s[1]); g.Fill(4, 44, 14, 2, s[2]);
            g.Fill(8, 28, 6, 16, s[1]); g.Fill(8, 28, 2, 16, s[3]);
            g.Fill(3, 24, 16, 4, s[1]); g.Fill(3, 24, 16, 1, s[2]);
            g.Fill(4, 12, 14, 12, s[0]); g.Fill(7, 14, 8, 8, Hex("#ffd27a")); g.Fill(9, 16, 4, 4, Hex("#fff0b8"));
            g.Fill(1, 8, 20, 4, s[2]); g.Fill(3, 7, 16, 1, s[3]); g.Fill(1, 11, 20, 1, s[1]);
            g.Fill(8, 3, 6, 5, s[2]); g.Fill(9, 2, 4, 2, s[3]);
            g.Outline();
            return g.ToImage(W / 2, H - 1);
        }

        public static PropImage PaintWell()
        {
            const int W = 72, H = 76;
            var g = new PixelCanvas(W, H);
            C3[] s = StoneGrey;
            g.FillEllipse(36, 70, 32, 7, 30, 20, 30, 0.3);
            g.FillEllipseSolid(36, 56, 30, 14, Hex("#6a645b"));
            g.FillEllipseSolid(36, 54, 29, 12, Hex("#9a9486"));
            g.FillEllipseSolid(36, 53, 21, 8, Hex("#25506a"));
            g.Fill(28, 50, 10, 1, Hex("#4f8b98"));
            for (int a = 0; a < 16; a++)
            {
                double x = 36 + Math.Cos(a / 16.0 * Math.PI * 2) * 25, y = 54 + Math.Sin(a / 16.0 * Math.PI * 2) * 10.5;
                g.Fill(JsRound(x), JsRound(y), 1, 2, s[0]);
            }
            g.Fill(6, 56, 60, 10, s[1]); g.Fill(6, 56, 60, 2, s[2]);
            for (int x = 10; x < 64; x += 8) g.Fill(x, 58, 1, 8, s[0]);
            g.Fill(10, 16, 4, 46, Hex("#4a2a18")); g.Fill(58, 16, 4, 46, Hex("#4a2a18"));
            g.Fill(11, 16, 1, 46, Hex("#7a4e2c")); g.Fill(59, 16, 1, 46, Hex("#7a4e2c"));
            g.Fill(12, 26, 48, 3, Hex("#5a3a20")); g.Fill(12, 26, 48, 1, Hex("#8a5a34"));
            for (int y = 0; y < 14; y++) g.Fill(2 + y, 2 + y, 68 - 2 * y, 1, RoofTile[3 - (y > 10 ? 1 : 0)]);
            g.Fill(14, 0, 44, 4, RoofRidge);
            g.Fill(34, 29, 2, 18, Hex("#6a645b")); g.Fill(31, 44, 8, 6, Hex("#7a5232"));
            g.Outline();
            return g.ToImage(W / 2, H - 2);
        }

        public static PropImage PaintMonument()
        {
            const int W = 30, H = 58;
            var g = new PixelCanvas(W, H);
            C3[] s = Pal("#4a4640", "#6a645b", "#8a8477", "#a8a294", "#c4bead");
            g.FillEllipse(15, 54, 13, 4, 30, 20, 30, 0.3);
            g.Fill(2, 46, 26, 9, s[1]); g.Fill(2, 46, 26, 2, s[2]); g.Fill(4, 52, 4, 3, s[0]); g.Fill(22, 52, 4, 3, s[0]);
            g.Fill(7, 10, 16, 37, s[2]); g.Fill(7, 10, 4, 37, s[3]); g.Fill(20, 10, 3, 37, s[1]);
            for (int y = 16; y < 42; y += 4) g.Fill(11, y, 8, 1, s[1]);
            g.Fill(5, 4, 20, 7, s[2]); g.Fill(6, 4, 18, 1, s[4]); g.Fill(5, 10, 20, 1, s[1]);
            g.Outline();
            return g.ToImage(W / 2, H - 2);
        }

        // ---------- Bildpuffer ----------

        /// <summary>Einfacher RGBA-Bildpuffer (Zeile 0 = oben) mit Rechtecken, Ellipsen, Alphamischung und
        /// farbigem Umriss wie bei den Figuren.</summary>
        private sealed class PixelCanvas
        {
            private readonly int _w, _h;
            private readonly byte[] _px;

            public PixelCanvas(int w, int h)
            {
                _w = w;
                _h = h;
                _px = new byte[w * h * 4];
            }

            public void Set(int x, int y, int r, int g, int b)
            {
                if (x < 0 || y < 0 || x >= _w || y >= _h) return;
                int o = (y * _w + x) * 4;
                _px[o] = (byte)r; _px[o + 1] = (byte)g; _px[o + 2] = (byte)b; _px[o + 3] = 255;
            }

            public void Blend(int x, int y, int r, int g, int b, double a)
            {
                if (x < 0 || y < 0 || x >= _w || y >= _h) return;
                if (a >= 1) { Set(x, y, r, g, b); return; }
                int o = (y * _w + x) * 4;
                double da = _px[o + 3] / 255.0;
                double oa = a + da * (1 - a);
                if (oa <= 0) return;
                _px[o] = (byte)Math.Round((r * a + _px[o] * da * (1 - a)) / oa);
                _px[o + 1] = (byte)Math.Round((g * a + _px[o + 1] * da * (1 - a)) / oa);
                _px[o + 2] = (byte)Math.Round((b * a + _px[o + 2] * da * (1 - a)) / oa);
                _px[o + 3] = (byte)Math.Round(oa * 255);
            }

            public void Fill(int x, int y, int w, int h, C3 c)
            {
                FillA(x, y, w, h, c.R, c.G, c.B, 1);
            }

            public void FillA(int x, int y, int w, int h, int r, int g, int b, double a)
            {
                for (int yy = Math.Max(0, y); yy < Math.Min(_h, y + h); yy++)
                    for (int xx = Math.Max(0, x); xx < Math.Min(_w, x + w); xx++)
                        Blend(xx, yy, r, g, b, a);
            }

            public void FillEllipse(double cx, double cy, double rx, double ry, int r, int g, int b, double a)
            {
                for (int y = (int)Math.Floor(cy - ry); y <= (int)Math.Ceiling(cy + ry); y++)
                    for (int x = (int)Math.Floor(cx - rx); x <= (int)Math.Ceiling(cx + rx); x++)
                    {
                        double dx = (x + 0.5 - cx) / rx, dy = (y + 0.5 - cy) / ry;
                        if (dx * dx + dy * dy <= 1) Blend(x, y, r, g, b, a);
                    }
            }

            public void FillEllipseSolid(double cx, double cy, double rx, double ry, C3 c)
            {
                FillEllipse(cx, cy, rx, ry, c.R, c.G, c.B, 1);
            }

            /// <summary>Umriss: Nachbarfarbe zu 74 % Richtung #26161e gemischt; halbtransparente Schatten nicht.</summary>
            public void Outline()
            {
                var src = (byte[])_px.Clone();
                int[] ddx = { 0, 0, 1, -1 }, ddy = { 1, -1, 0, 0 };
                for (int y = 0; y < _h; y++)
                    for (int x = 0; x < _w; x++)
                    {
                        int o = (y * _w + x) * 4;
                        if (src[o + 3] > 200) continue;
                        int n = -1;
                        for (int k = 0; k < 4; k++)
                        {
                            int xx = x + ddx[k], yy = y + ddy[k];
                            if (xx < 0 || yy < 0 || xx >= _w || yy >= _h) continue;
                            int j = (yy * _w + xx) * 4;
                            if (src[j + 3] > 200) { n = j; break; }
                        }
                        if (n < 0) continue;
                        if (src[o + 3] > 0 && src[o + 3] < 200) continue;
                        _px[o] = (byte)(src[n] + (0x26 - src[n]) * 0.74);
                        _px[o + 1] = (byte)(src[n + 1] + (0x16 - src[n + 1]) * 0.74);
                        _px[o + 2] = (byte)(src[n + 2] + (0x1e - src[n + 2]) * 0.74);
                        _px[o + 3] = 255;
                    }
            }

            public uint[] ToPacked(bool flipRows)
            {
                var r = new uint[_w * _h];
                for (int y = 0; y < _h; y++)
                {
                    int dst = (flipRows ? _h - 1 - y : y) * _w;
                    for (int x = 0; x < _w; x++)
                    {
                        int o = (y * _w + x) * 4;
                        r[dst + x] = (uint)_px[o] | ((uint)_px[o + 1] << 8) | ((uint)_px[o + 2] << 16) | ((uint)_px[o + 3] << 24);
                    }
                }
                return r;
            }

            public PropImage ToImage(int ox, int oy)
            {
                return new PropImage { Width = _w, Height = _h, Ox = ox, Oy = oy, Rgba = (byte[])_px.Clone() };
            }
        }
    }
}
