using System.Text.Json.Serialization;

namespace NotOnlyFiendsStudio.Models;

[JsonDerivedType(typeof(AddHitDie), "AddHitDie")]
[JsonDerivedType(typeof(AddBAB), "AddBAB")]
[JsonDerivedType(typeof(AddSaves), "AddSaves")]
[JsonDerivedType(typeof(GrantSkillPoints), "GrantSkillPoints")]
[JsonDerivedType(typeof(AddClassSkills), "AddClassSkills")]
[JsonDerivedType(typeof(GrantAbility), "GrantAbility")]
[JsonDerivedType(typeof(RevokeAbility), "RevokeAbility")]
[JsonDerivedType(typeof(GrantSLA), "GrantSLA")]
[JsonDerivedType(typeof(GrantSpecialAttack), "GrantSpecialAttack")]
[JsonDerivedType(typeof(GrantCasterLevelModifier), "GrantCasterLevelModifier")]
[JsonDerivedType(typeof(GrantItemActivationLevelRule), "GrantItemActivationLevelRule")]
[JsonDerivedType(typeof(GrantWarDomainWeaponFeats), "GrantWarDomainWeaponFeats")]
[JsonDerivedType(typeof(GrantDomainSpellLikeAbilities), "GrantDomainSpellLikeAbilities")]
[JsonDerivedType(typeof(RevokeSLA), "RevokeSLA")]
[JsonDerivedType(typeof(GrantBonusFeat), "GrantBonusFeat")]
[JsonDerivedType(typeof(ModifyAttribute), "ModifyAttribute")]
[JsonDerivedType(typeof(SetAttribute), "SetAttribute")]
[JsonDerivedType(typeof(GrantFeatSlot), "GrantFeatSlot")]
[JsonDerivedType(typeof(AdvanceSpellcasting), "AdvanceSpellcasting")]
[JsonDerivedType(typeof(AddSpellListSource), "AddSpellListSource")]
[JsonDerivedType(typeof(UpdateSpellcasting), "UpdateSpellcasting")]
[JsonDerivedType(typeof(GrantRacialSpellcasting), "GrantRacialSpellcasting")]
[JsonDerivedType(typeof(GrantDomainSelection), "GrantDomainSelection")]
[JsonDerivedType(typeof(GrantEffectiveLevels), "GrantEffectiveLevels")]
[JsonDerivedType(typeof(ModifyCounter), "ModifyCounter")]
[JsonDerivedType(typeof(GrantImmunity), "GrantImmunity")]
[JsonDerivedType(typeof(GrantAbilityModifierToSaves), "GrantAbilityModifierToSaves")]
[JsonDerivedType(typeof(GrantDR), "GrantDR")]
[JsonDerivedType(typeof(GrantSkillBonus), "GrantSkillBonus")]
[JsonDerivedType(typeof(GrantClassFeatureSelection), "GrantClassFeatureSelection")]
[JsonDerivedType(typeof(ApplyClassFeatureOptionBenefits), "ApplyClassFeatureOptionBenefits")]
[JsonDerivedType(typeof(GrantCapability), "GrantCapability")]
[JsonDerivedType(typeof(GrantCompanionSlot), "GrantCompanionSlot")]
[JsonDerivedType(typeof(ModifyLeadershipScore), "ModifyLeadershipScore")]
[JsonDerivedType(typeof(GrantTypedBonus), "GrantTypedBonus")]
[JsonDerivedType(typeof(GrantEquipmentSkillBonus), "GrantEquipmentSkillBonus")]
[JsonDerivedType(typeof(GrantArmorProfile), "GrantArmorProfile")]
[JsonDerivedType(typeof(GrantWeaponLine), "GrantWeaponLine")]
[JsonDerivedType(typeof(GrantLanguage), "GrantLanguage")]
[JsonDerivedType(typeof(GrantLanguageSlot), "GrantLanguageSlot")]
[JsonDerivedType(typeof(GrantMovement), "GrantMovement")]
public abstract class Permabuff
{
    public abstract void Apply(PermabuffContext ctx);

    // Backward-compatible convenience: apply with default rules and no content
    public void Apply(CharacterState state) => Apply(new PermabuffContext(state, GameRules.Standard35e()));
}

// --- Computed Permabuffs ---

public class AddHitDie : Permabuff
{
    public int DieSize { get; set; }

    public AddHitDie() { }
    public AddHitDie(int dieSize) => DieSize = dieSize;

    public override void Apply(PermabuffContext ctx)
    {
        var state = ctx.State;
        var dieSize = DieSize;
        if (ctx.CurrentDriverKind == DriverKind.RacialHD)
            dieSize = Math.Min(ctx.CurrentRacialHitDieMaximum ?? int.MaxValue,
                dieSize + state.RacialHitDieSizeAdjustment);
        var importedRoll = ctx.CurrentTickChoices?.HitPointsRolled;
        if (importedRoll.HasValue && (importedRoll.Value < 1 || importedRoll.Value > dieSize))
        {
            state.Warnings.Add(new Warning
            {
                TickIndex = state.TotalHD,
                Message = $"saved hit-point roll {importedRoll.Value} is outside d{dieSize}; preserved as source input",
            });
        }
        state.HitDice.Add(new HitDieEntry
        {
            DriverId = ctx.CurrentDriverId ?? string.Empty,
            DieSize = dieSize,
            IsRacial = ctx.CurrentDriverKind == DriverKind.RacialHD,
            SavedRoll = importedRoll,
        });
        var conMod = AbilityScoreSet.Modifier(state.AbilityScores.CON);
        var roll = importedRoll
            ?? ((ctx.Rules.FirstHDMaxHP && state.TotalHD == 1) ? dieSize : (dieSize / 2 + 1));
        state.HP += Math.Max(1, roll + conMod);
    }
}

