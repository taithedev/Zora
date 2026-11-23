"""Static regression checks for the WPF monochrome theme.

The interactive UI still requires a human Windows smoke test; these checks
catch the two concrete defects reported by screenshots in November 2026.
"""
from pathlib import Path
from xml.etree import ElementTree as ET
import re
import unittest

APP = Path(__file__).resolve().parents[1] / "Zora.Desktop"
NS = {"p": "http://schemas.microsoft.com/winfx/2006/xaml/presentation",
      "x": "http://schemas.microsoft.com/winfx/2006/xaml"}


class NativeThemeTests(unittest.TestCase):
    def test_xaml_documents_parse(self):
        for file in APP.rglob("*.xaml"):
            with self.subTest(file=file.relative_to(APP)):
                ET.parse(file)

    def test_textblock_inherits_its_foreground(self):
        root = ET.parse(APP / "App.xaml").getroot()
        # The implicit TextBlock.Foreground=White made every white button blank.
        styles = [style for style in root.findall(".//p:Style", NS)
                  if style.get("TargetType") == "TextBlock" and
                  "{%s}Key" % NS["x"] not in style.attrib]
        self.assertEqual(len(styles), 1)
        setters = styles[0].findall("p:Setter", NS)
        self.assertFalse(any(s.get("Property") == "Foreground" for s in setters))

    def test_accent_button_uses_black_text(self):
        root = ET.parse(APP / "App.xaml").getroot()
        for style in root.findall(".//p:Style", NS):
            if style.get("{%s}Key" % NS["x"]) == "AccentButton":
                settings = {s.get("Property"): s.get("Value") for s in style.findall("p:Setter", NS)}
                self.assertEqual(settings["Foreground"].upper(), "#000000")
                self.assertEqual(settings["Background"].upper(), "#FFFFFF")
                return
        self.fail("Missing AccentButton style")

    def test_data_grids_have_explicit_cell_text_styles(self):
        for file in [APP / "MainWindow.xaml", APP / "Views" / "ZuroPanel.xaml"]:
            root = ET.parse(file).getroot()
            with self.subTest(file=file.name):
                columns = root.findall(".//p:DataGridTextColumn", NS)
                self.assertTrue(columns)
                self.assertTrue(all(c.get("ElementStyle") == "{StaticResource GridValueText}" for c in columns))

    def test_zuro_key_label_deserializes_postgres_snake_case(self):
        source = (APP / "Views" / "ZuroPanel.xaml.cs").read_text(encoding="utf8")
        self.assertRegex(source, r'\[JsonPropertyName\("key_label"\)\]\s*public string KeyLabel')
        self.assertIn("UsernameDisplay", source)

    def test_all_sidebar_names_exist_and_empty_state_is_wired(self):
        xaml = (APP / "MainWindow.xaml").read_text(encoding="utf8")
        code = (APP / "MainWindow.xaml.cs").read_text(encoding="utf8")
        for name in ["ExplorerNav", "EditorNav", "DiscordNav", "ZuroNav", "EmptyAssetsPanel", "SearchHint"]:
            with self.subTest(name=name):
                self.assertIn(f'x:Name="{name}"', xaml)
                self.assertIn(name, code)


if __name__ == "__main__":
    unittest.main()
