using NotOnlyFiendsStudio.Models;

namespace NotOnlyFiendsStudio.Studio;

public class ReplayStudio
{
    private readonly ContentRegistry _content;
    private readonly GameRules _rules;

    public ReplayStudio(ContentRegistry content, GameRules? rules = null)
    {
        _content = content;
        _rules = rules ?? GameRules.Standard35e();
    }

    private PermabuffContext CreateContext(CharacterState state) =>
        new(state, _rules, _content);

    public CharacterState Evaluate(Character character, int? upToHD = null)
    {
        var state = new CharacterState();
        state.Alignment = character.Alignment;

        // Companion-side: surface origin so templates/formulas can read MasterLevel.
        if (character.CompanionOrigin != null)
        {
            state.CompanionOrigin = character.CompanionOrigin;
            state.EffectiveMasterLevel = character.CompanionOrigin.EffectiveMasterLevel;
        }

        var ctx = CreateContext(state);

        // 1. Apply race
        var race = _content.GetRace(character.RaceId);
        ApplyRace(ctx, race);

        // 2. Apply templates (in order)
        foreach (var templateId in character.TemplateIds)
        {
            var template = _content.GetTemplate(templateId);
            ApplyTemplateCreation(ctx, template);
        }

        // 3. Apply base ability scores (added to racial/template modifiers)
        ApplyBaseAbilities(state, character.BaseAbilityScores);

        // 4. Process each tick
        var driverLevelCounters = new Dictionary<string, int>();
        var effectiveHighWater = new Dictionary<string, int>();
        var racialSpellcastingSeeded = false;
        var maxTick = Math.Min(upToHD ?? character.Ticks.Count, character.Ticks.Count);

        for (int i = 0; i < maxTick; i++)
        {
            // Apply permanent events scheduled before this tick
            foreach (var evt in character.PermanentEvents.Where(e => e.BeforeTick == i))
                foreach (var buff in evt.Permabuffs)
                    buff.Apply(ctx);

            var tick = character.Ticks[i];
            state.TotalHD = i + 1;
            state.HDList.Add(tick.DriverId);
            ctx.CurrentTickChoices = tick.Choices;
            ctx.CurrentDriverId = tick.DriverId;

            // Update MaxHalfRanks from rules
            state.MaxHalfRanks = _rules.MaxHalfRanks(state.TotalHD);

            // Track per-driver level
            driverLevelCounters.TryAdd(tick.DriverId, 0);
            driverLevelCounters[tick.DriverId]++;
            var driverLevel = driverLevelCounters[tick.DriverId];

            // a. Validate driver prerequisites and max level. Epic characters (HD > 20)
            // may legally continue a class past its normal max level; the engine grants
            // HD, skill points and epic BAB/save bonuses but no new class features
            // (LevelPermabuffs is empty past MaxLevel).
            var driver = _content.GetDriver(tick.DriverId);
            if (driver is HDDriver hdDriver && hdDriver.MaxLevel.HasValue
                && driverLevel > hdDriver.MaxLevel.Value
                && state.TotalHD <= _rules.EpicThreshold)
                state.Warnings.Add($"HD {state.TotalHD}: {driver.Name} level {driverLevel} exceeds max level {hdDriver.MaxLevel.Value}");
            foreach (var prereq in driver.Prerequisites)
            {
                if (!prereq.IsMet(state))
                    state.Warnings.Add($"HD {state.TotalHD}: prerequisite not met for {driver.Name}: {prereq.Description}");
            }

            // b. Track class levels
            if (driver is HDDriver hd && hd.Kind == DriverKind.Class)
            {
                state.ClassLevels.TryAdd(tick.DriverId, 0);
                state.ClassLevels[tick.DriverId]++;
            }

            // c. Compute effective level (actual + bonuses from templates/feats)
            var effectiveLevel = driverLevel;
            foreach (var rule in state.EffectiveLevelRules.Where(r => r.TargetDriverId == tick.DriverId))
                effectiveLevel += rule.BonusFormula.Evaluate(state);
            var previousEffective = effectiveHighWater.GetValueOrDefault(tick.DriverId, 0);
            effectiveHighWater[tick.DriverId] = effectiveLevel;

            // c1. Seed racial spellcasting before the first class tick, so class-granted
            // AdvanceSpellcasting (e.g. Loremaster/Archmage) can find and advance it. Racial HD
            // always precede class levels, so RacialHD() formulas are final by this point. Seeding
            // is idempotent — a same-type class (e.g. Nymph's own Druid levels) overwrites the
            // seed with its effective (stacked) caster level, so no double counting occurs.
            if (!racialSpellcastingSeeded && driver is HDDriver clsHd && clsHd.Kind == DriverKind.Class)
            {
                FinalizeRacialSpellcasting(ctx, race);
                racialSpellcastingSeeded = true;
            }

            // d. Get and apply driver permabuffs
            var buffs = driver.GetPermabuffs(state, driverLevel, _rules, effectiveLevel, previousEffective);
            foreach (var buff in buffs)
                buff.Apply(ctx);

            // d1. Apply racial class skill delta (add/remove skills from the generic racial HD driver)
            if (driver is HDDriver racialHd && racialHd.Kind == DriverKind.RacialHD)
            {
                foreach (var skill in race.RacialClassSkillAdditions)
                    state.ClassSkills.Add(skill);
                foreach (var skill in race.RacialClassSkillRemovals)
                    state.ClassSkills.Remove(skill);
            }

            // d2. Expand parent class skills (e.g. "knowledge" → all knowledge_* subskills)
            ExpandParentSkillsInPlace(state.ClassSkills);

            // e. Epic progression (past epic threshold)
            if (state.TotalHD > _rules.EpicThreshold)
            {
                if (state.TotalHD % 2 == 1) // odd HD past threshold
                    state.EpicAttackBonus++;
                else // even HD past threshold
                    state.EpicSaveBonus++;
            }

            // f. Template tick injections
            foreach (var templateId in character.TemplateIds)
            {
                var template = _content.GetTemplate(templateId);
                var templateBuffs = template.GetTickPermabuffs(state.TotalHD, state);
                foreach (var buff in templateBuffs)
                    buff.Apply(ctx);
            }

            // g. Racial bonus skill points per HD
            if (race.BonusSkillPointsPerHD > 0)
            {
                var bonus = race.BonusSkillPointsPerHD;
                if (state.TotalHD == 1)
                    bonus *= _rules.RacialBonusSkillFirstHDMultiplier;
                state.UnspentSkillPoints += bonus;
            }

            // h. Race scaling abilities
            foreach (var sf in race.ScalingFormulas)
                new SetAttribute(sf.Target, sf.Formula.Evaluate(state), sf.ResistanceElement, sf.AbilityScore).Apply(ctx);

            // i. Ability score increase (every Nth HD per rules)
            if (state.TotalHD % _rules.AbilityIncreaseInterval == 0 && tick.Choices.AbilityIncrease.HasValue)
                ApplyAbilityIncrease(state, tick.Choices.AbilityIncrease.Value);

            // j. Feat slots — standard schedule from rules
            if (_rules.GrantsStandardFeat(state.TotalHD))
                state.FeatSlots.Add(new FeatSlot());

            // Epic feat slots from rules
            if (_rules.GrantsEpicFeat(state.TotalHD))
                state.FeatSlots.Add(new FeatSlot());

            // Racial bonus feats (at HD 1)
            if (state.TotalHD == 1)
            {
                for (int f = 0; f < race.BonusFeats; f++)
                    state.FeatSlots.Add(new FeatSlot());
            }

            // k. Set current tick's class skills for cost calculation
            if (driver is HDDriver currentHd)
            {
                state.CurrentTickClassSkills = ExpandParentSkills(currentHd.ClassSkills);
                // Apply racial class skill delta for racial HD ticks
                if (currentHd.Kind == DriverKind.RacialHD)
                {
                    foreach (var skill in race.RacialClassSkillAdditions)
                        state.CurrentTickClassSkills.Add(skill);
                    foreach (var skill in race.RacialClassSkillRemovals)
                        state.CurrentTickClassSkills.Remove(skill);
                    ExpandParentSkillsInPlace(state.CurrentTickClassSkills);
                }
            }
            else
                state.CurrentTickClassSkills = new HashSet<string>();

            // l. Resolve user choices: feats, skills, spells
            ApplyTickChoices(ctx, tick.Choices);

            // m. Cross-driver effective level update — fire newly-reached
            // LevelPermabuffs on OTHER drivers whose effective level has
            // grown due to EffectiveLevelRules added during this tick (e.g.,
            // Arcane Hierophant boosting druid wild shape progression).
            foreach (var (otherDriverId, otherActualLevel) in state.ClassLevels.ToList())
            {
                if (otherDriverId == tick.DriverId) continue;

                var otherEffective = otherActualLevel;
                foreach (var rule in state.EffectiveLevelRules.Where(r => r.TargetDriverId == otherDriverId))
                    otherEffective += rule.BonusFormula.Evaluate(state);

                var otherPrev = effectiveHighWater.GetValueOrDefault(otherDriverId, 0);
                if (otherEffective <= otherPrev) continue;

                var otherDriver = _content.GetDriver(otherDriverId);
                if (otherDriver is HDDriver otherHd)
                {
                    foreach (var (level, perms) in otherHd.LevelPermabuffs)
                    {
                        if (level > otherPrev && level <= otherEffective)
                            foreach (var buff in perms)
                                buff.Apply(ctx);
                    }
                }
                effectiveHighWater[otherDriverId] = otherEffective;
            }
        }

        ctx.CurrentDriverId = null;

        // 5. Apply equipment as a structured post-tick pass:
        //    resolve content → apply permabuffs (typed contributions collected in
        //    ctx.EquipmentPass) → finalize AC, attack lines, encumbrance.
        EvaluateEquipment(ctx, character);

        // 6. Tail pass — companion / leadership finalization.
        FinalizeCompanionAndLeadership(ctx, character);

        // 7. Seed racial spellcasting (e.g. Aranea Sorc N, Ghaele Cleric 14) for
        // characters that never take a level of the target class. When class levels
        // ARE taken, the class tick's UpdateSpellcasting already seeded state.Spellcasting
        // using featureLevel = actualLevel + EffectiveLevelRule (registered at race
        // creation), so this step skips the class and leaves the stacked value in place.
        FinalizeRacialSpellcasting(ctx, race);

        return state;
    }

