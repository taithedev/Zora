"""Read-only inspection and resource exports for unprotected PE files and ZIP containers.

No code execution, decryption, entitlement bypass, or source-code reconstruction.
"""
from __future__ import annotations

import hashlib
import json
import os
import re
import struct
import zipfile
from pathlib import Path, PurePosixPath

ARCHIVES = {'.apk', '.aab', '.jar', '.zip', '.msix', '.nupkg'}
EXECUTABLES = {'.exe', '.dll', '.sys', '.ocx'}
MAX_INPUT_SIZE = 2 * 1024**3
MAX_ENTRY_SIZE = 256 * 1024**2
MAX_TOTAL_EXPORTED = 2 * 1024**3
MAX_ARCHIVE_ROWS = 50000


def is_general_source(path: Path) -> bool:
    return path.suffix.lower() in ARCHIVES | EXECUTABLES


def _row(key: str, name: str, typ: str, origin: str, source: str, size: int, exportable=True):
    return {'id': key, 'name': name[:160], 'type': typ, 'origin': origin,
            'source': source, 'size': size, 'exportable': exportable}


def _rel(path: Path, root: Path) -> str:
    try: return path.relative_to(root).as_posix()
    except ValueError: return path.name


def _safe_member(name: str) -> PurePosixPath | None:
    if not name or '\\' in name or '\x00' in name or name.startswith('/') or re.match(r'^[A-Za-z]:', name):
        return None
    parts = PurePosixPath(name).parts
    if not parts or any(p in {'.', '..'} for p in parts):
        return None
    return PurePosixPath(*parts)


def _inside(base: Path, path: Path) -> Path:
    resolved = path.resolve()
    if not resolved.is_relative_to(base.resolve()):
        raise ValueError('Archive path escapes destination')
    return resolved


def _file_kind(filename: str) -> str:
    suffix = Path(filename).suffix.lower()
    if suffix in {'.png', '.webp', '.jpg', '.jpeg', '.bmp', '.gif', '.svg', '.xml', '.arsc'}: return 'Resource'
    if suffix in {'.dex', '.class', '.dll', '.so'}: return 'Binary'
    if suffix in {'.json', '.txt', '.html', '.css', '.js', '.properties', '.md'}: return 'Text'
    if suffix in {'.mp3', '.wav', '.ogg', '.flac'}: return 'Audio'
    if suffix in {'.fbx', '.obj', '.glb', '.gltf'}: return 'Model'
    return 'Archive File'


def inspect_archive(path: Path, root: Path) -> tuple[list[dict], list[str]]:
    rel = _rel(path, root)
    rows, warnings = [], []
    try:
        with zipfile.ZipFile(path) as archive:
            for index, entry in enumerate(archive.infolist()):
                if index >= MAX_ARCHIVE_ROWS:
                    warnings.append(f'{rel}: archive entries capped at {MAX_ARCHIVE_ROWS}')
                    break
                if entry.is_dir(): continue
                member = _safe_member(entry.filename)
                if member is None:
                    warnings.append(f'{rel}: skipped unsafe archive entry')
                    continue
                unix_mode = (entry.external_attr >> 16) & 0o170000
                ratio = entry.file_size / max(1, entry.compress_size)
                safe = not (entry.flag_bits & 1) and entry.file_size <= MAX_ENTRY_SIZE and unix_mode != 0o120000 and ratio <= 1000
                rows.append(_row(f'archive:{rel}:{index}', member.name, _file_kind(member.name), 'Archive',
                                 f'{rel}!{member.as_posix()}', entry.file_size, safe))
    except (OSError, zipfile.BadZipFile) as exc:
        warnings.append(f'{rel}: {type(exc).__name__}: {exc}')
    return rows, warnings


