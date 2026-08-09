using NotOnlyFiendsFeed.Components;
using NotOnlyFiendsStudio.Models;
using NotOnlyFiendsStudio.Studio;

namespace NotOnlyFiendsStudio.Tests.Api;

/// <summary>
/// Spell-like abilities are stored as a bare name and a uses-per-day string — not one
/// <c>GrantSLA</c> in the corpus carries a description of its own. The sheet has always had the
/// markup for a tooltip; without borrowing text from the spell the ability is named after, that
/// tooltip renders empty and a player has no way to see what the ability does.
/// </summary>
public class TooltipTests
{
    private static readonly Lazy<ContentRegistry> Content = new(TestContentHelper.LoadBundledPacks);

    [Fact]
    public void AnSlaNamedAfterASpellBorrowsThatSpellsText()
    {
        var text = Tooltips.ForSla(new SLA { Id = "x", Name = "Teleport" }, Content.Value);

        Assert.True(Content.Value.TryGetSpellByName("Teleport", out var teleport));
        Assert.Contains(teleport!.School, text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(teleport.Description[..40], text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Most SLA names carry a qualifier saying how the ability differs from the spell —
    /// "Invisibility (Self Only)", "Gaseous Form (1 hour)". It has to come off to find the spell,
    /// and it is the most important part of the tooltip, so it must survive into the text.
    /// </summary>
    [Fact]
    public void AQualifiedSlaNameResolvesTheSpellAndKeepsTheQualifier()
    {
        var text = Tooltips.ForSla(
            new SLA { Id = "x", Name = "Invisibility (Self Only)" }, Content.Value);

        Assert.Contains("As Invisibility, Self Only.", text, StringComparison.Ordinal);
        Assert.True(Content.Value.TryGetSpellByName("Invisibility", out var invisibility));
        Assert.Contains(invisibility!.Description[..40], text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnSlaWithItsOwnDescriptionKeepsIt()
    {
        var text = Tooltips.ForSla(
            new SLA { Id = "x", Name = "Teleport", Description = "Only to the Nine Hells." },
            Content.Value);

        Assert.Equal("Only to the Nine Hells.", text);
    }

    /// <summary>
    /// Psionics have no content yet, so a psi-like ability resolves to nothing. Say so rather than
    /// rendering an empty tooltip, which reads as "this ability does nothing".
    /// </summary>
    [Fact]
    public void AnUnresolvableSlaSaysSoRatherThanRenderingEmpty()
    {
        var text = Tooltips.ForSla(new SLA { Id = "x", Name = "Energy Ray (Psi-Like)" }, Content.Value);

        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.Contains("Energy Ray (Psi-Like)", text, StringComparison.Ordinal);
        Assert.Contains("no description in content", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ASpellTooltipLeadsWithTheRulesLineThenTheDescription()
    {
        Assert.True(Content.Value.TryGetSpellByName("Fireball", out var fireball));

        var text = Tooltips.ForSpell(fireball!);

        Assert.StartsWith(fireball!.School, text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"Save: {fireball.SavingThrow}", text, StringComparison.Ordinal);
        Assert.Contains(fireball.Description[..40], text, StringComparison.Ordinal);
        Assert.True(
            text.IndexOf("Save:", StringComparison.Ordinal)
                < text.IndexOf(fireball.Description[..40], StringComparison.Ordinal),
            "The rules line must precede the description — it is what a player checks most often.");
    }
}