    /// <summary>
    /// Seeds state.Spellcasting entries for any GrantRacialSpellcasting on the race whose
    /// target class has no state.Spellcasting entry yet (i.e. character took no class levels
    /// of it). Runs after all ticks so that formulas like RacialHD() see the final HD count.
    /// </summary>
    private void FinalizeRacialSpellcasting(PermabuffContext ctx, RaceDefinition race)
    {
        foreach (var buff in race.RacialPermabuffs.OfType<GrantRacialSpellcasting>())
        {
            if (ctx.State.Spellcasting.ContainsKey(buff.ClassId))
                continue;

            var level = (int)buff.LevelFormula.Evaluate(ctx.State);
            if (level <= 0)
                continue;

            HDDriver? hd = null;
            try
            {
                hd = _content.GetDriver(buff.ClassId) as HDDriver;
            }
            catch (KeyNotFoundException)
            {
                ctx.State.Warnings.Add($"GrantRacialSpellcasting: class driver '{buff.ClassId}' not in content");
                continue;
            }
            if (hd?.Spellcasting is null)
            {
                ctx.State.Warnings.Add($"GrantRacialSpellcasting: class driver '{buff.ClassId}' has no spellcasting progression");
                continue;
            }

            if (!hd.Spellcasting.SpellsPerDay.TryGetValue(level, out var spd))
            {
                ctx.State.Warnings.Add($"GrantRacialSpellcasting: '{buff.ClassId}' progression has no level {level} entry");
                continue;
            }

            Dictionary<int, int>? sk = null;
            hd.Spellcasting.SpellsKnown?.TryGetValue(level, out sk);

            new UpdateSpellcasting
            {
                ClassId = buff.ClassId,
                CastingType = hd.Spellcasting.CastingType,
                CastingStat = hd.Spellcasting.CastingStat,
                CasterLevel = level,
                SpellsPerDay = spd,
                SpellsKnown = sk,
                ProgressionRef = hd.Spellcasting,
            }.Apply(ctx);
        }
    }

