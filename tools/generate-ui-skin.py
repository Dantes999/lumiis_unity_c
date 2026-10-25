#!/usr/bin/env python3
"""Erzeugt die Pixelart-UI-Skin (Rahmen, Knöpfe, Slots, Kugeln, Minimap-Ring) für Login, Charakterauswahl
und HUD im Stil klassischer 2010er-MMO-Oberflächen: dunkler Stein/Holz, Bronze-/Goldkanten, Nieten.

Alles eigene, prozedural gezeichnete Formen (keine fremden Assets). Deterministisch: fester Zufalls-Seed,
GUIDs der .meta-Dateien werden aus dem Dateipfad abgeleitet - erneutes Ausführen erzeugt identische Dateien.

Aufruf (Repo-Root):  python3 tools/generate-ui-skin.py
Ausgabe:             client/Assets/Resources/UI/Skin/*.png (+ .meta, Point-Filter, keine Kompression)
"""
import hashlib
import math
import os
import random

from PIL import Image, ImageDraw

OUT_DIR = os.path.join("client", "Assets", "Resources", "UI", "Skin")

# Palette (eigene Werte)
BLACK = (8, 6, 5, 255)
STONE_DARK = (28, 22, 18, 255)
STONE = (42, 33, 26, 255)
STONE_LIGHT = (58, 46, 36, 255)
BRONZE_DARK = (92, 58, 24, 255)
BRONZE = (150, 102, 44, 255)
GOLD = (214, 168, 82, 255)
GOLD_LIGHT = (246, 216, 140, 255)
RED_DARK = (78, 18, 14, 255)
RED = (128, 32, 22, 255)

_rng = random.Random(2010)


def guid_for(path):
    return hashlib.md5(("lumiis-ui-skin:" + path.replace("\\", "/")).encode("utf-8")).hexdigest()


META_TEMPLATE = """fileFormatVersion: 2
guid: {guid}
TextureImporter:
  internalIDToNameTable: []
  externalObjects: {{}}
  serializedVersion: 11
  mipmaps:
    mipMapMode: 0
    enableMipMap: 0
    sRGBTexture: 1
    linearTexture: 0
    fadeOut: 0
    borderMipMap: 0
    mipMapsPreserveCoverage: 0
    alphaTestReferenceValue: 0.5
    mipMapFadeDistanceStart: 1
    mipMapFadeDistanceEnd: 3
  bumpmap:
    convertToNormalMap: 0
    externalNormalMap: 0
    heightScale: 0.25
    normalMapFilter: 0
  isReadable: 0
  streamingMipmaps: 0
  streamingMipmapsPriority: 0
  vTOnly: 0
  ignoreMasterTextureLimit: 0
  grayScaleToAlpha: 0
  generateCubemap: 6
  cubemapConvolution: 0
  seamlessCubemap: 0
  textureFormat: 1
  maxTextureSize: 2048
  textureSettings:
    serializedVersion: 2
    filterMode: {filter}
    aniso: 1
    mipBias: 0
    wrapU: 1
    wrapV: 1
    wrapW: 1
  nPOTScale: 0
  lightmap: 0
  compressionQuality: 50
  spriteMode: 0
  spriteExtrude: 1
  spriteMeshType: 1
  alignment: 0
  spritePivot: {{x: 0.5, y: 0.5}}
  spritePixelsToUnits: 32
  spriteBorder: {{x: 0, y: 0, z: 0, w: 0}}
  spriteGenerateFallbackPhysicsShape: 0
  alphaUsage: 1
  alphaIsTransparency: 1
  spriteTessellationDetail: -1
  textureType: 0
  textureShape: 1
  singleChannelComponent: 0
  flipbookRows: 1
  flipbookColumns: 1
  maxTextureSizeSet: 0
  compressionQualitySet: 0
  textureFormatSet: 0
  ignorePngGamma: 0
  applyGammaDecoding: 0
  platformSettings:
  - serializedVersion: 3
    buildTarget: DefaultTexturePlatform
    maxTextureSize: 2048
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 0
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  spriteSheet:
    serializedVersion: 2
    sprites: []
    outline: []
    physicsShape: []
    bones: []
    spriteID:
    internalID: 0
    vertices: []
    indices:
    edges: []
    weights: []
    secondaryTextures: []
    nameFileIdTable: {{}}
  spritePackingTag:
  pSDRemoveMatte: 0
  pSDShowRemoveMatteOption: 0
  userData:
  assetBundleName:
  assetBundleVariant:
"""