public class AddBAB : Permabuff
{
    public BABProgression Progression { get; set; }
    public int ClassLevel { get; set; }

    public AddBAB() { }
    public AddBAB(BABProgression progression, int classLevel)
    {
        Progression = progression;
        ClassLevel = classLevel;
    }

    public override void Apply(PermabuffContext ctx)
    {
        int totalAtLevel = ctx.Rules.CalculateBABTotal(Progression, ClassLevel);
        int totalAtPrev = ctx.Rules.CalculateBABTotal(Progression, ClassLevel - 1);
        ctx.State.BaseBAB += totalAtLevel - totalAtPrev;
    }

    // Keep static method for external use (e.g., tests)
    public static int CalculateTotal(BABProgression prog, int level) =>
        GameRules.Standard35e().CalculateBABTotal(prog, level);
}

public class AddSaves : Permabuff
{
    public SaveProgression Progression { get; set; } = new();
    public int ClassLevel { get; set; }

    public AddSaves() { }
    public AddSaves(SaveProgression progression, int classLevel)
    {
        Progression = progression;
        ClassLevel = classLevel;
    }

    public override void Apply(PermabuffContext ctx)
    {
        var state = ctx.State;
        var rules = ctx.Rules;
        var fort = CalculateIncrement(rules, Progression.Fort, ClassLevel);
        var reflex = CalculateIncrement(rules, Progression.Ref, ClassLevel);
        var will = CalculateIncrement(rules, Progression.Will, ClassLevel);

        state.BaseSaves.Fort += fort;
        state.BaseSaves.Ref += reflex;
        state.BaseSaves.Will += will;
        state.ProgressionBaseSaves.Fort += fort;
        state.ProgressionBaseSaves.Ref += reflex;
        state.ProgressionBaseSaves.Will += will;
    }

    public static int CalculateTotal(ProgressionRate rate, int level) =>
        GameRules.Standard35e().CalculateSaveTotal(rate, level);

    public static int CalculateIncrement(ProgressionRate rate, int classLevel) =>
        CalculateTotal(rate, classLevel) - CalculateTotal(rate, classLevel - 1);

    private static int CalculateIncrement(GameRules rules, ProgressionRate rate, int classLevel) =>
        rules.CalculateSaveTotal(rate, classLevel) - rules.CalculateSaveTotal(rate, classLevel - 1);
}

public class GrantSkillPoints : Permabuff
{
    public int BasePoints { get; set; }

    public GrantSkillPoints() { }
    public GrantSkillPoints(int basePoints) => BasePoints = basePoints;

    public override void Apply(PermabuffContext ctx)
    {
        var state = ctx.State;
        var intMod = AbilityScoreSet.Modifier(state.AbilityScores.INT);
        var points = Math.Max(1, BasePoints + intMod);
        if (state.TotalHD == 1)
            points *= ctx.Rules.FirstHDSkillMultiplier;
        state.UnspentSkillPoints += points;
    }
}

public class AddClassSkills : Permabuff
{
    public List<string> Skills { get; set; } = new();

    public AddClassSkills() { }
    public AddClassSkills(List<string> skills) => Skills = skills;

    public override void Apply(PermabuffContext ctx)
    {
        foreach (var skill in Skills)
            ctx.State.ClassSkills.Add(skill);
    }
}

// --- Grant/Revoke Permabuffs ---

public class GrantAbility : Permabuff
{
    public GrantedAbility Ability { get; set; } = new();

    public override void Apply(PermabuffContext ctx)
    {
        ctx.State.Abilities.Add(Ability);
    }
}

public class RevokeAbility : Permabuff
{
    public string AbilityId { get; set; } = string.Empty;

    public override void Apply(PermabuffContext ctx)
    {
        ctx.State.Abilities.RemoveAll(a => a.Id == AbilityId);
    }
}

public class ModifyCounter : Permabuff
{
    public string CounterId { get; set; } = string.Empty;
    public int Value { get; set; } = 1;

    public override void Apply(PermabuffContext ctx)
    {
        ctx.State.Counters.TryAdd(CounterId, 0);
        ctx.State.Counters[CounterId] += Value;
    }
}

public class GrantLanguage : Permabuff
{
    public string LanguageId { get; set; } = string.Empty;

    public override void Apply(PermabuffContext ctx) => ctx.State.Languages.Add(LanguageId);
}