    /// <summary>
    /// End-of-evaluation pass that:
    ///  (a) Recomputes EffectiveLevel for every master-side companion slot against the
    ///      final state, and binds SelectedSpecies from ClassFeatureSelections.
    ///  (b) Computes Leadership score / MaxCohortLevel / Followers if feat:leadership is present.
    ///  (c) On the companion side: fires every template's CompanionScalingPermabuffs whose
    ///      key &lt;= state.EffectiveMasterLevel.
    /// </summary>
    private void FinalizeCompanionAndLeadership(PermabuffContext ctx, Character character)
    {
        var state = ctx.State;

        // (a) Recompute companion slot effective levels and pull species selections.
        foreach (var slot in state.CompanionSlots)
        {
            slot.EffectiveLevel = slot.EffectiveLevelFormula.Evaluate(state);

            if (!string.IsNullOrEmpty(slot.ClassFeatureType)
                && state.ClassFeatureSelections.TryGetValue(slot.ClassFeatureType, out var picks)
                && picks.Count > 0)
            {
                // First pick wins for this slot (multi-slot of same featureType not yet supported).
                slot.SelectedSpecies = picks[0];
            }
        }

        // (b) Leadership finalization.
        if (state.Feats.Contains("leadership"))
        {
            state.LeadershipScore = state.TotalHD
                                    + AbilityScoreSet.Modifier(state.AbilityScores.CHA)
                                    + state.LeadershipScoreModifier;
            state.MaxCohortLevel = Math.Min(state.LeadershipScore - 2, state.TotalHD - 2);
            state.Followers = LeadershipTables.LookupFollowerCounts(state.LeadershipScore);

            // Re-evaluate any slot whose formula references LeadershipScore (cohort cap).
            foreach (var slot in state.CompanionSlots)
                slot.EffectiveLevel = slot.EffectiveLevelFormula.Evaluate(state);
        }

        // (c) Companion-side template scaling.
        if (state.CompanionOrigin != null)
        {
            foreach (var templateId in character.TemplateIds)
            {
                var template = _content.GetTemplate(templateId);
                foreach (var buff in template.GetCompanionScalingPermabuffs(state.EffectiveMasterLevel))
                    buff.Apply(ctx);
            }
        }
    }

    private void ApplyRace(PermabuffContext ctx, RaceDefinition race)
    {
        var state = ctx.State;
        state.RaceId = race.Id;
        state.Type = race.Type;
        state.Size = race.Size;
        state.LevelAdjustment = race.LevelAdjustment;

        foreach (var subtype in race.Subtypes)
            state.Subtypes.Add(subtype);

        foreach (var (mode, speed) in race.Speeds)
            state.Speeds[mode] = speed;

        if (race.AbilityModifiers != null)
        {
            state.AbilityScores.STR += race.AbilityModifiers.STR;
            state.AbilityScores.DEX += race.AbilityModifiers.DEX;
            state.AbilityScores.CON += race.AbilityModifiers.CON;
            state.AbilityScores.INT += race.AbilityModifiers.INT;
            state.AbilityScores.WIS += race.AbilityModifiers.WIS;
            state.AbilityScores.CHA += race.AbilityModifiers.CHA;
        }

        foreach (var buff in race.RacialPermabuffs)
            buff.Apply(ctx);
    }

    private void ApplyTemplateCreation(PermabuffContext ctx, TemplateDriver template)
    {
        var state = ctx.State;
        state.TemplateIds.Add(template.Id);

        if (template.TypeOverride.HasValue)
            state.Type = template.TypeOverride.Value;

        foreach (var subtype in template.SubtypeAdditions)
            state.Subtypes.Add(subtype);

        if (template.AbilityModifiers != null)
        {
            state.AbilityScores.STR += template.AbilityModifiers.STR;
            state.AbilityScores.DEX += template.AbilityModifiers.DEX;
            state.AbilityScores.CON += template.AbilityModifiers.CON;
            state.AbilityScores.INT += template.AbilityModifiers.INT;
            state.AbilityScores.WIS += template.AbilityModifiers.WIS;
            state.AbilityScores.CHA += template.AbilityModifiers.CHA;
        }

        if (template.NaturalArmor.HasValue)
            state.NaturalArmor += template.NaturalArmor.Value;

        foreach (var (mode, speed) in template.SpeedModifiers)
        {
            if (state.Speeds.ContainsKey(mode))
                state.Speeds[mode] += speed;
            else
                state.Speeds[mode] = speed;
        }

        state.LevelAdjustment += template.LevelAdjustment;

        foreach (var attack in template.NaturalAttacks)
            state.NaturalAttacks.Add(attack);

        foreach (var buff in template.CreationPermabuffs)
            buff.Apply(ctx);
    }

    private void ApplyBaseAbilities(CharacterState state, AbilityScoreSet baseScores)
    {
        state.AbilityScores.STR += baseScores.STR;
        state.AbilityScores.DEX += baseScores.DEX;
        state.AbilityScores.CON += baseScores.CON;
        state.AbilityScores.INT += baseScores.INT;
        state.AbilityScores.WIS += baseScores.WIS;
        state.AbilityScores.CHA += baseScores.CHA;
    }

    private HashSet<string> ExpandParentSkills(List<string> classSkills)
    {
        var expanded = new HashSet<string>(classSkills);
        ExpandParentSkillsInPlace(expanded);
        return expanded;
    }

