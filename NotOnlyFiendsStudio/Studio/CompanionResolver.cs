using NotOnlyFiendsStudio.Models;

namespace NotOnlyFiendsStudio.Studio;

/// <summary>
/// Orchestrates a master + companions build. ReplayStudio itself stays pure and
/// single-Character; this class is the host-side glue that:
///   1. Evaluates the master.
///   2. For each CompanionLink, looks up the companion Character via the supplied
///      lookup delegate, computes the link's effective master level against the
///      master's evaluated state, injects CompanionOrigin onto the companion, and
///      evaluates it.
///   3. Returns a CompositeBuildResult containing both states.
///
/// File I/O is deliberately delegated — pass a closure that knows how to load
/// companion JSON from disk, a content registry, an in-memory dict, etc.
/// </summary>
public class CompanionResolver
{
    /// <summary>
    /// Effective druid level for an animal companion. Druid levels count in full; a ranger's
    /// count for half ("a ranger's effective druid level is one-half his ranger level"), and only
    /// from 4th level, which is when the ability is gained. The DSL has no conditional, so the
    /// 4th-level gate is <c>max(0, ranger - 3) * 2</c> — zero below 4th, and above it always
    /// larger than the half-level it is min'd against, so the half-level wins.
    ///
    /// <see cref="Formula"/>'s EffectiveClassLevel, not ClassLevel: an animal companion is a class
    /// ability, so anything that raises the class level *for abilities* raises it — the Unseelie
    /// Champion template adding outsider HD to ranger level, an Arcane Hierophant advancing druid.
    /// Reading raw levels here made a 12th-level-for-abilities ranger resolve as a 3rd.
    ///
    /// The ranger variants are combined with max rather than sum. A template that boosts "ranger
    /// level" grants its rule to every ranger id, since it cannot know which one the character
    /// took — summing would then count the same bonus once per variant. The cost is that levels
    /// split across two ranger variants count only as the larger, which no real build does.
    /// </summary>
    public const string AnimalCompanionLevelExpression =
        "EffectiveClassLevel(druid) " +
        "+ min(max(EffectiveClassLevel(ranger), EffectiveClassLevel(planar_ranger)) / 2, " +
        "max(0, max(EffectiveClassLevel(ranger), EffectiveClassLevel(planar_ranger)) - 3) * 2)";

    /// <summary>
    /// The expression imports used before the half-level rule was applied. It counted a ranger's
    /// levels one-for-one past 3rd (ranger 20 → 17 instead of 10) and ignored ranger variants
    /// entirely, so a planar ranger's companion resolved to level 0 and gained no scaling at all.
    /// </summary>
    private const string LegacyAnimalCompanionExpression =
        "max(ClassLevel(druid), ClassLevel(druid) + ClassLevel(ranger) - 3)";

    private readonly ReplayStudio _engine;
    private readonly Func<string, Character?> _lookup;
    private readonly Func<string, Character?>? _lookupByName;

    /// <param name="companionLookup">Resolves a companion id to its character.</param>
    /// <param name="companionLookupByName">
    /// Optional repair path, consulted only when the id misses: resolves
    /// <see cref="CompanionLink.SourceName"/> to the one character with that name, or null when
    /// none or several match. Lets a link written before its companion existed heal itself once
    /// the companion is imported, without ever guessing between same-named characters.
    /// </param>
    public CompanionResolver(
        ReplayStudio engine,
        Func<string, Character?> companionLookup,
        Func<string, Character?>? companionLookupByName = null)
    {
        _engine = engine;
        _lookup = companionLookup;
        _lookupByName = companionLookupByName;
    }

