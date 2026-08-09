using System.Text.RegularExpressions;
using NotOnlyFiendsStudio.Models;
using NotOnlyFiendsStudio.Studio;

namespace NotOnlyFiendsFeed.Components;

/// <summary>
/// Hover text for the things a sheet lists by name alone. `bootstrap.bundle.js` is not loaded, so
/// these are plain `title` attributes rather than Bootstrap tooltips.
/// </summary>
public static class Tooltips
{
    /// <summary>
    /// Trailing qualifier on a spell-like ability's name — "Invisibility (Self Only)",
    /// "Gaseous Form (1 hour)". It is how the ability differs from the spell, so it is worth
    /// keeping in the tooltip even though it has to come off to find the spell.
    /// </summary>
    private static readonly Regex TrailingQualifier = new(@"\s*\(([^)]*)\)\s*$", RegexOptions.Compiled);

    /// <summary>
    /// Rules line plus description for a spell. The rules line alone is what a player checks most
    /// often (range, duration, save), so it leads.
    /// </summary>
    public static string ForSpell(SpellDefinition spell)
    {
        var school = string.IsNullOrEmpty(spell.Subschool)
            ? spell.School
            : $"{spell.School} ({spell.Subschool})";
        var descriptors = spell.Descriptors.Any() ? $" [{string.Join(", ", spell.Descriptors)}]" : string.Empty;
        var rules = $"{school}{descriptors}. {spell.CastingTime}; {spell.Range}; {spell.Duration}. "
            + $"Save: {spell.SavingThrow}. SR: {spell.SpellResistance}.";
        return string.IsNullOrWhiteSpace(spell.Description) ? rules : $"{rules} {spell.Description}";
    }

    /// <summary>
    /// Hover text for a spell-like ability. No <c>GrantSLA</c> in the corpus carries a description
    /// of its own — all 180 of them name a spell instead — so the text is borrowed from that spell,
    /// matching on name and then on the name with its qualifier removed. Roughly 93% resolve; the
    /// rest (psionics, a handful of absent spells) fall back to the name and whatever the content
    /// did supply, which is still better than an empty tooltip.
    /// </summary>
    public static string ForSla(SLA sla, ContentRegistry content)
    {
        if (!string.IsNullOrWhiteSpace(sla.Description))
            return sla.Description!;

        var parts = new List<string>();
        if (TryResolveSpell(sla.Name, content, out var spell, out var qualifier))
        {
            if (!string.IsNullOrEmpty(qualifier))
                parts.Add($"As {spell!.Name}, {qualifier}.");
            parts.Add(ForSpell(spell!));
        }

        if (parts.Count == 0)
            return $"{sla.Name} — no description in content.";

        return string.Join(" ", parts);
    }

    /// <summary>
    /// Finds the spell an ability is named after. Tries the whole name first: some spells really do
    /// have parentheses in their name, and stripping unconditionally would lose them.
    /// </summary>
    private static bool TryResolveSpell(
        string name,
        ContentRegistry content,
        out SpellDefinition? spell,
        out string qualifier)
    {
        qualifier = string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            spell = null;
            return false;
        }

        if (content.TryGetSpellByName(name.Trim(), out spell) && spell != null)
            return true;

        var match = TrailingQualifier.Match(name);
        if (!match.Success)
            return false;

        qualifier = match.Groups[1].Value.Trim();
        var bare = TrailingQualifier.Replace(name, string.Empty).Trim();
        return content.TryGetSpellByName(bare, out spell) && spell != null;
    }
}