def _pe_details(path: Path) -> tuple[dict, list[dict], list[str]]:
    # Parse the standard Portable Executable header even if the optional pefile package is missing.
    with path.open('rb') as stream:
        if stream.read(2) != b'MZ': raise ValueError('Not a Windows PE executable')
        stream.seek(0x3c)
        data = stream.read(4)
        if len(data) < 4: raise ValueError('Truncated executable')
        offset = struct.unpack('<I', data)[0]
        if offset < 64 or offset > 16 * 1024 * 1024: raise ValueError('Invalid PE header offset')
        stream.seek(offset)
        if stream.read(4) != b'PE\x00\x00': raise ValueError('Not a PE executable')
        header = stream.read(20)
        if len(header) != 20: raise ValueError('Truncated PE file header')
        machine, sections, timestamp, _, _, optional_size, flags = struct.unpack('<HHIIIHH', header)
        if sections > 96: raise ValueError('Excessive PE sections')
        optional = stream.read(optional_size)
        if len(optional) != optional_size: raise ValueError('Truncated PE optional header')
        magic = struct.unpack('<H', optional[:2])[0] if len(optional) >= 2 else 0
        architecture = {0x8664: 'x64', 0x14c: 'x86', 0xAA64: 'ARM64', 0x1C4: 'ARM'}.get(machine, f'0x{machine:04X}')
        meta = {'format': 'Portable Executable', 'architecture': architecture, 'machine': machine,
                'section_count': sections, 'timestamp': timestamp, 'flags': flags,
                'optional_header_magic': hex(magic), 'size': path.stat().st_size,
                'sha256': file_sha256(path)}
        items = []
        for i in range(sections):
            section = stream.read(40)
            if len(section) < 40: raise ValueError('Truncated PE section table')
            name = section[:8].split(b'\x00', 1)[0].decode('latin1', errors='replace')
            raw_size, raw_offset = struct.unpack_from('<II', section, 16)
            if raw_offset + raw_size > meta['size']:
                raw_size = 0  # Invalid section; do not try to extract it.
            items.append({'name': name, 'offset': raw_offset, 'size': raw_size, 'index': i})
        return meta, items, []


def file_sha256(path: Path) -> str:
    h = hashlib.sha256()
    with path.open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b''):
            h.update(chunk)
    return h.hexdigest()



def _pe_extra(path: Path) -> tuple[dict, list[dict]]:
    """Optional PE import/export details and ordinary resources via pefile."""
    try:
        import pefile
    except ImportError:
        return {'resource_support': 'Basic PE headers only; install pefile to read resource tables'}, []
    try:
        pe = pefile.PE(str(path), fast_load=True)
        pe.parse_data_directories(directories=[
            pefile.DIRECTORY_ENTRY['IMAGE_DIRECTORY_ENTRY_IMPORT'],
            pefile.DIRECTORY_ENTRY['IMAGE_DIRECTORY_ENTRY_EXPORT'],
            pefile.DIRECTORY_ENTRY['IMAGE_DIRECTORY_ENTRY_RESOURCE']])
        imports = [entry.dll.decode('ascii', 'replace') for entry in getattr(pe, 'DIRECTORY_ENTRY_IMPORT', [])][:5000]
        exported = [item.name.decode('ascii', 'replace') for item in
                    getattr(getattr(pe, 'DIRECTORY_ENTRY_EXPORT', None), 'symbols', []) if item.name][:5000]
        resources = []
        for kind in getattr(getattr(pe, 'DIRECTORY_ENTRY_RESOURCE', None), 'entries', []):
            if not hasattr(kind, 'directory'): continue
            for item in kind.directory.entries:
                if not hasattr(item, 'directory'): continue
                for language in item.directory.entries:
                    if not hasattr(language, 'data'): continue
                    info = language.data.struct
                    if info.Size <= 0 or info.Size > MAX_ENTRY_SIZE: continue
                    kind_id = getattr(kind, 'id', None)
                    name = pefile.RESOURCE_TYPE.get(kind_id, f'RT_{kind_id}') if kind_id is not None else str(kind.name)
                    name = str(name)
                    identity = str(getattr(item, 'id', None) or getattr(item, 'name', None) or '0')
                    language_id = str(getattr(language, 'id', 0))
                    resources.append({'type': name, 'identity': identity, 'language': language_id,
                                      'rva': int(info.OffsetToData), 'size': int(info.Size)})
                    if len(resources) >= 10000: break
                if len(resources) >= 10000: break
            if len(resources) >= 10000: break
        return {'imports': imports, 'exports': exported, 'resource_count': len(resources)}, resources
    except Exception as exc:
        return {'pe_directory_warning': str(exc)[:160]}, []
    finally:
        if 'pe' in locals(): pe.close()


