# Content Policy

This repository treats content packs as `private by default`.

## Open Game Content Declaration

Per the terms of the Open Game License v1.0a (see [OGL.md](OGL.md)), this repository distributes Open Game Content and must clearly identify which portions are OGC.

**Open Game Content** — the following are designated as Open Game Content:

- All files under `NotOnlyFiendsStudio/Content/packs/srd_core/`
- All files under `NotOnlyFiendsStudio/Content/packs/srd_epic/`
- All files under `NotOnlyFiendsStudio/Content/packs/srd_monsters/`
- All files under `NotOnlyFiendsStudio/Content/packs/srd_unearthed_arcana/`
This includes race, class, feat, skill, spell, domain, template, and monster definitions; mechanical descriptions, prerequisites, and rules text; formula expressions; and game-mechanic identifiers contained in those packs.

**Not Open Game Content** — the following are explicitly excluded from OGC designation:

- All source code in this repository (licensed under MIT — see [LICENSE](LICENSE))
- The project name "Not Only Fiends" and any associated branding, logos, or identifiers used to distinguish this project as Product Identity
- Documentation files (`README.md`, `ARCHITECTURE.md`, `AGENTS.md`, `CONTENT_POLICY.md`, etc.)
- Content in any pack not listed above, including private/homebrew/third-party packs

Anyone redistributing the SRD packs or derivative works thereof must do so under the OGL v1.0a, include the full license text, and preserve the Section 15 copyright chain in `OGL.md`.

## Public Packs

Public packs are the only packs bundled into the app (via `content-public.json`).

The checked-in allowlist lives in [`content-public.json`](content-public.json).

Current public pack set:

- `srd_core`
- `srd_epic`
- `srd_monsters`
- `srd_unearthed_arcana`

## Private Packs

Any pack not listed in `content-public.json` is treated as private or unreviewed and is only loaded when configured via `EXTRA_PACKS_PATH` in `.env`.

Private packs are not stored in this repository — they live in a separate private repository and are only loaded from the path configured in `EXTRA_PACKS_PATH`. Examples include:

- `12_to_midnight`
- `deceit_homebrew`
- `fiendish_codex_1`
- `mongoose_publishing`

This list is illustrative, not authoritative. The allowlist is the source of truth.

## Publishing Workflow

`ServerContentService` loads packs directly from the filesystem. Bundled packs are filtered through `content-public.json`. Private packs load from `EXTRA_PACKS_PATH` configured in `.env`.

To mark a pack public:

1. Review that the pack is safe to publish.
2. Add its pack ID to `content-public.json`.
3. Verify the app loads only the intended public set by default.

## Test Workflow

`NotOnlyFiendsStudio.Tests` now loads bundled packs from `content-public.json` by default.

Tests that require private or campaign-specific content are skipped unless you configure:

- `EXTRA_PACKS_PATH` in `.env`

## Local Development Workflow

For local/private development, create `.env` in the repo root (gitignored):

```env
CHARACTERS_PATH=C:\path\to\cloud-synced\characters
EXTRA_PACKS_PATH=C:\path\to\private-packs-repo
# PCGEN_CHARACTERS_PATH=C:\path\to\pcgen_characters
```

Then run:

```bash
dotnet run --project NotOnlyFiendsFeed
```

The same `.env` file is also used by `docker compose` for volume bind mounts. One config file for both workflows.