/// <summary>
/// Grants the right to know a language without saying which — "a raven familiar can speak one
/// language of its master's choice". Distinct from <see cref="GrantLanguage"/>, which names the
/// language, and from the starting-Intelligence budget, which a creature with Int 2 cannot draw
/// on at all. The pick itself lives in <c>Character.GrantedLanguageIds</c>.
/// </summary>
public class GrantLanguageSlot : Permabuff
{
    public int Count { get; set; } = 1;

    /// <summary>Why the slot exists, for the builder to label the picker with.</summary>
    public string? Source { get; set; }

    public override void Apply(PermabuffContext ctx)
    {
        ctx.State.GrantedLanguageSlots += Count;
        if (!string.IsNullOrWhiteSpace(Source))
            ctx.State.GrantedLanguageSources.Add(Source!);
    }
}

public class GrantMovement : Permabuff
{
    public MovementMode Mode { get; set; }
    public int Speed { get; set; }
    public FlightManeuverability? FlyManeuverability { get; set; }

    public override void Apply(PermabuffContext ctx)
    {
        var state = ctx.State;
        var effectiveSpeed = Math.Max(state.BaseSpeeds.GetValueOrDefault(Mode), Speed);
        state.BaseSpeeds[Mode] = effectiveSpeed;
        state.Speeds[Mode] = effectiveSpeed;

        if (Mode == MovementMode.Fly && FlyManeuverability.HasValue
            && (!state.FlyManeuverability.HasValue
                || FlyManeuverability.Value > state.FlyManeuverability.Value))
        {
            state.FlyManeuverability = FlyManeuverability.Value;
        }
    }
}

public class GrantSLA : Permabuff
{
    public SLA SLA { get; set; } = new();
    public bool CasterLevelEqualsTotalHD { get; set; }

    public override void Apply(PermabuffContext ctx)
    {
        var sla = new SLA
        {
            Id = SLA.Id,
            Name = SLA.Name,
            Description = SLA.Description,
            UsesPerDay = SLA.UsesPerDay,
            CasterLevel = CasterLevelEqualsTotalHD ? ctx.State.TotalHD : SLA.CasterLevel,
            SaveDC = SLA.SaveDC,
            CasterLevelTracksTotalHD = CasterLevelEqualsTotalHD
        };
        ctx.State.SLAs.Add(sla);
    }
}

public class GrantSpecialAttack : Permabuff
{
    public SpecialAttack Attack { get; set; } = new();
    public override void Apply(PermabuffContext ctx) => ctx.State.SpecialAttacks.Add(Attack);
}

public class GrantCasterLevelModifier : Permabuff
{
    public CasterLevelModifier Modifier { get; set; } = new();
    public override void Apply(PermabuffContext ctx) => ctx.State.CasterLevelModifiers.Add(Modifier);
}

public class GrantItemActivationLevelRule : Permabuff
{
    public ItemActivationLevelRule Rule { get; set; } = new();
    public override void Apply(PermabuffContext ctx) => ctx.State.ItemActivationLevelRules.Add(Rule);
}

/// <summary>
/// Grants the War domain's permanent weapon feats. Until deity definitions carry favored-weapon
/// data, the player supplies a weapon content ID in CurrentTickChoices["war_favored_weapon"].
/// </summary>
public class GrantWarDomainWeaponFeats : Permabuff
{
    public const string ChoiceKey = "war_favored_weapon";

    public override void Apply(PermabuffContext ctx)
    {
        var picks = ctx.CurrentTickChoices?.ClassFeatureChoices?.GetValueOrDefault(ChoiceKey);
        var weaponId = picks?.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(weaponId) || ctx.Content?.TryGetEquipment(weaponId, out var weapon) != true || weapon?.Category != EquipmentCategory.Weapon)
        {
            ctx.State.Warnings.Add(new Warning { TickIndex = ctx.State.TotalHD, Message = "War domain requires a valid favored weapon selection" });
            return;
        }

        // A class-wide martial proficiency already covers every martial weapon.
        if (!ctx.State.Feats.Contains("feat:weapon_proficiency_martial"))
            new GrantBonusFeat { FeatId = $"feat:martial_weapon_proficiency_{weaponId}" }.Apply(ctx);
        new GrantBonusFeat { FeatId = $"feat:weapon_focus_{weaponId}" }.Apply(ctx);
    }
}

/// <summary>
/// Turns the character's chosen domains into spell-like abilities, at a usage tier set by each
/// bonus spell's level. Content that wants "gains SLAs based on its domains" (the ascended
/// archfiend) previously stated that only as prose, so nothing ever reached the sheet.
///
/// Deferred rather than applied here: domains are picked during the tick loop, and this is
/// authored on a template's creation permabuffs, which run before any tick. It records the request
/// and <c>ReplayStudio</c>'s tail pass fulfils it once the domain list is final.
/// </summary>
public class GrantDomainSpellLikeAbilities : Permabuff
{
    /// <summary>Domain spells up to this level are usable at will.</summary>
    public int AtWillMaxSpellLevel { get; set; } = 3;
    /// <summary>Up to this level, three times per day.</summary>
    public int ThreePerDayMaxSpellLevel { get; set; } = 6;
    /// <summary>Up to this level, once per day. Above it, nothing is granted.</summary>
    public int OncePerDayMaxSpellLevel { get; set; } = 9;
    /// <summary>Ability that sets the save DC (10 + spell level + modifier).</summary>
    public Ability SaveAbility { get; set; } = Ability.CHA;

