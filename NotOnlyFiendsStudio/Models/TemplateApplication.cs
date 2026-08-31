namespace NotOnlyFiendsStudio.Models;

// The single code path that applies a template to a character's state, whether at creation
// (inherited templates, acquisitionHD == null) or mid-timeline (acquired templates and
// capstone-granted ones, acquisitionHD == the 1-based HD whose tick is starting).
public static class TemplateApplication
{
    public static void Apply(PermabuffContext ctx, TemplateDriver template, int? acquisitionHD)
    {
        var state = ctx.State;

        // A template applies once. A capstone can fire again through the cross-driver
        // effective-level catch-up, and a saved character may carry both the template id
        // and a class that grants it.
        if (state.TemplateIds.Contains(template.Id))
            return;

        // Checked against the base creature, before this template mutates it.
        foreach (var prereq in template.ApplicabilityPrerequisites)
        {
            if (!prereq.IsMet(state))
                state.Warnings.Add(new Warning { TickIndex = acquisitionHD ?? 0, Message = $"applicability prerequisite not met for template {template.Name}: {prereq.Description}" });
        }

        state.TemplateIds.Add(template.Id);

        var baseType = state.Type;

        if (template.TypeOverride.HasValue)
            state.Type = template.TypeOverride.Value;
        else if (template.TypeOverridesByBaseType.TryGetValue(baseType, out var conditionalType))
            state.Type = conditionalType;

        // A template that moves the creature to undead or construct has made it non-living by
        // that fact alone — the lich and vampire templates say so nowhere else. Only recompute
        // when the type actually moved, so a race's explicit override survives templates that
        // leave the type alone.
        if (state.Type != baseType)
        {
            state.IsLiving = CreatureTypes.IsLiving(state.Type);

            // The new type brings its weapon proficiencies with it, and for the same reason: a
            // wizard who becomes a lich is undead, so he is proficient with all simple weapons
            // whatever his class list said. Granted at the acquisition HD like everything else a
            // template does, so the timeline before it is unaffected. Not reversed on revocation
            // — a proficiency cannot be told apart from one the character also has by class, so
            // it follows the "cannot invert, effect left in place" convention below.
            foreach (var featId in CreatureTypes.WeaponProficiencyFeats(state.Type))
                new GrantBonusFeat { FeatId = featId }.Apply(ctx);
        }

        state.RacialHitDieSizeAdjustment += template.RacialHitDieSizeAdjustment;
        if (template.HitDieSizeFloor.HasValue)
        {
            state.HitDieSizeFloor = Math.Max(state.HitDieSizeFloor, template.HitDieSizeFloor.Value);
            // "Increase all current and future Hit Dice to d12s": the floor restates dice
            // already banked by earlier ticks. Saved rolls stay untouched; FinalizeHitPoints
            // re-derives HP from the restated sizes. No-op at creation (nothing banked yet).
            foreach (var die in state.HitDice)
                die.DieSize = Math.Max(die.DieSize, state.HitDieSizeFloor);
        }

        foreach (var subtype in template.SubtypeAdditions)
            state.Subtypes.Add(subtype);

        if (template.SubtypeAdditions.Contains(CreatureTypes.IncorporealSubtype))
            state.IsCorporeal = false;

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
        if (template.NaturalArmorFloor.HasValue)
            state.NaturalArmor = Math.Max(state.NaturalArmor, template.NaturalArmorFloor.Value);

        if (template.SpellResistanceFloor.HasValue)
            state.SpellResistance = Math.Max(state.SpellResistance ?? 0, template.SpellResistanceFloor.Value);

        foreach (var (mode, speed) in template.SpeedModifiers)
        {
            if (state.BaseSpeeds.ContainsKey(mode))
                state.BaseSpeeds[mode] += speed;
            else
                state.BaseSpeeds[mode] = speed;
            state.Speeds[mode] = state.BaseSpeeds[mode];
        }
        if (template.FlyManeuverability.HasValue)
            state.FlyManeuverability = template.FlyManeuverability;

        state.LevelAdjustment += template.LevelAdjustment;

        foreach (var attack in template.NaturalAttacks)
            state.NaturalAttacks.Add(attack);

        foreach (var buff in template.CreationPermabuffs)
            buff.Apply(ctx);

        // At creation, derived speeds resolve once after every template has applied
        // (ReplayStudio, so ordering between templates is preserved). Mid-timeline there is
        // no later pass, so resolve this template's rules now.
        if (acquisitionHD != null)
            ResolveDerivedSpeeds(state, template);
    }