    private void ExpandParentSkillsInPlace(HashSet<string> classSkills)
    {
        foreach (var skill in _content.GetAllSkills())
        {
            if (skill.ParentSkill != null && classSkills.Contains(skill.ParentSkill))
                classSkills.Add(skill.Id);
        }
    }

    private void ApplyAbilityIncrease(CharacterState state, Ability ability)
    {
        var current = state.AbilityScores.GetScore(ability);
        state.AbilityScores.SetScore(ability, current + 1);
    }

    private static string? ChooseDomainOwner(CharacterState state, string? currentDriverId)
    {
        // Prefer the current tick's class if it has a pending slot, else any class with one.
        if (currentDriverId != null
            && state.PendingDomainSelections.GetValueOrDefault(currentDriverId) > 0)
            return currentDriverId;
        return state.PendingDomainSelections.FirstOrDefault(kv => kv.Value > 0).Key;
    }

    public List<FeatDefinition> GetAvailableFeats(CharacterState state, string? restriction = null)
    {
        var available = new List<FeatDefinition>();
        foreach (var feat in _content.GetAllFeats())
        {
            if (!feat.Selectable)
                continue;

            if (!feat.Repeatable && state.Feats.Contains(feat.Id))
                continue;

            if (restriction != null && !FeatMatchesRestriction(feat, restriction))
                continue;

            if (feat.Prerequisites.All(p => p.IsMet(state)))
                available.Add(feat);
        }
        return available;
    }

    /// <summary>
    /// Spontaneous casters (sorcerer, bard) know a fixed number of spells per level; prepared
    /// casters have <c>SpellsKnown == null</c> because a wizard's spellbook is unbounded, so
    /// they are skipped. Domain picks are granted rather than known and do not count.
    /// </summary>
    private static void CheckSpellsKnownLimits(CharacterState state)
    {
        foreach (var sc in state.Spellcasting.Values)
        {
            if (sc.SpellsKnown == null) continue;

            var selectedByLevel = sc.SelectedSpells
                .Where(s => !s.ClassId.StartsWith("domain:", StringComparison.Ordinal))
                .GroupBy(s => s.SpellLevel);

            foreach (var group in selectedByLevel)
            {
                var limit = sc.SpellsKnown.GetValueOrDefault(group.Key);
                var chosen = group.Count();
                if (chosen > limit)
                {
                    state.Warnings.Add(
                        $"HD {state.TotalHD}: {sc.ClassId} knows {chosen} level-{group.Key} spells, exceeding {limit}");
                }
            }
        }
    }

