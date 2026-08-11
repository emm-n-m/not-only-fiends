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

    /// <summary>
    /// Applies a permabuff to an already-evaluated state, outside the tick loop. For the
    /// familiar rule, where the benefit runs from the familiar's race to the master's finished
    /// state and so has no tick to belong to.
    /// </summary>
    public void ApplyToFinishedState(CharacterState state, Permabuff buff) =>
        buff.Apply(CreateContext(state));

    public RaceDefinition? FindRace(string? raceId)
    {
        if (string.IsNullOrEmpty(raceId)) return null;
        try { return _content.GetRace(raceId); }
        catch (KeyNotFoundException) { return null; }
    }

    private PermabuffContext CreateContext(CharacterState state) =>
        new(state, _rules, _content);

    public CharacterState Evaluate(Character character, int? upToHD = null)
    {
        var state = new CharacterState();
        state.Alignment = character.Alignment;
        state.Deity = string.IsNullOrWhiteSpace(character.Deity) ? null : character.Deity;

        // Mirror the variant→base map onto the state so formulas can resolve it without a
        // registry. Cheap: it is only the handful of drivers that declare a base.
        foreach (var hd in _content.GetAllDrivers().OfType<HDDriver>())
            if (hd.VariantOf != null)
                state.ClassVariantBases[hd.Id] = hd.VariantOf;

        // Companion-side: surface origin so templates/formulas can read MasterLevel.
        if (character.CompanionOrigin != null)
        {
            state.CompanionOrigin = character.CompanionOrigin;
            state.EffectiveMasterLevel = character.CompanionOrigin.EffectiveMasterLevel;
        }

        var ctx = CreateContext(state);
        ctx.SavedRollDieCeiling = ComputeSavedRollDieCeiling(character);

        // 1. Apply race
        var race = _content.GetRace(character.RaceId);
        ApplyRace(ctx, race);

        // 2. Apply templates (in order). Inherited templates — and every template on a
        // character saved before acquisition HDs existed — apply at creation. A template
        // acquired mid-career applies at its acquisition tick instead, inside the loop below.
        int AcquisitionHDOf(string id) => character.TemplateAcquisitionHD.GetValueOrDefault(id, 0);
        var acquiredTemplateIds = character.TemplateIds.Where(id => AcquisitionHDOf(id) > 1).ToList();
        foreach (var templateId in character.TemplateIds.Except(acquiredTemplateIds))
        {
            var template = _content.GetTemplate(templateId);
            TemplateApplication.Apply(ctx, template, acquisitionHD: null);
        }

        // Derived movement is permanent character state. Resolve it after all template
        // transformations and before the post-tick armor/load speed pass. Acquired templates
        // resolve theirs at the moment they apply.
        ResolveDerivedSpeeds(state, character.TemplateIds.Except(acquiredTemplateIds));

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
        var deferredDriverFeatPrerequisites =
            new List<(int TickIndex, string DriverName, Prerequisite Prerequisite)>();

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

            // Templates acquired at this HD transform the character before the tick's driver
            // grants anything, so their ability modifiers feed this level's skill points and
            // their type change is visible to the driver's prerequisites. Everything they
            // banked earlier stays as it was earned — except hit dice, which the template's
            // die floor restates per the SRD's "current and future Hit Dice" rule.
            foreach (var templateId in acquiredTemplateIds)
                if (AcquisitionHDOf(templateId) == state.TotalHD)
                    TemplateApplication.Apply(ctx, _content.GetTemplate(templateId), state.TotalHD);

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
            // Read the cap from the templates applied so far (state), not the character's
            // full list: a cap is forward-only, so a template acquired later must not squeeze
            // racial dice banked before it existed.
            ctx.CurrentRacialHitDieMaximum = ctx.CurrentDriverKind == DriverKind.RacialHD
                ? state.TemplateIds
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
                // Imported PCG files do not record feat acquisition levels, so their feats are
                // intentionally stored on the final tick. Feat-based class-entry requirements
                // must therefore be checked after the evaluated timeline has applied its feat
                // choices. This also lets a feat chosen at the entry HD qualify the class.
                // Other requirements remain chronological and are checked immediately.
                if (IsFeatBasedPrerequisite(prereq))
                {
                    if (driverLevel == 1)
                        deferredDriverFeatPrerequisites.Add((state.TotalHD, driver.Name, prereq));
                    continue;
                }

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
            foreach (var rule in state.EffectiveLevelRules.Where(r =>
                RuleTargetsDriver(r, tick.DriverId) && r.Scope == EffectiveLevelScope.ClassFeatures))
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
                // The whole tick list, not just the classes ticked so far: a variant taken later
                // still has to own the seed, or its racial levels land on the abandoned base id.
                FinalizeRacialSpellcasting(ctx, race, character.Ticks
                    .Select(t => t.DriverId)
                    .Distinct(StringComparer.Ordinal)
                    .ToList());
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

            // f. Template tick injections — only templates applied by now (state), so an
            // acquired template's scaling entries keyed below its acquisition never fire.
            // Snapshot: an ApplyTemplate inside a scaling buff may grow the list mid-loop.
            foreach (var templateId in state.TemplateIds.ToList())
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
                var multiplier = 1;
                if (state.TotalHD == 1)
                {
                    multiplier = _rules.RacialBonusSkillFirstHDMultiplier;
                    bonus *= multiplier;
                }
                state.UnspentSkillPoints += bonus;
                state.SkillPointAccruals.Add(new SkillPointAccrual
                {
                    Source = race.Id,
                    BasePoints = race.BonusSkillPointsPerHD,
                    FirstHdMultiplier = multiplier,
                    Points = bonus
                });
            }

            // h. Race scaling abilities
            foreach (var sf in race.ScalingFormulas)
                new SetAttribute(sf.Target, sf.Formula.Evaluate(state), sf.ResistanceElement, sf.AbilityScore).Apply(ctx);

            // i. Ability score increase (scheduled class ticks only; racial adjustments are on the race)
            if (driver is HDDriver abilityDriver
                && _rules.GrantsAbilityIncrease(state.TotalHD, abilityDriver.Kind)
                && tick.Choices.AbilityIncrease.HasValue)
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
                foreach (var rule in state.EffectiveLevelRules.Where(r =>
                    RuleTargetsDriver(r, otherDriverId) && r.Scope == EffectiveLevelScope.ClassFeatures))
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

        // A permanent event at the end of the timeline is a valid "after the last
        // level" event. Apply it before the post-tick passes so its effects are visible
        // to equipment, hit points, skills, and other final derived values. A template
        // acquired past the last tick lands in the same slot.
        if (maxTick == character.Ticks.Count)
        {
            foreach (var evt in character.PermanentEvents.Where(e => e.BeforeTick >= maxTick))
                foreach (var buff in evt.Permabuffs)
                    buff.Apply(ctx);

            foreach (var templateId in acquiredTemplateIds)
                if (AcquisitionHDOf(templateId) > character.Ticks.Count)
                    TemplateApplication.Apply(ctx, _content.GetTemplate(templateId), AcquisitionHDOf(templateId));
        }

        foreach (var (tickIndex, driverName, prerequisite) in deferredDriverFeatPrerequisites)
        {
            if (!prerequisite.IsMet(state))
                state.Warnings.Add(new Warning
                {
                    TickIndex = tickIndex,
                    Message = $"prerequisite not met for {driverName}: {prerequisite.Description}"
                });
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
        // so this cannot run inside TemplateApplication.Apply. Only templates that actually
        // applied are validated: an evaluation truncated below a template's acquisition HD
        // must not warn about prerequisites the character was never claimed to meet yet.
        foreach (var templateId in character.TemplateIds)
        {
            if (!state.TemplateIds.Contains(templateId))
                continue;
            var template = _content.GetTemplate(templateId);
            foreach (var prereq in template.Prerequisites)
            {
                if (!prereq.IsMet(state))
                    state.Warnings.Add(new Warning { TickIndex = AcquisitionHDOf(templateId), Message = $"prerequisite not met for template {template.Name}: {prereq.Description}" });
            }
        }

        // 6. Tail pass — companion / leadership finalization.
        FinalizeCompanionAndLeadership(ctx, character, maxTick);

        // 7. Seed racial spellcasting (e.g. Aranea Sorc N, Ghaele Cleric 14) for
        // characters that never take a level of the target class. When class levels
        // ARE taken, the class tick's UpdateSpellcasting already seeded state.Spellcasting
        // using featureLevel = actualLevel + EffectiveLevelRule (registered at race
        // creation), so this step skips the class and leaves the stacked value in place.
        FinalizeRacialSpellcasting(ctx, race, character.Ticks
            .Select(t => t.DriverId)
            .Distinct(StringComparer.Ordinal)
            .ToList());

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

        // 11. Tail pass — casting-ability bonus spell slots. Ability scores are final only after
        // equipment has been applied, and developed epic spells use a knowledge-based pool rather
        // than ordinary spell slots.
        FinalizeAbilityBonusSpellSlots(state);

        // 12. Validate and materialize the optional daily prepared-spell loadout after all slot
        // components and spellbook contents are final.
        FinalizePreparedSpellSelections(state, character);

        // 13. Tail pass — languages taken against a granted slot. Must run after every racial and
        // template permabuff, since any of them can be what granted the slot.
        FinalizeGrantedLanguages(state, character);

        return state;
    }

    private static void FinalizeAbilityBonusSpellSlots(CharacterState state)
    {
        foreach (var spellcasting in state.Spellcasting.Values)
        {
            spellcasting.AbilityBonusSlots.Clear();
            if (spellcasting.Acquisition == SpellAcquisition.Developed)
                continue;

            var abilityModifier = state.AbilityModifier(spellcasting.CastingStat);
            foreach (var spellLevel in spellcasting.SpellsPerDay.Keys.Where(level => level >= 1))
            {
                var bonus = GameRules.BonusSpellSlots(abilityModifier, spellLevel);
                if (bonus > 0)
                    spellcasting.AbilityBonusSlots[spellLevel] = bonus;
            }
        }
    }

    private void FinalizePreparedSpellSelections(CharacterState state, Character character)
    {
        state.PreparedSpellSelections.Clear();
        if (character.PreparedSpellSelections.Count == 0)
            return;

        var used = new Dictionary<(string ClassId, int Level, PreparedSpellSlotKind Kind), int>();
        foreach (var selection in character.PreparedSpellSelections)
        {
            if (string.IsNullOrWhiteSpace(selection.ClassId) || string.IsNullOrWhiteSpace(selection.SpellId))
            {
                state.Warnings.Add(new Warning { TickIndex = state.TotalHD, Message = "incomplete prepared spell selection ignored" });
                continue;
            }
            if (selection.SpellLevel < 1)
            {
                state.Warnings.Add(new Warning { TickIndex = state.TotalHD, Message = $"prepared spell '{selection.SpellId}' must be level 1 or higher" });
                continue;
            }
            if (!state.Spellcasting.TryGetValue(selection.ClassId, out var spellcasting)
                || spellcasting.Acquisition is not (SpellAcquisition.FullList or SpellAcquisition.Spellbook))
            {
                state.Warnings.Add(new Warning { TickIndex = state.TotalHD, Message = $"prepared spell '{selection.SpellId}' references non-prepared caster '{selection.ClassId}'" });
                continue;
            }
            if (selection.SpellLevel > spellcasting.MaxSpellLevel)
            {
                state.Warnings.Add(new Warning { TickIndex = state.TotalHD, Message = $"prepared spell '{selection.SpellId}' exceeds max spell level {spellcasting.MaxSpellLevel} for {selection.ClassId}" });
                continue;
            }

            if (!_content.TryGetSpell(selection.SpellId, out var spell) || spell == null)
            {
                state.Warnings.Add(new Warning { TickIndex = state.TotalHD, Message = $"unknown prepared spell '{selection.SpellId}'" });
                continue;
            }

            var isDomainSpell = selection.SlotKind == PreparedSpellSlotKind.Domain
                && IsDomainSpell(state, selection);
            var legalLevel = selection.SlotKind == PreparedSpellSlotKind.Domain
                ? isDomainSpell
                : TryGetSpellLevelForList(state, spell, selection.ClassId, out var classSpellLevel)
                    && classSpellLevel == selection.SpellLevel;
            if (spellcasting.Acquisition == SpellAcquisition.Spellbook && selection.SlotKind != PreparedSpellSlotKind.Domain)
            {
                legalLevel = spellcasting.SelectedSpells.Any(bookSpell =>
                    bookSpell.ClassId == selection.ClassId
                    && bookSpell.SpellId == selection.SpellId
                    && bookSpell.SpellLevel == selection.SpellLevel);
            }
            if (!legalLevel)
            {
                state.Warnings.Add(new Warning { TickIndex = state.TotalHD, Message = $"prepared spell '{selection.SpellId}' is not available to {selection.ClassId} at level {selection.SpellLevel}" });
                continue;
            }
            if (WizardSchools.IsProhibited(state, spell.School))
            {
                state.Warnings.Add(new Warning { TickIndex = state.TotalHD, Message = $"prepared spell '{selection.SpellId}' is from a prohibited school" });
                continue;
            }

            var allowed = selection.SlotKind switch
            {
                PreparedSpellSlotKind.Normal => spellcasting.SpellsPerDay.GetValueOrDefault(selection.SpellLevel)
                    + spellcasting.AbilityBonusSlots.GetValueOrDefault(selection.SpellLevel),
                PreparedSpellSlotKind.Domain => spellcasting.DomainBonusSlots.GetValueOrDefault(selection.SpellLevel),
                PreparedSpellSlotKind.Specialty => spellcasting.SpecialtyBonusSlots.GetValueOrDefault(selection.SpellLevel),
                _ => 0
            };
            var key = (selection.ClassId, selection.SpellLevel, selection.SlotKind);
            var alreadyUsed = used.GetValueOrDefault(key);
            if (alreadyUsed >= allowed)
            {
                state.Warnings.Add(new Warning { TickIndex = state.TotalHD, Message = $"too many {selection.SlotKind.ToString().ToLowerInvariant()} prepared spells for {selection.ClassId} level {selection.SpellLevel}" });
                continue;
            }

            if (selection.SlotKind == PreparedSpellSlotKind.Domain && !isDomainSpell)
            {
                state.Warnings.Add(new Warning { TickIndex = state.TotalHD, Message = $"prepared domain spell '{selection.SpellId}' is not offered by a domain owned by {selection.ClassId}" });
                continue;
            }
            if (selection.SlotKind == PreparedSpellSlotKind.Specialty
                && !string.Equals(WizardSchools.Specialty(state), spell.School, StringComparison.OrdinalIgnoreCase))
            {
                state.Warnings.Add(new Warning { TickIndex = state.TotalHD, Message = $"prepared specialty spell '{selection.SpellId}' is not from the specialty school" });
                continue;
            }

            state.PreparedSpellSelections.Add(new PreparedSpellSelection
            {
                ClassId = selection.ClassId,
                SpellLevel = selection.SpellLevel,
                SpellId = selection.SpellId,
                SlotKind = selection.SlotKind,
            });
            used[key] = alreadyUsed + 1;
        }
    }

    private bool IsDomainSpell(CharacterState state, PreparedSpellSelection selection)
    {
        foreach (var (domainId, ownerClassId) in state.DomainOwners)
        {
            if (ownerClassId != selection.ClassId || !_content.TryGetDomain(domainId, out var domain) || domain == null)
                continue;
            if (domain.BonusSpells.GetValueOrDefault(selection.SpellLevel) == selection.SpellId)
                return true;
        }
        return false;
    }

    private bool TryGetSpellLevelForList(CharacterState state, SpellDefinition spell, string spellListId,
        out int level)
    {
        if (state.IsSpellExcludedFromList(spellListId, spell.Id))
        {
            level = 0;
            return false;
        }

        return _content.TryGetSpellLevelForList(spell, spellListId, out level);
    }

    private static bool IsFeatBasedPrerequisite(Prerequisite prerequisite) => prerequisite switch
    {
        HasFeat => true,
        HasFeatOfType => true,
        HasFeatWithTag => true,
        HasFeatSelections => true,
        HasFeatOfAnyType => true,
        AnyOf anyOf => anyOf.Options.Any(IsFeatBasedPrerequisite),
        _ => false,
    };

    private static void RefreshDynamicSLAs(CharacterState state)
    {
        foreach (var sla in state.SLAs.Where(s => s.CasterLevelTracksTotalHD))
            sla.CasterLevel = state.TotalHD;
    }

    private void FinalizeHitPoints(CharacterState state)
    {
        var conMod = state.AbilityModifier(Ability.CON);
        state.HP = state.HitDice.Select((hitDie, index) =>
        {
            var roll = hitDie.SavedRoll
                ?? (_rules.FirstHDMaxHP && index == 0
                    ? hitDie.DieSize
                    : hitDie.DieSize / 2 + 1);
            return Math.Max(1, roll + conMod);
        }).Sum() + state.FlatHitPointBonuses;
    }

    /// <summary>
    /// Fulfils <see cref="GrantDomainSpellLikeAbilities"/> requests, turning each chosen domain's
    /// bonus spells into SLAs at the tier its spell level earns.
    /// </summary>
    private void FinalizeDomainSpellLikeAbilities(PermabuffContext ctx)
    {
        var state = ctx.State;
        if (state.PendingDomainSLAGrants.Count == 0) return;

        var saveMod = state.AbilityModifier(state.PendingDomainSLAGrants[0].SaveAbility);

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
    /// <summary>
    /// Recomputes synergies and skill totals from the state's current ranks and ability scores.
    /// Exposed for the familiar rule, which rewrites a familiar's ranks after its own replay has
    /// finished and needs the totals brought back in line with the new ranks.
    /// </summary>
    public void RecomputeSkillTotals(CharacterState state) => FinalizeSkills(state);

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
                abilityMod = state.AbilityModifier(keyAbility);

            // Hide is the one skill with a size modifier, and it is not the AC/attack one — a
            // Diminutive toad hides at +12, not +4. Kept out of SkillBonuses so that dictionary
            // keeps meaning "what content granted".
            var sizeModifier = _rules.SkillSizeModifiers.TryGetValue(skillId, out var sizeRule)
                ? sizeRule(_rules, state.Size)
                : 0;

            state.SkillTotals[skillId] = WholeRanks(skillId)
                                         + abilityMod
                                         + sizeModifier
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
    private void FinalizeRacialSpellcasting(
        PermabuffContext ctx, RaceDefinition race, IReadOnlyCollection<string> classDriverIds)
    {
        foreach (var buff in race.RacialPermabuffs.OfType<GrantRacialSpellcasting>())
        {
            // "Casts as a 7th-level druid" seeds a caster the character's own druid levels then
            // overwrite with the stacked total. A variant druid overwrites a different key, so
            // the seed has to be laid down under the variant's id or the racial levels are
            // counted twice — once in the abandoned seed, once in the variant's caster.
            var classId = classDriverIds
                .FirstOrDefault(id => VariantBaseOf(id) == buff.ClassId) ?? buff.ClassId;

            if (ctx.State.Spellcasting.ContainsKey(classId))
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
                ClassId = classId,
                CastingType = hd.Spellcasting.CastingType,
                CastingStat = hd.Spellcasting.CastingStat,
                CasterLevel = level,
                SpellsPerDay = spd,
                SpellsKnown = sk,
                ProgressionRef = hd.Spellcasting,
            }.Apply(ctx);
        }
    }

    /// <summary>The class a driver is a variant of, or null when it is not a variant.</summary>
    private string? VariantBaseOf(string driverId)
    {
        try
        {
            return (_content.GetDriver(driverId) as HDDriver)?.VariantOf;
        }
        catch (KeyNotFoundException)
        {
            return null;
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
    private void FinalizeCompanionAndLeadership(PermabuffContext ctx, Character character, int maxTick)
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

            ApplyCompanionTemplateChoice(state, character, slot, maxTick);
        }

        // (b) Leadership finalization.
        if (state.Feats.Contains("feat:leadership"))
        {
            state.LeadershipScore = state.TotalHD
                                    + state.AbilityModifier(Ability.CHA)
                                    + state.LeadershipScoreModifier;

            // Epic Leadership replaces the table rather than adding to it: "The character attracts
            // a cohort and followers as shown below on Table: Epic Leadership … Normal: The
            // Leadership feat provides no benefit for leadership scores beyond 25."
            ApplyLeadershipModifiers(state, character);

            // Cohort level is a table column, not arithmetic — the SRD progression is irregular
            // (score 20 → 14th, 21 → 15th, 22 → 15th) — and is then held down by a separate cap
            // that Epic Leadership relaxes; see LeadershipTables.CohortLevelCap.
            state.MaxCohortLevel = Math.Min(
                LeadershipTables.LookupCohortLevelFor(state.Feats, state.LeadershipCohortScore),
                LeadershipTables.CohortLevelCap(state.Feats, state.TotalHD));

            state.Followers =
                LeadershipTables.LookupFollowerCountsFor(state.Feats, state.LeadershipFollowerScore);

            // Re-evaluate any slot whose formula references LeadershipScore (cohort cap).
            foreach (var slot in state.CompanionSlots)
                slot.EffectiveLevel = slot.EffectiveLevelFormula.Evaluate(state);
        }

        // (c) Companion-side template scaling. Reads the templates that actually applied
        // (state), which for companions is always the full list — they are inherited.
        if (state.CompanionOrigin != null)
        {
            foreach (var templateId in state.TemplateIds.ToList())
            {
                var template = _content.GetTemplate(templateId);
                foreach (var buff in template.GetCompanionScalingPermabuffs(state.EffectiveMasterLevel))
                    buff.Apply(ctx);
            }
        }
    }

    private void ApplyCompanionTemplateChoice(
        CharacterState state,
        Character character,
        CompanionSlotState slot,
        int maxTick)
    {
        if (slot.LinkType != "animal_companion")
            return;

        var templateId = character.Ticks
            .Take(maxTick)
            .Select(tick => tick.Choices.CompanionTemplateChoices?.GetValueOrDefault(slot.LinkType))
            .LastOrDefault(id => !string.IsNullOrWhiteSpace(id));
        if (string.IsNullOrWhiteSpace(templateId))
            return;

        if (state.ClassLevels.GetValueOrDefault("class:planar_ranger") <= 0)
        {
            state.Warnings.Add(new Warning
            {
                TickIndex = state.TotalHD,
                Message = $"companion template '{templateId}' requires a planar ranger animal companion"
            });
            return;
        }

        if (templateId is not ("template:celestial" or "template:fiendish"))
        {
            state.Warnings.Add(new Warning
            {
                TickIndex = state.TotalHD,
                Message = $"unsupported planar ranger companion template '{templateId}'"
            });
            return;
        }

        TemplateDriver template;
        try
        {
            template = _content.GetTemplate(templateId);
        }
        catch (KeyNotFoundException)
        {
            state.Warnings.Add(new Warning
            {
                TickIndex = state.TotalHD,
                Message = $"unknown companion template '{templateId}'"
            });
            return;
        }

        var isGood = state.Alignment is Alignment.LG or Alignment.NG or Alignment.CG;
        var isEvil = state.Alignment is Alignment.LE or Alignment.NE or Alignment.CE;
        if ((templateId == "template:celestial" && isEvil)
            || (templateId == "template:fiendish" && isGood))
        {
            state.Warnings.Add(new Warning
            {
                TickIndex = state.TotalHD,
                Message = $"companion template '{template.Name}' is not allowed for planar ranger alignment {state.Alignment}"
            });
            return;
        }

        slot.SelectedTemplateId = templateId;
    }

    private void ApplyRace(PermabuffContext ctx, RaceDefinition race)
    {
        var state = ctx.State;
        state.RaceId = race.Id;
        state.Type = race.Type;
        state.Size = race.Size;

        // An alternative animal companion is fielded at a reduced effective level, and the
        // reduction belongs to the species, not the master. Applied here so everything downstream
        // — companion scaling tiers and any MasterLevel formula — sees the one adjusted number.
        // Never below zero: a master too low-level for this companion gets no progression, not
        // negative progression.
        if (state.CompanionOrigin != null && race.CompanionLevelModifier != 0)
        {
            state.EffectiveMasterLevel =
                Math.Max(0, state.EffectiveMasterLevel + race.CompanionLevelModifier);
        }
        // Both derive from what the race *is* unless it explicitly contradicts itself. Authoring
        // them separately is what let race:companion_shadow — undead and incorporeal — report as
        // a living corporeal creature.
        state.IsLiving = race.IsLiving ?? CreatureTypes.IsLiving(race.Type);
        state.IsCorporeal = race.IsCorporeal
            ?? !race.Subtypes.Contains(CreatureTypes.IncorporealSubtype);
        // Null LA (source never priced this as a PC race) contributes 0 to ECL.
        state.LevelAdjustment = race.LevelAdjustment ?? 0;

        foreach (var subtype in race.Subtypes)
            state.Subtypes.Add(subtype);

        foreach (var (mode, speed) in race.Speeds)
        {
            state.BaseSpeeds[mode] = speed;
            state.Speeds[mode] = speed;
        }
        state.FlyManeuverability = race.FlyManeuverability;

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

        foreach (var languageId in character.SourceLanguageIds.Distinct(StringComparer.Ordinal))
            state.Languages.Add(languageId);
    }

    /// <summary>
    /// Spends languages taken against a granted slot — a raven familiar's "one language of its
    /// master's choice". These are not priced off Intelligence and are not restricted to the
    /// race's bonus-language list: the grant says "of its master's choice", so any language the
    /// content offers is legal. Runs as a tail pass because the slot itself may be granted by a
    /// racial or template permabuff that has already fired by then.
    /// </summary>
    private static void FinalizeGrantedLanguages(CharacterState state, Character character)
    {
        var spent = 0;
        foreach (var languageId in character.GrantedLanguageIds.Distinct(StringComparer.Ordinal))
        {
            if (state.Languages.Contains(languageId))
            {
                state.Warnings.Add(new Warning
                {
                    Message = $"Granted language '{languageId}' is already known — slot not spent"
                });
                continue;
            }

            if (spent >= state.GrantedLanguageSlots)
            {
                state.Warnings.Add(new Warning
                {
                    Message = $"Granted language '{languageId}' exceeds the "
                        + $"{state.GrantedLanguageSlots} language slot(s) this character has — skipped"
                });
                continue;
            }

            state.Languages.Add(languageId);
            spent++;
        }
    }

    private void ResolveDerivedSpeeds(CharacterState state, IEnumerable<string> templateIds)
    {
        foreach (var templateId in templateIds)
            TemplateApplication.ResolveDerivedSpeeds(state, _content.GetTemplate(templateId));
    }

    /// <summary>
    /// The earliest 1-based HD at which the template's <see cref="TemplateDriver.Prerequisites"/>
    /// are all met — the default answer for "when was this template acquired?" when the source
    /// records nothing. Acquiring at HD N means applying at the start of tick N, so the check
    /// runs against the state after tick N−1, on a timeline WITHOUT the template (acquisition
    /// cannot be gated on what the template itself grants). Feat-based prerequisites are checked
    /// once against the final state, mirroring driver-entry checks: imported characters store
    /// every feat on the last tick, so a chronological check would push acquisition past where
    /// the feat was really taken. Returns Ticks.Count + 1 when only the finished character
    /// qualifies, and null when the prerequisites are never met.
    /// </summary>
    public int? FindEarliestAcquisitionHD(Character character, string templateId)
    {
        var template = _content.GetTemplate(templateId);
        var clone = character.Clone();
        clone.TemplateIds.Remove(templateId);
        clone.TemplateAcquisitionHD.Remove(templateId);

        var featBased = template.Prerequisites.Where(IsFeatBasedPrerequisite).ToList();
        var chronological = template.Prerequisites.Except(featBased).ToList();

        if (featBased.Count > 0)
        {
            var finalState = Evaluate(clone);
            if (featBased.Any(prereq => !prereq.IsMet(finalState)))
                return null;
        }

        for (var evaluatedHD = 0; evaluatedHD <= clone.Ticks.Count; evaluatedHD++)
        {
            var state = Evaluate(clone, upToHD: evaluatedHD);
            if (chronological.All(prereq => prereq.IsMet(state)))
                return evaluatedHD + 1;
        }
        return null;
    }

    /// <summary>
    /// The largest hit-die size any template on this character's timeline will ever impose —
    /// including templates that arrive mid-career, chained consequence templates, and
    /// capstone-granted ones. Saved rolls are validated against this ceiling rather than the
    /// die size at the tick they were banked, because a roll recorded by the source is a
    /// fact about the finished character (a lich's 8 at bard 3 was re-rolled on a d12).
    /// </summary>
    private int ComputeSavedRollDieCeiling(Character character)
    {
        var ceiling = 0;
        var seen = new HashSet<string>();
        var queue = new Queue<string>(character.TemplateIds);

        var levelsReached = character.Ticks
            .GroupBy(t => t.DriverId)
            .ToDictionary(g => g.Key, g => g.Count());
        foreach (var hd in _content.GetAllDrivers().OfType<HDDriver>())
        {
            if (!levelsReached.TryGetValue(hd.Id, out var reached))
                continue;
            foreach (var (level, buffs) in hd.LevelPermabuffs)
                if (level <= reached)
                    foreach (var buff in buffs.OfType<ApplyTemplate>())
                        queue.Enqueue(buff.TemplateId);
        }

        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (!seen.Add(id) || !_content.TryGetTemplate(id, out var template) || template == null)
                continue;
            if (template.HitDieSizeFloor.HasValue)
                ceiling = Math.Max(ceiling, template.HitDieSizeFloor.Value);
            foreach (var buff in template.CreationPermabuffs.OfType<ApplyTemplate>())
                queue.Enqueue(buff.TemplateId);
            foreach (var buffs in template.ScalingPermabuffs.Values)
                foreach (var buff in buffs.OfType<ApplyTemplate>())
                    queue.Enqueue(buff.TemplateId);
        }
        return ceiling;
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

    private static string SourceAuthorizedDomainOwner(CharacterState state, string? currentDriverId)
    {
        // PCGen records the class level at which the user made the selection, which is not
        // necessarily the feature that granted the slot. Prefer a real pending slot on that
        // class, then a race/template (orphan) slot, before falling back to the recorded class.
        if (currentDriverId != null
            && state.PendingDomainSelections.GetValueOrDefault(currentDriverId) > 0)
            return currentDriverId;
        if (state.PendingDomainSelections.GetValueOrDefault(GrantDomainSelection.OrphanOwner) > 0)
            return GrantDomainSelection.OrphanOwner;
        return currentDriverId ?? GrantDomainSelection.OrphanOwner;
    }

    private void ApplyDomainSelections(PermabuffContext ctx, IEnumerable<string> domainIds,
        bool sourceAuthorized)
    {
        var state = ctx.State;
        foreach (var domainId in domainIds)
        {
            if (state.Domains.Contains(domainId))
            {
                state.Warnings.Add(new Warning { TickIndex = state.TotalHD, Message = $"duplicate domain selection '{domainId}' ignored" });
                continue;
            }

            var ownerClassId = sourceAuthorized
                ? SourceAuthorizedDomainOwner(state, ctx.CurrentDriverId)
                : ChooseDomainOwner(state, ctx.CurrentDriverId);
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

            // A class that narrows its domain list keeps the pick — the .pcg is the record of
            // what was played — but says so, the same as an unmet feat prerequisite.
            if (state.DomainSelectionRestrictions.TryGetValue(ownerClassId, out var allowedDomains)
                && allowedDomains.Count > 0
                && !allowedDomains.Contains(domainId, StringComparer.Ordinal))
            {
                state.Warnings.Add(new Warning
                {
                    TickIndex = state.TotalHD,
                    Message = $"domain '{domainId}' is not on {ownerClassId}'s list "
                        + $"({string.Join(", ", allowedDomains)})"
                });
            }

            state.Domains.Add(domainId);
            state.DomainOwners[domainId] = ownerClassId;
            if (state.DomainSpellListExclusionRules.TryGetValue(ownerClassId, out var opposedDomains)
                && opposedDomains.TryGetValue(domainId, out var opposedDomainId)
                && _content.TryGetDomain(opposedDomainId, out var opposedDomain)
                && opposedDomain != null)
            {
                if (!state.DynamicSpellListExclusions.TryGetValue(ownerClassId, out var excludedSpellIds))
                    state.DynamicSpellListExclusions[ownerClassId] = excludedSpellIds = new HashSet<string>(StringComparer.Ordinal);
                excludedSpellIds.UnionWith(opposedDomain.BonusSpells.Values);
            }

            if (state.PendingDomainSelections.GetValueOrDefault(ownerClassId) > 0)
            {
                state.PendingDomainSelections[ownerClassId]--;
                if (state.PendingDomainSelections[ownerClassId] == 0)
                    state.PendingDomainSelections.Remove(ownerClassId);
            }

            // Spell-list-source domains (Archfiend / Red Dragon): the domain contributes its
            // spell list to the caster's known pool, nothing else — no granted power, and no
            // cleric-style prepared bonus slot. The player picks the domain; the caster simply
            // gets to know its spells.
            if (state.SpellListSourceDomainOwners.Contains(ownerClassId))
            {
                state.ExtraSpellListSources.Add(new SpellListSourceRule
                {
                    ClassId = ownerClassId,
                    ListId = domainId
                });
                continue;
            }

            foreach (var buff in domainDef.GrantedPermabuffs)
                buff.Apply(ctx);

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
                classLevel, state.AbilityModifier(sc.CastingStat));

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

        if (choices.UnknownChoices is { Count: > 0 })
            foreach (var key in choices.UnknownChoices.Keys.OrderBy(key => key, StringComparer.Ordinal))
                state.Warnings.Add(new Warning
                {
                    TickIndex = state.TotalHD,
                    Message = $"unknown tick choice '{key}' ignored; use the documented choice fields"
                });

        // Level advancement buys skill ranks before selecting feats. A feat gained at this HD may
        // therefore use ranks bought at this HD to satisfy its prerequisites (PHB advancement
        // order). This is observable on imported builds because both choices can share a tick.
        ApplySkillAllocations(state, choices.SkillAllocations);

        // Feats taken at the same HD are a set, not a sequence: the order they happen to sit in
        // FeatIds is a storage artifact, and PCGen imports put a character's whole feat list in
        // one tick. Prerequisites are therefore checked once the tick's feats have all landed —
        // taking Cleave and Power Attack together is legal, taking Cleave without it is not.
        var pendingFeatPrerequisites = new List<(FeatDefinition Feat, Prerequisite Prerequisite)>();

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
                    pendingFeatPrerequisites.Add((featDef, prereq));

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

        foreach (var (feat, prerequisite) in pendingFeatPrerequisites)
        {
            if (!prerequisite.IsMet(state))
                state.Warnings.Add(new Warning
                {
                    TickIndex = state.TotalHD,
                    Message = $"prerequisite not met for feat {feat.Name}: {prerequisite.Description}"
                });
        }

        // Domain selections — each pick consumes a pending slot from the granting class
        // (preferring the current tick's class) and grants its bonus slot only to that class.
        // NOTE: must process before SpellSelections so domain spell picks can resolve their owner class.
        if (choices.ClassFeatureChoices?.TryGetValue("domains", out var domainIds) == true && domainIds != null)
            ApplyDomainSelections(ctx, domainIds, sourceAuthorized: false);
        if (choices.ClassFeatureChoices?.TryGetValue("imported_source_domains", out var importedDomainIds) == true
            && importedDomainIds != null)
            ApplyDomainSelections(ctx, importedDomainIds, sourceAuthorized: true);

        // Epic spell lists are PCGen pseudo classes rather than HD drivers. Materialize their
        // computed slot pool after feats and skills have landed, and before selections replay.
        EnsureEpicSpellcasting(state, choices.SpellSelections);

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
                    var extraListSources = state.ExtraSpellListSources
                        .Where(rule => (rule.ClassId == null || rule.ClassId == sc.ClassId)
                                    && (!rule.CastingType.HasValue || rule.CastingType == sc.CastingType))
                        .Select(rule => rule.ListId);
                    // Resolve the owning class through the registry so inherited class/domain
                    // sources and that class's exclusions are applied together. Extra sources
                    // granted dynamically are additive and resolve directly.
                    var matchingLevels = new[] { routedClassId }
                        .Select(source => TryGetSpellLevelForList(state, spellDef, source, out var lvl) ? (int?)lvl : null)
                        .Concat(extraListSources
                            .Select(source => _content.TryGetSpellLevelForSource(spellDef, source, out var lvl) ? (int?)lvl : null))
                        .Where(lvl => lvl.HasValue)
                        .Select(lvl => lvl!.Value)
                        .ToList();
                    if (matchingLevels.Count == 0)
                    {
                        state.Warnings.Add(new Warning { TickIndex = state.TotalHD, Message = $"spell '{selection.SpellId}' is not on the {routedClassId} spell list" });
                    }
                    else if (!matchingLevels.Contains(selection.SpellLevel))
                    {
                        var levels = string.Join("/", matchingLevels.Distinct().OrderBy(level => level));
                        state.Warnings.Add(new Warning { TickIndex = state.TotalHD, Message = $"spell '{selection.SpellId}' is level {levels} for {routedClassId}, not {selection.SpellLevel}" });
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
                if (featureType is "domains" or "imported_source_domains" or "advance_spellcasting")
                    continue;

                if (selectedIds == null) continue;

                if (!_content.TryGetClassFeature(featureType, out var featureDef) || featureDef == null)
                {
                    var validKeys = string.Join(", ", state.PendingClassFeatureSelections.Keys.OrderBy(key => key, StringComparer.Ordinal));
                    state.Warnings.Add(new Warning
                    {
                        TickIndex = state.TotalHD,
                        Message = $"unknown class feature type '{featureType}'{(validKeys.Length == 0 ? string.Empty : $"; valid pending keys: {validKeys}")}"
                    });
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

    private void ApplySkillAllocations(
        CharacterState state,
        IReadOnlyCollection<SkillAllocation>? allocations)
    {
        if (allocations == null)
            return;

        foreach (var alloc in allocations)
        {
            // Unknown ids would otherwise silently consume skill points and materialise
            // a phantom skill on the sheet, so surface them the way unknown feats are.
            if (!_content.TryGetSkill(alloc.SkillId, out _))
            {
                state.Warnings.Add(new Warning { TickIndex = state.TotalHD, Message = $"unknown skill '{alloc.SkillId}'" });
                continue;
            }

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

    private static void EnsureEpicSpellcasting(
        CharacterState state,
        IReadOnlyCollection<SpellSelection>? currentSelections)
    {
        if (!state.Feats.Contains("feat:epic_spellcasting"))
            return;

        var requestedLists = (currentSelections ?? Array.Empty<SpellSelection>())
            .Select(selection => selection.ClassId)
            .Concat(state.Spellcasting.Keys.Where(EpicSpellcasting.IsSpellList))
            .Where(EpicSpellcasting.IsSpellList)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // A newly built character has no imported pseudo-class selection to identify the casting
        // stat. If all qualifying 9th-level sources agree, expose that list automatically.
        if (requestedLists.Count == 0)
        {
            var inferredAbilities = state.Spellcasting.Values
                .Where(caster => !EpicSpellcasting.IsSpellList(caster.ClassId)
                    && caster.MaxSpellLevel >= 9)
                .Select(caster => caster.CastingStat)
                .Where(ability => ability is Ability.INT or Ability.WIS or Ability.CHA)
                .Distinct()
                .ToList();
            if (inferredAbilities.Count == 1)
                requestedLists.Add(EpicSpellcasting.ListIdFor(inferredAbilities[0]));
        }

        foreach (var listId in requestedLists)
        {
            if (!EpicSpellcasting.TryGetCastingAbility(listId, out var castingAbility))
                continue;

            var eligibleCasters = state.Spellcasting.Values
                .Where(caster => !EpicSpellcasting.IsSpellList(caster.ClassId)
                    && caster.CastingStat == castingAbility
                    && caster.MaxSpellLevel >= 9)
                .ToList();
            if (eligibleCasters.Count == 0)
                continue;

            var spellcraftRanks = WholeSkillRanks(state, "skill:spellcraft");
            var arcanaRanks = WholeSkillRanks(state, "skill:knowledge_arcana");
            var religionRanks = WholeSkillRanks(state, "skill:knowledge_religion");
            var natureRanks = WholeSkillRanks(state, "skill:knowledge_nature");

            var arcaneSlots = eligibleCasters.Any(caster => caster.CastingType == CastingType.Arcane)
                && spellcraftRanks >= 24 && arcanaRanks >= 24
                    ? arcanaRanks / 10
                    : 0;
            var divineSlots = eligibleCasters.Any(caster => caster.CastingType == CastingType.Divine)
                && spellcraftRanks >= 24
                    ? Math.Max(religionRanks >= 24 ? religionRanks / 10 : 0,
                        natureRanks >= 24 ? natureRanks / 10 : 0)
                    : 0;
            var slots = arcaneSlots + divineSlots;

            if (!state.Spellcasting.TryGetValue(listId, out var epicCaster))
            {
                epicCaster = new SpellcastingState { ClassId = listId };
                state.Spellcasting[listId] = epicCaster;
            }

            epicCaster.CastingType = eligibleCasters.Any(caster => caster.CastingType == CastingType.Arcane)
                ? CastingType.Arcane
                : CastingType.Divine;
            epicCaster.CastingStat = castingAbility;
            epicCaster.CasterLevel = eligibleCasters.Max(caster => caster.CasterLevel);
            epicCaster.MaxSpellLevel = EpicSpellcasting.SpellLevel;
            epicCaster.SpellsPerDay = new Dictionary<int, int> { [EpicSpellcasting.SpellLevel] = slots };
            epicCaster.AcquisitionOverride = SpellAcquisition.Developed;
        }
    }

    /// <summary>
    /// SRD Leadership, "Leadership Modifiers". Reputation shifts the score whoever the leader is
    /// recruiting; the other two groups apply only to cohorts or only to followers, which is why
    /// a character ends up with two effective scores rather than one.
    ///
    /// Most of the table is a campaign fact and comes from <see cref="Character.LeadershipModifiers"/>.
    /// One entry is derivable and so is computed here rather than trusted as input: "Has a familiar,
    /// special mount, or animal companion -2". The remaining derivable one — "Recruits a cohort of
    /// a different alignment -1" — needs the cohort itself and is applied host-side by
    /// <c>CompanionResolver</c>.
    /// </summary>
    private static void ApplyLeadershipModifiers(CharacterState state, Character character)
    {
        var modifiers = character.LeadershipModifiers;
        var notes = state.LeadershipModifierNotes;

        void Note(string label, int value)
        {
            if (value != 0) notes.Add($"{label} {value:+#;-#;0}");
        }

        Note("Great renown", modifiers.GreatRenown ? 2 : 0);
        Note("Fairness and generosity", modifiers.FairnessAndGenerosity ? 1 : 0);
        Note("Special power", modifiers.SpecialPower ? 1 : 0);
        Note("Failure", modifiers.Failure ? -1 : 0);
        Note("Aloofness", modifiers.Aloofness ? -1 : 0);
        Note("Cruelty", modifiers.Cruelty ? -2 : 0);

        // Cohort side. A familiar, special mount or animal companion costs 2 whether the character
        // chose it or a class handed it over, so it is read off the resolved slots.
        var boundCompanion = state.CompanionSlots.Any(slot =>
            slot.LinkType is "familiar" or "improved_familiar" or "special_mount" or "animal_companion");
        var companionPenalty = boundCompanion ? -2 : 0;
        Note("Has a familiar, special mount, or animal companion (cohort only)", companionPenalty);
        Note("Caused the death of a cohort (cohort only)", -2 * Math.Max(0, modifiers.CohortDeathsCaused));

        // Follower side.
        Note("Has a stronghold or base of operations (followers only)", modifiers.HasStronghold ? 2 : 0);
        Note("Moves around a lot (followers only)", modifiers.MovesAroundALot ? -1 : 0);
        Note("Caused the death of other followers (followers only)", modifiers.CausedFollowerDeaths ? -1 : 0);

        state.LeadershipCohortScore = state.LeadershipScore + modifiers.CohortModifier + companionPenalty;
        state.LeadershipFollowerScore = state.LeadershipScore + modifiers.FollowerModifier;
    }

    private static int WholeSkillRanks(CharacterState state, string skillId) =>
        state.SkillHalfRanks.GetValueOrDefault(skillId) / 2;

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

                if (def != null)
                {
                    var unitWeight = item.WeightLbsOverride ?? def.WeightLbs;
                    pass.TotalWeightLbs += unitWeight * Math.Max(0, item.Quantity);
                }

                // Carried items count toward encumbrance but are not equipped and grant no effects.
                if (string.Equals(item.Slot, "carried", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Apply granted permabuffs from content, then any inline permabuffs on the entry.
                if (def != null)
                {
                    var weaponCountBeforeBuffs = pass.Weapons.Count;
                    foreach (var buff in def.GrantedPermabuffs)
                        buff.Apply(ctx);
                    // Auto-derive weapon/armor profile from content when not already pushed by permabuffs.
                    if (def.Weapon != null
                        && !pass.Weapons.Skip(weaponCountBeforeBuffs).Any(w => ReferenceEquals(w.Profile, def.Weapon)))
                    {
                        var weapon = new WeaponContribution
                        {
                            ItemId = item.ContentId ?? item.ItemId,
                            Profile = def.Weapon,
                            EnhancementBonus = item.EnhancementBonusOverride ?? def.EnhancementBonus,
                            MainHand = item.MainHand,
                            TwoHanded = item.TwoHanded,
                            DisplayName = def.Name
                        };
                        pass.Weapons.Add(weapon);

                        if (item.DoubleWeapon || def.Weapon.DoubleWeapon)
                        {
                            pass.Weapons.Add(new WeaponContribution
                            {
                                ItemId = item.ContentId ?? item.ItemId,
                                Profile = def.Weapon,
                                EnhancementBonus = item.EnhancementBonusOverride ?? def.EnhancementBonus,
                                MainHand = false,
                                TwoHanded = false,
                                DisplayName = def.Name,
                            });
                        }
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
        var typedContributions = state.PersistentBonusContributions
            .Select(contribution => (
                Target: contribution.Target,
                BonusType: contribution.BonusType,
                Value: contribution.Value))
            .Concat(pass.Contributions.SelectMany(entry => entry.Value.Select(value =>
                (
                    Target: entry.Key.Target,
                    BonusType: entry.Key.Type,
                    Value: value))))
            .GroupBy(entry => (entry.Target, entry.BonusType))
            .ToDictionary(group => group.Key, group => group.Select(entry => entry.Value).ToList());

        // --- Skill bonuses (typed per skill; two competence bonuses do not stack) ---
        foreach (var (key, values) in pass.SkillContributions)
        {
            var (skillId, type) = key;
            var aggregate = BonusStack.Aggregate(type, values);
            state.SkillBonuses.TryAdd(skillId, 0);
            state.SkillBonuses[skillId] += aggregate;
        }

        // --- Ability score bonuses (typed; e.g., +4 enhancement STR from gauntlets) ---
        foreach (var (key, values) in typedContributions)
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
                case BonusTarget.SaveFort:
                    state.BaseSaves.Fort += agg;
                    break;
                case BonusTarget.SaveRef:
                    state.BaseSaves.Ref += agg;
                    break;
                case BonusTarget.SaveWill:
                    state.BaseSaves.Will += agg;
                    break;
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
        foreach (var (key, values) in typedContributions)
        {
            if (key.Target != BonusTarget.AC) continue;
            var agg = BonusStack.Aggregate(key.BonusType, values);
            if (agg == 0) continue;
                state.AC.Components[key.BonusType] =
                state.AC.Components.GetValueOrDefault(key.BonusType) +
                (BonusStack.IsStacking(key.BonusType) ? agg : Math.Max(0, agg - state.AC.Components.GetValueOrDefault(key.BonusType)));
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
        FinalizeAttackLines(state, pass, typedContributions);

        // --- Encumbrance + speed reduction ---
        FinalizeEncumbrance(state, pass, armorContribs, shieldContribs);

        // Ability-derived AC features are evaluated only after equipment and load are final.
        // This is what makes the monk's unarmored/unencumbered condition real rather than a
        // permanent bonus that incorrectly survives putting on armor.
        ApplyAbilityACBonuses(state, armorContribs, shieldContribs);
        RecalculateArmorClass(state);
    }

    private static void ApplyAbilityACBonuses(
        CharacterState state,
        List<ArmorContribution> armors,
        List<ArmorContribution> shields)
    {
        var unarmored = armors.Count == 0 && shields.Count == 0;
        var unencumbered = state.Encumbrance.Load == LoadCategory.Light;

        var values = state.AbilityACBonuses
            .Where(bonus => (!bonus.RequiresUnarmored || unarmored)
                && (!bonus.RequiresUnencumbered || unencumbered))
            .GroupBy(bonus => bonus.BonusType)
            .ToDictionary(
                group => group.Key,
                group => group.Select(bonus =>
                {
                    var value = bonus.Value.Evaluate(state);
                    if (bonus.Ability.HasValue)
                    {
                        var modifier = state.AbilityModifier(bonus.Ability.Value);
                        value += bonus.PositiveOnly ? Math.Max(0, modifier) : modifier;
                    }
                    return value;
                }).ToList());

        foreach (var (bonusType, contributions) in values)
        {
            var value = BonusStack.Aggregate(bonusType, contributions);
            if (value == 0) continue;
            state.AC.Components[bonusType] = BonusStack.IsStacking(bonusType)
                ? state.AC.Components.GetValueOrDefault(bonusType) + value
                : Math.Max(state.AC.Components.GetValueOrDefault(bonusType), value);
        }
    }

    private static void RecalculateArmorClass(CharacterState state)
    {
        var componentTotal = state.AC.Components.Values.Sum();
        state.AC.Total = 10 + componentTotal + state.AC.DexContribution;
        var touchExcluded = new HashSet<BonusType>
        {
            BonusType.Armor,
            BonusType.Shield,
            BonusType.Natural,
            BonusType.NaturalEnhancement
        };
        var touchSum = state.AC.Components
            .Where(kv => !touchExcluded.Contains(kv.Key))
            .Sum(kv => kv.Value);
        state.AC.Touch = 10 + touchSum + state.AC.DexContribution;
        var flatComponents = state.AC.Components
            .Where(kv => kv.Key != BonusType.Dodge)
            .Sum(kv => kv.Value);
        state.AC.FlatFooted = 10 + flatComponents + Math.Min(0, state.AC.DexContribution);
    }

    private static void AddAbility(CharacterState state, Ability ability, int value)
    {
        var current = state.AbilityScores.GetScore(ability);
        state.AbilityScores.SetScore(ability, current + value);
    }

    private void FinalizeAttackLines(
        CharacterState state,
        EquipmentPass pass,
        Dictionary<(BonusTarget Target, BonusType BonusType), List<int>> typedContributions)
    {
        state.AttackLines.Clear();
        if (pass.Weapons.Count == 0) return;

        var mainHand = pass.Weapons.FirstOrDefault(w => w.MainHand) ?? pass.Weapons[0];
        var offHand = pass.Weapons.FirstOrDefault(w => !w.MainHand);
        var twoWeaponFighting = state.Feats.Contains("feat:two_weapon_fighting");

        var bab = state.EffectiveBAB;
        var strMod = state.AbilityModifier(Ability.STR);
        var dexMod = state.AbilityModifier(Ability.DEX);
        // SRD, incorporeal subtype: "It has no Strength score, so its Dexterity modifier applies
        // to both its melee attacks and its ranged attacks." Damage keeps the nonability's +0.
        var meleeAttackMod = state.HasAbility(Ability.STR) ? strMod : dexMod;

        // Generic typed attack bonus aggregation (excluding the weapon's own enhancement).
        int typedAttackBonus = 0;
        foreach (var (key, values) in typedContributions)
        {
            if (key.Target != BonusTarget.Attack) continue;
            typedAttackBonus += BonusStack.Aggregate(key.BonusType, values);
        }
        var typedDamageBonus = 0;
        foreach (var (key, values) in typedContributions)
        {
            if (key.Target != BonusTarget.Damage) continue;
            typedDamageBonus += BonusStack.Aggregate(key.BonusType, values);
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

        state.AttackLines.Add(BuildLine(mainHand, bab, strMod, dexMod, meleeAttackMod, typedAttackBonus,
            typedDamageBonus + WeaponBonus(state, mainHand, BonusTarget.Damage),
            WeaponBonus(state, mainHand, BonusTarget.Attack),
            attackPenalty: mainPenalty,
            isOffHand: false));

        if (offHand != null)
        {
            state.AttackLines.Add(BuildLine(offHand, bab, strMod, dexMod, meleeAttackMod, typedAttackBonus,
                typedDamageBonus + WeaponBonus(state, offHand, BonusTarget.Damage),
                WeaponBonus(state, offHand, BonusTarget.Attack),
                attackPenalty: offPenalty,
                isOffHand: true));
        }
    }

    private static int WeaponBonus(CharacterState state, WeaponContribution weapon, BonusTarget target)
    {
        return state.WeaponBonusContributions
            .Where(contribution => contribution.WeaponId == weapon.ItemId && contribution.Target == target)
            .GroupBy(contribution => contribution.BonusType)
            .Sum(group => BonusStack.Aggregate(group.Key, group.Select(contribution => contribution.Value)));
    }

    private static AttackLine BuildLine(WeaponContribution w, int bab, int strMod, int dexMod, int meleeAttackMod, int typedAttackBonus, int typedDamageBonus, int weaponAttackBonus, int attackPenalty, bool isOffHand)
    {
        var profile = w.Profile;
        var abilityMod = profile.Ranged && !profile.Thrown ? dexMod : meleeAttackMod;
        var damageMod = profile.Ranged && !profile.Thrown
            ? 0
            : (isOffHand ? FloorDivBy2(strMod) : (w.TwoHanded ? (strMod * 3) / 2 : strMod));
        var attackBase = bab + abilityMod + w.EnhancementBonus + typedAttackBonus + weaponAttackBonus + attackPenalty;

        var iterations = isOffHand ? 1 : IterativeCount(bab);
        var bonuses = new List<int>();
        for (int i = 0; i < iterations; i++)
            bonuses.Add(attackBase - 5 * i);

        var damageBonus = damageMod + w.EnhancementBonus + typedDamageBonus;
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
        // A nonability is not a score of zero and cannot use the carrying-capacity table.
        // In particular, incorporeal creatures have no Strength and "cannot exert force".
        var (light, medium, heavy) = state.HasAbility(Ability.STR)
            ? _rules.GetCarryingCapacity(state.AbilityScores.STR)
            : (0, 0, 0);
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
    /// <summary>
    /// Whether an effective-level rule aimed at a class reaches the given driver. A rule naming a
    /// base class reaches its variants too — see <see cref="HDDriver.VariantOf"/>. A driver the
    /// registry cannot resolve falls back to an exact id match rather than throwing: an unknown
    /// driver id has already been reported where it was ticked.
    /// </summary>
    private bool RuleTargetsDriver(EffectiveLevelRule rule, string driverId)
    {
        if (rule.TargetDriverId == driverId)
            return true;

        try
        {
            return _content.GetDriver(driverId) is HDDriver hd && hd.Targets(rule);
        }
        catch (KeyNotFoundException)
        {
            return false;
        }
    }

    public static bool FeatMatchesRestriction(FeatDefinition? feat, string restriction) => restriction switch
    {
        "fighter_bonus" => feat != null
            && (feat.Type == FeatType.FighterBonus || feat.Tags.Contains(FighterBonusTag)),
        "metamagic" => feat?.Type == FeatType.Metamagic,
        // Wizard bonus feat: "a metamagic feat, an item creation feat, or Spell Mastery" — the
        // one named exception is why this is not just the two types.
        "wizard_bonus" => feat != null
            && (feat.Type is FeatType.Metamagic or FeatType.ItemCreation
                || feat.Id == SpellMasteryFeatId),
        // A pool with exactly one member — an epic dragon's Improved Spell Capacity grants —
        // is still a slot rather than an outright grant, because the pick carries a choice
        // (which spell level the new slot sits at).
        _ => feat != null
            && restriction.StartsWith("feat:", StringComparison.Ordinal)
            && feat.Id == restriction
    };

    public const string FighterBonusTag = "fighter_bonus";
    public const string SpellMasteryFeatId = "feat:spell_mastery";

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