FOLDER_META = """fileFormatVersion: 2
guid: {guid}
folderAsset: yes
DefaultImporter:
  externalObjects: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
"""


def save(img, name, smooth=False):
    path = os.path.join(OUT_DIR, name + ".png")
    img.save(path, optimize=True)
    with open(path + ".meta", "w", newline="\n") as f:
        f.write(META_TEMPLATE.format(guid=guid_for(path), filter=1 if smooth else 0))
    print("geschrieben:", path, img.size)


def jitter(color, amount):
    r, g, b, a = color
    d = _rng.randint(-amount, amount)
    return (max(0, min(255, r + d)), max(0, min(255, g + d)), max(0, min(255, b + d)), a)


def lerp(c1, c2, t):
    return tuple(int(round(c1[i] + (c2[i] - c1[i]) * t)) for i in range(4))


def fill_noise(img, box, base, amount):
    x0, y0, x1, y1 = box
    px = img.load()
    for y in range(y0, y1):
        for x in range(x0, x1):
            px[x, y] = jitter(base, amount)


def rivet(img, cx, cy):
    px = img.load()
    for dx, dy, c in ((0, 0, GOLD), (1, 0, GOLD_LIGHT), (0, 1, BRONZE), (1, 1, GOLD),
                      (-1, 0, BRONZE_DARK), (0, -1, GOLD_LIGHT), (2, 1, BRONZE_DARK), (1, 2, BRONZE_DARK)):
        x, y = cx + dx, cy + dy
        if 0 <= x < img.width and 0 <= y < img.height:
            px[x, y] = c


def bevel_frame(img, inset, light, dark):
    """Einpixel-Fase: oben/links hell, unten/rechts dunkel."""
    w, h = img.size
    d = ImageDraw.Draw(img)
    d.line([(inset, inset), (w - 1 - inset, inset)], fill=light)
    d.line([(inset, inset), (inset, h - 1 - inset)], fill=light)
    d.line([(inset, h - 1 - inset), (w - 1 - inset, h - 1 - inset)], fill=dark)
    d.line([(w - 1 - inset, inset), (w - 1 - inset, h - 1 - inset)], fill=dark)


def framed_panel(size, base, noise, corner_rivets=True, gold=True):
    w, h = size
    img = Image.new("RGBA", size, BLACK)
    fill_noise(img, (4, 4, w - 4, h - 4), base, noise)
    bevel_frame(img, 1, GOLD if gold else STONE_LIGHT, BRONZE_DARK)
    bevel_frame(img, 2, BRONZE, BRONZE_DARK)
    bevel_frame(img, 3, BLACK, BLACK)
    # leichte Innenschatten-Kante
    bevel_frame(img, 4, lerp(base, BLACK, 0.5), lerp(base, STONE_LIGHT, 0.35))
    if corner_rivets:
        for cx, cy in ((5, 5), (w - 7, 5), (5, h - 7), (w - 7, h - 7)):
            rivet(img, cx, cy)
    return img


def panel():
    save(framed_panel((48, 48), STONE, 4), "panel")


def panel_inset():
    w = h = 24
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    fill_noise(img, (1, 1, w - 1, h - 1), (14, 11, 9, 235), 2)
    bevel_frame(img, 0, BRONZE_DARK, lerp(BRONZE, GOLD, 0.3))
    save(img, "panel_inset")


def titlebar():
    w, h = 48, 22
    img = Image.new("RGBA", (w, h), BLACK)
    px = img.load()
    for y in range(2, h - 2):
        t = (y - 2) / (h - 5)
        c = lerp(RED, RED_DARK, t)
        for x in range(2, w - 2):
            px[x, y] = jitter(c, 3)
    bevel_frame(img, 1, GOLD_LIGHT, BRONZE_DARK)
    d = ImageDraw.Draw(img)
    d.line([(3, 3), (w - 4, 3)], fill=lerp(RED, GOLD, 0.35))
    save(img, "titlebar")


