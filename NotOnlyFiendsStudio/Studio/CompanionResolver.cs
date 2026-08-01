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
    private readonly ReplayStudio _engine;
    private readonly Func<string, Character?> _lookup;

    public CompanionResolver(ReplayStudio engine, Func<string, Character?> companionLookup)
    {
        _engine = engine;
        _lookup = companionLookup;
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
            if (companion == null)
            {
                result.MasterState.Warnings.Add(new Warning
                {
                    TickIndex = null,
                    Message = $"Companion link '{link.LinkType}' references missing companion '{link.CompanionId}'"
                });
                continue;
            }

            var effective = EvaluateEffectiveMasterLevel(link, result.MasterState);

            // Inject origin so the companion's templates/formulas can read MasterLevel.
            companion.CompanionOrigin = new CompanionOrigin
            {
                LinkType = link.LinkType,
                EffectiveMasterLevel = effective,
                MasterCharacterId = master.Name
            };

            var companionState = _engine.Evaluate(companion);
            if (IsFamiliarLinkType(link.LinkType))
                ApplyFamiliarMasterStats(result.MasterState, companionState);

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

        return link.EffectiveLevelFormula.Evaluate(master);
    }

    private static bool IsFamiliarLinkType(string linkType) =>
        linkType is "familiar" or "improved_familiar";

    private static void ApplyFamiliarMasterStats(CharacterState master, CharacterState familiar)
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
