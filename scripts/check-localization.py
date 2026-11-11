#!/usr/bin/env python3
"""Check translated catalogs without installing any third-party Python package."""
import json
import re
from pathlib import Path

root = Path(__file__).resolve().parents[1] / "src/LocalAuth.App/Locales"
catalogs = {path.stem: json.loads(path.read_text(encoding="utf-8")) for path in root.glob("*.json")}
expected = {"en", "fr", "ja", "zh-TW"}
assert set(catalogs) == expected, f"Expected catalogs: {expected}"
keys = set(catalogs["en"])
for language, catalog in catalogs.items():
    assert set(catalog) == keys, f"Missing or extra translations in {language}"
    for source, translated in catalog.items():
        assert translated.strip(), f"Empty translation: {language}: {source}"
        assert sorted(re.findall(r"\{\d+\}", source)) == sorted(re.findall(r"\{\d+\}", translated)), f"Format arguments differ: {language}: {source}"
        if "|*." in source:
            assert source.count("|") == translated.count("|"), f"File filter broken: {language}: {source}"
print(f"Validated {len(keys)} messages in {len(catalogs)} translated catalogs; Simplified Chinese uses source text.")
