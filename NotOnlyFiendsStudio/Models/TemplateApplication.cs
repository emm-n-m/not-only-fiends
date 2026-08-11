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
            state.IsLiving = CreatureTypes.IsLiving(state.Type);

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
