# Agent skill architecture

Not Only Fiends treats agent workflows as product code. Codex and Claude Code expose the same
capabilities from one reviewed source rather than maintaining independent prompt copies.

## Layout

```text
agent-skills/skills/<name>/
  SKILL.md                       # portable source: name, description, workflow
  agents/openai.yaml             # optional Codex presentation metadata
  platforms/claude.frontmatter   # optional Claude-only invocation metadata
  references/                    # optional supporting documents
  scripts/                       # optional deterministic helpers
  assets/                        # optional templates or output assets

.agents/skills/                  # generated Codex discovery tree
.claude/skills/                  # generated Claude Code discovery tree
.claude/agents/                  # thin Claude orchestration adapters only
```

Each projection has an `.agent-skills-projection.json` ownership manifest. The generator removes
only a stale path recorded there whose content still matches its recorded hash. Unrelated files
are preserved, and modified generated files require manual review instead of being overwritten or
deleted.

The canonical `SKILL.md` frontmatter contains only `name` and `description`, which are shared by
both platforms. A Claude overlay may add fields such as `argument-hint` or `allowed-tools`.
Codex-specific UI metadata stays in `agents/openai.yaml`. Workflow rules, domain knowledge,
commands, references, and output contracts always stay in the canonical skill.

This follows the current [Codex skill structure](https://developers.openai.com/codex/skills)
and [Claude Code skill structure](https://code.claude.com/docs/en/slash-commands).

## Editing a skill

1. Edit `agent-skills/skills/<name>/`, never either generated projection.
2. Keep the body host-neutral. Do not use platform invocation syntax (`$skill` or `/skill`),
   platform-specific tool names, dated run results, or handoff/session notes in shared workflow
   text.
3. Put durable lessons in the workflow or a focused reference. Put run output under
   `test-reports/`, not in a skill.
4. Add platform metadata only when it improves that host without changing the capability.
5. Regenerate and verify:

   ```bash
   uv run --isolated --with-requirements tools/requirements-agent-skills.txt python tools/sync_agent_skills.py
   uv run --isolated --with-requirements tools/requirements-agent-skills.txt python tools/sync_agent_skills.py --check
   ```

   `uv run` creates an isolated environment for the pinned tooling dependency. It does not modify
   the system Python installation, including Python installations marked as externally managed.

`--check` fails on missing or stale canonical projection files and ownership metadata. Host-local
files not owned by the manifest are ignored. Use `--platform codex` or `--platform claude` to
target one host. `--projection-root <path>` can render one platform to a temporary directory
without deleting unrelated contents.

Repositories migrating an existing projection without an ownership manifest use
`--adopt-existing` once, after reviewing the `--check` output. The flag permits overwriting only
paths that collide with canonical output; unrelated files are still preserved. Subsequent syncs
must run without it so hash ownership protects manual edits.

## Capability parity

Parity means a user can select Codex or Claude Code and invoke the same named skill with the same
domain workflow and safety constraints. It does not require identical orchestration primitives.
Claude's `.claude/agents/` files are therefore thin adapters that preload canonical skills; they
must not contain unique business logic. Multi-agent work on either host should compose the same
skills and produce the same output contract.

Every canonical skill must include `agents/openai.yaml`, even if Claude needs no overlay. This
makes missing Codex presentation metadata a validation error and keeps newly added capabilities
from becoming Claude-only by accident.
