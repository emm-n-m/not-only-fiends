using NotOnlyFiendsStudio.Studio;
using NotOnlyFiendsStudio.Models;

namespace NotOnlyFiendsStudio.Tests;

/// <summary>
/// Walks every loaded definition and asserts that every id it references actually resolves.
///
/// Nothing checked this before. The failure mode is silent by construction: a domain that points
/// at a spell id that does not exist simply grants nothing, a prerequisite naming a feat that does
/// not exist can never be met, and no test, no loader warning and no UI surface says so. Fourteen
/// core-domain bonus-spell slots shipped broken for months this way, and the equivalent
/// private-pack bug (domain:fury → spell:bull_s_strength) was correct per the book and still
/// broken, so a content audit against the source would not have found it either.
///
/// Runs against the bundled packs plus the private packs when they are present, so it grows
/// coverage on machines that have them without failing on machines that do not.
/// </summary>
public class ContentIntegrityTests
{
    private sealed record Reference(string Context, string Kind, string Id)
    {
        public override string ToString() => $"{Context}: {Kind} -> {Id}";
    }

    /// <summary>
    /// References that do not resolve today and that could not be fixed without inventing a rules
    /// value or designing a new primitive — so per the run's ground rules they are reported, not
    /// guessed at. Each is listed individually rather than pattern-matched, so anything *new* that
    /// breaks still fails the test.
    ///
    /// See ENGINE_UI_TASKS_REPORT.md, Task 2, for the detail behind each.
    /// </summary>
    private static readonly HashSet<string> KnownGaps = new(StringComparer.Ordinal)
    {
        // Speak Language is a real SRD skill that no pack defines, and it has no sub-skills to
        // stand in for it. Nine classes list it as a class skill. Adding the definition means
        // choosing a key ability, which is not derivable from anything in this repository.
        "skill:speak_language",

        // Pseudo-selectors meaning "any Perform skill" / "any Strength-based skill" and so on.
        // They are not misspellings of a real id — expressing them needs a prerequisite that can
        // match a skill *category*, which does not exist. Feats gated on them (Disguise Spell and
        // several epic feats) are unenterable until it does.
        "skill:type_perform",
        "skill:type_strength",
        "skill:type_dexterity",
        "skill:type_constitution",
        "skill:type_intelligence",
        "skill:type_wisdom",
        "skill:type_charisma",

        // Private pack (en_elements_of_magic). Its feats gate on skills from a different magic
        // system that this content set does not define.
        "skill:scry",
        "skill:divination",
        "skill:dispel_magic",
    };