    /// <summary>
    /// Removes a previously applied template — the ascension case, where acquiring one
    /// template consumes another (an alu-fiend promoted to archfiend stops being an
    /// alu-fiend). Deltas are subtracted, known creation buffs inverted, the creature type
    /// rebuilt from the base race up through the still-applied templates, and the template's
    /// scaling-formula targets reset so they stop tracking from this tick on. Not invertible,
    /// and deliberately left in place: floors already banked (hit-die sizes, natural-armor or
    /// SR floors taken by Math.Max), max-semantics buffs (GrantMovement, GrantTurnResistance),
    /// and anything a RevokeAbility/RevokeSLA already removed — each unhandled buff warns.
    /// </summary>
    public static void Revoke(PermabuffContext ctx, TemplateDriver template)
    {
        var state = ctx.State;
        if (!state.TemplateIds.Remove(template.Id))
            return; // never applied, or already revoked — nothing to unwind

        if (template.AbilityModifiers != null)
        {
            state.AbilityScores.STR -= template.AbilityModifiers.STR;
            state.AbilityScores.DEX -= template.AbilityModifiers.DEX;
            state.AbilityScores.CON -= template.AbilityModifiers.CON;
            state.AbilityScores.INT -= template.AbilityModifiers.INT;
            state.AbilityScores.WIS -= template.AbilityModifiers.WIS;
            state.AbilityScores.CHA -= template.AbilityModifiers.CHA;
        }

        state.LevelAdjustment -= template.LevelAdjustment;
        state.RacialHitDieSizeAdjustment -= template.RacialHitDieSizeAdjustment;

        foreach (var subtype in template.SubtypeAdditions)
            state.Subtypes.Remove(subtype);

        if (template.NaturalArmor.HasValue)
            state.NaturalArmor -= template.NaturalArmor.Value;

        foreach (var (mode, speed) in template.SpeedModifiers)
        {
            if (!state.BaseSpeeds.ContainsKey(mode))
                continue;
            state.BaseSpeeds[mode] -= speed;
            if (state.BaseSpeeds[mode] <= 0)
            {
                state.BaseSpeeds.Remove(mode);
                state.Speeds.Remove(mode);
            }
            else
            {
                state.Speeds[mode] = state.BaseSpeeds[mode];
            }
        }

        foreach (var attack in template.NaturalAttacks)
            state.NaturalAttacks.RemoveAll(a => a.Name == attack.Name);

        // A revoked template's scaling formulas stop tracking; reset their targets so the
        // last-set value does not linger. Formulas of the templates that remain re-set
        // theirs later in this same tick.
        foreach (var formula in template.ScalingFormulas)
        {
            if (formula.Target == AttributeTarget.SpellResistance)
                state.SpellResistance = null;
            else
                state.Warnings.Add(new Warning
                {
                    TickIndex = state.TotalHD,
                    Message = $"revoking template {template.Name}: scaling formula target {formula.Target} cannot be reset; last value kept",
                });
        }

        foreach (var buff in template.CreationPermabuffs)
        {
            switch (buff)
            {
                case GrantAbility grant:
                    state.Abilities.RemoveAll(a => a.Id == grant.Ability.Id);
                    break;
                case GrantSLA grant:
                    state.SLAs.RemoveAll(s => s.Id == grant.SLA.Id);
                    break;
                case GrantImmunity grant:
                    state.Immunities.Remove(grant.Immunity);
                    break;
                case GrantSkillBonus grant:
                    if (state.SkillBonuses.ContainsKey(grant.SkillId))
                    {
                        state.SkillBonuses[grant.SkillId] -= grant.Value;
                        if (state.SkillBonuses[grant.SkillId] == 0)
                            state.SkillBonuses.Remove(grant.SkillId);
                    }
                    break;
                case ModifyAttribute modify:
                    new ModifyAttribute
                    {
                        Target = modify.Target,
                        ResistanceElement = modify.ResistanceElement,
                        AbilityScore = modify.AbilityScore,
                        Value = -modify.Value,
                    }.Apply(ctx);
                    break;
                case GrantDR grant:
                {
                    var entry = state.DamageReduction.FirstOrDefault(dr =>
                        string.Equals(dr.BypassedBy, grant.BypassedBy, StringComparison.OrdinalIgnoreCase));
                    if (entry != null && entry.Value <= grant.Value)
                        state.DamageReduction.Remove(entry);
                    break;
                }
                case ApplyTemplate chained:
                    if (ctx.Content != null && ctx.Content.TryGetTemplate(chained.TemplateId, out var chainedTemplate)
                        && chainedTemplate != null)
                        Revoke(ctx, chainedTemplate);
                    break;
                default:
                    state.Warnings.Add(new Warning
                    {
                        TickIndex = state.TotalHD,
                        Message = $"revoking template {template.Name}: cannot invert {buff.GetType().Name}; its effect is left in place",
                    });
                    break;
            }
        }

        // Rebuild the creature type from the bottom of the stack: base race, then every
        // still-applied template's override in application order. Same convention as
        // application: IsLiving follows the type only when the type actually moved.
        var rebuiltType = state.BaseRaceType;
        foreach (var appliedId in state.TemplateIds)
        {
            if (ctx.Content == null || !ctx.Content.TryGetTemplate(appliedId, out var applied) || applied == null)
                continue;
            if (applied.TypeOverride.HasValue)
                rebuiltType = applied.TypeOverride.Value;
            else if (applied.TypeOverridesByBaseType.TryGetValue(rebuiltType, out var conditional))
                rebuiltType = conditional;
        }
        if (rebuiltType != state.Type)
        {
            state.Type = rebuiltType;
            state.IsLiving = CreatureTypes.IsLiving(rebuiltType);
        }
    }

    public static void ResolveDerivedSpeeds(CharacterState state, TemplateDriver template)
    {
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
