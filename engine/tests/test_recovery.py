import importlib.util
import json
import os
import pathlib
import sys
import tempfile
import types
import unittest
import zipfile
import struct

sys.modules.setdefault('UnityPy', types.ModuleType('UnityPy'))
mod_path = pathlib.Path(__file__).resolve().parents[1] / 'zora_engine.py'
sys.path.insert(0, str(mod_path.parent))
spec = importlib.util.spec_from_file_location('zora_engine', mod_path)
engine = importlib.util.module_from_spec(spec)
spec.loader.exec_module(engine)


class TestRecovery(unittest.TestCase):
    def test_nonunity_windows_executable_is_inspected_not_recovered(self):
        with tempfile.TemporaryDirectory() as temp:
            folder = pathlib.Path(temp)
            exe = folder / 'Standalone.exe'
            header = bytearray(256)
            header[:2] = b'MZ'
            struct.pack_into('<I', header, 0x3c, 0x80)
            header[0x80:0x84] = b'PE\x00\x00'
            struct.pack_into('<HHIIIHH', header, 0x84, 0x8664, 0, 0, 0, 0, 2, 0)
            exe.write_bytes(header)
            indexed = engine.scan(str(exe))
            self.assertEqual(indexed['warnings'], [])
            self.assertEqual(indexed['assets'][0]['type'], 'PE Report')
            with self.assertRaisesRegex(ValueError, 'Not a Unity build'):
                engine.recover_project(str(exe), str(folder / 'output'))

    def test_nonunity_android_package_cannot_claim_unity_project(self):
        with tempfile.TemporaryDirectory() as temp:
            folder = pathlib.Path(temp)
            apk = folder / 'Standalone.apk'
            with zipfile.ZipFile(apk, 'w') as archive:
                archive.writestr('AndroidManifest.xml', b'placeholder')
                archive.writestr('res/drawable/logo.png', b'placeholder')
            with self.assertRaisesRegex(ValueError, 'not a recognizable Unity'):
                engine.recover_project(str(apk), str(folder / 'output'))

    def test_original_project_meta_and_scripts(self):
        with tempfile.TemporaryDirectory() as temp:
            folder = pathlib.Path(temp)
            source = folder / 'OwnedProject'
            (source / 'Assets' / 'Scripts').mkdir(parents=True)
            (source / 'Assets' / 'Scripts' / 'Movement.cs').write_text('public class Movement {}')
            (source / 'Assets' / 'Scripts' / 'Movement.cs.meta').write_text('fileFormatVersion: 2')
            (source / 'ProjectSettings').mkdir()
            (source / 'ProjectSettings' / 'ProjectVersion.txt').write_text('m_EditorVersion: 2022.3.13f1')
            output = folder / 'output'
            result = engine.recover_project(str(source), str(output))
            project = pathlib.Path(result['project_path'])
            self.assertTrue((project / 'Assets' / 'Scripts' / 'Movement.cs').exists())
            self.assertTrue((project / 'Assets' / 'Scripts' / 'Movement.cs.meta').exists())
            report = json.loads((project / 'RECOVERY_REPORT.json').read_text())
            self.assertTrue(report['original_project_preserved'])
            self.assertEqual(report['unity_version'], '2022.3.13f1')

    def test_compiled_exe_creates_scaffold_without_false_source_claim(self):
        with tempfile.TemporaryDirectory() as temp:
            folder = pathlib.Path(temp)
            exe = folder / 'Demo.exe'
            exe.write_bytes(b'MZ')
            data = folder / 'Demo_Data'
            data.mkdir()
            output = folder / 'output'
            result = engine.recover_project(str(exe), str(output))
            project = pathlib.Path(result['project_path'])
            self.assertTrue((project / 'Assets' / 'ZoraRecovered').is_dir())
            self.assertTrue((project / 'ProjectSettings' / 'ProjectVersion.txt').is_file())
            self.assertTrue((project / 'Packages' / 'manifest.json').is_file())
            report = json.loads((project / 'RECOVERY_REPORT.json').read_text())
            self.assertFalse(report['original_project_preserved'])
            self.assertGreater(len(report['limitations']), 0)

    def test_no_overwriting_existing_project(self):
        with tempfile.TemporaryDirectory() as temp:
            folder = pathlib.Path(temp)
            source = folder / 'Owned'
            (source / 'Assets').mkdir(parents=True)
            (source / 'ProjectSettings').mkdir()
            out = folder / 'dest'
            engine.recover_project(str(source), str(out))
            with self.assertRaises(ValueError):
                engine.recover_project(str(source), str(out))

    def test_reject_recovery_destination_inside_source(self):
        with tempfile.TemporaryDirectory() as temp:
            source = pathlib.Path(temp) / 'Owned'
            (source / 'Assets').mkdir(parents=True)
            (source / 'ProjectSettings').mkdir()
            with self.assertRaises(ValueError):
                engine.recover_project(str(source), str(source / 'output'))


if __name__ == '__main__':
    unittest.main()