    public string? UsesFor(int spellLevel) =>
        spellLevel <= AtWillMaxSpellLevel ? "at will"
        : spellLevel <= ThreePerDayMaxSpellLevel ? "3/day"
        : spellLevel <= OncePerDayMaxSpellLevel ? "1/day"
        : null;

    public override void Apply(PermabuffContext ctx) =>
        ctx.State.PendingDomainSLAGrants.Add(this);
}

public class RevokeSLA : Permabuff
{
    public string SLAId { get; set; } = string.Empty;

    public override void Apply(PermabuffContext ctx)
    {
        ctx.State.SLAs.RemoveAll(s => s.Id == SLAId);
    }
}

public class GrantBonusFeat : Permabuff
{
    public string FeatId { get; set; } = string.Empty;

    public override void Apply(PermabuffContext ctx)
    {
        ctx.State.Feats.Add(FeatId);
        // Cascade: look up FeatDefinition and apply its GrantedPermabuffs,
        // and mirror the user-pick counting so HasFeatOfType / HasFeatWithTag see granted feats.
        if (ctx.Content != null && ctx.Content.TryGetFeat(FeatId, out var featDef) && featDef != null)
        {
            ctx.State.FeatTypeCounts[featDef.Type] =
                ctx.State.FeatTypeCounts.GetValueOrDefault(featDef.Type) + 1;

            foreach (var tag in featDef.Tags)
                ctx.State.FeatTagCounts[tag] = ctx.State.FeatTagCounts.GetValueOrDefault(tag) + 1;

            var prevFeatId = ctx.CurrentFeatId;
            ctx.CurrentFeatId = FeatId;
            foreach (var buff in featDef.GrantedPermabuffs)
                buff.Apply(ctx);
            ctx.CurrentFeatId = prevFeatId;
        }
    }
}

public class ModifyAttribute : Permabuff
{
    public AttributeTarget Target { get; set; }
    public int Value { get; set; }
    public string? ResistanceElement { get; set; }
    public Ability? AbilityScore { get; set; }

    public override void Apply(PermabuffContext ctx)
    {
        var state = ctx.State;
        switch (Target)
        {
            case AttributeTarget.NaturalArmor:
                state.NaturalArmor += Value;
                break;
            case AttributeTarget.SpellResistance:
                state.SpellResistance = (state.SpellResistance ?? 0) + Value;
                break;
            case AttributeTarget.LevelAdjustment:
                state.LevelAdjustment += Value;
                break;
            case AttributeTarget.Resistance:
                if (ResistanceElement != null)
                {
                    state.Resistances.TryAdd(ResistanceElement, 0);
                    state.Resistances[ResistanceElement] += Value;
                }
                break;
            case AttributeTarget.AbilityScore:
                if (AbilityScore.HasValue)
                {
                    var current = state.AbilityScores.GetScore(AbilityScore.Value);
                    state.AbilityScores.SetScore(AbilityScore.Value, current + Value);
                }
                break;
            case AttributeTarget.AllSaves:
                state.BaseSaves.Fort += Value;
                state.BaseSaves.Ref += Value;
                state.BaseSaves.Will += Value;
                break;
            case AttributeTarget.HitPoints:
                state.HP += Value;
                break;
        }
    }
}

public class SetAttribute : Permabuff
{
    public AttributeTarget Target { get; set; }
    public int Value { get; set; }
    public string? ResistanceElement { get; set; }
    public Ability? AbilityScore { get; set; }

    public SetAttribute() { }
    public SetAttribute(AttributeTarget target, int value, string? resistanceElement = null, Ability? abilityScore = null)
    {
        Target = target;
        Value = value;
        ResistanceElement = resistanceElement;
        AbilityScore = abilityScore;
    }

    public override void Apply(PermabuffContext ctx)
    {
        var state = ctx.State;
        switch (Target)
        {
            case AttributeTarget.NaturalArmor:
                state.NaturalArmor = Value;
                break;
            case AttributeTarget.SpellResistance:
                state.SpellResistance = Value;
                break;
            case AttributeTarget.Resistance:
                if (ResistanceElement != null)
                    state.Resistances[ResistanceElement] = Value;
                break;
            case AttributeTarget.AbilityScore:
                if (AbilityScore.HasValue)
                    state.AbilityScores.SetScore(AbilityScore.Value, Value);
                break;
        }
    }
}

// --- Slot Permabuffs ---

public class GrantFeatSlot : Permabuff
{
    public string? Restriction { get; set; }

    public override void Apply(PermabuffContext ctx)
    {
        ctx.State.FeatSlots.Add(new FeatSlot { Restriction = Restriction });
    }
}

