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