    private void ApplyTickChoices(PermabuffContext ctx, TickChoices choices)
    {
        var state = ctx.State;

        if (choices.FeatIds != null)
        {
            foreach (var featId in choices.FeatIds)
            {
                _content.TryGetFeat(featId, out var featDef);

                // A non-repeatable feat taken twice is illegal; GetAvailableFeats already
                // filters these out, so reaching here means the choice bypassed that list.
                if (featDef is { Repeatable: false } && state.Feats.Contains(featId))
                {
                    state.Warnings.Add(
                        $"HD {state.TotalHD}: duplicate feat '{featId}' — {featDef.Name} is not repeatable");
                    continue;
                }

                // Grant-only entries (class proficiencies, markers) are not choosable with a slot.
                if (featDef is { Selectable: false })
                {
                    state.Warnings.Add(
                        $"HD {state.TotalHD}: feat '{featId}' cannot be selected — {featDef.Name} is granted, not chosen");
                    continue;
                }

                // Resolve slot BEFORE mutating state: matching restricted-bonus slot first,
                // then fall back to unrestricted. If nothing fits, drop the feat entirely.
                FeatSlot? slot = null;
                if (featDef != null)
                {
                    slot = state.FeatSlots.FirstOrDefault(s =>
                        s.Restriction != null && FeatMatchesRestriction(featDef, s.Restriction));
                }
                slot ??= state.FeatSlots.FirstOrDefault(s => s.Restriction == null);

                if (slot == null)
                {
                    state.Warnings.Add(
                        $"HD {state.TotalHD}: feat '{featId}' dropped — no available feat slot");
                    continue;
                }

                state.FeatSlots.Remove(slot);
                state.Feats.Add(featId);

                if (featDef == null)
                {
                    state.Warnings.Add($"HD {state.TotalHD}: unknown feat '{featId}'");
                    continue;
                }

                foreach (var prereq in featDef.Prerequisites)
                {
                    if (!prereq.IsMet(state))
                        state.Warnings.Add($"HD {state.TotalHD}: prerequisite not met for feat {featDef.Name}: {prereq.Description}");
                }

                ctx.CurrentFeatId = featId;
                foreach (var buff in featDef.GrantedPermabuffs)
                    buff.Apply(ctx);
                ctx.CurrentFeatId = null;

                state.FeatTypeCounts[featDef.Type] =
                    state.FeatTypeCounts.GetValueOrDefault(featDef.Type) + 1;

                foreach (var tag in featDef.Tags)
                    state.FeatTagCounts[tag] = state.FeatTagCounts.GetValueOrDefault(tag) + 1;
            }
        }

        if (choices.SkillAllocations != null)
        {
            foreach (var alloc in choices.SkillAllocations)
            {
                // Unknown ids would otherwise silently consume skill points and materialise
                // a phantom skill on the sheet, so surface them the way unknown feats are.
                if (!_content.TryGetSkill(alloc.SkillId, out _))
                    state.Warnings.Add($"HD {state.TotalHD}: unknown skill '{alloc.SkillId}'");

                state.SkillHalfRanks.TryAdd(alloc.SkillId, 0);
                var newTotal = state.SkillHalfRanks[alloc.SkillId] + alloc.HalfRanks;

                if (newTotal > state.MaxHalfRanks)
                    state.Warnings.Add($"HD {state.TotalHD}: skill '{alloc.SkillId}' would have {newTotal / 2.0} ranks, exceeding max {state.MaxHalfRanks / 2.0}");

                state.SkillHalfRanks[alloc.SkillId] = newTotal;
                var cost = state.CurrentTickClassSkills.Contains(alloc.SkillId)
                    ? (alloc.HalfRanks + 1) / 2
                    : alloc.HalfRanks;
                state.UnspentSkillPoints -= cost;
            }

            if (state.UnspentSkillPoints < 0)
                state.Warnings.Add($"HD {state.TotalHD}: spent {-state.UnspentSkillPoints} more skill points than available");
        }

        // Domain selections — each pick consumes a pending slot from the granting class
        // (preferring the current tick's class) and grants its bonus slot only to that class.
        // NOTE: must process before SpellSelections so domain spell picks can resolve their owner class.
        if (choices.ClassFeatureChoices?.TryGetValue("domains", out var domainIds) == true && domainIds != null)
        {
            foreach (var domainId in domainIds)
            {
                if (state.Domains.Contains(domainId))
                {
                    state.Warnings.Add($"HD {state.TotalHD}: duplicate domain selection '{domainId}' ignored");
                    continue;
                }

                var ownerClassId = ChooseDomainOwner(state, ctx.CurrentDriverId);
                if (ownerClassId == null)
                {
                    state.Warnings.Add($"HD {state.TotalHD}: no pending domain selections for '{domainId}'");
                    continue;
                }

                if (!_content.TryGetDomain(domainId, out var domainDef) || domainDef == null)
                {
                    state.Warnings.Add($"HD {state.TotalHD}: unknown domain '{domainId}'");
                    continue;
                }

                state.Domains.Add(domainId);
                state.DomainOwners[domainId] = ownerClassId;
                state.PendingDomainSelections[ownerClassId]--;
                if (state.PendingDomainSelections[ownerClassId] == 0)
                    state.PendingDomainSelections.Remove(ownerClassId);

                // Apply granted permabuffs (granted powers)
                foreach (var buff in domainDef.GrantedPermabuffs)
                    buff.Apply(ctx);

                // Add +1 domain bonus slot per spell level 1+ on the OWNING class's spellcasting.
                // Orphan-owned domains (race/template grants with no caster class) skip this step —
                // granted powers fired above, but there's no spellcasting state to extend.
                if (ownerClassId != GrantDomainSelection.OrphanOwner
                    && state.Spellcasting.TryGetValue(ownerClassId, out var ownerSc))
                {
                    foreach (var lvl in ownerSc.SpellsPerDay.Keys.Where(l => l >= 1))
                    {
                        ownerSc.DomainBonusSlots.TryAdd(lvl, 0);
                        ownerSc.DomainBonusSlots[lvl]++;
                    }
                }
            }
        }

        if (choices.SpellSelections != null)
        {
            foreach (var selection in choices.SpellSelections)
            {
                if (string.IsNullOrWhiteSpace(selection.ClassId) || string.IsNullOrWhiteSpace(selection.SpellId))
                {
                    state.Warnings.Add($"HD {state.TotalHD}: incomplete spell selection ignored");
                    continue;
                }

                // Domain spell selection: route to the class that owns this domain.
                var routedClassId = selection.ClassId;
                if (selection.ClassId.StartsWith("domain:", StringComparison.Ordinal))
                {
                    if (!state.DomainOwners.TryGetValue(selection.ClassId, out var owner))
                    {
                        state.Warnings.Add(
                            $"HD {state.TotalHD}: domain spell '{selection.SpellId}' references unselected domain '{selection.ClassId}'");
                        continue;
                    }
                    if (owner == GrantDomainSelection.OrphanOwner)
                    {
                        state.Warnings.Add(
                            $"HD {state.TotalHD}: domain '{selection.ClassId}' has no spellcasting owner; spell '{selection.SpellId}' dropped");
                        continue;
                    }
                    routedClassId = owner;
                }

                if (!state.Spellcasting.TryGetValue(routedClassId, out var sc))
                {
                    state.Warnings.Add(
                        $"HD {state.TotalHD}: unknown spellcasting class '{routedClassId}' for spell '{selection.SpellId}'");
                    continue;
                }

                if (selection.SpellLevel < 0)
                {
                    state.Warnings.Add(
                        $"HD {state.TotalHD}: invalid spell level {selection.SpellLevel} for spell '{selection.SpellId}'");
                    continue;
                }

                if (selection.SpellLevel > sc.MaxSpellLevel)
                {
                    state.Warnings.Add(
                        $"HD {state.TotalHD}: spell '{selection.SpellId}' at level {selection.SpellLevel} exceeds max spell level {sc.MaxSpellLevel} for {selection.ClassId}");
                }

                if (!_content.TryGetSpell(selection.SpellId, out var spellDef) || spellDef == null)
                {
                    state.Warnings.Add($"HD {state.TotalHD}: unknown spell '{selection.SpellId}'");
                }
                else if (!selection.ClassId.StartsWith("domain:", StringComparison.Ordinal))
                {
                    // Domain picks come from the domain's own list, so only class picks are
                    // checked against the class spell list.
                    if (!spellDef.ClassLevels.TryGetValue(routedClassId, out var listLevel))
                    {
                        state.Warnings.Add(
                            $"HD {state.TotalHD}: spell '{selection.SpellId}' is not on the {routedClassId} spell list");
                    }
                    else if (listLevel != selection.SpellLevel)
                    {
                        state.Warnings.Add(
                            $"HD {state.TotalHD}: spell '{selection.SpellId}' is level {listLevel} for {routedClassId}, not {selection.SpellLevel}");
                    }
                }

                // Preserve original ClassId (which may be "domain:*") so the UI can render the source list.
                sc.SelectedSpells.Add(new SpellSelection
                {
                    ClassId = selection.ClassId,
                    SpellLevel = selection.SpellLevel,
                    SpellId = selection.SpellId
                });
            }

            CheckSpellsKnownLimits(state);
        }

        // Class feature selections (High Arcana, Loremaster Secrets, etc.)
        if (choices.ClassFeatureChoices != null)
        {
            foreach (var (featureType, selectedIds) in choices.ClassFeatureChoices)
            {
                if (featureType == "domains" || featureType == "advance_spellcasting")
                    continue;

                if (selectedIds == null) continue;

                if (!_content.TryGetClassFeature(featureType, out var featureDef) || featureDef == null)
                {
                    state.Warnings.Add($"HD {state.TotalHD}: unknown class feature type '{featureType}'");
                    continue;
                }

                foreach (var optionId in selectedIds)
                {
                    if (!state.PendingClassFeatureSelections.TryGetValue(featureType, out var pending) || pending <= 0)
                    {
                        state.Warnings.Add($"HD {state.TotalHD}: no pending '{featureType}' selections for '{optionId}'");
                        continue;
                    }

                    // Prevent duplicate selection within the same feature type
                    if (state.ClassFeatureSelections.TryGetValue(featureType, out var existing) && existing.Contains(optionId))
                    {
                        state.Warnings.Add($"HD {state.TotalHD}: duplicate '{featureType}' selection '{optionId}' ignored");
                        continue;
                    }

                    // Try static option first
                    var option = featureDef.Options.FirstOrDefault(o => o.Id == optionId);
                    if (option != null)
                    {
                        state.ClassFeatureSelections.TryAdd(featureType, new List<string>());
                        state.ClassFeatureSelections[featureType].Add(optionId);
                        state.PendingClassFeatureSelections[featureType]--;

                        foreach (var buff in option.GrantedPermabuffs)
                            buff.Apply(ctx);
                        continue;
                    }

                    // Fall through to dynamic source
                    if (featureDef.DynamicSource != null && ValidateDynamicSelection(state, featureDef.DynamicSource, optionId, featureType))
                    {
                        state.ClassFeatureSelections.TryAdd(featureType, new List<string>());
                        state.ClassFeatureSelections[featureType].Add(optionId);
                        state.PendingClassFeatureSelections[featureType]--;
                        // Dynamic selections do not grant permabuffs — the selection itself is the record.
                        continue;
                    }

                    state.Warnings.Add($"HD {state.TotalHD}: unknown class feature option '{featureType}/{optionId}'");
                }
            }
        }
    }