/// <summary>
/// Grants a caster access to an additional spell list beyond the one its HD driver declares —
/// e.g. an Archfiend's "choose your list" template borrowing the sorcerer, cleric, or druid list.
/// Records a <see cref="SpellListSourceRule"/> onto the character state; the spell-selection
/// validator then treats spells on <see cref="ListId"/> as legal for the matched caster.
///
/// Recording onto the state (rather than mutating a <see cref="SpellcastingState"/> directly) keeps
/// this order-independent: a creation-time template permabuff can register the list before the
/// caster's class levels — and its spellcasting state — even exist.
/// </summary>
public class AddSpellListSource : Permabuff
{
    /// <summary>Target caster by class id (e.g. <c>class:archfiend</c>). Null matches any caster.</summary>
    public string? ClassId { get; set; }

    /// <summary>Target caster by casting type. Null matches any. Combined with <see cref="ClassId"/> (both must match).</summary>
    public CastingType? CastingType { get; set; }

    /// <summary>The spell list to borrow, e.g. <c>class:sorcerer</c>.</summary>
    public string ListId { get; set; } = string.Empty;

    public override void Apply(PermabuffContext ctx)
    {
        if (string.IsNullOrEmpty(ListId))
            return;

        ctx.State.ExtraSpellListSources.Add(new SpellListSourceRule
        {
            ClassId = ClassId,
            CastingType = CastingType,
            ListId = ListId
        });
    }
}

public class AdvanceSpellcasting : Permabuff
{
    /// <summary>
    /// Restricts advancement to one casting type. Null means any existing spellcasting class,
    /// which is what the SRD specifies for Loremaster and Thaumaturgist ("+1 level of existing
    /// class"); with several candidates the caller picks via
    /// <c>ClassFeatureChoices["advance_spellcasting"]</c>, exactly as for a restricted type.
    /// </summary>
    public CastingType? CastingType { get; set; }

    /// <summary>
    /// When true, only <see cref="SpellcastingState.CasterLevel"/> advances — spells per day/known
    /// are left untouched. Used by prestige classes that stack for caster level but do not advance
    /// spell progression (e.g. Hierophant: "even though they do not advance spell progression...
    /// still stack with the character's base spellcasting levels to determine caster level").
    /// </summary>
    public bool CasterLevelOnly { get; set; }

    private string TypeLabel => CastingType?.ToString() ?? "any";

    public override void Apply(PermabuffContext ctx)
    {
        var state = ctx.State;
        var matches = state.Spellcasting.Values
            .Where(s => !CastingType.HasValue || s.CastingType == CastingType.Value)
            .ToList();

        if (matches.Count == 1)
        {
            var sc = matches[0];
            sc.CasterLevel++;
            if (!CasterLevelOnly) UpdateSpellcastingFromProgression(ctx, sc);
        }
        else if (matches.Count == 0)
        {
            state.Warnings.Add(new Warning { TickIndex = state.TotalHD, Message = $"AdvanceSpellcasting({TypeLabel}): no matching spellcasting class found" });
        }
        else
        {
            // Check for user selection
            var choices = ctx.CurrentTickChoices?.ClassFeatureChoices
                ?.GetValueOrDefault("advance_spellcasting") ?? new List<string>();
            var selected = matches.FirstOrDefault(match => choices.Contains(match.ClassId, StringComparer.Ordinal));

            if (selected != null)
            {
                selected.CasterLevel++;
                if (!CasterLevelOnly) UpdateSpellcastingFromProgression(ctx, selected);
            }
            else
            {
                var classNames = string.Join(", ", matches.Select(m => m.ClassId));
                state.Warnings.Add(new Warning { TickIndex = state.TotalHD, Message = $"AdvanceSpellcasting({TypeLabel}): multiple matching classes ({classNames}), selection required via ClassFeatureChoices[\"advance_spellcasting\"]" });
            }
        }
    }

    private void UpdateSpellcastingFromProgression(PermabuffContext ctx, SpellcastingState sc)
    {
        if (sc.ProgressionData != null)
        {
            sc.ApplyProgression(sc.CasterLevel);
            RecomputeDomainBonusSlots(ctx, sc);
        }
    }

    internal static void RecomputeDomainBonusSlots(PermabuffContext ctx, SpellcastingState sc)
    {
        sc.DomainBonusSlots.Clear();
        // A spell-list-source caster (Archfiend / Red Dragon) draws its domains into its known
        // spells and never gets cleric-style prepared domain slots — even though the domains are
        // still recorded as owned for display.
        if (ctx.State.SpellListSourceDomainOwners.Contains(sc.ClassId))
            return;
        // Only count domains owned by THIS class — multiclass casters don't share domain slots.
        var ownedCount = ctx.State.DomainOwners.Count(kv => kv.Value == sc.ClassId);
        if (ownedCount == 0) return;
        // SRD: "a cleric can prepare one additional spell per spell level each day, which must be
        // a domain spell." Holding two domains widens which spell may fill that one slot; it does
        // not add a second slot. The count is therefore 1 however many domains the class owns.
        foreach (var lvl in sc.SpellsPerDay.Keys.Where(l => l >= 1))
            sc.DomainBonusSlots[lvl] = 1;
    }
}

