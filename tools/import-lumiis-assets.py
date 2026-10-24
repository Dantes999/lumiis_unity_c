#!/usr/bin/env python3
"""Importiert die Lumiis-Referenzgrafiken in das Unity-Projekt.

Quellen:
  /home/nutzer/Desktop/lumiis-assets-archiv/assets/icons   (Gegenstands-Symbole)
  /home/nutzer/Desktop/lumiis/packages/client/public/assets (Charakter-Sheets)

Ziele:
  client/Assets/Resources/Icons/<kategorie>/...
  client/Assets/Resources/Characters/{body,head,hair,armor,weapon}/...

Erzeugt außerdem ein Icon-Manifest (JsonUtility-freundliche Array-Form) und
stabile .meta-Dateien (Default-Texture, Point-Filter, keine Kompression).
Die Dateien werden nur kopiert, nie verändert oder aus dem Quellprojekt gelöscht.
"""

from __future__ import annotations

import json
import re
import shutil
import uuid
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
ICON_SRC = Path("/home/nutzer/Desktop/lumiis-assets-archiv/assets/icons")
CHAR_SRC = Path("/home/nutzer/Desktop/lumiis/packages/client/public/assets")
ICON_DST = ROOT / "client" / "Assets" / "Resources" / "Icons"
CHAR_DST = ROOT / "client" / "Assets" / "Resources" / "Characters"

GUID_NAMESPACE = uuid.UUID("6f3a6e2c-1d58-4f0e-9a5e-7b2f2f7c8a11")


def meta_guid(relative: str) -> str:
    return uuid.uuid5(GUID_NAMESPACE, relative).hex


def safe_name(name: str) -> str:
    """Windows-tauglicher Dateiname (keine : * ? " < > |)."""
    return re.sub(r"[^A-Za-z0-9._-]+", "_", name).strip("_") or "icon"


def write_texture_meta(path: Path, relative: str) -> None:
    meta = path.with_suffix(path.suffix + ".meta")
    meta.write_text(
        f"""fileFormatVersion: 2
guid: {meta_guid(relative)}
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
  maxTextureSize: 512
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
    maxTextureSize: 512
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
""",
        encoding="utf-8",
    )


def write_folder_meta(path: Path, relative: str) -> None:
    meta = path.with_suffix(".meta")
    meta.write_text(
        f"""fileFormatVersion: 2
guid: {meta_guid("folder:" + relative)}
folderAsset: yes
DefaultImporter:
  externalObjects: {{}}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
""",
        encoding="utf-8",
    )


def ensure_folder(path: Path) -> None:
    path.mkdir(parents=True, exist_ok=True)
    rel = path.relative_to(ROOT).as_posix()
    write_folder_meta(path, rel.replace("client/Assets/", ""))


def copy_icons() -> list[dict]:
    if not ICON_SRC.exists():
        raise SystemExit(f"Icon-Quelle fehlt: {ICON_SRC}")

    ensure_folder(ICON_DST)
    manifest: list[dict] = []
    seen_files: set[str] = set()

    icons_json = ICON_SRC / "icons.json"
    if icons_json.exists():
        data = json.loads(icons_json.read_text(encoding="utf-8"))
        for key, rel in data.get("eintraege", {}).items():
            src = ICON_SRC / rel
            if not src.exists():
                continue
            parts = Path(rel).parts
            category = parts[0] if len(parts) > 1 else "misc"
            dst_dir = ICON_DST / category
            ensure_folder(dst_dir)
            safe_file = safe_name(Path(rel).name)
            safe_key = safe_name(key)
            dst = dst_dir / safe_file
            shutil.copy2(src, dst)
            target_rel = (Path(category) / safe_file).as_posix() if category != "misc" else safe_file
            resource = "Icons/" + Path(target_rel).with_suffix("").as_posix()
            manifest.append({"key": safe_key, "sourceKey": key, "category": category, "file": target_rel, "resource": resource})
            seen_files.add(rel)

    # Dateien, die (noch) nicht in icons.json stehen, trotzdem übernehmen.
    for src in sorted(ICON_SRC.rglob("*.png")):
        rel = src.relative_to(ICON_SRC).as_posix()
        if rel in seen_files:
            continue
        category = Path(rel).parent.name if Path(rel).parent.name != "." else "misc"
        dst_dir = ICON_DST / category
        ensure_folder(dst_dir)
        safe_file = safe_name(src.name)
        safe_key = safe_name(src.stem)
        dst = dst_dir / safe_file
        shutil.copy2(src, dst)
        target_rel = (Path(category) / safe_file).as_posix() if category != "misc" else safe_file
        resource = "Icons/" + Path(target_rel).with_suffix("").as_posix()
        manifest.append({"key": safe_key, "sourceKey": src.stem, "category": category, "file": target_rel, "resource": resource})

    # Alte, Windows-invalide Dateien aus früheren Importen entfernen.
    for old in list(ICON_DST.rglob("*:*")):
        if old.is_file():
            old.unlink()
        old_meta = Path(str(old) + ".meta")
        if old_meta.exists():
            old_meta.unlink()

    manifest.sort(key=lambda x: (x["category"], x["key"]))
    for item in manifest:
        png = ICON_DST / item["file"]
        write_texture_meta(png, "Resources/Icons/" + item["file"])

    manifest_path = ICON_DST / "icon-manifest.json"
    manifest_path.write_text(
        json.dumps({"icons": manifest}, ensure_ascii=False, indent=1) + "\n",
        encoding="utf-8",
    )
    return manifest


def copy_characters() -> int:
    if not CHAR_SRC.exists():
        raise SystemExit(f"Charakter-Quelle fehlt: {CHAR_SRC}")

    ensure_folder(CHAR_DST)
    count = 0
    for folder in ("body", "head", "hair", "armor", "weapon"):
        src_dir = CHAR_SRC / folder
        if not src_dir.exists():
            continue
        dst_dir = CHAR_DST / folder
        ensure_folder(dst_dir)
        for src in sorted(src_dir.glob("*.png")):
            dst = dst_dir / src.name
            shutil.copy2(src, dst)
            write_texture_meta(dst, f"Resources/Characters/{folder}/{src.name}")
            count += 1
    return count


if __name__ == "__main__":
    icons = copy_icons()
    characters = copy_characters()
    print(f"Icons importiert: {len(icons)} -> {ICON_DST.relative_to(ROOT)}")
    print(f"Charakter-Sheets importiert: {characters} -> {CHAR_DST.relative_to(ROOT)}")