def button(name, top, bottom, edge_light, edge_dark):
    w, h = 48, 24
    img = Image.new("RGBA", (w, h), BLACK)
    px = img.load()
    for y in range(2, h - 2):
        t = (y - 2) / (h - 5)
        c = lerp(top, bottom, t)
        for x in range(2, w - 2):
            px[x, y] = jitter(c, 2)
    bevel_frame(img, 1, edge_light, edge_dark)
    d = ImageDraw.Draw(img)
    d.line([(3, 2), (w - 4, 2)], fill=lerp(top, GOLD_LIGHT, 0.4))
    save(img, name)


def buttons():
    button("button", (112, 78, 38, 255), (58, 38, 18, 255), GOLD, BRONZE_DARK)
    button("button_hover", (150, 108, 52, 255), (82, 54, 24, 255), GOLD_LIGHT, BRONZE)
    button("button_pressed", (52, 34, 16, 255), (92, 62, 30, 255), BRONZE_DARK, GOLD)
    button("button_disabled", (60, 54, 48, 255), (36, 32, 28, 255), (90, 82, 74, 255), (30, 26, 22, 255))


def slot(name, edge, inner):
    w = h = 36
    img = Image.new("RGBA", (w, h), BLACK)
    fill_noise(img, (2, 2, w - 2, h - 2), inner, 2)
    # vertieft: oben/links dunkel, unten/rechts hell
    bevel_frame(img, 0, BRONZE_DARK, edge)
    bevel_frame(img, 1, BLACK, lerp(edge, BLACK, 0.4))
    save(img, name)


def slots():
    slot("slot", BRONZE, (20, 16, 13, 255))
    slot("slot_hover", GOLD_LIGHT, (34, 27, 20, 255))
    slot("slot_selected", (240, 200, 90, 255), (48, 36, 18, 255))


def input_field():
    w, h = 32, 22
    img = Image.new("RGBA", (w, h), BLACK)
    fill_noise(img, (2, 2, w - 2, h - 2), (12, 10, 8, 255), 1)
    bevel_frame(img, 0, BRONZE_DARK, BRONZE)
    bevel_frame(img, 1, BLACK, (40, 32, 24, 255))
    save(img, "input")
    img2 = img.copy()
    bevel_frame(img2, 0, BRONZE, GOLD)
    save(img2, "input_focus")


def taskbar():
    # Die Taskleiste wird per stretch-to-fill auf die volle Bildschirmbreite gezogen. Jede Variation
    # in X-Richtung würde dabei zu breiten Blöcken/Nähten gestreckt - deshalb nur zeilenweise
    # Variation (leichter Verlauf + Fugenlinien), jede Zeile ist in sich einfarbig.
    w, h = 64, 64
    img = Image.new("RGBA", (w, h), BLACK)
    # Zufallsfolge wie in früheren Versionen verbrauchen, damit alle nachfolgend erzeugten Bilder
    # bytegleich bleiben (deterministischer Generator, kleine Diffs).
    fill_noise(Image.new("RGBA", (w, h)), (0, 5, w, h), STONE_DARK, 4)
    px = img.load()
    for y in range(5, h):
        t = (y - 5) / (h - 5)
        c = lerp(lerp(STONE_DARK, STONE, 0.25), lerp(STONE_DARK, BLACK, 0.35), t)
        for x in range(w):
            px[x, y] = c
    for y in (22, 42):
        for x in range(w):
            px[x, y] = lerp(STONE_DARK, BLACK, 0.6)
    d = ImageDraw.Draw(img)
    d.line([(0, 0), (w, 0)], fill=BLACK)
    d.line([(0, 1), (w, 1)], fill=GOLD)
    d.line([(0, 2), (w, 2)], fill=BRONZE)
    d.line([(0, 3), (w, 3)], fill=BRONZE_DARK)
    d.line([(0, 4), (w, 4)], fill=BLACK)
    save(img, "taskbar")


