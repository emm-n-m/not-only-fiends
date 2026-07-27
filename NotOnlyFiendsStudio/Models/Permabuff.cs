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
[JsonDerivedType(typeof(RevokeSLA), "RevokeSLA")]
[JsonDerivedType(typeof(GrantBonusFeat), "GrantBonusFeat")]
[JsonDerivedType(typeof(ModifyAttribute), "ModifyAttribute")]
[JsonDerivedType(typeof(SetAttribute), "SetAttribute")]
[JsonDerivedType(typeof(GrantFeatSlot), "GrantFeatSlot")]
[JsonDerivedType(typeof(AdvanceSpellcasting), "AdvanceSpellcasting")]
[JsonDerivedType(typeof(UpdateSpellcasting), "UpdateSpellcasting")]
[JsonDerivedType(typeof(GrantRacialSpellcasting), "GrantRacialSpellcasting")]
[JsonDerivedType(typeof(GrantDomainSelection), "GrantDomainSelection")]
[JsonDerivedType(typeof(GrantEffectiveLevels), "GrantEffectiveLevels")]
[JsonDerivedType(typeof(ModifyCounter), "ModifyCounter")]
[JsonDerivedType(typeof(GrantImmunity), "GrantImmunity")]
[JsonDerivedType(typeof(GrantDR), "GrantDR")]
[JsonDerivedType(typeof(GrantSkillBonus), "GrantSkillBonus")]
[JsonDerivedType(typeof(GrantClassFeatureSelection), "GrantClassFeatureSelection")]
[JsonDerivedType(typeof(GrantCapability), "GrantCapability")]
[JsonDerivedType(typeof(GrantCompanionSlot), "GrantCompanionSlot")]
[JsonDerivedType(typeof(ModifyLeadershipScore), "ModifyLeadershipScore")]
[JsonDerivedType(typeof(GrantTypedBonus), "GrantTypedBonus")]
[JsonDerivedType(typeof(GrantArmorProfile), "GrantArmorProfile")]
[JsonDerivedType(typeof(GrantWeaponLine), "GrantWeaponLine")]
[JsonDerivedType(typeof(GrantLanguage), "GrantLanguage")]
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
        var conMod = AbilityScoreSet.Modifier(state.AbilityScores.CON);
        var roll = (ctx.Rules.FirstHDMaxHP && state.TotalHD == 1) ? DieSize : (DieSize / 2 + 1);
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
        state.BaseSaves.Fort += CalculateIncrement(rules, Progression.Fort, ClassLevel);
        state.BaseSaves.Ref += CalculateIncrement(rules, Progression.Ref, ClassLevel);
        state.BaseSaves.Will += CalculateIncrement(rules, Progression.Will, ClassLevel);
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

public class GrantSLA : Permabuff
{
    public SLA SLA { get; set; } = new();

    public override void Apply(PermabuffContext ctx)
    {
        ctx.State.SLAs.Add(SLA);
    }
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
            state.Warnings.Add($"AdvanceSpellcasting({TypeLabel}): no matching spellcasting class found");
        }
        else
        {
            // Check for user selection
            var choice = ctx.CurrentTickChoices?.ClassFeatureChoices
                ?.GetValueOrDefault("advance_spellcasting")?.FirstOrDefault();
            var selected = choice != null
                ? matches.FirstOrDefault(s => s.ClassId == choice)
                : null;

            if (selected != null)
            {
                selected.CasterLevel++;
                if (!CasterLevelOnly) UpdateSpellcastingFromProgression(ctx, selected);
            }
            else
            {
                var classNames = string.Join(", ", matches.Select(m => m.ClassId));
                state.Warnings.Add($"AdvanceSpellcasting({TypeLabel}): multiple matching classes ({classNames}), selection required via ClassFeatureChoices[\"advance_spellcasting\"]");
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
        // Only count domains owned by THIS class — multiclass casters don't share domain slots.
        var ownedCount = ctx.State.DomainOwners.Count(kv => kv.Value == sc.ClassId);
        sc.DomainBonusSlots.Clear();
        if (ownedCount == 0) return;
        foreach (var lvl in sc.SpellsPerDay.Keys.Where(l => l >= 1))
            sc.DomainBonusSlots[lvl] = ownedCount;
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

    public override void Apply(PermabuffContext ctx)
    {
        var owner = ClassId ?? ctx.CurrentDriverId ?? OrphanOwner;
        ctx.State.PendingDomainSelections[owner] =
            ctx.State.PendingDomainSelections.GetValueOrDefault(owner) + Count;
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
            BonusFormula = LevelFormula
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
            EffectiveLevelFormula = EffectiveLevelFormula
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