def inspect_pe(path: Path, root: Path) -> tuple[list[dict], list[str]]:
    rel = _rel(path, root)
    try:
        meta, sections, warnings = _pe_details(path)
        extra, resources = _pe_extra(path)
        meta.update(extra)
        rows = [_row(f'pe-report:{rel}', f'{path.name} / PE report', 'PE Report', 'Executable', rel,
                     len(json.dumps(meta)), True)]
        rows.extend(_row(f'pe-section:{rel}:{section["index"]}', section['name'] or f'Section {section["index"]}',
                         'PE Section', 'Executable', rel, section['size'], section['size'] > 0)
                    for section in sections)
        rows.extend(_row(f'pe-resource:{rel}:{i}', f'{resource["type"]} / {resource["identity"]}',
                         'PE Resource', 'Executable', rel, resource['size'], True)
                    for i, resource in enumerate(resources))
        return rows, warnings
    except (OSError, ValueError) as exc:
        return [], [f'{rel}: {exc}']


def inspect_source(path: Path, root: Path) -> tuple[list[dict], list[str]]:
    if path.stat().st_size > MAX_INPUT_SIZE: return [], [f'{path.name}: file exceeds 2 GB inspection limit']
    if path.suffix.lower() in ARCHIVES: return inspect_archive(path, root)
    if path.suffix.lower() in EXECUTABLES: return inspect_pe(path, root)
    return [], []


def _source_id(row_id: str, kind: str) -> tuple[str, str]:
    prefix = kind + ':'
    if not row_id.startswith(prefix): raise ValueError('Invalid row ID')
    rest = row_id[len(prefix):]
    filename, _, index = rest.rpartition(':')
    if not filename or not index.isdigit(): raise ValueError('Invalid row ID')
    return filename, index


