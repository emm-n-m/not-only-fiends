#!/usr/bin/env python3
"""Restore legacy equipment descriptions that were truncated during bulk extraction.

The affected JSON values end in a literal ``...``. Their source entries are selected by the
description anchors in the local SRD HTML, not by arbitrary anchors such as embedded tables.
Run without arguments to report; pass ``--write`` to update the JSON files.
"""

from __future__ import annotations

import argparse
import html
import json
import re
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
EQUIPMENT_DIR = ROOT / "NotOnlyFiendsStudio/Content/packs/srd_core/equipment"
SRD_DIR = ROOT / "NotOnlyFiendsStudio/Content/srd_html"

SOURCE_BY_CATEGORY = {
    "ring": ["magicItemsPRR.html"],
    "rod": ["magicItemsPRR.html"],
    "staff": ["magicItemsSSW.html"],
    "wondrous": ["magicItemsWI.html"],
}

ANCHOR_OVERRIDES = {
    "wondrous:bag_of_tricks_gray": "bag-of-tricks",
    "wondrous:bag_of_tricks_rust": "bag-of-tricks",
    "wondrous:bag_of_tricks_tan": "bag-of-tricks",
    "wondrous:stone_horse_courser": "stone-horse",
    "wondrous:stone_horse_destrier": "stone-horse",
    "wondrous:strand_of_prayer_beads_lesser": "strand-of-prayer-beads",
    "wondrous:strand_of_prayer_beads_standard": "strand-of-prayer-beads",
    "wondrous:strand_of_prayer_beads_greater": "strand-of-prayer-beads",
}

DESCRIPTION_ANCHOR = re.compile(
    r'<a id="([^"]+)"></a>\s*'
    r'(?=(?:<p>)?(?:<b>|<i>|<span[^>]*font-(?:style|weight):\s*bold))',
    re.IGNORECASE,
)
TAG = re.compile(r"<[^>]+>")
WHITESPACE = re.compile(r"\s+")


def source_entries(source_name: str) -> dict[str, str]:
    raw = (SRD_DIR / source_name).read_text(encoding="utf-8")
    matches = list(DESCRIPTION_ANCHOR.finditer(raw))
    entries: dict[str, str] = {}
    for index, match in enumerate(matches):
        end = matches[index + 1].start() if index + 1 < len(matches) else len(raw)
        fragment = raw[match.end() : end]
        text = html.unescape(TAG.sub(" ", fragment))
        text = WHITESPACE.sub(" ", text).strip()
        _, separator, description = text.partition(":")
        if separator and description.strip():
            entries[match.group(1)] = description.strip()
    return entries


def resolve_anchor(item: dict, entries: dict[str, str]) -> str:
    item_id = item["id"]
    if item_id in ANCHOR_OVERRIDES:
        return ANCHOR_OVERRIDES[item_id]

    category, bare_id = item_id.split(":", 1)
    bare = bare_id.replace("_", "-")
    candidates = [bare, f"{category}-of-{bare}"]
    for candidate in candidates:
        if candidate in entries:
            return candidate

    suffix_matches = [anchor for anchor in entries if anchor.endswith(f"-{bare}")]
    if len(suffix_matches) == 1:
        return suffix_matches[0]
    raise ValueError(f"cannot uniquely resolve SRD anchor for {item_id}: {suffix_matches}")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--write", action="store_true", help="write restored descriptions")
    args = parser.parse_args()

    sources = {
        source: source_entries(source)
        for names in SOURCE_BY_CATEGORY.values()
        for source in names
    }
    restored = 0

    for path in sorted(EQUIPMENT_DIR.glob("*.json")):
        items = json.loads(path.read_text(encoding="utf-8"))
        changed = False
        for item in items:
            description = item.get("description", "")
            if not description.endswith("..."):
                continue

            category = item["category"]
            source_names = SOURCE_BY_CATEGORY.get(category, [])
            matches: list[str] = []
            for source_name in source_names:
                entries = sources[source_name]
                try:
                    anchor = resolve_anchor(item, entries)
                except ValueError:
                    continue
                matches.append(entries[anchor])

            if len(matches) != 1:
                raise ValueError(f"expected one source description for {item['id']}, found {len(matches)}")
            item["description"] = matches[0]
            restored += 1
            changed = True

        if changed and args.write:
            path.write_text(json.dumps(items, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")

    action = "restored" if args.write else "would restore"
    print(f"{action} {restored} truncated equipment descriptions")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
