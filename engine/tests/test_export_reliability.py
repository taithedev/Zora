"""Regressions found in real desktop export / recovery workflows."""
import pathlib
import sys
import tempfile
import types
import unittest
import zipfile
from unittest.mock import patch

sys.modules.setdefault('UnityPy', types.ModuleType('UnityPy'))
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))
import zora_engine as engine


class ExportReliabilityTests(unittest.TestCase):
    def test_file_source_may_export_into_sibling_folder(self):
        with tempfile.TemporaryDirectory() as tmp:
            base = pathlib.Path(tmp)
            archive = base / 'owned.apk'
            with zipfile.ZipFile(archive, 'w') as z:
                z.writestr('res/icon.png', b'original')
            out = base / 'exported'
            result = engine.export_assets(str(archive), str(out), None)
            self.assertEqual(result['count'], 1)
            self.assertEqual((out/'Archive'/'owned.apk'/'res'/'icon.png').read_bytes(), b'original')
            self.assertTrue((out/'ZORA_EXPORT_REPORT.json').exists())

    def test_repeat_archive_exports_preserve_original_file(self):
        with tempfile.TemporaryDirectory() as tmp:
            base = pathlib.Path(tmp)
            archive = base / 'owned.apk'
            with zipfile.ZipFile(archive, 'w') as z:
                z.writestr('assets/readme.txt', b'first')
            out = base / 'exported'
            self.assertEqual(engine.export_assets(str(archive), str(out), None)['count'], 1)
            self.assertEqual(engine.export_assets(str(archive), str(out), None)['count'], 1)
            self.assertEqual((out/'Archive'/'owned.apk'/'assets'/'readme.txt').read_bytes(), b'first')
            self.assertEqual((out/'Archive'/'owned.apk'/'assets'/'readme_1.txt').read_bytes(), b'first')

    def test_unsupported_unity_asset_preserves_raw_bytes_when_possible(self):
        class FakeType:
            name = 'Texture2D'
        class FakeObject:
            type = FakeType()
            path_id = 123
            byte_size = 4
            def peek_name(self): return 'texture'
            def parse_as_object(self): raise ValueError('unknown texture decoder')
            def get_raw_data(self): return b'raw!'
        with tempfile.TemporaryDirectory() as tmp:
            base = pathlib.Path(tmp)
            source = base / 'test.assets'
            source.write_bytes(b'not used')
            with patch.object(engine, 'unity_objects', return_value=(None, [FakeObject()])):
                result = engine.export_assets(str(source), str(base / 'output'), None)
            self.assertEqual(result['count'], 1)
            self.assertTrue(any('unknown texture decoder' in warning for warning in result['warnings']))
            self.assertEqual(pathlib.Path(result['exported'][0]).read_bytes(), b'raw!')

    def test_directory_source_cannot_export_inside_itself(self):
        with tempfile.TemporaryDirectory() as tmp:
            project = pathlib.Path(tmp) / 'Project'
            (project/'Assets').mkdir(parents=True)
            (project/'ProjectSettings').mkdir()
            with self.assertRaisesRegex(ValueError, 'outside'):
                engine.export_assets(str(project), str(project/'export'), None)


if __name__ == '__main__': unittest.main()
