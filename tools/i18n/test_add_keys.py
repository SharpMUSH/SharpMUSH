import os
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from add_keys import add_keys, read  # noqa: E402

RESX = """<?xml version="1.0" encoding="utf-8"?>
<root>
  <data name="Existing&amp;Key" xml:space="preserve">
    <value>Already here</value>
  </data>
</root>
"""


def names(path: str) -> dict[str, str]:
    return {d.get("name"): d.findtext("value") for d in ET.parse(path).getroot().iter("data")}


class AddKeysTests(unittest.TestCase):
    def setUp(self):
        self.dir = tempfile.TemporaryDirectory()
        self.addCleanup(self.dir.cleanup)
        for name in ("SharedResource.resx", "SharedResource.de.resx"):
            with open(os.path.join(self.dir.name, name), "w", encoding="utf-8") as f:
                f.write(RESX)

    def path(self, locale: str = "") -> str:
        return os.path.join(self.dir.name, "SharedResource.resx" if locale == "" else f"SharedResource.{locale}.resx")

    def test_key_with_xml_attribute_characters_stays_well_formed(self):
        key = 'Say"Hello<&>'
        self.assertEqual(add_keys({key: {"": "Hi & <bye>", "de": "Hallo"}}, self.dir.name), 0)
        self.assertEqual(names(self.path())[key], "Hi & <bye>")
        self.assertEqual(names(self.path("de"))[key], "Hallo")

    def test_duplicate_detection_compares_decoded_names(self):
        before = read(self.path())
        self.assertEqual(add_keys({"Existing&Key": {"": "Again"}}, self.dir.name), 1)
        self.assertEqual(read(self.path()), before)

    def test_missing_locale_writes_nothing(self):
        before = read(self.path())
        self.assertEqual(add_keys({"NewKey": {"": "New", "xx": "Nope"}}, self.dir.name), 1)
        self.assertEqual(read(self.path()), before)


if __name__ == "__main__":
    unittest.main()