    private void EvaluateEquipment(PermabuffContext ctx, Character character)
    {
        var state = ctx.State;
        var pass = new EquipmentPass();
        ctx.EquipmentPass = pass;
        try
        {
            foreach (var item in character.Equipment)
            {
                EquipmentDefinition? def = null;
                if (!string.IsNullOrEmpty(item.ContentId))
                    ctx.Content?.TryGetEquipment(item.ContentId!, out def);

                // Apply granted permabuffs from content, then any inline permabuffs on the entry.
                if (def != null)
                {
                    foreach (var buff in def.GrantedPermabuffs)
                        buff.Apply(ctx);
                    pass.TotalWeightLbs += def.WeightLbs;
                    // Auto-derive weapon/armor profile from content when not already pushed by permabuffs.
                    if (def.Weapon != null && !pass.Weapons.Any(w => ReferenceEquals(w.Profile, def.Weapon)))
                    {
                        pass.Weapons.Add(new WeaponContribution
                        {
                            Profile = def.Weapon,
                            MainHand = item.MainHand,
                            TwoHanded = item.TwoHanded,
                            DisplayName = def.Name
                        });
                    }
                    if (def.Armor != null && !pass.Armors.Any(a => ReferenceEquals(a.Profile, def.Armor)))
                    {
                        pass.Armors.Add(new ArmorContribution
                        {
                            Profile = def.Armor,
                            AsShield = def.Category == EquipmentCategory.Shield
                        });
                    }
                }
                foreach (var buff in item.Permabuffs)
                    buff.Apply(ctx);
            }

            FinalizeEquipment(state, pass);
        }
        finally
        {
            ctx.EquipmentPass = null;
        }
    }