    private static IEnumerable<Reference> CollectBrokenReferences(ContentRegistry registry)
    {
        var skills = registry.GetAllSkills().ToList();
        var skillIds = new HashSet<string>(skills.Select(s => s.Id), StringComparer.Ordinal);

        // A parent-skill id ("skill:craft", "skill:knowledge") is a legal class-skill entry even
        // though no skill carries it: ReplayStudio.ExpandParentSkillsInPlace expands it to every
        // sub-skill that declares it as ParentSkill.
        var parentSkillIds = new HashSet<string>(
            skills.Where(s => s.ParentSkill != null).Select(s => s.ParentSkill!), StringComparer.Ordinal);

        bool SkillResolves(string id) => skillIds.Contains(id) || parentSkillIds.Contains(id);

        var featIds = new HashSet<string>(registry.GetAllFeats().Select(f => f.Id), StringComparer.Ordinal);

        // "feat:spell_focus_conjuration" resolves against the repeatable "feat:spell_focus" with a
        // selection — the same rule ContentRegistry.IsSelectableFeatVariant applies.
        var selectableFeatPrefixes = registry.GetAllFeats()
            .Where(f => f.SelectionRequired != null && f.Repeatable)
            .Select(f => f.Id + "_")
            .ToList();

        bool FeatResolves(string id) =>
            featIds.Contains(id)
            || selectableFeatPrefixes.Any(p => id.StartsWith(p, StringComparison.Ordinal))
            // HasFeatSelections names the base feat and is satisfied by any variant of it.
            || featIds.Any(f => f.StartsWith(id + "_", StringComparison.Ordinal));

        var driverIds = new HashSet<string>(registry.GetAllDrivers().Select(d => d.Id), StringComparer.Ordinal);
        var spellIds = new HashSet<string>(registry.GetAllSpells().Select(s => s.Id), StringComparer.Ordinal);
        var raceIds = new HashSet<string>(registry.GetAllRaces().Select(r => r.Id), StringComparer.Ordinal);
        var templateIds = new HashSet<string>(registry.GetAllTemplates().Select(t => t.Id), StringComparer.Ordinal);
        var domainIds = new HashSet<string>(registry.GetAllDomains().Select(d => d.Id), StringComparer.Ordinal);
        var classFeatureIds = new HashSet<string>(registry.GetAllClassFeatures().Select(f => f.Id), StringComparer.Ordinal);

        var broken = new List<Reference>();

        void Check(bool resolves, string context, string kind, string id)
        {
            if (!string.IsNullOrEmpty(id) && !resolves)
                broken.Add(new Reference(context, kind, id));
        }

        void CheckPrerequisites(IEnumerable<Prerequisite> prerequisites, string context)
        {
            foreach (var prereq in prerequisites)
            {
                switch (prereq)
                {
                    case HasFeat p:
                        Check(FeatResolves(p.FeatId), context, "HasFeat", p.FeatId);
                        break;
                    case HasFeatSelections p:
                        Check(FeatResolves(p.FeatId), context, "HasFeatSelections", p.FeatId);
                        break;
                    case MinSkillRanks p:
                        Check(SkillResolves(p.SkillId), context, "MinSkillRanks", p.SkillId);
                        break;
                    case MinSkillRanksAcross p:
                        foreach (var id in p.SkillIds)
                            Check(SkillResolves(id), context, "MinSkillRanksAcross", id);
                        break;
                    case MinClassLevel p:
                        Check(driverIds.Contains(p.ClassId), context, "MinClassLevel", p.ClassId);
                        break;
                    case HasRace p:
                        Check(raceIds.Contains(p.RaceId), context, "HasRace", p.RaceId);
                        break;
                    case LacksTemplate p:
                        Check(templateIds.Contains(p.TemplateId), context, "LacksTemplate", p.TemplateId);
                        break;
                }
            }
        }

        void CheckPermabuffs(IEnumerable<Permabuff> permabuffs, string context)
        {
            foreach (var buff in permabuffs)
            {
                switch (buff)
                {
                    case GrantBonusFeat b:
                        Check(FeatResolves(b.FeatId), context, "GrantBonusFeat", b.FeatId);
                        break;
                    case GrantSkillBonus b:
                        Check(SkillResolves(b.SkillId), context, "GrantSkillBonus", b.SkillId);
                        break;
                    case AddClassSkills b:
                        foreach (var id in b.Skills)
                            Check(SkillResolves(id), context, "AddClassSkills", id);
                        break;
                    case GrantEffectiveLevels b:
                        Check(driverIds.Contains(b.TargetDriverId), context, "GrantEffectiveLevels", b.TargetDriverId);
                        break;
                    case GrantRacialSpellcasting b:
                        Check(driverIds.Contains(b.ClassId), context, "GrantRacialSpellcasting", b.ClassId);
                        break;
                    case ApplyClassFeatureOptionBenefits b:
                        Check(classFeatureIds.Contains(b.FeatureType), context, "ApplyClassFeatureOptionBenefits", b.FeatureType);
                        break;
                }
            }
        }

        foreach (var driver in registry.GetAllDrivers())
        {
            var context = $"Driver '{driver.Id}'";
            CheckPrerequisites(driver.Prerequisites, context);

            if (driver is HDDriver hd)
            {
                foreach (var id in hd.ClassSkills)
                    Check(SkillResolves(id), context, "classSkills", id);

                CheckPermabuffs(hd.PerLevelPermabuffs, context);
                foreach (var (level, buffs) in hd.LevelPermabuffs)
                    CheckPermabuffs(buffs, $"{context} level {level}");
            }
        }

        foreach (var feat in registry.GetAllFeats())
        {
            var context = $"Feat '{feat.Id}'";
            CheckPrerequisites(feat.Prerequisites, context);
            CheckPermabuffs(feat.GrantedPermabuffs, context);
        }

        foreach (var race in registry.GetAllRaces())
        {
            var context = $"Race '{race.Id}'";
            if (race.RacialHDDriverId != null)
                Check(driverIds.Contains(race.RacialHDDriverId), context, "racialHDDriverId", race.RacialHDDriverId);

            foreach (var id in race.RacialClassSkillAdditions)
                Check(SkillResolves(id), context, "racialClassSkillAdditions", id);
            foreach (var id in race.RacialClassSkillRemovals)
                Check(SkillResolves(id), context, "racialClassSkillRemovals", id);

            CheckPermabuffs(race.RacialPermabuffs, context);
        }

        foreach (var template in registry.GetAllTemplates())
        {
            var context = $"Template '{template.Id}'";
            CheckPermabuffs(template.CreationPermabuffs, context);
            foreach (var (hd, buffs) in template.ScalingPermabuffs)
                CheckPermabuffs(buffs, $"{context} HD {hd}");
            foreach (var (masterLevel, buffs) in template.CompanionScalingPermabuffs)
                CheckPermabuffs(buffs, $"{context} master level {masterLevel}");
        }

        foreach (var domain in registry.GetAllDomains())
        {
            var context = $"Domain '{domain.Id}'";
            CheckPermabuffs(domain.GrantedPermabuffs, context);
            foreach (var (level, spellId) in domain.BonusSpells)
                Check(spellIds.Contains(spellId), context, $"bonusSpells[{level}]", spellId);
        }

        foreach (var classFeature in registry.GetAllClassFeatures())
            foreach (var option in classFeature.Options)
            {
                CheckPermabuffs(option.GrantedPermabuffs, $"ClassFeature '{classFeature.Id}' option '{option.Id}'");
                foreach (var (benefitSet, buffs) in option.AdditionalPermabuffs)
                    CheckPermabuffs(buffs, $"ClassFeature '{classFeature.Id}' option '{option.Id}' benefit set '{benefitSet}'");
            }

        foreach (var equipment in registry.GetAllEquipment())
        {
            var context = $"Equipment '{equipment.Id}'";
            CheckPrerequisites(equipment.Prerequisites, context);
            CheckPermabuffs(equipment.GrantedPermabuffs, context);
        }

        foreach (var spell in registry.GetAllSpells())
        {
            foreach (var (listId, _) in spell.ClassLevels)
            {
                var resolves = EpicSpellcasting.IsSpellList(listId)
                               || domainIds.Contains(listId)
                               || (driverIds.Contains(listId)
                                   && registry.GetAllDrivers().OfType<HDDriver>()
                                       .Any(d => d.Id == listId && d.Kind == DriverKind.Class));
                Check(resolves, $"Spell '{spell.Id}'", "classLevels", listId);
            }
        }

        return broken;
    }