def sphere(size, base, highlight, shadow):
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    px = img.load()
    r = size / 2 - 0.5
    cx = cy = (size - 1) / 2
    lx, ly = cx - r * 0.35, cy - r * 0.45
    for y in range(size):
        for x in range(size):
            dist = math.hypot(x - cx, y - cy)
            if dist > r:
                continue
            light = 1 - min(1, math.hypot(x - lx, y - ly) / (r * 1.6))
            edge = (dist / r) ** 3
            c = lerp(base, highlight, light * 0.75)
            c = lerp(c, shadow, edge * 0.8)
            # Pixelart-Quantisierung in 6 Stufen
            q = tuple((v // 12) * 12 for v in c[:3]) + (255,)
            px[x, y] = q
    return img


def orbs():
    size = 84
    save(sphere(size, (176, 24, 20, 255), (255, 120, 90, 255), (40, 4, 4, 255)), "orb_hp")
    save(sphere(size, (28, 64, 180, 255), (120, 170, 255, 255), (4, 10, 40, 255)), "orb_sp")

    # Leere Kugel (Hintergrund)
    save(sphere(size, (24, 18, 16, 255), (48, 40, 34, 255), (6, 4, 4, 255)), "orb_empty")

    # Glanz-Overlay
    glass = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(glass)
    d.ellipse([size * 0.22, size * 0.12, size * 0.58, size * 0.34], fill=(255, 255, 255, 70))
    d.ellipse([size * 0.28, size * 0.16, size * 0.44, size * 0.24], fill=(255, 255, 255, 110))
    save(glass, "orb_glass")

    # Ringrahmen mit Nieten
    fs = 100
    frame = Image.new("RGBA", (fs, fs), (0, 0, 0, 0))
    px = frame.load()
    c = (fs - 1) / 2
    inner = size / 2 - 1
    outer = fs / 2 - 0.5
    for y in range(fs):
        for x in range(fs):
            dist = math.hypot(x - c, y - c)
            if inner <= dist <= outer:
                t = (dist - inner) / (outer - inner)
                ang = math.atan2(y - c, x - c)
                shade = 0.5 + 0.5 * math.sin(ang + math.pi * 0.75)
                if t < 0.12 or t > 0.9:
                    col = BLACK
                elif t < 0.3:
                    col = lerp(BRONZE_DARK, GOLD, shade)
                elif t < 0.7:
                    col = jitter(lerp(STONE, STONE_LIGHT, shade), 3)
                else:
                    col = lerp(BRONZE_DARK, BRONZE, shade)
                px[x, y] = col
    for i in range(8):
        ang = i * math.pi / 4 + math.pi / 8
        rr = (inner + outer) / 2
        rivet(frame, int(round(c + math.cos(ang) * rr)) - 1, int(round(c + math.sin(ang) * rr)) - 1)
    save(frame, "orb_frame")


def exp_orbs():
    s = 18
    empty = sphere(s, (30, 24, 18, 255), (60, 50, 38, 255), (8, 6, 4, 255))
    full = sphere(s, (212, 160, 40, 255), (255, 240, 160, 255), (90, 56, 8, 255))
    for img in (empty, full):
        d = ImageDraw.Draw(img)
        d.ellipse([0, 0, s - 1, s - 1], outline=BRONZE_DARK)
    save(empty, "exp_orb_empty")
    save(full, "exp_orb_full")


def minimap_frame():
    fs = 176
    inner = 74
    frame = Image.new("RGBA", (fs, fs), (0, 0, 0, 0))
    px = frame.load()
    c = (fs - 1) / 2
    outer = fs / 2 - 0.5
    for y in range(fs):
        for x in range(fs):
            dist = math.hypot(x - c, y - c)
            if inner <= dist <= outer:
                t = (dist - inner) / (outer - inner)
                ang = math.atan2(y - c, x - c)
                shade = 0.5 + 0.5 * math.sin(ang + math.pi * 0.75)
                if t < 0.08 or t > 0.92:
                    col = BLACK
                elif t < 0.25:
                    col = lerp(BRONZE_DARK, GOLD_LIGHT, shade)
                elif t < 0.75:
                    col = jitter(lerp(STONE_DARK, STONE_LIGHT, shade), 4)
                else:
                    col = lerp(BRONZE_DARK, GOLD, shade)
                px[x, y] = col
    # Himmelsrichtungs-Markierungen (Rauten oben/unten/links/rechts)
    rr = (inner + outer) / 2
    for ang in (0, math.pi / 2, math.pi, 3 * math.pi / 2):
        mx = c + math.cos(ang) * rr
        my = c + math.sin(ang) * rr
        for dy in range(-4, 5):
            span = 4 - abs(dy)
            for dx in range(-span, span + 1):
                x, y = int(round(mx + dx)), int(round(my + dy))
                px[x, y] = GOLD_LIGHT if abs(dx) + abs(dy) < 2 else GOLD
    for i in range(4):
        ang = i * math.pi / 2 + math.pi / 4
        rivet(frame, int(round(c + math.cos(ang) * rr)) - 1, int(round(c + math.sin(ang) * rr)) - 1)
    save(frame, "minimap_frame")


def close_button():
    for name, bg in (("close", (110, 28, 20, 255)), ("close_hover", (170, 46, 30, 255))):
        s = 16
        img = Image.new("RGBA", (s, s), BLACK)
        fill_noise(img, (1, 1, s - 1, s - 1), bg, 2)
        bevel_frame(img, 0, GOLD, BRONZE_DARK)
        d = ImageDraw.Draw(img)
        d.line([(4, 4), (11, 11)], fill=GOLD_LIGHT, width=2)
        d.line([(11, 4), (4, 11)], fill=GOLD_LIGHT, width=2)
        save(img, name)


def vignette():
    s = 256
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    px = img.load()
    c = (s - 1) / 2
    for y in range(s):
        for x in range(s):
            d = math.hypot((x - c) / c, (y - c) / c)
            a = max(0.0, min(1.0, (d - 0.45) / 0.75))
            px[x, y] = (4, 3, 2, int(235 * a * a))
    save(img, "vignette", smooth=True)


def bar_frame():
    w, h = 32, 14
    img = Image.new("RGBA", (w, h), BLACK)
    fill_noise(img, (2, 2, w - 2, h - 2), (16, 12, 10, 255), 1)
    bevel_frame(img, 0, BRONZE_DARK, GOLD)
    bevel_frame(img, 1, BLACK, BLACK)
    save(img, "bar_frame")


def charslot(name, edge, base):
    img = framed_panel((48, 64), base, 3, corner_rivets=True)
    bevel_frame(img, 1, edge, BRONZE_DARK)
    save(img, name)


def tooltip():
    w = h = 24
    img = Image.new("RGBA", (w, h), (6, 5, 4, 240))
    bevel_frame(img, 0, BRONZE, BRONZE_DARK)
    save(img, "tooltip")


def silhouette():
    """Einfache Figuren-Silhouette als Hintergrund des Ausrüstungsbereichs."""
    w, h = 64, 96
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    col = (70, 56, 42, 120)
    d.ellipse([24, 6, 40, 24], fill=col)
    d.polygon([(18, 28), (46, 28), (52, 60), (12, 60)], fill=col)
    d.rectangle([20, 60, 30, 92], fill=col)
    d.rectangle([34, 60, 44, 92], fill=col)
    d.polygon([(18, 30), (8, 54), (12, 56), (22, 36)], fill=col)
    d.polygon([(46, 30), (56, 54), (52, 56), (42, 36)], fill=col)
    save(img, "silhouette")


def checkbox():
    # 14x14-Kästchen für Toggles: bronzener Rahmen, dunkle Mulde; die Variante "checked" trägt
    # einen goldenen Haken.
    size = 14
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, size - 1, size - 1], fill=BLACK)
    d.rectangle([1, 1, size - 2, size - 2], fill=BRONZE)
    d.rectangle([2, 2, size - 3, size - 3], fill=(22, 16, 12, 255))
    d.line([(2, 2), (size - 3, 2)], fill=BLACK)
    save(img, "checkbox")
    checked = img.copy()
    d = ImageDraw.Draw(checked)
    d.line([(3, 7), (5, 10), (10, 3)], fill=GOLD_LIGHT, width=2)
    save(checked, "checkbox_checked")


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    folder_meta = OUT_DIR + ".meta"
    with open(folder_meta, "w", newline="\n") as f:
        f.write(FOLDER_META.format(guid=guid_for(OUT_DIR)))
    panel()
    panel_inset()
    titlebar()
    buttons()
    slots()
    input_field()
    taskbar()
    orbs()
    exp_orbs()
    minimap_frame()
    close_button()
    vignette()
    bar_frame()
    charslot("charslot", BRONZE, STONE_DARK)
    charslot("charslot_selected", GOLD_LIGHT, (60, 42, 22, 255))
    tooltip()
    silhouette()
    checkbox()


if __name__ == "__main__":
    main()