    public CompositeBuildResult Build(Character master)
    {
        var result = new CompositeBuildResult
        {
            Master = master,
            MasterState = _engine.Evaluate(master)
        };

        foreach (var link in master.CompanionLinks)
        {
            var companion = _lookup(link.CompanionId);

            // Id missed — fall back to the name the source used, if that names exactly one
            // character. This is what repairs a link written before its companion was imported.
            if (companion == null && !string.IsNullOrWhiteSpace(link.SourceName) && _lookupByName != null)
                companion = _lookupByName(link.SourceName!);

            if (companion == null)
            {
                // A dangling link is usually an identity mismatch rather than an absent character:
                // the id comes from what the master calls the companion, while the saved character
                // is filed under the name in its own record. When those disagree by so much as a
                // letter the lookup misses, so the warning carries the link's provenance note —
                // naming the source it was imported from is what makes the mismatch findable.
                var byName = string.IsNullOrWhiteSpace(link.SourceName)
                    ? ""
                    : $", and no single saved character is named '{link.SourceName}'";
                var provenance = string.IsNullOrWhiteSpace(link.Notes) ? "" : $" ({link.Notes})";
                result.MasterState.Warnings.Add(new Warning
                {
                    TickIndex = null,
                    Message = $"Companion link '{link.LinkType}' references missing companion "
                        + $"'{link.CompanionId}' — no saved character has that id{byName}{provenance}"
                });
                continue;
            }

            var effective = EvaluateEffectiveMasterLevel(link, result.MasterState);

            // Level 0 means the master does not qualify for this companion at all — a ranger
            // below 4th, a would-be druid with no druid levels. The companion still evaluates,
            // but it receives none of the scaling the link exists to deliver, so saying nothing
            // reads as "this companion is fine" when it is inert.
            if (effective <= 0)
            {
                result.MasterState.Warnings.Add(new Warning
                {
                    TickIndex = null,
                    Message = $"Companion link '{link.LinkType}' to '{link.CompanionId}' resolves to "
                        + $"effective master level {effective} — the master does not qualify for it, "
                        + "so the companion gains no scaling from the link."
                });
            }

            // Inject origin so the companion's templates/formulas can read MasterLevel.
            companion.CompanionOrigin = new CompanionOrigin
            {
                LinkType = link.LinkType,
                EffectiveMasterLevel = effective,
                MasterCharacterId = master.Name
            };

            var companionState = _engine.Evaluate(companion);

            // Must precede the cohort-level check below, which reads MaxCohortLevel.
            if (link.LinkType == "leadership_cohort")
                ApplyDifferentAlignmentCohortPenalty(result.MasterState, companionState);

            if (IsFamiliarLinkType(link.LinkType))
                ApplyFamiliarMasterStats(result.MasterState, companionState, _engine);

            result.Companions.Add(new CompanionBuild
            {
                Link = link,
                Character = companion,
                State = companionState
            });

            // Leadership cohort validation: cohort ECL must not exceed the master's
            // MaxCohortLevel (computed in master's tail pass from Leadership score).
            if (link.LinkType == "leadership_cohort"
                && companionState.ECL > result.MasterState.MaxCohortLevel)
            {
                result.MasterState.Warnings.Add(new Warning
                {
                    TickIndex = null,
                    Message = $"Leadership cohort '{link.CompanionId}' ECL {companionState.ECL} exceeds "
                        + $"max cohort level {result.MasterState.MaxCohortLevel} "
                        + $"(Leadership score {result.MasterState.LeadershipScore})."
                });
            }
        }

        return result;
    }

    private static int EvaluateEffectiveMasterLevel(CompanionLink link, CharacterState master)
    {
        // Existing imported saves may still contain the former caster-level formula.
        // Migrate that exact legacy expression at replay time without overriding a
        // deliberately authored custom familiar progression formula.
        if (IsFamiliarLinkType(link.LinkType)
            && link.EffectiveLevelFormula.Expression
                == "CasterLevel(wizard) + CasterLevel(sorcerer)")
        {
            return new Formula("ClassLevel(wizard) + ClassLevel(sorcerer)").Evaluate(master);
        }

        // Same migration for the pre-half-level animal companion expression, so saves written
        // before the fix stop under-advancing their companions without needing a re-import.
        if (link.LinkType == "animal_companion"
            && link.EffectiveLevelFormula.Expression == LegacyAnimalCompanionExpression)
        {
            return new Formula(AnimalCompanionLevelExpression).Evaluate(master);
        }

        return link.EffectiveLevelFormula.Evaluate(master);
    }

    /// <summary>
    /// SRD Leadership modifiers: "Recruits a cohort of a different alignment -1". The engine cannot
    /// see this during replay — it needs the cohort — so it lands here, and only the cohort score
    /// and the cohort level derived from it move. Follower counts are untouched: the penalty is in
    /// the cohort-only group.
    /// </summary>
    private static void ApplyDifferentAlignmentCohortPenalty(CharacterState master, CharacterState cohort)
    {
        if (master.Alignment == cohort.Alignment)
            return;

        master.LeadershipCohortScore -= 1;
        master.LeadershipModifierNotes.Add("Recruits a cohort of a different alignment (cohort only) -1");
        master.MaxCohortLevel = Math.Min(
            LeadershipTables.LookupCohortLevelFor(master.Feats, master.LeadershipCohortScore),
            master.TotalHD - 1);
    }

