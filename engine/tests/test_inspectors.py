import json
import pathlib
import struct
import sys
import tempfile
import unittest
import zipfile

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))
import inspectors


class InspectorTests(unittest.TestCase):
    def test_apk_archive_index_and_export(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp)
            src = root / 'owned.apk'
            with zipfile.ZipFile(src, 'w') as archive:
                archive.writestr('res/drawable/icon.png', b'PNG image for testing')
                archive.writestr('classes.dex', b'dex\n')
            rows, warnings = inspectors.inspect_source(src, root)
            self.assertEqual(len(rows), 2)
            self.assertEqual(warnings, [])
            self.assertEqual({x['type'] for x in rows}, {'Resource', 'Binary'})
            results, warnings = inspectors.export_source(src, root, root / 'out', None)
            self.assertEqual(len(results), 2)
            self.assertEqual(warnings, [])
            self.assertEqual((root/'out'/'Archive'/'owned.apk'/'classes.dex').read_bytes(), b'dex\n')

    def test_archive_prevents_traversal(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp)
            src = root / 'owned.zip'
            with zipfile.ZipFile(src, 'w') as archive:
                archive.writestr('../escape.txt', 'bad')
                archive.writestr('/absolute.txt', 'bad')
                archive.writestr('C:/fake/file.txt', 'bad')
                archive.writestr('legit/ok.txt', 'good')
            rows, warnings = inspectors.inspect_source(src, root)
            self.assertEqual(len(rows), 1)
            self.assertGreaterEqual(len(warnings), 3)
            extracted, _ = inspectors.export_source(src, root, root / 'out', None)
            self.assertEqual(len(extracted), 1)
            self.assertFalse((root / 'escape.txt').exists())

    def test_pe_header_and_sections(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=pathlib.Path(tmp)
            path=root/'owned.exe'
            header=bytearray(512)
            header[:2]=b'MZ'
            struct.pack_into('<I', header,0x3c,0x80)
            header[0x80:0x84]=b'PE\x00\x00'
            struct.pack_into('<HHIIIHH',header,0x84,0x8664,1,0,0,0,2,0)
            struct.pack_into('<H',header,0x98,0x20b)
            struct.pack_into('<8sIIIIIIHHI',header,0x9A,b'.rdata\x00\x00',4,0,4,508,0,0,0,0,0)
            header[508:512]=b'abcd'
            path.write_bytes(header)
            rows, warnings=inspectors.inspect_source(path,root)
            self.assertEqual(warnings,[])
            self.assertEqual([x['type'] for x in rows],['PE Report','PE Section'])
            exported, warnings=inspectors.export_source(path,root,root/'out',None)
            self.assertEqual(len(exported),2)
            report=json.loads((root/'out'/'PE'/'owned.exe'/'report.json').read_text())
            self.assertEqual(report['architecture'],'x64')
            self.assertEqual(report['sha256'],inspectors.file_sha256(path))

    def test_oversized_archive_entry_skipped(self):
        self.assertIsNone(inspectors._safe_member('..\\private.txt'))
        self.assertIsNone(inspectors._safe_member('../private.txt'))
        self.assertIsNone(inspectors._safe_member('/private.txt'))
        self.assertIsNotNone(inspectors._safe_member('assets/icon.png'))


if __name__ == '__main__': unittest.main()
