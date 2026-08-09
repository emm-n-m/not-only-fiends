namespace NotOnlyFiendsStudio.Tests;

public sealed class RequiresPrivatePacksFactAttribute : FactAttribute
{
    public RequiresPrivatePacksFactAttribute()
    {
        if (!TestContentHelper.HasOptionalPrivatePacks())
        {
            Skip =
                $"Set EXTRA_PACKS_PATH in {TestContentHelper.EnvFileName} to run this private-content test.";
        }
    }
}

public sealed class RequiresPrivatePacksTheoryAttribute : TheoryAttribute
{
    public RequiresPrivatePacksTheoryAttribute()
    {
        if (!TestContentHelper.HasOptionalPrivatePacks())
        {
            Skip =
                $"Set EXTRA_PACKS_PATH in {TestContentHelper.EnvFileName} to run this private-content test.";
        }
    }
}

public sealed class RequiresPcgenCharactersFactAttribute : FactAttribute
{
    public RequiresPcgenCharactersFactAttribute()
    {
        if (!TestContentHelper.HasOptionalPcgenCharacters())
        {
            Skip =
                $"Set PCGEN_CHARACTERS_PATH in {TestContentHelper.EnvFileName} to run this test.";
        }
    }
}

public sealed class RequiresPcgenCharactersTheoryAttribute : TheoryAttribute
{
    public RequiresPcgenCharactersTheoryAttribute()
    {
        if (!TestContentHelper.HasOptionalPcgenCharacters())
        {
            Skip =
                $"Set PCGEN_CHARACTERS_PATH in {TestContentHelper.EnvFileName} to run this test.";
        }
    }
}

/// <summary>
/// Golden reconstruction tests assert exact values, so they need both halves of the external
/// data: the .pcg sources and the private packs that carry the third-party classes, races and
/// templates those characters use. With only one configured the reconstruction is a different
/// character, not a failing one.
/// </summary>
public sealed class RequiresPcgenGoldenDataFactAttribute : FactAttribute
{
    public RequiresPcgenGoldenDataFactAttribute()
    {
        if (!TestContentHelper.HasOptionalPcgenCharacters())
        {
            Skip =
                $"Set PCGEN_CHARACTERS_PATH in {TestContentHelper.EnvFileName} to run this test.";
        }
        else if (!TestContentHelper.HasOptionalPrivatePacks())
        {
            Skip =
                $"Set EXTRA_PACKS_PATH in {TestContentHelper.EnvFileName} to run this test.";
        }
    }
}

/// <summary>
/// For tests that assert exact values against a single named character. These read the frozen
/// fixtures in the materials repo rather than the live PCGen working directory, so editing a
/// character in PCGen can never break them — see
/// <see cref="TestContentHelper.GetOptionalPcgFixturesPath"/>. Both the fixtures and the private
/// packs are needed: the fixtures use third-party classes, races and templates.
/// </summary>
public sealed class RequiresPcgFixturesFactAttribute : FactAttribute
{
    public RequiresPcgFixturesFactAttribute()
    {
        if (!TestContentHelper.HasOptionalPrivatePacks())
        {
            Skip =
                $"Set EXTRA_PACKS_PATH in {TestContentHelper.EnvFileName} to run this test.";
        }
        else if (!TestContentHelper.HasOptionalPcgFixtures())
        {
            Skip =
                $"No .pcg fixtures at {TestContentHelper.GetOptionalPcgFixturesPath()} — "
                + "copy them from PCGEN_CHARACTERS_PATH and commit them in the materials repo.";
        }
    }
}