    private static bool IsFamiliarLinkType(string linkType) =>
        linkType is "familiar" or "improved_familiar";

    /// <summary>
    /// SRD familiar skills: "For each skill in which either the master or the familiar has ranks,
    /// use either the normal skill ranks for an animal of that type or the master's skill ranks,
    /// whichever are better. In either case, the familiar uses its own ability modifiers."
    ///
    /// So this is a per-skill maximum, not a replacement — the familiar keeps any skill where its
    /// own ranks are higher, which is what it would use if dismissed. Ranks borrowed this way are
    /// not bought, so they deliberately ignore the familiar's own max-ranks cap and skill points.
    /// Totals are then recomputed, which is where "its own ability modifiers" applies: a toad
    /// borrowing a sorcerer's Hide ranks still adds the toad's Dexterity.
    /// </summary>
    private static void ApplyFamiliarMasterSkills(
        CharacterState master,
        CharacterState familiar,
        ReplayStudio engine)
    {
        var touched = false;
        foreach (var (skillId, masterHalfRanks) in master.SkillHalfRanks)
        {
            if (masterHalfRanks <= familiar.SkillHalfRanks.GetValueOrDefault(skillId))
                continue;
            familiar.SkillHalfRanks[skillId] = masterHalfRanks;
            touched = true;
        }

        if (touched)
            engine.RecomputeSkillTotals(familiar);
    }

    private static void ApplyFamiliarMasterStats(
        CharacterState master,
        CharacterState familiar,
        ReplayStudio engine)
    {
        // PCGen's standard familiar modifier mirrors the 3.5e familiar rules:
        // COPYMASTERBAB:MASTER, COPYMASTERCHECK:MASTER, and
        // COPYMASTERHP:max(1,MASTER/2). Preserve bonuses belonging to the familiar
        // itself while replacing only its HD-derived base saves.
        var ownFortBonus = familiar.BaseSaves.Fort - familiar.ProgressionBaseSaves.Fort;
        var ownRefBonus = familiar.BaseSaves.Ref - familiar.ProgressionBaseSaves.Ref;
        var ownWillBonus = familiar.BaseSaves.Will - familiar.ProgressionBaseSaves.Will;

        familiar.HP = Math.Max(1, master.HP / 2);
        // "Base attack bonus" is the pre-epic class/racial-HD value. Epic attack is a
        // character-level bonus, not BAB, and is not copied by the familiar rule.
        familiar.BaseBAB = master.BaseBAB;
        familiar.EpicAttackBonus = 0;
        familiar.BaseSaves = new SaveSet
        {
            Fort = master.ProgressionBaseSaves.Fort + ownFortBonus,
            Ref = master.ProgressionBaseSaves.Ref + ownRefBonus,
            Will = master.ProgressionBaseSaves.Will + ownWillBonus
        };
        familiar.EpicSaveBonus = 0;

        ApplyFamiliarMasterSkills(master, familiar, engine);
        ApplyFamiliarSpecialToMaster(master, familiar, engine);
    }

    /// <summary>
    /// The SRD "Familiar Special" column, where the benefit runs from familiar to master: a toad
    /// gives +3 hit points, a rat +2 on Fortitude saves, a cat +3 on Move Silently. Applied to the
    /// master's finished state, because that is what the bonus modifies — nothing about it belongs
    /// to a tick. Only fires for an actual familiar binding, so a druid's animal companion of the
    /// same species grants its master nothing.
    /// </summary>
    private static void ApplyFamiliarSpecialToMaster(
        CharacterState master,
        CharacterState familiar,
        ReplayStudio engine)
    {
        var race = engine.FindRace(familiar.RaceId);
        if (race == null || race.FamiliarMasterPermabuffs.Count == 0)
            return;

        foreach (var buff in race.FamiliarMasterPermabuffs)
            engine.ApplyToFinishedState(master, buff);

        // A granted skill bonus lands in SkillBonuses, which the master's totals were computed
        // from before this ran.
        engine.RecomputeSkillTotals(master);
    }
}

public class CompositeBuildResult
{
    public Character Master { get; set; } = new();
    public CharacterState MasterState { get; set; } = new();
    public List<CompanionBuild> Companions { get; set; } = new();
}

public class CompanionBuild
{
    public CompanionLink Link { get; set; } = new();
    public Character Character { get; set; } = new();
    public CharacterState State { get; set; } = new();
}
