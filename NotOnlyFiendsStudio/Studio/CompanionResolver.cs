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
                result.MasterState.Warnings.Add(
                    $"Companion link '{link.LinkType}' references missing companion '{link.CompanionId}'");
                continue;
            }

            var effective = link.EffectiveLevelFormula.Evaluate(result.MasterState);

            // Inject origin so the companion's templates/formulas can read MasterLevel.
            companion.CompanionOrigin = new CompanionOrigin
            {
                LinkType = link.LinkType,
                EffectiveMasterLevel = effective,
                MasterCharacterId = master.Name
            };

            var companionState = _engine.Evaluate(companion);
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
                result.MasterState.Warnings.Add(
                    $"Leadership cohort '{link.CompanionId}' ECL {companionState.ECL} exceeds "
                    + $"max cohort level {result.MasterState.MaxCohortLevel} "
                    + $"(Leadership score {result.MasterState.LeadershipScore}).");
            }
        }

        return result;
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
