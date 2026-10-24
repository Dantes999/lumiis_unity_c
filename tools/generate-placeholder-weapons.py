#!/usr/bin/env python3
"""Erzeugt die einfachen Platzhalter-Waffen-PNGs für den Paper-Doll-Client.

Bewusst eigene, triviale Formen (kein Referenz- oder Original-Asset). Die Dateien liegen unter
client/Assets/Resources/Sprites/Items/ und werden über items.json -> spriteKey geladen. Der Pivot
liegt am Griff (unten), Pixel pro Einheit = 64 wie in looks.json settings.ppu.
"""

from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "client" / "Assets" / "Resources" / "Sprites" / "Items"
SIZE = 128
PPU = 64
PIVOT = (0.5, 0.125)

TRANSPARENT = (0, 0, 0, 0)
OUTLINE = (35, 25, 20, 255)
WOOD_DARK = (92, 58, 34, 255)
WOOD = (138, 92, 52, 255)
GOLD = (196, 150, 52, 255)
STEEL_DARK = (95, 102, 112, 255)
STEEL = (190, 196, 206, 255)
STEEL_LIGHT = (230, 234, 240, 255)


def rect(draw, box, fill):
    draw.rectangle(box, fill=fill)


def poly(draw, points, fill):
    draw.polygon(points, fill=fill)


def sword(name, blade, blade_dark, wood: bool):
    image = Image.new("RGBA", (SIZE, SIZE), TRANSPARENT)
    d = ImageDraw.Draw(image)

    # Klinge (Umriss + Füllung + Mittellinie)
    outline = [(54, 90), (74, 90), (72, 25), (64, 7), (56, 25)]
    fill = [(57, 87), (71, 87), (70, 26), (64, 12), (58, 26)]
    poly(d, outline, OUTLINE)
    poly(d, fill, blade)
    d.line([(64, 22), (64, 84)], fill=blade_dark, width=2)

    # Parierstange
    rect(d, (46, 82, 82, 94), OUTLINE)
    rect(d, (49, 85, 79, 91), GOLD)

    # Griff und Knauf
    rect(d, (58, 93, 70, 120), OUTLINE)
    rect(d, (61, 94, 67, 118), WOOD_DARK if wood else WOOD)
    for y in range(98, 116, 5):
        d.line([(61, y), (67, y)], fill=GOLD, width=1)
    rect(d, (58, 119, 70, 126), OUTLINE)
    rect(d, (60, 120, 68, 125), GOLD)

    write(image, name)


def axe(name):
    image = Image.new("RGBA", (SIZE, SIZE), TRANSPARENT)
    d = ImageDraw.Draw(image)

    # Stiel
    rect(d, (58, 8, 70, 122), OUTLINE)
    rect(d, (61, 10, 67, 120), WOOD)
    for y in range(95, 119, 6):
        d.line([(61, y), (67, y)], fill=WOOD_DARK, width=2)

    # Axtblatt
    outline = [(68, 22), (102, 14), (112, 34), (102, 58), (68, 64)]
    fill = [(71, 26), (99, 19), (107, 35), (99, 54), (71, 60)]
    poly(d, outline, OUTLINE)
    poly(d, fill, STEEL_DARK)
    poly(d, [(74, 29), (97, 23), (103, 35), (97, 50), (74, 56)], STEEL)
    d.line([(105, 26), (105, 49)], fill=STEEL_LIGHT, width=3)

    write(image, name)


def write(image, name):
    OUT.mkdir(parents=True, exist_ok=True)
    png = OUT / f"{name}.png"
    image.save(png)
    meta = OUT / f"{name}.png.meta"
    meta.write_text(meta_text(name), encoding="utf-8")
    print(f"geschrieben: {png.relative_to(ROOT)}")


def meta_text(name):
    guid = {
        "weapon_2_wood": "c1000000000000000000000000000001",
        "weapon_2_iron": "c1000000000000000000000000000002",
        "weapon_3_axe": "c1000000000000000000000000000003",
    }[name]
    sprite_id = {
        "weapon_2_wood": "d1000000000000000000000000000001",
        "weapon_2_iron": "d1000000000000000000000000000002",
        "weapon_3_axe": "d1000000000000000000000000000003",
    }[name]
    return f"""fileFormatVersion: 2
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
    filterMode: 0
    aniso: 1
    mipBias: 0
    wrapU: 1
    wrapV: 1
    wrapW: 1
  nPOTScale: 0
  lightmap: 0
  compressionQuality: 50
  spriteMode: 1
  spriteExtrude: 1
  spriteMeshType: 1
  alignment: 9
  spritePivot: {{x: {PIVOT[0]}, y: {PIVOT[1]}}}
  spritePixelsToUnits: {PPU}
  spriteBorder: {{x: 0, y: 0, z: 0, w: 0}}
  spriteGenerateFallbackPhysicsShape: 0
  alphaUsage: 1
  alphaIsTransparency: 1
  spriteTessellationDetail: -1
  textureType: 8
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
    spriteID: {sprite_id}
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


if __name__ == "__main__":
    sword("weapon_2_wood", WOOD, WOOD_DARK, wood=True)
    sword("weapon_2_iron", STEEL, STEEL_DARK, wood=False)
    axe("weapon_3_axe")
