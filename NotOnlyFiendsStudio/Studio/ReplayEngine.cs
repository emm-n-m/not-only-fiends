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
        state.Deity = string.IsNullOrWhiteSpace(character.Deity) ? null : character.Deity;

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
            foreach (var prereq in template.ApplicabilityPrerequisites)
            {
                if (!prereq.IsMet(state))
                    state.Warnings.Add(new Warning { TickIndex = 0, Message = $"applicability prerequisite not met for template {template.Name}: {prereq.Description}" });
            }
            ApplyTemplateCreation(ctx, template);
        }

        // Derived movement is permanent character state. Resolve it after all template
        // transformations and before the post-tick armor/load speed pass.
        ResolveDerivedSpeeds(state, character.TemplateIds);

        // 3. Apply base ability scores (added to racial/template modifiers)
        ApplyBaseAbilities(state, character.BaseAbilityScores);

        // 3a. Spend bonus-language picks — needs starting Int from step 3, and must land before
        //     the tick loop so a 1st-level class can gate on the result.
        ApplyBonusLanguages(ctx, character, race);

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
            RefreshDynamicSLAs(state);
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
            ctx.CurrentDriverKind = driver is HDDriver contextDriver ? contextDriver.Kind : null;
            ctx.CurrentRacialHitDieMaximum = ctx.CurrentDriverKind == DriverKind.RacialHD
                ? character.TemplateIds
                    .Select(id => _content.GetTemplate(id).RacialHitDieMaximum)
                    .Where(max => max.HasValue)
                    .Select(max => max!.Value)
                    .Cast<int?>()
                    .Min()
                : null;
            if (driver is HDDriver hdDriver && hdDriver.MaxLevel.HasValue
                && driverLevel > hdDriver.MaxLevel.Value
                && state.TotalHD <= _rules.EpicThreshold)
                state.Warnings.Add(new Warning { TickIndex = state.TotalHD, Message = $"{driver.Name} level {driverLevel} exceeds max level {hdDriver.MaxLevel.Value}" });
            foreach (var prereq in driver.Prerequisites)
            {
                if (!prereq.IsMet(state))
                    state.Warnings.Add(new Warning { TickIndex = state.TotalHD, Message = $"prerequisite not met for {driver.Name}: {prereq.Description}" });
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
        ctx.CurrentDriverKind = null;
        ctx.CurrentRacialHitDieMaximum = null;

        // 5. Apply equipment as a structured post-tick pass:
        //    resolve content → apply permabuffs (typed contributions collected in
        //    ctx.EquipmentPass) → finalize AC, attack lines, encumbrance.
        EvaluateEquipment(ctx, character);

        // Constitution changes are retroactive to every existing hit die, including
        // level increases, permanent events, and worn equipment.
        FinalizeHitPoints(state);

        // 5a. Validate template prerequisites against the finished state. Acquired templates
        // (e.g. Unseelie Champion's ranger-level gate) reference class levels that do not
        // exist at creation time, and ability requirements should see post-equipment scores,
        // so this cannot run inside ApplyTemplateCreation.
        foreach (var templateId in character.TemplateIds)
        {
            var template = _content.GetTemplate(templateId);
            foreach (var prereq in template.Prerequisites)
            {
                if (!prereq.IsMet(state))
                    state.Warnings.Add(new Warning { TickIndex = 0, Message = $"prerequisite not met for template {template.Name}: {prereq.Description}" });
            }
        }

        // 6. Tail pass — companion / leadership finalization.
        FinalizeCompanionAndLeadership(ctx, character);

        // 7. Seed racial spellcasting (e.g. Aranea Sorc N, Ghaele Cleric 14) for
        // characters that never take a level of the target class. When class levels
        // ARE taken, the class tick's UpdateSpellcasting already seeded state.Spellcasting
        // using featureLevel = actualLevel + EffectiveLevelRule (registered at race
        // creation), so this step skips the class and leaves the stacked value in place.
        FinalizeRacialSpellcasting(ctx, race);

        // 8. Tail pass — skill synergies and skill totals. Must run last: it reads the final
        // ability scores, which equipment (step 5) can still move.
        FinalizeSkills(state);

        // 9. Tail pass — specialist wizard schools. Must be a tail pass, not per-tick: spell
        // selections are applied before class feature choices within a tick, so a wizard choosing
        // its specialty and its first spells at 1st level would otherwise be checked against
        // schools it had not yet picked. Spells per day are also final only now.
        FinalizeWizardSchools(state);

        // 10. Tail pass — domain-derived spell-like abilities. Must be a tail pass: the granting
        // template applies at creation, but the domains it reads are chosen during the tick loop.
        FinalizeDomainSpellLikeAbilities(ctx);

        return state;
    }

    private static void RefreshDynamicSLAs(CharacterState state)
    {
        foreach (var sla in state.SLAs.Where(s => s.CasterLevelTracksTotalHD))
            sla.CasterLevel = state.TotalHD;
    }

    private void FinalizeHitPoints(CharacterState state)
    {
        var conMod = AbilityScoreSet.Modifier(state.AbilityScores.CON);
        state.HP = state.HitDice.Select((hitDie, index) =>
        {
            var roll = _rules.FirstHDMaxHP && index == 0
                ? hitDie.DieSize
                : hitDie.DieSize / 2 + 1;
            return Math.Max(1, roll + conMod);
        }).Sum();
    }

    /// <summary>
    /// Fulfils <see cref="GrantDomainSpellLikeAbilities"/> requests, turning each chosen domain's
    /// bonus spells into SLAs at the tier its spell level earns.
    /// </summary>
    private void FinalizeDomainSpellLikeAbilities(PermabuffContext ctx)
    {
        var state = ctx.State;
        if (state.PendingDomainSLAGrants.Count == 0) return;

        var saveMod = AbilityScoreSet.Modifier(state.AbilityScores.GetScore(
            state.PendingDomainSLAGrants[0].SaveAbility));

        // A spell can sit in two of the character's domains; grant it once, at its best tier.
        var granted = new Dictionary<string, (int SpellLevel, string Uses, string DomainName)>();

        foreach (var grant in state.PendingDomainSLAGrants)
        {
            foreach (var domainId in state.Domains)
            {
                if (!_content.TryGetDomain(domainId, out var domain) || domain is null) continue;

                foreach (var (spellLevel, spellId) in domain.BonusSpells)
                {
                    var uses = grant.UsesFor(spellLevel);
                    if (uses is null) continue;
                    if (granted.TryGetValue(spellId, out var existing) && existing.SpellLevel <= spellLevel)
                        continue;
                    granted[spellId] = (spellLevel, uses, domain.Name);
                }
            }
        }

        foreach (var (spellId, (spellLevel, uses, domainName)) in granted.OrderBy(g => g.Value.SpellLevel)
                     .ThenBy(g => g.Key, StringComparer.Ordinal))
        {
            var name = _content.TryGetSpell(spellId, out var spell) && spell is not null
                ? spell.Name
                : spellId;

            state.SLAs.Add(new SLA
            {
                Id = $"domain_sla_{spellId}",
                Name = name,
                Description = $"{domainName} domain spell-like ability (level {spellLevel}).",
                UsesPerDay = uses,
                CasterLevel = state.TotalHD,
                SaveDC = 10 + spellLevel + saveMod
            });
        }

        state.PendingDomainSLAGrants.Clear();
    }

    /// <summary>
    /// Grants a specialist wizard's bonus spell slots and validates its school choices against the
    /// spells it has taken.
    ///
    /// Selection is never blocked — as everywhere else in this engine, illegal input produces a
    /// warning and the build continues — but the builder does not offer prohibited-school spells
    /// in the first place, so reaching these warnings means the character was assembled through
    /// the API or by hand.
    /// </summary>
    private void FinalizeWizardSchools(CharacterState state)
    {
        var specialty = WizardSchools.Specialty(state);
        var prohibited = WizardSchools.ProhibitedSchools(state);

        // SRD: "A specialist wizard can prepare one additional spell of her specialty school per
        // spell level each day." Applied to every level she can cast.
        if (specialty != null)
        {
            foreach (var sc in state.Spellcasting.Values)
            {
                if (sc.Acquisition != SpellAcquisition.Spellbook)
                    continue;

                sc.SpecialtyBonusSlots.Clear();
                foreach (var level in sc.SpellsPerDay.Keys)
                    sc.SpecialtyBonusSlots[level] = 1;
            }
        }

        // SRD: "A wizard can never give up divination to fulfill this requirement."
        if (prohibited.Contains(WizardSchools.Divination))
            state.Warnings.Add(new Warning
            {
                TickIndex = state.TotalHD,
                Message = "wizard gives up divination, which can never be a prohibited school",
            });

        if (specialty != null && prohibited.Contains(specialty))
            state.Warnings.Add(new Warning
            {
                TickIndex = state.TotalHD,
                Message = $"wizard specializes in {specialty} but also gives it up as a prohibited school",
            });

        var required = WizardSchools.RequiredProhibitedCount(specialty);
        if (prohibited.Count != required)
            state.Warnings.Add(new Warning
            {
                TickIndex = state.TotalHD,
                Message = specialty == null
                    ? $"wizard has no specialty school but gives up {prohibited.Count} school(s); a universalist gives up none"
                    : $"wizard specializing in {specialty} gives up {prohibited.Count} school(s), but must give up {required}",
            });

        if (prohibited.Count == 0)
            return;

        foreach (var sc in state.Spellcasting.Values)
        {
            foreach (var selection in sc.SelectedSpells)
            {
                if (!_content.TryGetSpell(selection.SpellId, out var spellDef) || spellDef == null)
                    continue;

                if (WizardSchools.IsProhibited(state, spellDef.School))
                    state.Warnings.Add(new Warning
                    {
                        TickIndex = state.TotalHD,
                        Message = $"spell '{selection.SpellId}' is {spellDef.School}, a school this wizard has given up",
                    });
            }
        }
    }

    /// <summary>
    /// Computes skill synergies and skill totals once ranks, bonuses and ability scores are final.
    ///
    /// A tail pass rather than per-tick for the same reason <see cref="FinalizeRacialSpellcasting"/>
    /// is one: a character who crosses 5 ranks at 7th level would otherwise get an order-dependent
    /// answer depending on when the synergy happened to be evaluated.
    ///
    /// Synergies key off <em>ranks</em>, not totals, so they cannot chain — one pass is exact and
    /// no iteration to a fixed point is needed. Multiple synergies into the same skill do stack:
    /// Diplomacy legitimately receives three separate +2s (Bluff, Sense Motive, Knowledge
    /// (nobility)), so they are summed rather than deduplicated.
    /// </summary>
    private void FinalizeSkills(CharacterState state)
    {
        state.SkillSynergyBonuses.Clear();
        state.SkillTotals.Clear();

        // Whole ranks: half-ranks halved, truncating. The half-rank representation is how
        // cross-class ranks are stored (CharacterState.SkillHalfRanks).
        int WholeRanks(string skillId) => state.SkillHalfRanks.GetValueOrDefault(skillId) / 2;

        foreach (var skill in _content.GetAllSkills())
        {
            if (skill.Synergies.Count == 0 || WholeRanks(skill.Id) < SynergyRankThreshold)
                continue;

            foreach (var synergy in skill.Synergies)
            {
                state.SkillSynergyBonuses.TryAdd(synergy.TargetSkillId, 0);
                state.SkillSynergyBonuses[synergy.TargetSkillId] += synergy.Bonus;
            }
        }

        // Total every skill the character has any reason to show: ranks, a granted bonus, or a
        // synergy. Skills with none of the three are left out, matching what the sheet lists today.
        var skillIds = new HashSet<string>(state.SkillHalfRanks.Keys);
        skillIds.UnionWith(state.SkillBonuses.Keys);
        skillIds.UnionWith(state.SkillSynergyBonuses.Keys);

        foreach (var skillId in skillIds)
        {
            var abilityMod = 0;
            if (_content.TryGetSkill(skillId, out var def) && def != null
                && Enum.TryParse<Ability>(def.KeyAbility, ignoreCase: true, out var keyAbility))
                abilityMod = AbilityScoreSet.Modifier(state.AbilityScores.GetScore(keyAbility));

            state.SkillTotals[skillId] = WholeRanks(skillId)
                                         + abilityMod
                                         + state.SkillBonuses.GetValueOrDefault(skillId)
                                         + state.SkillSynergyBonuses.GetValueOrDefault(skillId);
        }
    }

    /// <summary>Whole ranks in the source skill needed before a synergy applies (SRD: 5).</summary>
    private const int SynergyRankThreshold = 5;

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
                ctx.State.Warnings.Add(new Warning { TickIndex = ctx.State.TotalHD, Message = $"GrantRacialSpellcasting: class driver '{buff.ClassId}' not in content" });
                continue;
            }
            if (hd?.Spellcasting is null)
            {
                ctx.State.Warnings.Add(new Warning { TickIndex = ctx.State.TotalHD, Message = $"GrantRacialSpellcasting: class driver '{buff.ClassId}' has no spellcasting progression" });
                continue;
            }

            if (!hd.Spellcasting.SpellsPerDay.TryGetValue(level, out var spd))
            {
                ctx.State.Warnings.Add(new Warning { TickIndex = ctx.State.TotalHD, Message = $"GrantRacialSpellcasting: '{buff.ClassId}' progression has no level {level} entry" });
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
        if (state.Feats.Contains("feat:leadership"))
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
        state.IsLiving = race.IsLiving;
        state.IsCorporeal = race.IsCorporeal;
        // Null LA (source never priced this as a PC race) contributes 0 to ECL.
        state.LevelAdjustment = race.LevelAdjustment ?? 0;

        foreach (var subtype in race.Subtypes)
            state.Subtypes.Add(subtype);

        foreach (var (mode, speed) in race.Speeds)
        {
            state.BaseSpeeds[mode] = speed;
            state.Speeds[mode] = speed;
        }

        if (race.AbilityModifiers != null)
        {
            state.AbilityScores.STR += race.AbilityModifiers.STR;
            state.AbilityScores.DEX += race.AbilityModifiers.DEX;
            state.AbilityScores.CON += race.AbilityModifiers.CON;
            state.AbilityScores.INT += race.AbilityModifiers.INT;
            state.AbilityScores.WIS += race.AbilityModifiers.WIS;
            state.AbilityScores.CHA += race.AbilityModifiers.CHA;
        }

        foreach (var attack in race.NaturalAttacks)
            state.NaturalAttacks.Add(attack);

        foreach (var language in race.AutomaticLanguages)
            state.Languages.Add(language);

        foreach (var buff in race.RacialPermabuffs)
            buff.Apply(ctx);
    }

    /// <summary>
    /// Spends the character's stored bonus-language picks. Runs after base ability scores are in
    /// so the allowance reflects starting Intelligence, and before the tick loop so a class taken
    /// at 1st level can see the result (<c>class:dragon_disciple</c>'s Draconic requirement).
    /// </summary>
    private void ApplyBonusLanguages(PermabuffContext ctx, Character character, RaceDefinition? race)
    {
        var state = ctx.State;
        if (character.BonusLanguageIds.Count == 0) return;

        var allowance = LanguageCatalog.Allowance(state.AbilityScores.INT);
        var offered = LanguageCatalog.OfferedBonusLanguages(race, _content.GetAllLanguages())
            .Select(l => l.Id)
            .ToHashSet(StringComparer.Ordinal);

        var spent = 0;
        foreach (var languageId in character.BonusLanguageIds.Distinct(StringComparer.Ordinal))
        {
            if (state.Languages.Contains(languageId))
            {
                state.Warnings.Add(new Warning
                {
                    Message = $"Bonus language '{languageId}' is already known — pick not spent"
                });
                continue;
            }

            if (offered.Count > 0 && !offered.Contains(languageId))
            {
                state.Warnings.Add(new Warning
                {
                    Message = $"Bonus language '{languageId}' is not offered by "
                        + $"{race?.Name ?? "this race"} — skipped"
                });
                continue;
            }

            if (spent >= allowance)
            {
                state.Warnings.Add(new Warning
                {
                    Message = $"Bonus language '{languageId}' exceeds the {allowance} allowed by "
                        + $"starting Intelligence {state.AbilityScores.INT} — skipped"
                });
                continue;
            }

            state.Languages.Add(languageId);
            spent++;
        }
    }

    private void ApplyTemplateCreation(PermabuffContext ctx, TemplateDriver template)
    {
        var state = ctx.State;
        state.TemplateIds.Add(template.Id);

        var baseType = state.Type;

        if (template.TypeOverride.HasValue)
            state.Type = template.TypeOverride.Value;
        else if (template.TypeOverridesByBaseType.TryGetValue(baseType, out var conditionalType))
            state.Type = conditionalType;

        state.RacialHitDieSizeAdjustment += template.RacialHitDieSizeAdjustment;

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
            if (state.BaseSpeeds.ContainsKey(mode))
                state.BaseSpeeds[mode] += speed;
            else
                state.BaseSpeeds[mode] = speed;
            state.Speeds[mode] = state.BaseSpeeds[mode];
        }

        state.LevelAdjustment += template.LevelAdjustment;

        foreach (var attack in template.NaturalAttacks)
            state.NaturalAttacks.Add(attack);

        foreach (var buff in template.CreationPermabuffs)
            buff.Apply(ctx);
    }

    private void ResolveDerivedSpeeds(CharacterState state, IEnumerable<string> templateIds)
    {
        foreach (var templateId in templateIds)
        {
            var template = _content.GetTemplate(templateId);
            foreach (var rule in template.DerivedSpeedRules)
            {
                if (rule.MinimumSize.HasValue && state.Size < rule.MinimumSize.Value)
                    continue;
                var source = state.BaseSpeeds.GetValueOrDefault(rule.SourceMode);
                if (source <= 0) continue;
                var derived = source * rule.Multiplier;
                if (rule.Maximum.HasValue) derived = Math.Min(derived, rule.Maximum.Value);
                if (rule.PreserveBetterExisting && state.BaseSpeeds.GetValueOrDefault(rule.Mode) > derived)
                    continue;
                state.BaseSpeeds[rule.Mode] = derived;
                state.Speeds[rule.Mode] = derived;
            }
        }
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
    /// How many spells a wizard of <paramref name="wizardLevel"/> may have written into a
    /// spellbook, excluding 0-level spells (every one of those is in the book from 1st level).
    ///
    /// SRD: three 1st-level spells at 1st level, plus one more per point of Intelligence
    /// <em>bonus</em> — so a penalty does not reduce the three — then two of any castable level at
    /// each new wizard level.
    ///
    /// Counted against actual wizard class levels, not caster level: a prestige class that
    /// advances spellcasting grants caster level and spells per day, not new spellbook spells.
    /// Spells found on scrolls or copied from another wizard's book are not modelled and are not
    /// counted against this.
    /// </summary>
    public static int SpellbookSpellsAllowed(int wizardLevel, int intelligenceModifier) =>
        wizardLevel < 1 ? 0 : 3 + Math.Max(0, intelligenceModifier) + 2 * (wizardLevel - 1);

    /// <summary>
    /// A wizard's spellbook is bounded, unlike a cleric's or druid's list — it just isn't bounded
    /// per spell level, so it needs its own check rather than a <see cref="CheckSpellsKnownLimits"/>
    /// style per-level one.
    /// </summary>
    private static void CheckSpellbookLimits(CharacterState state)
    {
        foreach (var sc in state.Spellcasting.Values)
        {
            if (sc.Acquisition != SpellAcquisition.Spellbook) continue;

            // 0-level spells are automatic, and domain picks are granted rather than chosen.
            var chosen = sc.SelectedSpells
                .Where(s => s.SpellLevel > 0
                            && !s.ClassId.StartsWith("domain:", StringComparison.Ordinal))
                .Count();

            var classLevel = state.ClassLevels.GetValueOrDefault(sc.ClassId);
            var limit = SpellbookSpellsAllowed(
                classLevel, AbilityScoreSet.Modifier(state.AbilityScores.GetScore(sc.CastingStat)));

            if (chosen > limit)
                state.Warnings.Add(new Warning
                {
                    TickIndex = state.TotalHD,
                    Message = $"{sc.ClassId} spellbook holds {chosen} spells of 1st level or higher, exceeding {limit}",
                });
        }
    }

    /// <summary>
    /// Spontaneous casters (sorcerer, bard) know a fixed number of spells per level. Full-list
    /// casters (cleric, druid, paladin, ranger) have their whole list available and are skipped;
    /// the wizard's spellbook is bounded differently and is handled by
    /// <see cref="CheckSpellbookLimits"/>. Domain picks are granted rather than known and do not
    /// count.
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
                    state.Warnings.Add(new Warning { TickIndex = state.TotalHD, Message = $"{sc.ClassId} knows {chosen} level-{group.Key} spells, exceeding {limit}" });
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
                    state.Warnings.Add(new Warning { TickIndex = state.TotalHD, Message = $"duplicate feat '{featId}' — {featDef.Name} is not repeatable" });
                    continue;
                }

                // Grant-only entries (class proficiencies, markers) are not choosable with a slot.
                if (featDef is { Selectable: false })
                {
                    state.Warnings.Add(new Warning { TickIndex = state.TotalHD, Message = $"feat '{featId}' cannot be selected — {featDef.Name} is granted, not chosen" });
                    continue;
                }

                if (featDef?.SelectionRequired is { } selectionKind)
                {
                    var prefix = featDef.Id + "_";
                    var selection = featId.StartsWith(prefix, StringComparison.Ordinal) ? featId[prefix.Length..] : null;
                    var valid = selectionKind switch
                    {
                        "special_attack" => selection != null && state.SpecialAttacks.Any(a => a.Id == selection),
                        "spell_like_ability" => selection != null && state.SLAs.Any(s => s.Id == selection),
                        // Existing feat selections are stored as display-friendly suffixes and
                        // several legacy callers submit the base ID. Keep those save formats
                        // valid; only the new typed target sources are enforceable here.
                        _ => true
                    };
                    if (!valid)
                    {
                        state.Warnings.Add(new Warning { TickIndex = state.TotalHD, Message = $"feat '{featId}' requires a valid {selectionKind} selection" });
                        continue;
                    }
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
                    state.Warnings.Add(new Warning { TickIndex = state.TotalHD, Message = $"feat '{featId}' dropped — no available feat slot" });
                    continue;
                }

                state.FeatSlots.Remove(slot);
                state.Feats.Add(featId);

                if (featDef == null)
                {
                    state.Warnings.Add(new Warning { TickIndex = state.TotalHD, Message = $"unknown feat '{featId}'" });
                    continue;
                }

                foreach (var prereq in featDef.Prerequisites)
                {
                    if (!prereq.IsMet(state))
                        state.Warnings.Add(new Warning { TickIndex = state.TotalHD, Message = $"prerequisite not met for feat {featDef.Name}: {prereq.Description}" });
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
                    state.Warnings.Add(new Warning { TickIndex = state.TotalHD, Message = $"unknown skill '{alloc.SkillId}'" });

                state.SkillHalfRanks.TryAdd(alloc.SkillId, 0);
                var newTotal = state.SkillHalfRanks[alloc.SkillId] + alloc.HalfRanks;

                if (newTotal > state.MaxHalfRanks)
                    state.Warnings.Add(new Warning { TickIndex = state.TotalHD, Message = $"skill '{alloc.SkillId}' would have {newTotal / 2.0} ranks, exceeding max {state.MaxHalfRanks / 2.0}" });

                state.SkillHalfRanks[alloc.SkillId] = newTotal;
                var cost = state.CurrentTickClassSkills.Contains(alloc.SkillId)
                    ? (alloc.HalfRanks + 1) / 2
                    : alloc.HalfRanks;
                state.UnspentSkillPoints -= cost;
            }

            if (state.UnspentSkillPoints < 0)
                state.Warnings.Add(new Warning { TickIndex = state.TotalHD, Message = $"spent {-state.UnspentSkillPoints} more skill points than available" });
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
                    state.Warnings.Add(new Warning { TickIndex = state.TotalHD, Message = $"duplicate domain selection '{domainId}' ignored" });
                    continue;
                }

                var ownerClassId = ChooseDomainOwner(state, ctx.CurrentDriverId);
                if (ownerClassId == null)
                {
                    state.Warnings.Add(new Warning { TickIndex = state.TotalHD, Message = $"no pending domain selections for '{domainId}'" });
                    continue;
                }

                if (!_content.TryGetDomain(domainId, out var domainDef) || domainDef == null)
                {
                    state.Warnings.Add(new Warning { TickIndex = state.TotalHD, Message = $"unknown domain '{domainId}'" });
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
                    state.Warnings.Add(new Warning { TickIndex = state.TotalHD, Message = $"incomplete spell selection ignored" });
                    continue;
                }

                // Domain spell selection: route to the class that owns this domain.
                var routedClassId = selection.ClassId;
                if (selection.ClassId.StartsWith("domain:", StringComparison.Ordinal))
                {
                    if (!state.DomainOwners.TryGetValue(selection.ClassId, out var owner))
                    {
                        state.Warnings.Add(new Warning { TickIndex = state.TotalHD, Message = $"domain spell '{selection.SpellId}' references unselected domain '{selection.ClassId}'" });
                        continue;
                    }
                    if (owner == GrantDomainSelection.OrphanOwner)
                    {
                        state.Warnings.Add(new Warning { TickIndex = state.TotalHD, Message = $"domain '{selection.ClassId}' has no spellcasting owner; spell '{selection.SpellId}' dropped" });
                        continue;
                    }
                    routedClassId = owner;
                }

                if (!state.Spellcasting.TryGetValue(routedClassId, out var sc))
                {
                    state.Warnings.Add(new Warning { TickIndex = state.TotalHD, Message = $"unknown spellcasting class '{routedClassId}' for spell '{selection.SpellId}'" });
                    continue;
                }

                if (selection.SpellLevel < 0)
                {
                    state.Warnings.Add(new Warning { TickIndex = state.TotalHD, Message = $"invalid spell level {selection.SpellLevel} for spell '{selection.SpellId}'" });
                    continue;
                }

                if (selection.SpellLevel > sc.MaxSpellLevel)
                {
                    state.Warnings.Add(new Warning { TickIndex = state.TotalHD, Message = $"spell '{selection.SpellId}' at level {selection.SpellLevel} exceeds max spell level {sc.MaxSpellLevel} for {selection.ClassId}" });
                }

                if (!_content.TryGetSpell(selection.SpellId, out var spellDef) || spellDef == null)
                {
                    state.Warnings.Add(new Warning { TickIndex = state.TotalHD, Message = $"unknown spell '{selection.SpellId}'" });
                }
                else if (!string.IsNullOrEmpty(spellDef.School))
                {
                    // Feed CanCastSpellSchool: the definition is only in hand here, so the
                    // school is recorded now rather than resolved at prerequisite time.
                    var school = spellDef.School.ToLowerInvariant();
                    if (!state.SpellLevelsBySchool.TryGetValue(school, out var levels))
                        state.SpellLevelsBySchool[school] = levels = new List<int>();
                    levels.Add(selection.SpellLevel);
                }

                if (spellDef != null && !selection.ClassId.StartsWith("domain:", StringComparison.Ordinal))
                {
                    // Domain picks come from the domain's own list, so only class picks are
                    // checked against the class spell list.
                    if (!spellDef.ClassLevels.TryGetValue(routedClassId, out var listLevel))
                    {
                        state.Warnings.Add(new Warning { TickIndex = state.TotalHD, Message = $"spell '{selection.SpellId}' is not on the {routedClassId} spell list" });
                    }
                    else if (listLevel != selection.SpellLevel)
                    {
                        state.Warnings.Add(new Warning { TickIndex = state.TotalHD, Message = $"spell '{selection.SpellId}' is level {listLevel} for {routedClassId}, not {selection.SpellLevel}" });
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
            CheckSpellbookLimits(state);
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
                    state.Warnings.Add(new Warning { TickIndex = state.TotalHD, Message = $"unknown class feature type '{featureType}'" });
                    continue;
                }

                foreach (var optionId in selectedIds)
                {
                    if (!state.PendingClassFeatureSelections.TryGetValue(featureType, out var pending) || pending <= 0)
                    {
                        state.Warnings.Add(new Warning { TickIndex = state.TotalHD, Message = $"no pending '{featureType}' selections for '{optionId}'" });
                        continue;
                    }

                    // Prevent duplicate selection within the same feature type
                    if (state.ClassFeatureSelections.TryGetValue(featureType, out var existing) && existing.Contains(optionId))
                    {
                        state.Warnings.Add(new Warning { TickIndex = state.TotalHD, Message = $"duplicate '{featureType}' selection '{optionId}' ignored" });
                        continue;
                    }

                    // Try static option first
                    var option = featureDef.Options.FirstOrDefault(o => o.Id == optionId);
                    if (option != null)
                    {
                        foreach (var violation in GetClassFeatureOptionViolations(state, featureType, option))
                        {
                            state.Warnings.Add(new Warning
                            {
                                TickIndex = state.TotalHD,
                                Message = $"class feature option '{featureType}/{optionId}' {violation}"
                            });
                        }

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

                    state.Warnings.Add(new Warning { TickIndex = state.TotalHD, Message = $"unknown class feature option '{featureType}/{optionId}'" });
                }
            }
        }
    }

    private static IEnumerable<string> GetClassFeatureOptionViolations(
        CharacterState state,
        string featureType,
        ClassFeatureOption option)
    {
        if (option.MinEffectiveLevel > 0)
        {
            var slotLevels = state.CompanionSlots
                .Where(slot => slot.ClassFeatureType == featureType)
                .Select(slot => slot.EffectiveLevelFormula.Evaluate(state))
                .ToList();
            var effectiveLevel = slotLevels.Count > 0
                ? slotLevels.Max()
                : state.Spellcasting.Values.Select(caster => caster.CasterLevel).DefaultIfEmpty(0).Max();
            if (effectiveLevel < option.MinEffectiveLevel)
                yield return $"requires effective level {option.MinEffectiveLevel} (current {effectiveLevel})";
        }

        if (option.RequiredCasterLevel > 0)
        {
            var casterLevel = state.Spellcasting.Values
                .Select(caster => caster.CasterLevel)
                .DefaultIfEmpty(0)
                .Max();
            if (casterLevel < option.RequiredCasterLevel)
                yield return $"requires caster level {option.RequiredCasterLevel} (current {casterLevel})";
        }

        if (!string.IsNullOrWhiteSpace(option.RequiredAlignment)
            && !MatchesAlignmentRequirement(state.Alignment, option.RequiredAlignment))
        {
            yield return $"requires alignment {option.RequiredAlignment} (current {state.Alignment})";
        }
    }

    private static bool MatchesAlignmentRequirement(Alignment alignment, string requirement) =>
        requirement.Trim().ToLowerInvariant() switch
        {
            "any" => true,
            "good" => alignment is Alignment.LG or Alignment.NG or Alignment.CG,
            "evil" => alignment is Alignment.LE or Alignment.NE or Alignment.CE,
            // The Improved Familiar table uses "neutral" for neutral on the good/evil axis.
            "neutral" => alignment is Alignment.LN or Alignment.N or Alignment.CN,
            "lawful" => alignment is Alignment.LG or Alignment.LN or Alignment.LE,
            "chaotic" => alignment is Alignment.CG or Alignment.CN or Alignment.CE,
            "lawful good" => alignment == Alignment.LG,
            "lawful neutral" => alignment == Alignment.LN,
            "lawful evil" => alignment == Alignment.LE,
            "neutral good" => alignment == Alignment.NG,
            "neutral evil" => alignment == Alignment.NE,
            "chaotic good" => alignment == Alignment.CG,
            "chaotic neutral" => alignment == Alignment.CN,
            "chaotic evil" => alignment == Alignment.CE,
            _ => false
        };

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
                            EnhancementBonus = def.EnhancementBonus,
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
        var sizeModifier = _rules.CalculateSizeModifier(state.Size);
        if (sizeModifier != 0)
            state.AC.Components[BonusType.Size] = sizeModifier;
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
        state.AC.FlatFooted = 10 + flatComponents + Math.Min(0, dexContrib);

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
        var twoWeaponFighting = state.Feats.Contains("feat:two_weapon_fighting");

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
        typedAttackBonus += _rules.CalculateSizeModifier(state.Size);

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
        "metamagic" => feat?.Type == FeatType.Metamagic,
        _ => false
    };

    public const string FighterBonusTag = "fighter_bonus";

    private bool ValidateDynamicSelection(CharacterState state, DynamicOptionSource source, string optionId, string featureType)
    {
        if (source.Kind == "feat")
        {
            if (!state.Feats.Contains(optionId))
            {
                state.Warnings.Add(new Warning { TickIndex = state.TotalHD, Message = $"'{featureType}' selection '{optionId}' — character does not have that feat" });
                return false;
            }

            if (!_content.TryGetFeat(optionId, out var featDef) || featDef == null)
            {
                state.Warnings.Add(new Warning { TickIndex = state.TotalHD, Message = $"'{featureType}' selection '{optionId}' — feat not found in content" });
                return false;
            }

            if (source.FeatType != null)
            {
                if (!Enum.TryParse<FeatType>(source.FeatType, ignoreCase: true, out var requiredType))
                {
                    state.Warnings.Add(new Warning { TickIndex = state.TotalHD, Message = $"'{featureType}' has invalid featType '{source.FeatType}'" });
                    return false;
                }
                if (featDef.Type != requiredType)
                {
                    state.Warnings.Add(new Warning { TickIndex = state.TotalHD, Message = $"'{featureType}' selection '{optionId}' — feat is not of type {source.FeatType}" });
                    return false;
                }
            }

            if (source.Tag != null && !featDef.Tags.Contains(source.Tag))
            {
                state.Warnings.Add(new Warning { TickIndex = state.TotalHD, Message = $"'{featureType}' selection '{optionId}' — feat lacks tag '{source.Tag}'" });
                return false;
            }

            return true;
        }

        state.Warnings.Add(new Warning { TickIndex = state.TotalHD, Message = $"'{featureType}' has unknown dynamicSource kind '{source.Kind}'" });
        return false;
    }
}