public class UpdateSpellcasting : Permabuff
{
    public string ClassId { get; set; } = string.Empty;
    public CastingType CastingType { get; set; }
    public Ability CastingStat { get; set; }
    public int CasterLevel { get; set; }
    public Dictionary<int, int>? SpellsPerDay { get; set; }
    public Dictionary<int, int>? SpellsKnown { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public SpellcastingProgression? ProgressionRef { get; set; }

    public override void Apply(PermabuffContext ctx)
    {
        var state = ctx.State;
        if (!state.Spellcasting.ContainsKey(ClassId))
        {
            state.Spellcasting[ClassId] = new SpellcastingState
            {
                ClassId = ClassId,
                CastingType = CastingType,
                CastingStat = CastingStat
            };
        }

        var sc = state.Spellcasting[ClassId];
        sc.CasterLevel = CasterLevel;

        if (SpellsPerDay != null)
        {
            sc.SpellsPerDay = new Dictionary<int, int>(SpellsPerDay);
            sc.MaxSpellLevel = SpellsPerDay.Keys.Max();
        }

        if (SpellsKnown != null)
        {
            sc.SpellsKnown = new Dictionary<int, int>(SpellsKnown);
        }

        if (ProgressionRef != null)
            sc.ProgressionData = ProgressionRef;

        AdvanceSpellcasting.RecomputeDomainBonusSlots(ctx, sc);
    }
}

public class GrantDomainSelection : Permabuff
{
    // Sentinel owner used when a race/template grants domains with no associated
    // spellcasting class. Orphan domains fire granted powers only — no bonus slots,
    // no spell picks (since there's no caster to attach them to).
    public const string OrphanOwner = "";

    public int Count { get; set; } = 2;
    // Optional explicit granting class. If null, uses the current tick's driver;
    // if that's also null (race/template-level fire), domains are orphaned.
    public string? ClassId { get; set; }

    /// <summary>
    /// When true, the selected domains add their spell list to this caster's known-spell pool
    /// rather than granting cleric-style prepared domain slots and granted powers. Used by the
    /// Archfiend (an arcane spontaneous caster who draws from two domains, like the Red Dragon):
    /// the player still picks the domains, but they become spell-list sources, not bonus slots.
    /// </summary>
    public bool AsSpellListSources { get; set; }

    public override void Apply(PermabuffContext ctx)
    {
        var owner = ClassId ?? ctx.CurrentDriverId ?? OrphanOwner;
        ctx.State.PendingDomainSelections[owner] =
            ctx.State.PendingDomainSelections.GetValueOrDefault(owner) + Count;
        if (AsSpellListSources)
            ctx.State.SpellListSourceDomainOwners.Add(owner);
    }
}

public class GrantCapability : Permabuff
{
    public string Capability { get; set; } = string.Empty;

    public override void Apply(PermabuffContext ctx)
    {
        if (!string.IsNullOrEmpty(Capability))
            ctx.State.Capabilities.Add(Capability);
    }
}

public class GrantClassFeatureSelection : Permabuff
{
    public string FeatureType { get; set; } = string.Empty;
    public int Count { get; set; } = 1;

    public override void Apply(PermabuffContext ctx)
    {
        ctx.State.PendingClassFeatureSelections.TryAdd(FeatureType, 0);
        ctx.State.PendingClassFeatureSelections[FeatureType] += Count;
    }
}

/// <summary>
/// Applies a named set of benefits from the option previously selected for a class feature.
/// This lets a single, persistent choice (such as a ranger's combat style) grow at later levels.
/// </summary>
public class ApplyClassFeatureOptionBenefits : Permabuff
{
    public string FeatureType { get; set; } = string.Empty;
    public string BenefitSet { get; set; } = string.Empty;

    public override void Apply(PermabuffContext ctx)
    {
        if (ctx.Content == null
            || !ctx.Content.TryGetClassFeature(FeatureType, out var feature)
            || feature == null
            || !ctx.State.ClassFeatureSelections.TryGetValue(FeatureType, out var picks))
            return;

        foreach (var optionId in picks)
        {
            var option = feature.Options.FirstOrDefault(o => o.Id == optionId);
            if (option?.AdditionalPermabuffs.TryGetValue(BenefitSet, out var buffs) != true || buffs == null)
                continue;

            foreach (var buff in buffs)
                buff.Apply(ctx);
        }
    }
}

public class GrantEffectiveLevels : Permabuff
{
    public string TargetDriverId { get; set; } = string.Empty;
    public Formula BonusFormula { get; set; } = new();

    public override void Apply(PermabuffContext ctx)
    {
        ctx.State.EffectiveLevelRules.Add(new EffectiveLevelRule
        {
            TargetDriverId = TargetDriverId,
            BonusFormula = BonusFormula
        });
    }
}

/// <summary>
/// Races like Aranea (Sorcerer 3), Nymph (Druid 7), Ghaele (Cleric 14), Lillend (Bard 6)
/// cast spells as a given class at a given level — a built-in class level count granted
/// by the race itself, independent of class levels the character takes.
///
/// At race-creation time this only registers an EffectiveLevelRule so that any class
/// levels the character later takes stack on top (e.g. Aranea + 1 Sorcerer = CL 4).
/// The actual seeding of state.Spellcasting when NO class levels are taken happens in
/// the engine's FinalizeRacialSpellcasting phase, after all ticks are processed so that
/// formulas referencing RacialHD() see the final HD count.
/// </summary>
public class GrantRacialSpellcasting : Permabuff
{
    public string ClassId { get; set; } = string.Empty;
    public Formula LevelFormula { get; set; } = new();

