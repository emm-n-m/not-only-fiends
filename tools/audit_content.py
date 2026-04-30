#!/usr/bin/env python3
"""Audit content directories for duplicate IDs across JSON files."""

import json
import os
import sys
from collections import defaultdict


def scan_directory(content_dir: str, subdir: str) -> dict[str, list[tuple[str, dict]]]:
    """Scan a content subdirectory, returning {id: [(filename, item), ...]}."""
    path = os.path.join(content_dir, subdir)
    if not os.path.isdir(path):
        return {}

    items_by_id: dict[str, list[tuple[str, dict]]] = defaultdict(list)

    for root, _, files in os.walk(path):
        for fname in sorted(files):
            if not fname.endswith(".json"):
                continue
            fpath = os.path.join(root, fname)
            rel = os.path.relpath(fpath, content_dir)
            with open(fpath, encoding="utf-8") as f:
                data = json.load(f)
            if not isinstance(data, list):
                continue
            for item in data:
                if isinstance(item, dict) and "id" in item:
                    items_by_id[item["id"]].append((rel, item))

    return items_by_id


def summarize_item(item: dict) -> str:
    """One-line summary of an item's key fields."""
    parts = []
    if "type" in item:
        parts.append(f"type={item['type']}")
    if "grantedPermabuffs" in item:
        parts.append(f"{len(item['grantedPermabuffs'])} permabuffs")
    if "prerequisites" in item:
        parts.append(f"{len(item['prerequisites'])} prereqs")
    if "description" in item:
        desc = item["description"]
        if len(desc) > 50:
            desc = desc[:47] + "..."
        parts.append(f'desc="{desc}"')
    return ", ".join(parts) if parts else "(no notable fields)"


def main():
    if len(sys.argv) < 2:
        print(f"Usage: {sys.argv[0]} <content-dir>")
        sys.exit(1)

    content_dir = sys.argv[1]
    subdirs = ["classes", "racial_hd", "races", "templates", "feats", "domains", "skills", "spells"]

    total_dupes = 0

    for subdir in subdirs:
        items_by_id = scan_directory(content_dir, subdir)
        dupes = {k: v for k, v in items_by_id.items() if len(v) > 1}

        if not dupes:
            continue

        total_dupes += len(dupes)
        print(f"\n{subdir}/ — {len(dupes)} duplicate(s):")
        for item_id, entries in sorted(dupes.items()):
            print(f"  {item_id}:")
            for fname, item in entries:
                print(f"    {fname}: {summarize_item(item)}")

    if total_dupes == 0:
        print("No duplicate IDs found.")
    else:
        print(f"\nTotal: {total_dupes} duplicate ID(s) across all content types.")


if __name__ == "__main__":
    main()
