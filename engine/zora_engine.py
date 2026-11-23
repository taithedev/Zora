"""Zora native asset indexing / exporting backend.

Imports open, ordinary Unity project assets and UnityPy-supported serialized Unity
files. Does not bypass DRM, encrypted bundles, or access controls.
Only use with projects or builds you have permission to inspect.
"""
from __future__ import annotations

import argparse
import json
import os
import re
import shutil
import sys
from pathlib import Path

try:
    import UnityPy
except ImportError:
    print(json.dumps({"error": "UnityPy is missing. Install engine/requirements.txt or use the packaged engine."}))
    sys.exit(2)

SERIALIZED_EXT = {".assets", ".bundle", ".unity3d", ".ab", ".assetbundle", ".apk"}
SERIALIZED_NAMES = {"globalgamemanagers", "globalgamemanagers.assets", "resources.assets", "level0", "level1", "data.unity3d"}
SUPPORTED_TYPES = {"Texture2D", "Sprite", "Mesh", "AudioClip", "TextAsset", "MonoBehaviour", "Material", "AnimationClip", "Font", "Shader"}
MAX_ASSETS = 50000


def clean_name(name: str) -> str:
    text = re.sub(r"[^\w. -]", "_", name or "unnamed", flags=re.UNICODE).strip(" .")
    return text[:92] or "unnamed"


def source_context(value: str) -> tuple[Path, Path, bool]:
    source = Path(value).expanduser().resolve()
    if not source.exists():
        raise ValueError("Source does not exist")
    if source.is_file() and source.suffix.lower() == ".exe":
        folder = source.with_name(source.stem + "_Data")
        if not folder.is_dir():
            raise ValueError("Unity game data folder not found next to the EXE")
        return folder, folder, False
    if source.is_file():
        return source, source.parent, False
    is_project = (source / "Assets").is_dir() and (source / "ProjectSettings").is_dir()
    return source, source, is_project


def is_serialized(path: Path) -> bool:
    name = path.name.lower()
    return path.suffix.lower() in SERIALIZED_EXT or name in SERIALIZED_NAMES or bool(re.fullmatch(r"sharedassets\d+\.assets", name))


def category(path: Path) -> str:
    ext = path.suffix.lower()
    if ext in {".png", ".jpg", ".jpeg", ".tga", ".tif", ".tiff", ".psd", ".exr"}: return "Texture"
    if ext in {".fbx", ".obj", ".blend", ".gltf", ".glb"}: return "Model"
    if ext in {".wav", ".mp3", ".ogg", ".aiff", ".flac"}: return "Audio"
    if ext in {".cs", ".shader", ".compute", ".hlsl"}: return "Source"
    if ext in {".anim", ".controller", ".overridecontroller"}: return "Animation"
    if ext in {".prefab", ".unity"}: return "Scene/Prefab"
    if ext in {".mat", ".asset"}: return "Material/Data"
    if ext in {".ttf", ".otf"}: return "Font"
    return "File"


def candidate_files(source: Path, project: bool):
    if source.is_file():
        yield source, False
        return
    for root, dirs, names in os.walk(source, followlinks=False):
        base = Path(root)
        dirs[:] = [d for d in dirs if not d.startswith('.') and not (base / d).is_symlink() and d not in {'Temp', 'Logs', 'obj', 'Build', 'Builds', '.git', '.vs'}]
        for name in sorted(names):
            path = base / name
            if path.is_symlink() or path.suffix.lower() == '.meta': continue
            if project and "Assets" in path.relative_to(source).parts and "Library" not in path.relative_to(source).parts:
                yield path, True
            elif is_serialized(path):
                yield path, False


def safe_relative(path: Path, root: Path) -> str:
    try: return path.relative_to(root).as_posix()
    except ValueError: return path.name


def unity_objects(path: Path):
    # Let the parser manage file resources; consumers finish before it is discarded.
    env = UnityPy.load(str(path))
    return env, list(env.objects)


def asset_row(source_rel: str, obj) -> dict:
    typ = obj.type.name
    name = f"{typ}_{obj.path_id}"
    try:
        name = obj.peek_name() or name
    except Exception:
        pass
    return {
        "id": f"unity:{source_rel}:{obj.path_id}",
        "name": str(name)[:160], "type": typ, "origin": "Serialized",
        "source": source_rel, "size": int(getattr(obj, 'byte_size', 0) or 0),
        "exportable": typ in SUPPORTED_TYPES,
    }


def native_row(path: Path, root: Path) -> dict:
    rel = safe_relative(path, root)
    return {"id": "native:" + rel, "name": path.name, "type": category(path), "origin": "Project",
            "source": rel, "size": path.stat().st_size, "exportable": True}


def scan(input_path: str) -> dict:
    source, root, project = source_context(input_path)
    assets, warnings = [], []
    for file_path, native in candidate_files(source, project):
        try:
            if native:
                assets.append(native_row(file_path, root))
            else:
                rel = safe_relative(file_path, root)
                _, objects = unity_objects(file_path)
                for obj in objects:
                    assets.append(asset_row(rel, obj))
                    if len(assets) >= MAX_ASSETS: break
        except Exception as exc:
            if len(warnings) < 150: warnings.append(f"{safe_relative(file_path, root)}: {type(exc).__name__}: {str(exc)[:150]}")
        if len(assets) >= MAX_ASSETS:
            warnings.append(f"Asset list capped at {MAX_ASSETS} items")
            break
    return {"assets": assets, "warnings": warnings, "project": project, "source": str(source)}