    private void FinalizeEquipment(CharacterState state, EquipmentPass pass)
    {
        // --- Ability score bonuses (typed; e.g., +4 enhancement STR from gauntlets) ---
        foreach (var (key, values) in pass.Contributions)
        {
            var (target, type) = key;
            var agg = BonusStack.Aggregate(type, values);
            switch (target)
            {
                case BonusTarget.AbilityStr: AddAbility(state, Ability.STR, agg); break;
                case BonusTarget.AbilityDex: AddAbility(state, Ability.DEX, agg); break;
                case BonusTarget.AbilityCon: AddAbility(state, Ability.CON, agg); break;
                case BonusTarget.AbilityInt: AddAbility(state, Ability.INT, agg); break;
                case BonusTarget.AbilityWis: AddAbility(state, Ability.WIS, agg); break;
                case BonusTarget.AbilityCha: AddAbility(state, Ability.CHA, agg); break;
                case BonusTarget.SaveFort: state.BaseSaves.Fort += agg; break;
                case BonusTarget.SaveRef: state.BaseSaves.Ref += agg; break;
                case BonusTarget.SaveWill: state.BaseSaves.Will += agg; break;
                case BonusTarget.AllSaves:
                    state.BaseSaves.Fort += agg;
                    state.BaseSaves.Ref += agg;
                    state.BaseSaves.Will += agg;
                    break;
                case BonusTarget.NaturalArmor: state.NaturalArmor += agg; break;
                case BonusTarget.SR: state.SpellResistance = (state.SpellResistance ?? 0) + agg; break;
            }
        }

        // --- Armor / Shield contributions: highest of each kind wins; dex cap = min(maxdex) ---
        var armorContribs = pass.Armors.Where(a => !a.AsShield && a.Profile.Kind != ArmorKind.Shield && a.Profile.Kind != ArmorKind.TowerShield).ToList();
        var shieldContribs = pass.Armors.Where(a => a.AsShield || a.Profile.Kind == ArmorKind.Shield || a.Profile.Kind == ArmorKind.TowerShield).ToList();

        var bestArmor = armorContribs.OrderByDescending(a => a.Profile.ArmorBonus).FirstOrDefault();
        var bestShield = shieldContribs.OrderByDescending(a => a.Profile.ArmorBonus).FirstOrDefault();

        state.AC.Components.Clear();
        if (bestArmor != null) state.AC.Components[BonusType.Armor] = bestArmor.Profile.ArmorBonus;
        if (bestShield != null) state.AC.Components[BonusType.Shield] = bestShield.Profile.ArmorBonus;

        // Aggregate AC typed contributions (deflection, dodge, natural enhancement, etc.).
        foreach (var (key, values) in pass.Contributions)
        {
            if (key.Target != BonusTarget.AC) continue;
            var agg = BonusStack.Aggregate(key.Type, values);
            if (agg == 0) continue;
            state.AC.Components[key.Type] =
                state.AC.Components.GetValueOrDefault(key.Type) +
                (BonusStack.IsStacking(key.Type) ? agg : Math.Max(0, agg - state.AC.Components.GetValueOrDefault(key.Type)));
        }

        // Carry natural armor that race/template applied to state.NaturalArmor.
        if (state.NaturalArmor > 0)
        {
            // Combine with any natural AC contributed via typed bonuses (running max for non-stacking).
            var prior = state.AC.Components.GetValueOrDefault(BonusType.Natural);
            state.AC.Components[BonusType.Natural] = Math.Max(prior, state.NaturalArmor);
        }

        // Dex cap: minimum MaxDex across worn armor + shield (null = uncapped).
        int? maxDex = null;
        foreach (var contrib in armorContribs.Concat(shieldContribs))
            if (contrib.Profile.MaxDex.HasValue)
                maxDex = maxDex.HasValue ? Math.Min(maxDex.Value, contrib.Profile.MaxDex.Value) : contrib.Profile.MaxDex.Value;

        var dexMod = AbilityScoreSet.Modifier(state.AbilityScores.DEX);
        var dexContrib = maxDex.HasValue ? Math.Min(dexMod, maxDex.Value) : dexMod;
        state.AC.MaxDexCap = maxDex;
        state.AC.DexContribution = dexContrib;

        var componentTotal = state.AC.Components.Values.Sum();
        state.AC.Total = 10 + componentTotal + dexContrib;
        // Touch AC excludes Armor, Shield, Natural, NaturalEnhancement.
        var touchExcluded = new HashSet<BonusType> { BonusType.Armor, BonusType.Shield, BonusType.Natural, BonusType.NaturalEnhancement };
        var touchSum = state.AC.Components.Where(kv => !touchExcluded.Contains(kv.Key)).Sum(kv => kv.Value);
        state.AC.Touch = 10 + touchSum + dexContrib;
        // Flat-footed AC excludes Dex and Dodge.
        var flatComponents = state.AC.Components.Where(kv => kv.Key != BonusType.Dodge).Sum(kv => kv.Value);
        state.AC.FlatFooted = 10 + flatComponents;

        // --- Attack lines from equipped weapons ---
        FinalizeAttackLines(state, pass);

        // --- Encumbrance + speed reduction ---
        FinalizeEncumbrance(state, pass, armorContribs, shieldContribs);
    }

    private static void AddAbility(CharacterState state, Ability ability, int value)
    {
        var current = state.AbilityScores.GetScore(ability);
        state.AbilityScores.SetScore(ability, current + value);
    }

    private void FinalizeAttackLines(CharacterState state, EquipmentPass pass)
    {
        state.AttackLines.Clear();
        if (pass.Weapons.Count == 0) return;

        var mainHand = pass.Weapons.FirstOrDefault(w => w.MainHand) ?? pass.Weapons[0];
        var offHand = pass.Weapons.FirstOrDefault(w => !w.MainHand);
        var twoWeaponFighting = state.Feats.Contains("two_weapon_fighting");

        var bab = state.EffectiveBAB;
        var strMod = AbilityScoreSet.Modifier(state.AbilityScores.STR);
        var dexMod = AbilityScoreSet.Modifier(state.AbilityScores.DEX);

        // Generic typed attack bonus aggregation (excluding the weapon's own enhancement).
        int typedAttackBonus = 0;
        foreach (var (key, values) in pass.Contributions)
        {
            if (key.Target != BonusTarget.Attack) continue;
            typedAttackBonus += BonusStack.Aggregate(key.Type, values);
        }

        // SRD TWF penalties:
        //   no feat, heavy off-hand: -6 / -10
        //   no feat, light off-hand: -4 / -8     (light off-hand reduces both by 2)
        //   TWF feat, heavy off-hand: -4 / -4    (feat: primary -2, off -6 less)
        //   TWF feat, light off-hand: -2 / -2
        int mainPenalty = 0, offPenalty = 0;
        if (offHand != null)
        {
            var light = offHand.Profile.Light;
            if (twoWeaponFighting && light) { mainPenalty = -2; offPenalty = -2; }
            else if (twoWeaponFighting) { mainPenalty = -4; offPenalty = -4; }
            else if (light) { mainPenalty = -4; offPenalty = -8; }
            else { mainPenalty = -6; offPenalty = -10; }
        }

        state.AttackLines.Add(BuildLine(mainHand, bab, strMod, dexMod, typedAttackBonus,
            attackPenalty: mainPenalty,
            isOffHand: false));

        if (offHand != null)
        {
            state.AttackLines.Add(BuildLine(offHand, bab, strMod, dexMod, typedAttackBonus,
                attackPenalty: offPenalty,
                isOffHand: true));
        }
    }