def export_source(path: Path, root: Path, output: Path, selected_ids: set[str] | None):
    rows, warnings = inspect_source(path, root)
    exported, total = [], 0
    rel = _rel(path, root)
    output = output.resolve()
    if path.suffix.lower() in ARCHIVES:
        try:
            with zipfile.ZipFile(path) as archive:
                entries = archive.infolist()
                for row in rows:
                    if selected_ids is not None and row['id'] not in selected_ids: continue
                    if not row['exportable']: continue
                    filename, index = _source_id(row['id'], 'archive')
                    if filename != rel: continue
                    entry = entries[int(index)]
                    member = _safe_member(entry.filename)
                    if member is None or entry.flag_bits & 1: continue
                    # Bound extraction and never follow links or write outside export root.
                    unix_mode = (entry.external_attr >> 16) & 0o170000
                    if unix_mode == 0o120000: continue
                    if entry.file_size > MAX_ENTRY_SIZE or total + entry.file_size > MAX_TOTAL_EXPORTED:
                        warnings.append(f'{entry.filename}: exceeds safe extraction limits')
                        continue
                    if entry.compress_size and entry.file_size > entry.compress_size * 1000:
                        warnings.append(f'{entry.filename}: suspicious compression ratio')
                        continue
                    destination = _inside(output, output / 'Archive' / path.name / Path(*member.parts))
                    destination.parent.mkdir(parents=True, exist_ok=True)
                    if destination.exists():
                        # Repeat exports should not fail or overwrite an earlier
                        # result. Choose an unused deterministic suffix instead.
                        base = destination
                        for suffix in range(1, 1001):
                            destination = base.with_name(f'{base.stem}_{suffix}{base.suffix}')
                            if not destination.exists(): break
                        else:
                            warnings.append(f'{entry.filename}: too many existing versions')
                            continue
                    try:
                        with archive.open(entry) as source, destination.open('xb') as target:
                            copied = 0
                            while True:
                                block = source.read(min(1024 * 1024, MAX_ENTRY_SIZE - copied + 1))
                                if not block: break
                                copied += len(block)
                                if copied > MAX_ENTRY_SIZE: raise ValueError('Exceeded entry size limit')
                                target.write(block)
                        total += copied
                        exported.append(str(destination))
                    except Exception as exc:
                        destination.unlink(missing_ok=True)
                        warnings.append(f'{entry.filename}: {type(exc).__name__}: {str(exc)[:120]}')
        except (OSError, zipfile.BadZipFile) as exc:
            warnings.append(f'{path.name}: {exc}')
    elif path.suffix.lower() in EXECUTABLES:
        try:
            meta, sections, _ = _pe_details(path)
            extra, resources = _pe_extra(path)
            meta.update(extra)
            for row in rows:
                if selected_ids is not None and row['id'] not in selected_ids: continue
                if row['id'].startswith('pe-report:'):
                    destination = _inside(output, output / 'PE' / path.name / 'report.json')
                    destination.parent.mkdir(parents=True, exist_ok=True)
                    if destination.exists(): continue
                    destination.write_text(json.dumps(meta, indent=2), encoding='utf-8')
                    exported.append(str(destination))
                elif row['id'].startswith('pe-resource:'):
                    filename, index = _source_id(row['id'], 'pe-resource')
                    if filename != rel: continue
                    resource = resources[int(index)]
                    if resource['size'] > MAX_ENTRY_SIZE: continue
                    import pefile
                    pe = pefile.PE(str(path), fast_load=True)
                    try:
                        blob = pe.get_data(resource['rva'], resource['size'])
                        if len(blob) != resource['size']: raise ValueError('Resource size mismatch')
                    finally:
                        pe.close()
                    label = re.sub(r'[^a-zA-Z0-9._-]', '_',
                                   resource['type'] + '_' + resource['identity'] + '_' + resource['language'])
                    extension = '.xml' if 'MANIFEST' in resource['type'] else '.bin'
                    destination = _inside(output, output / 'PE' / path.name / 'Resources' / (label + extension))
                    destination.parent.mkdir(parents=True, exist_ok=True)
                    if destination.exists(): continue
                    destination.write_bytes(blob)
                    exported.append(str(destination))
                elif row['id'].startswith('pe-section:'):
                    filename, index = _source_id(row['id'], 'pe-section')
                    if filename != rel: continue
                    section = sections[int(index)]
                    if section['size'] <= 0 or section['size'] > MAX_ENTRY_SIZE: continue
                    # Only raw data. Do not execute, patch, or bypass any protection.
                    destination = _inside(output, output / 'PE' / path.name /
                                          f'{section["index"]:02d}_{re.sub(r"[^a-zA-Z0-9._-]", "_", section["name"])}.bin')
                    destination.parent.mkdir(parents=True, exist_ok=True)
                    if destination.exists(): continue
                    with path.open('rb') as source, destination.open('xb') as target:
                        source.seek(section['offset'])
                        remaining = section['size']
                        while remaining:
                            chunk = source.read(min(1024 * 1024, remaining))
                            if not chunk: raise ValueError('Truncated PE section')
                            target.write(chunk)
                            remaining -= len(chunk)
                    exported.append(str(destination))
        except (OSError, ValueError) as exc:
            warnings.append(f'{path.name}: {exc}')
    return exported, warnings
