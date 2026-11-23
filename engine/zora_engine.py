"""Zora native asset indexing / exporting backend.

Imports open, ordinary Unity project assets and UnityPy-supported serialized Unity
files. Does not bypass DRM, encrypted bundles, or access controls.
Only use with projects or builds you have permission to inspect.
"""
from __future__ import annotations

import argparse
import json
import datetime
import os
import re
import shutil
import sys
from pathlib import Path

from inspectors import is_general_source, inspect_source, export_source, ARCHIVES

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
        if folder.is_dir():
            return folder, folder, False
        return source, source.parent, False
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
            elif is_serialized(path) or is_general_source(path):
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
                if is_general_source(file_path):
                    general_rows, general_warnings = inspect_source(file_path, root)
                    assets.extend(general_rows[:max(0, MAX_ASSETS - len(assets))])
                    warnings.extend(general_warnings[:max(0, 150 - len(warnings))])
                    if file_path.suffix.lower() != ".apk": continue
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
            # UnityPy returns WAV, OGG or M4A depending on the original codec.
            # Renaming OGG to .wav makes the file unplayable in some viewers.
            sample = clean_name(Path(sample_name).stem)
            ext = Path(sample_name).suffix.lower()
            if ext not in {'.wav', '.ogg', '.m4a', '.mp3'}: ext = '.bin'
            path = make_destination(out, 'AudioClip', base + '_' + sample + ext)
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
    # For file imports, the user can safely export beside the source file.
    # Only directory scans risk recursively scanning their own output.
    if source.is_dir() and (out == source or out.is_relative_to(source)):
        raise ValueError('Export destination must be outside the scanned source folder')
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
            if is_general_source(path):
                general_files, general_warnings = export_source(path, root, out, selected_ids)
                exported.extend(general_files)
                warnings.extend(general_warnings)
                if path.suffix.lower() != ".apk": continue
            try:
                _, objects = unity_objects(path)
                for obj in objects:
                    row = asset_row(rel, obj)
                    if selected_ids is not None and row['id'] not in selected_ids: continue
                    if not row['exportable']: continue
                    try: exported.extend(export_object(obj, row, out / 'Extracted'))
                    except Exception as exc:
                        warnings.append(f"{row['name']}: {type(exc).__name__}: {str(exc)[:180]}")
                        # A corrupt/unsupported decoder should not destroy the
                        # rest of a batch. Preserve the original serialized bytes
                        # as a clearly labelled fallback, where available.
                        try:
                            data = obj.get_raw_data()
                            if data and len(data) <= 256 * 1024 * 1024:
                                fallback = make_destination(out / 'Unconverted', 'RawUnity',
                                    f"{Path(rel).stem}_{obj.path_id}_{clean_name(row['name'])}.bin")
                                fallback.write_bytes(bytes(data))
                                exported.append(str(fallback))
                        except Exception as raw_exc:
                            warnings.append(f"{row['name']}: raw fallback unavailable: {str(raw_exc)[:100]}")
            except Exception as exc: warnings.append(f'{rel}: {type(exc).__name__}: {str(exc)[:150]}')
    if not exported and not warnings:
        warnings.append('No matching exportable assets were found. For compiled .NET assemblies use DLL Explorer to inspect C# types and save decompiled sources.')
    report = {
        'generated_at': datetime.datetime.now(datetime.timezone.utc).isoformat(),
        'source': str(Path(input_path).resolve()),
        'files_exported': len(exported),
        'warning_count': len(warnings),
        'warnings': warnings[:500],
        'files': [str(Path(x).relative_to(out)) for x in exported]
    }
    (out / 'ZORA_EXPORT_REPORT.json').write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding='utf-8')
    return {"exported": exported, "count": len(exported), "warnings": warnings[:200]}



def detect_unity_version(source: Path, project: bool) -> str:
    if project:
        version_file = source / "ProjectSettings" / "ProjectVersion.txt"
        if version_file.is_file():
            m = re.search(r"m_EditorVersion:\s*([^\s]+)", version_file.read_text(encoding="utf-8", errors="replace"))
            if m: return m.group(1)
    for path, native in candidate_files(source, project):
        if native: continue
        try:
            env = UnityPy.load(str(path))
            for file in getattr(env, 'files', {}).values():
                version = getattr(file, 'unity_version', None)
                if version and re.match(r"^20\d\d\.\d+", str(version)): return str(version)
        except Exception: pass
    return "2022.3.0f1"  # Fallback only; report explains that this version was inferred.


