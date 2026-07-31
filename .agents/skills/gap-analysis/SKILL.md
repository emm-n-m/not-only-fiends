---
name: gap-analysis
description: Run the PCGen reconstruction/buildability tests and summarize missing races, classes, feats, templates, domains, or mechanical reconstruction failures. Use to prioritize content needed by the configured real-character corpus.
---

# Run PCGen Gap Analysis

Use the configured `PCGEN_CHARACTERS_PATH` corpus as evidence for missing content and
reconstruction behavior.

## Workflow

1. Confirm `.env` defines `PCGEN_CHARACTERS_PATH` and that the directory exists. If not,
   report that the external corpus is unavailable; do not present skipped tests as a clean
   analysis.
2. Run the buildability report:

   ```bash
   dotnet test --filter "FullyQualifiedName~BuildabilityReport" --logger "console;verbosity=detailed"
   ```

3. Run the relevant category tests, or all gap tests:

   ```bash
   dotnet test --filter "FullyQualifiedName~GapAnalysis"
   dotnet test --filter "FullyQualifiedName~GapAnalysis_Race"
   dotnet test --filter "FullyQualifiedName~GapAnalysis_Classes"
   dotnet test --filter "FullyQualifiedName~GapAnalysis_Feats"
   dotnet test --filter "FullyQualifiedName~GapAnalysis_Templates"
   dotnet test --filter "FullyQualifiedName~GapAnalysis_Domains"
   ```

4. Run reconstruction tests for mechanically asserted characters:

   ```bash
   dotnet test --filter "FullyQualifiedName~Reconstruct"
   ```

5. Distinguish expected gap failures/skips from genuine reconstruction failures.
6. Summarize:
   - buildable versus blocked character counts;
   - missing content ranked by the number of affected characters;
   - characters closest to buildable;
   - mechanical failures in otherwise buildable characters.

## Follow-up

After approved content additions, update `NotOnlyFiendsStudio/PcGen/PcgIdMapper.cs` only when
the PCGen name cannot resolve through existing conventions. Re-run this analysis and the
`pcg-baseline` skill.

## References

- `NotOnlyFiendsStudio.Tests/PcGen/PcgReconstructionTests.cs`
- `NotOnlyFiendsStudio/PcGen/PcgParser.cs`
- `NotOnlyFiendsStudio/PcGen/PcgIdMapper.cs`
- `.env` (`PCGEN_CHARACTERS_PATH`)