    private static AttackLine BuildLine(WeaponContribution w, int bab, int strMod, int dexMod, int typedAttackBonus, int attackPenalty, bool isOffHand)
    {
        var profile = w.Profile;
        var abilityMod = profile.Ranged && !profile.Thrown ? dexMod : strMod;
        var damageMod = profile.Ranged && !profile.Thrown
            ? 0
            : (isOffHand ? FloorDivBy2(strMod) : (w.TwoHanded ? (strMod * 3) / 2 : strMod));
        var attackBase = bab + abilityMod + w.EnhancementBonus + typedAttackBonus + attackPenalty;

        var iterations = isOffHand ? 1 : IterativeCount(bab);
        var bonuses = new List<int>();
        for (int i = 0; i < iterations; i++)
            bonuses.Add(attackBase - 5 * i);

        var damageBonus = damageMod + w.EnhancementBonus;
        var damageStr = damageBonus == 0
            ? profile.Damage
            : (damageBonus > 0 ? $"{profile.Damage}+{damageBonus}" : $"{profile.Damage}{damageBonus}");
        var critStr = profile.CritRangeLow >= 20
            ? $"x{profile.CritMultiplier}"
            : $"{profile.CritRangeLow}-20/x{profile.CritMultiplier}";

        return new AttackLine
        {
            Name = string.IsNullOrEmpty(w.DisplayName) ? (profile.Ranged ? "Ranged" : "Melee") : w.DisplayName,
            Bonuses = bonuses,
            Damage = damageStr,
            Crit = critStr,
            IsOffHand = isOffHand,
            IsRanged = profile.Ranged
        };
    }

    private static int IterativeCount(int bab)
    {
        if (bab >= 16) return 4;
        if (bab >= 11) return 3;
        if (bab >= 6) return 2;
        return 1;
    }

    // Negative-safe halving (toward -infinity for off-hand STR-half damage).
    private static int FloorDivBy2(int v) => v >= 0 ? v / 2 : -((-v + 1) / 2);

    private void FinalizeEncumbrance(CharacterState state, EquipmentPass pass, List<ArmorContribution> armors, List<ArmorContribution> shields)
    {
        var (light, medium, heavy) = _rules.GetCarryingCapacity(state.AbilityScores.STR);
        state.Encumbrance.LightMax = light;
        state.Encumbrance.MediumMax = medium;
        state.Encumbrance.HeavyMax = heavy;
        state.Encumbrance.TotalWeightLbs = pass.TotalWeightLbs;
        state.Encumbrance.Load = pass.TotalWeightLbs <= light ? LoadCategory.Light
            : pass.TotalWeightLbs <= medium ? LoadCategory.Medium
            : pass.TotalWeightLbs <= heavy ? LoadCategory.Heavy
            : LoadCategory.OverLoad;

        // Speed reduction: medium/heavy armor or medium/heavy load reduces base 30 → 20.
        var landSpeed = state.Speeds.GetValueOrDefault(MovementMode.Land);
        if (landSpeed <= 0) return;

        var armorReducesSpeed = armors.Concat(shields).Any(a =>
            a.Profile.Kind == ArmorKind.Medium || a.Profile.Kind == ArmorKind.Heavy ||
            a.Profile.Kind == ArmorKind.TowerShield);
        var loadReducesSpeed = state.Encumbrance.Load >= LoadCategory.Medium;

        if (armorReducesSpeed || loadReducesSpeed)
        {
            // Use the worst armor's speed-30 reduction if armor is the cause; otherwise standard medium-load reduction.
            var worstArmor = armors.Where(a => a.Profile.Kind == ArmorKind.Medium || a.Profile.Kind == ArmorKind.Heavy)
                .OrderBy(a => a.Profile.Speed30)
                .FirstOrDefault();
            var reduced = worstArmor != null && landSpeed == 30 ? worstArmor.Profile.Speed30 :
                          worstArmor != null && landSpeed == 20 ? worstArmor.Profile.Speed20 :
                          (landSpeed == 30 ? 20 : (landSpeed * 2 / 3 / 5) * 5);
            state.Speeds[MovementMode.Land] = reduced;
        }
    }

    /// <summary>
    /// Fighter-bonus eligibility is orthogonal to <see cref="FeatType"/>: Power Attack is a
    /// general feat *and* a fighter bonus feat, which a single type enum cannot express. The
    /// "fighter_bonus" tag carries that second axis, so a feat qualifies via either the
    /// dedicated type or the tag — not merely by being General.
    /// </summary>
    public static bool FeatMatchesRestriction(FeatDefinition? feat, string restriction) => restriction switch
    {
        "fighter_bonus" => feat != null
            && (feat.Type == FeatType.FighterBonus || feat.Tags.Contains(FighterBonusTag)),
        _ => false
    };

    public const string FighterBonusTag = "fighter_bonus";

    private bool ValidateDynamicSelection(CharacterState state, DynamicOptionSource source, string optionId, string featureType)
    {
        if (source.Kind == "feat")
        {
            if (!state.Feats.Contains(optionId))
            {
                state.Warnings.Add($"HD {state.TotalHD}: '{featureType}' selection '{optionId}' — character does not have that feat");
                return false;
            }

            if (!_content.TryGetFeat(optionId, out var featDef) || featDef == null)
            {
                state.Warnings.Add($"HD {state.TotalHD}: '{featureType}' selection '{optionId}' — feat not found in content");
                return false;
            }

            if (source.FeatType != null)
            {
                if (!Enum.TryParse<FeatType>(source.FeatType, ignoreCase: true, out var requiredType))
                {
                    state.Warnings.Add($"HD {state.TotalHD}: '{featureType}' has invalid featType '{source.FeatType}'");
                    return false;
                }
                if (featDef.Type != requiredType)
                {
                    state.Warnings.Add($"HD {state.TotalHD}: '{featureType}' selection '{optionId}' — feat is not of type {source.FeatType}");
                    return false;
                }
            }

            if (source.Tag != null && !featDef.Tags.Contains(source.Tag))
            {
                state.Warnings.Add($"HD {state.TotalHD}: '{featureType}' selection '{optionId}' — feat lacks tag '{source.Tag}'");
                return false;
            }

            return true;
        }

        state.Warnings.Add($"HD {state.TotalHD}: '{featureType}' has unknown dynamicSource kind '{source.Kind}'");
        return false;
    }
}