def recover_project(input_path: str, destination: str) -> dict:
    source, root, existing_project = source_context(input_path)
    if not existing_project and source.is_file() and source.suffix.lower() == ".exe":
        raise ValueError("Not a Unity build: a matching *_Data folder is required to recover a Unity project")
    if not existing_project and source.is_file() and source.suffix.lower() in ARCHIVES - {".apk"}:
        raise ValueError("Cannot create a Unity project from a non-Unity archive")
    if not existing_project and source.is_file() and source.suffix.lower() == ".apk":
        import zipfile
        with zipfile.ZipFile(source) as apk:
            if not any(name.startswith("assets/bin/Data/") for name in apk.namelist()):
                raise ValueError("This APK is not a recognizable Unity build")
    out = Path(destination).expanduser().resolve()
    if source.is_dir() and (out == source or out.is_relative_to(source)):
        raise ValueError('Recovery destination must be outside the scanned source directory')
    name = clean_name(source.name if existing_project else "ZoraRecoveredProject")
    project = out / name
    if project.exists():
        raise ValueError('Recovered project folder already exists; choose another output folder')
    project.mkdir(parents=True)
    warnings = []
    exported_count = 0
    version = detect_unity_version(source, existing_project)
    try:
        if existing_project:
            for folder in ("Assets", "Packages", "ProjectSettings"):
                src = source / folder
                if src.is_dir():
                    # Preserve the original .meta files, source C#, scenes, and prefabs.
                    shutil.copytree(src, project / folder, symlinks=False)
            exported_count = sum(1 for item in (project / 'Assets').rglob('*') if item.is_file())
            summary = "Original Unity project folders copied, including source scripts and metadata."
            is_original = True
        else:
            (project / "Assets" / "ZoraRecovered").mkdir(parents=True)
            (project / "ProjectSettings").mkdir()
            (project / "Packages").mkdir()
            (project / "ProjectSettings" / "ProjectVersion.txt").write_text(f"m_EditorVersion: {version}\n", encoding="utf-8")
            (project / "Packages" / "manifest.json").write_text(json.dumps({"dependencies": {}}, indent=2), encoding="utf-8")
            result = export_assets(input_path, str(project / 'Assets' / 'ZoraRecovered'), None)
            exported_count = result['count']
            warnings.extend(result['warnings'])
            summary = ('Importable Unity project scaffold made from recoverable textures, meshes, audio, fonts, '
                       'text assets and JSON metadata. Original scripts, prefab wiring, scenes, shaders and '
                       'project settings may not be reconstructable from a compiled build.')
            is_original = False
        report = {
            "source": str(Path(input_path).expanduser().resolve()),
            "unity_version": version,
            "unity_version_approximate": not existing_project,
            "original_project_preserved": is_original,
            "exported_files": exported_count,
            "summary": summary,
            "limitations": [] if is_original else [
                "This is not the original Unity source project.",
                "Compiled Mono/IL2CPP game code is not restored to original editable C#.",
                "Scene hierarchies, animator controllers, materials and prefab references may be incomplete or missing.",
                "The inferred Unity editor version may not match the build.",
                "DRM/encrypted assets are not bypassed."
            ],
            "warnings": warnings[:200],
        }
        (project / 'RECOVERY_REPORT.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
        (project / 'README.txt').write_text('Zora recovered Unity project\n\n' + summary +
                                           '\n\nSee RECOVERY_REPORT.json before opening with Unity Hub.\n', encoding='utf-8')
        return {"project_path": str(project), "count": exported_count, "warnings": warnings[:200]}
    except Exception:
        # Avoid leaving a misleading half-created project when reconstruction fails.
        shutil.rmtree(project, ignore_errors=True)
        raise

def main():
    parser = argparse.ArgumentParser(description='Zora Unity project/build asset engine')
    sub = parser.add_subparsers(dest='command', required=True)
    p_scan = sub.add_parser('scan'); p_scan.add_argument('--source', required=True)
    p_export = sub.add_parser('export'); p_export.add_argument('--source', required=True)
    p_export.add_argument('--destination', required=True)
    p_export.add_argument('--ids', nargs='*', default=None)
    p_export.add_argument('--ids-file', help='UTF-8 JSON array of selected asset IDs (for large selections)')
    p_recover = sub.add_parser('recover'); p_recover.add_argument('--source', required=True)
    p_recover.add_argument('--destination', required=True)
    args = parser.parse_args()
    try:
        if args.command == 'scan': value = scan(args.source)
        elif args.command == 'recover': value = recover_project(args.source, args.destination)
        else:
            ids = args.ids
            if args.ids_file:
                ids = json.loads(Path(args.ids_file).read_text(encoding='utf-8'))
                if not isinstance(ids, list) or len(ids) > 50000 or any(not isinstance(x, str) for x in ids):
                    raise ValueError('Selection file must contain an array of up to 50,000 asset IDs')
            value = export_assets(args.source, args.destination, set(ids) if ids is not None else None)
        print(json.dumps(value, ensure_ascii=False))
    except Exception as exc:
        print(json.dumps({"error": f"{type(exc).__name__}: {exc}"}))
        sys.exit(1)


if __name__ == '__main__': main()
