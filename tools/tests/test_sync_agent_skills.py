from __future__ import annotations

import importlib.util
import sys
import tempfile
import unittest
from pathlib import Path


SCRIPT = Path(__file__).resolve().parents[1] / "sync_agent_skills.py"
SPEC = importlib.util.spec_from_file_location("sync_agent_skills", SCRIPT)
sync = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = sync
SPEC.loader.exec_module(sync)


class FrontmatterTests(unittest.TestCase):
    def test_folded_description_is_parsed_as_yaml(self) -> None:
        content = """---
name: demo
description: >-
  First line folded
  into the second line.
---
Body
"""

        _, _, metadata = sync.parse_frontmatter(
            Path("/tmp/demo/SKILL.md"), content
        )

        self.assertEqual(
            "First line folded into the second line.", metadata["description"]
        )

    def test_markdown_examples_are_not_misparsed_as_links(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            skill = Path(temporary) / "demo"
            skill.mkdir()
            (skill / "SKILL.md").write_text(
                """---
name: demo
description: Demo skill.
---
```md
[missing](not-a-real-file.md)
```
[titled](real.md "Title") and [parenthesized](real_(v1).md)
""",
                encoding="utf-8",
            )

            rendered = sync.render_skill(skill, "codex")

        self.assertIn(b"not-a-real-file.md", rendered)


class ClaudeAdapterTests(unittest.TestCase):
    def setUp(self) -> None:
        self.old_agents_root = sync.CLAUDE_AGENTS_ROOT
        self.old_source_root = sync.SOURCE_ROOT
        self.old_adapters = sync.SHARED_CLAUDE_ADAPTERS
        self.temporary = tempfile.TemporaryDirectory()
        root = Path(self.temporary.name)
        sync.CLAUDE_AGENTS_ROOT = root / "agents"
        sync.CLAUDE_AGENTS_ROOT.mkdir()
        sync.SOURCE_ROOT = root / "skills"
        (sync.SOURCE_ROOT / "content-qa").mkdir(parents=True)
        (sync.SOURCE_ROOT / "content-qa" / "SKILL.md").write_text("placeholder")
        sync.SHARED_CLAUDE_ADAPTERS = {"content-qa"}

    def tearDown(self) -> None:
        sync.CLAUDE_AGENTS_ROOT = self.old_agents_root
        sync.SOURCE_ROOT = self.old_source_root
        sync.SHARED_CLAUDE_ADAPTERS = self.old_adapters
        self.temporary.cleanup()

    def test_ordinary_agents_are_ignored_and_inline_skills_are_accepted(self) -> None:
        (sync.CLAUDE_AGENTS_ROOT / "pr-reviewer.md").write_text(
            "---\nname: pr-reviewer\ndescription: Review pull requests.\n---\nReview.\n"
        )
        (sync.CLAUDE_AGENTS_ROOT / "content-qa.md").write_text(
            """---
name: content-qa
description: Validate content.
skills: [content-qa]
---
Follow `agent-skills/skills/content-qa/`.
"""
        )

        sync.validate_claude_adapters()


class ProjectionOwnershipTests(unittest.TestCase):
    def test_unrelated_files_are_preserved(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            unrelated = root / "unrelated.txt"
            unrelated.write_text("keep me")
            expected = {
                Path("demo/SKILL.md"): sync.ProjectedFile(b"generated\n")
            }

            sync.write_projection(root, expected, allow_unowned_overwrite=False)

            self.assertEqual("keep me", unrelated.read_text())
            self.assertTrue((root / sync.MANIFEST_NAME).is_file())

    def test_unowned_collision_is_not_overwritten_at_alternate_root(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            destination = root / "demo" / "SKILL.md"
            destination.parent.mkdir()
            destination.write_text("manual skill\n")
            expected = {
                Path("demo/SKILL.md"): sync.ProjectedFile(b"generated\n")
            }

            with self.assertRaisesRegex(ValueError, "unowned file"):
                sync.write_projection(root, expected, allow_unowned_overwrite=False)

            self.assertEqual("manual skill\n", destination.read_text())

    def test_only_unmodified_manifest_owned_files_are_deleted(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            relative = Path("retired/SKILL.md")
            initial = {relative: sync.ProjectedFile(b"generated\n")}
            sync.write_projection(root, initial, allow_unowned_overwrite=False)

            (root / relative).write_text("manual edit\n")
            with self.assertRaisesRegex(ValueError, "modified generated file"):
                sync.write_projection(root, {}, allow_unowned_overwrite=False)

            self.assertEqual("manual edit\n", (root / relative).read_text())

    def test_unmodified_manifest_owned_file_is_removed_when_stale(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            relative = Path("retired/SKILL.md")
            initial = {relative: sync.ProjectedFile(b"generated\n")}
            sync.write_projection(root, initial, allow_unowned_overwrite=False)

            removed = sync.write_projection(root, {}, allow_unowned_overwrite=False)

            self.assertEqual([relative], removed)
            self.assertFalse((root / relative).exists())

    def test_adoption_overwrites_only_colliding_expected_paths(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            destination = root / "demo" / "SKILL.md"
            destination.parent.mkdir()
            destination.write_text("legacy projection\n")
            unrelated = root / "notes.md"
            unrelated.write_text("preserve\n")
            expected = {
                Path("demo/SKILL.md"): sync.ProjectedFile(b"generated\n")
            }

            sync.write_projection(root, expected, allow_unowned_overwrite=True)

            self.assertEqual("generated\n", destination.read_text())
            self.assertEqual("preserve\n", unrelated.read_text())


if __name__ == "__main__":
    unittest.main()