def make_destination(out: Path, *segments: str) -> Path:
    target = out.joinpath(*(clean_name(s) for s in segments)).resolve()
    if not target.is_relative_to(out.resolve()): raise ValueError('Unsafe output path')
    target.parent.mkdir(parents=True, exist_ok=True)
    return target


def short_json(value):
    def default(v):
        if isinstance(v, (bytes, bytearray)): return f"<{len(v)} bytes>"
        return str(v)
    text = json.dumps(value, indent=2, ensure_ascii=False, default=default)
    if len(text) > 4_000_000: raise ValueError('Metadata exceeds 4 MB')
    return text


def export_object(obj, row: dict, out: Path) -> list[str]:
    typ = row['type']
    if typ not in SUPPORTED_TYPES: return []
    parsed = obj.parse_as_object() if typ in {"Texture2D", "Sprite", "Mesh", "AudioClip", "TextAsset", "Font"} else None
    base = clean_name(f"{Path(row['source']).stem}_{obj.path_id}_{row['name']}")
    exported = []
    if typ in {"Texture2D", "Sprite"}:
        path = make_destination(out, typ, base + '.png')
        parsed.image.save(path)
        exported.append(str(path))
    elif typ == 'Mesh':
        path = make_destination(out, 'Mesh', base + '.obj')
        path.write_text(parsed.export(), encoding='utf-8', newline='')
        exported.append(str(path))
    elif typ == 'AudioClip':
        for sample_name, sample_bytes in parsed.samples.items():
            sample = clean_name(Path(sample_name).stem)
            path = make_destination(out, 'AudioClip', base + '_' + sample + '.wav')
            path.write_bytes(sample_bytes)
            exported.append(str(path))
    elif typ == 'TextAsset':
        path = make_destination(out, 'TextAsset', base + '.txt')
        content = parsed.m_Script
        path.write_bytes(content.encode('utf-8', 'surrogateescape') if isinstance(content, str) else bytes(content))
        exported.append(str(path))
    elif typ == 'Font':
        blob = bytes(parsed.m_FontData)
        if not blob: raise ValueError('Font has no embedded data')
        ext = '.otf' if blob[:4] == b'OTTO' else '.ttf'
        path = make_destination(out, 'Font', base + ext)
        path.write_bytes(blob)
        exported.append(str(path))
    else:
        path = make_destination(out, typ, base + '.json')
        path.write_text(short_json(obj.parse_as_dict()), encoding='utf-8')
        exported.append(str(path))
    return exported


def export_assets(input_path: str, destination: str, selected_ids: set[str] | None) -> dict:
    source, root, project = source_context(input_path)
    out = Path(destination).expanduser().resolve()
    if out == root or root.is_relative_to(out):
        raise ValueError('Export destination must not contain the source folder')
    # Never walk an output directory nested inside the source tree.
    if out.is_relative_to(root): raise ValueError('Export destination must be outside the source folder')
    out.mkdir(parents=True, exist_ok=True)
    exported, warnings = [], []
    for path, native in candidate_files(source, project):
        rel = safe_relative(path, root)
        if native:
            row = native_row(path, root)
            if selected_ids is not None and row['id'] not in selected_ids: continue
            try:
                target = out / 'Project' / Path(rel)
                target = target.resolve()
                if not target.is_relative_to(out): raise ValueError('Unsafe native file path')
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(path, target)
                exported.append(str(target))
            except Exception as exc:
                warnings.append(f'{rel}: {exc}')
        else:
            try:
                _, objects = unity_objects(path)
                for obj in objects:
                    row = asset_row(rel, obj)
                    if selected_ids is not None and row['id'] not in selected_ids: continue
                    if not row['exportable']: continue
                    try: exported.extend(export_object(obj, row, out / 'Extracted'))
                    except Exception as exc: warnings.append(f"{row['name']}: {type(exc).__name__}: {str(exc)[:180]}")
            except Exception as exc: warnings.append(f'{rel}: {type(exc).__name__}: {str(exc)[:150]}')
    return {"exported": exported, "count": len(exported), "warnings": warnings[:200]}


def main():
    parser = argparse.ArgumentParser(description='Zora Unity project/build asset engine')
    sub = parser.add_subparsers(dest='command', required=True)
    p_scan = sub.add_parser('scan'); p_scan.add_argument('--source', required=True)
    p_export = sub.add_parser('export'); p_export.add_argument('--source', required=True)
    p_export.add_argument('--destination', required=True)
    p_export.add_argument('--ids', nargs='*', default=None)
    args = parser.parse_args()
    try:
        value = scan(args.source) if args.command == 'scan' else export_assets(args.source, args.destination, set(args.ids) if args.ids else None)
        print(json.dumps(value, ensure_ascii=False))
    except Exception as exc:
        print(json.dumps({"error": f"{type(exc).__name__}: {exc}"}))
        sys.exit(1)


if __name__ == '__main__': main()