    [Fact]
    public void EveryReference_Resolves_OrIsAKnownGap()
    {
        var registry = TestContentHelper.LoadBundledAndPrivatePacksIfAvailable();

        var unexpected = CollectBrokenReferences(registry)
            .Where(r => !KnownGaps.Contains(r.Id))
            .Select(r => r.ToString())
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.True(unexpected.Count == 0,
            $"{unexpected.Count} unresolved content reference(s):\n{string.Join("\n", unexpected)}");
    }

    [Fact]
    public void EveryDomainBonusSpell_Resolves()
    {
        // Ungated successor to PrivatePackRulesAccuracyTests.FiendishCodexDomains_ReferenceOnlyRealSpells,
        // which was scoped to seven domains because the public packs would have failed it.
        // No known gaps: every domain bonus spell must resolve, in every pack.
        var registry = TestContentHelper.LoadBundledAndPrivatePacksIfAvailable();

        var dangling = registry.GetAllDomains()
            .SelectMany(d => d.BonusSpells.Select(kv => (Domain: d.Id, Level: kv.Key, SpellId: kv.Value)))
            .Where(x => !registry.TryGetSpell(x.SpellId, out _))
            .Select(x => $"{x.Domain}[{x.Level}] -> {x.SpellId}")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.True(dangling.Count == 0,
            $"{dangling.Count} dangling domain bonus spell(s):\n{string.Join("\n", dangling)}");
    }

    [Fact]
    public void SharedDomainSpells_AreReachableFromEveryDomainThatGrantsThem()
    {
        // The domain spell picker (BuilderView) filters on the *spell* side — a spell is offered
        // for a domain only if its classLevels carries that "domain:<id>" key. So the two links
        // are independent, and elemental_swarm / summon_monster_ix had only the domain side.
        var registry = TestContentHelper.LoadBundledPacks();

        var elementalSwarm = registry.GetSpell("spell:elemental_swarm");
        foreach (var domain in new[] { "domain:air", "domain:earth", "domain:fire", "domain:water" })
            Assert.Equal(9, elementalSwarm.ClassLevels[domain]);

        var summonMonsterIx = registry.GetSpell("spell:summon_monster_ix");
        foreach (var domain in new[] { "domain:chaos", "domain:evil", "domain:good", "domain:law" })
            Assert.Equal(9, summonMonsterIx.ClassLevels[domain]);
    }

    [Fact]
    public void KnownGaps_AreStillGaps()
    {
        // Guards the allowlist itself: once a gap is genuinely fixed, its entry has to be deleted
        // rather than left behind masking a future regression at the same id.
        var registry = TestContentHelper.LoadBundledAndPrivatePacksIfAvailable();

        var stillBroken = new HashSet<string>(
            CollectBrokenReferences(registry).Select(r => r.Id), StringComparer.Ordinal);

        var obsolete = KnownGaps.Where(gap => !stillBroken.Contains(gap)).ToList();

        Assert.True(obsolete.Count == 0,
            $"KnownGaps entries that now resolve and should be removed:\n{string.Join("\n", obsolete)}");
    }
}