    public override void Apply(PermabuffContext ctx)
    {
        ctx.State.EffectiveLevelRules.Add(new EffectiveLevelRule
        {
            TargetDriverId = ClassId,
            BonusFormula = LevelFormula,
            // Casting as an Nth-level druid is not being an Nth-level druid.
            Scope = EffectiveLevelScope.SpellcastingOnly
        });
    }
}

/// <summary>
/// "Apply your <see cref="Ability"/> modifier as a bonus on all saving throws" — paladin Divine
/// Grace, blackguard Dark Blessing. Registers a rule rather than adding a number, because the
/// modifier must follow the final ability score; see <see cref="AbilitySaveBonus"/>.
/// </summary>
public class GrantAbilityModifierToSaves : Permabuff
{
    public string SourceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Ability Ability { get; set; } = Ability.CHA;
    public bool PositiveOnly { get; set; } = true;

    public override void Apply(PermabuffContext ctx)
    {
        ctx.State.AbilitySaveBonuses.Add(new AbilitySaveBonus
        {
            SourceId = SourceId,
            Name = Name,
            Ability = Ability,
            PositiveOnly = PositiveOnly,
        });
    }
}

public class GrantImmunity : Permabuff
{
    public string Immunity { get; set; } = string.Empty;

    public override void Apply(PermabuffContext ctx)
    {
        ctx.State.Immunities.Add(Immunity);
    }
}

public class GrantDR : Permabuff
{
    public int Value { get; set; }
    public string BypassedBy { get; set; } = string.Empty;

