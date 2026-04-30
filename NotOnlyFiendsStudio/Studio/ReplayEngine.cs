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

        // 5. Apply equipment (stub)
        foreach (var item in character.Equipment)
            ApplyEquipment(ctx, item);

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
            if (!feat.Repeatable && state.Feats.Contains(feat.Id))
                continue;

            if (restriction == "fighter_bonus" &&
                feat.Type != FeatType.FighterBonus && feat.Type != FeatType.General)
                continue;

            if (feat.Prerequisites.All(p => p.IsMet(state)))
                available.Add(feat);
        }
        return available;
    }

    private void ApplyTickChoices(PermabuffContext ctx, TickChoices choices)
    {
        var state = ctx.State;

        if (choices.FeatIds != null)
        {
            foreach (var featId in choices.FeatIds)
            {
                _content.TryGetFeat(featId, out var featDef);

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
                state.SkillRanks.TryAdd(alloc.SkillId, 0);
                var newTotal = state.SkillRanks[alloc.SkillId] + alloc.HalfRanks;

                if (newTotal > state.MaxHalfRanks)
                    state.Warnings.Add($"HD {state.TotalHD}: skill '{alloc.SkillId}' would have {newTotal / 2.0} ranks, exceeding max {state.MaxHalfRanks / 2.0}");

                state.SkillRanks[alloc.SkillId] = newTotal;
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

                // Preserve original ClassId (which may be "domain:*") so the UI can render the source list.
                sc.SelectedSpells.Add(new SpellSelection
                {
                    ClassId = selection.ClassId,
                    SpellLevel = selection.SpellLevel,
                    SpellId = selection.SpellId
                });
            }
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

    private void ApplyEquipment(PermabuffContext ctx, EquipmentEntry item)
    {
        foreach (var buff in item.Permabuffs)
            buff.Apply(ctx);
    }

    private static bool FeatMatchesRestriction(FeatDefinition? feat, string restriction) => restriction switch
    {
        "fighter_bonus" => feat?.Type is FeatType.FighterBonus or FeatType.General,
        _ => false
    };

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