    public override void Apply(PermabuffContext ctx)
    {
        // DR from the same bypass condition does not stack. A higher later grant
        // (for example barbarian DR 1/- becoming DR 2/-) replaces the prior value.
        var existing = ctx.State.DamageReduction.FirstOrDefault(dr =>
            string.Equals(dr.BypassedBy, BypassedBy, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
            existing.Value = Math.Max(existing.Value, Value);
        else
            ctx.State.DamageReduction.Add(new DREntry { Value = Value, BypassedBy = BypassedBy });
    }
}

public class GrantSkillBonus : Permabuff
{
    public string SkillId { get; set; } = string.Empty;
    public int Value { get; set; }

    public override void Apply(PermabuffContext ctx)
    {
        ctx.State.SkillBonuses.TryAdd(SkillId, 0);
        ctx.State.SkillBonuses[SkillId] += Value;
    }
}

// --- Companion / Leadership Permabuffs ---

/// <summary>
/// Grants the master a companion slot (animal companion / familiar / mount /
/// cohort / wild cohort). Adds a CompanionSlotState with the effective-level
/// formula (recomputed in tail pass), bumps PendingCompanionSelections[LinkType],
/// and bumps PendingClassFeatureSelections[ClassFeatureType] for the species pick.
///
/// UpgradeOnly=true: do not create a new slot or pending selection — only update
/// the EffectiveLevelFormula on an existing slot of the same LinkType. Used for
/// druid+ranger AC stacking and for Leadership cap recalc at higher HD.
/// </summary>
public class GrantCompanionSlot : Permabuff
{
    public string LinkType { get; set; } = string.Empty;
    public string ClassFeatureType { get; set; } = string.Empty;
    /// <summary>A fixed species for class features that do not ask the player to choose one.</summary>
    public string? SelectedSpecies { get; set; }
    public Formula EffectiveLevelFormula { get; set; } = new();
    public bool UpgradeOnly { get; set; }

    public override void Apply(PermabuffContext ctx)
    {
        var state = ctx.State;
        var existing = state.CompanionSlots.FirstOrDefault(s => s.LinkType == LinkType);

        if (existing != null)
        {
            // Always rewrite formula so later granters can stack/upgrade scaling.
            existing.EffectiveLevelFormula = EffectiveLevelFormula;
            if (!string.IsNullOrEmpty(SelectedSpecies))
                existing.SelectedSpecies = SelectedSpecies;
            return;
        }

        if (UpgradeOnly)
            return; // nothing to upgrade

        // Attribute source: feat beats class. Prefer CurrentFeatId (set by the
        // feat-application cascade, already carrying the "feat:" prefix) over
        // CurrentDriverId (the driver currently ticking).
        var granter = ctx.CurrentFeatId ?? (ctx.CurrentDriverId ?? string.Empty);

        state.CompanionSlots.Add(new CompanionSlotState
        {
            LinkType = LinkType,
            Granter = granter,
            ClassFeatureType = ClassFeatureType,
            EffectiveLevelFormula = EffectiveLevelFormula,
            SelectedSpecies = SelectedSpecies,
        });

        state.PendingCompanionSelections[LinkType] =
            state.PendingCompanionSelections.GetValueOrDefault(LinkType) + 1;

        if (!string.IsNullOrEmpty(ClassFeatureType))
        {
            state.PendingClassFeatureSelections.TryAdd(ClassFeatureType, 0);
            state.PendingClassFeatureSelections[ClassFeatureType] += 1;
        }
    }
}

/// <summary>
/// Accumulates a Leadership-score modifier on the master. Final score is computed
/// in ReplayStudio's tail pass when feat:leadership is present:
///   LeadershipScore = TotalHD + Mod(CHA) + LeadershipScoreModifier.
/// Sources: reputation, fair/cruel treatment, having a stronghold, etc.
/// </summary>
public class ModifyLeadershipScore : Permabuff
{
    public int Value { get; set; }
    public string? Reason { get; set; }

    public override void Apply(PermabuffContext ctx)
    {
        ctx.State.LeadershipScoreModifier += Value;
    }
}

// --- Equipment Permabuffs ---
// These three types are intended for use during the post-tick equipment pass.
// When ctx.EquipmentPass is set, they push contributions to the collector and the
// finalize step applies 3.5e stacking rules. Outside the equipment pass they fall
// back to direct application (legacy compatibility for tests using ad-hoc states).

public class GrantTypedBonus : Permabuff
{
    public BonusTarget Target { get; set; }
    public BonusType BonusType { get; set; } = BonusType.Untyped;
    public Formula Value { get; set; } = new();

    public override void Apply(PermabuffContext ctx)
    {
        var v = Value.Evaluate(ctx.State);
        if (ctx.EquipmentPass != null)
        {
            ctx.EquipmentPass.Add(Target, BonusType, v);
            return;
        }
        ApplyDirect(ctx.State, v);
    }

    private void ApplyDirect(CharacterState state, int v)
    {
        switch (Target)
        {
            case BonusTarget.SaveFort: state.BaseSaves.Fort += v; break;
            case BonusTarget.SaveRef: state.BaseSaves.Ref += v; break;
            case BonusTarget.SaveWill: state.BaseSaves.Will += v; break;
            case BonusTarget.AllSaves:
                state.BaseSaves.Fort += v;
                state.BaseSaves.Ref += v;
                state.BaseSaves.Will += v;
                break;
            case BonusTarget.NaturalArmor: state.NaturalArmor += v; break;
            case BonusTarget.SR: state.SpellResistance = (state.SpellResistance ?? 0) + v; break;
            case BonusTarget.AbilityStr: AddAbility(state, Ability.STR, v); break;
            case BonusTarget.AbilityDex: AddAbility(state, Ability.DEX, v); break;
            case BonusTarget.AbilityCon: AddAbility(state, Ability.CON, v); break;
            case BonusTarget.AbilityInt: AddAbility(state, Ability.INT, v); break;
            case BonusTarget.AbilityWis: AddAbility(state, Ability.WIS, v); break;
            case BonusTarget.AbilityCha: AddAbility(state, Ability.CHA, v); break;
            // AC / Attack / Damage / SkillRanks are only meaningful in equipment pass
        }
    }

    private static void AddAbility(CharacterState state, Ability ability, int v)
    {
        var current = state.AbilityScores.GetScore(ability);
        state.AbilityScores.SetScore(ability, current + v);
    }
}

/// <summary>
/// A typed equipment bonus to one named skill. Unlike the general GrantSkillBonus permabuff,
/// this participates in equipment stacking (for example, two +5 competence bonuses to Bluff
/// still contribute +5 total).
/// </summary>
public class GrantEquipmentSkillBonus : Permabuff
{
    public string SkillId { get; set; } = string.Empty;
    public BonusType BonusType { get; set; } = BonusType.Competence;
    public Formula Value { get; set; } = new();

    public override void Apply(PermabuffContext ctx)
    {
        var value = Value.Evaluate(ctx.State);
        if (ctx.EquipmentPass != null)
        {
            ctx.EquipmentPass.AddSkill(SkillId, BonusType, value);
            return;
        }

        ctx.State.SkillBonuses.TryAdd(SkillId, 0);
        ctx.State.SkillBonuses[SkillId] += value;
    }
}

public class GrantArmorProfile : Permabuff
{
    public ArmorProfile Profile { get; set; } = new();
    public bool AsShield { get; set; }

    public override void Apply(PermabuffContext ctx)
    {
        if (ctx.EquipmentPass != null)
            ctx.EquipmentPass.Armors.Add(new ArmorContribution { Profile = Profile, AsShield = AsShield });
    }
}

public class GrantWeaponLine : Permabuff
{
    public WeaponProfile Profile { get; set; } = new();
    public int EnhancementBonus { get; set; }
    public bool MainHand { get; set; } = true;
    public bool TwoHanded { get; set; }
    public string DisplayName { get; set; } = string.Empty;

    public override void Apply(PermabuffContext ctx)
    {
        if (ctx.EquipmentPass != null)
            ctx.EquipmentPass.Weapons.Add(new WeaponContribution
            {
                Profile = Profile,
                EnhancementBonus = EnhancementBonus,
                MainHand = MainHand,
                TwoHanded = TwoHanded,
                DisplayName = DisplayName
            });
    }
}
