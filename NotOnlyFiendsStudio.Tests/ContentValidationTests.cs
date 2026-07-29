using NotOnlyFiendsStudio.Studio;
using NotOnlyFiendsStudio.Models;
using NotOnlyFiendsStudio.PcGen;

namespace NotOnlyFiendsStudio.Tests;

public class ContentValidationTests
{
    [Fact]
    public void SRDContent_PassesValidation()
    {
        var registry = TestContentHelper.LoadAllPacks();
        registry.Validate();

        Assert.False(registry.HasErrors, string.Join("\n", registry.Errors.Select(e => e.Message)));
    }

    [Fact]
    public void SRDEquipment_LoadsCatalog()
    {
        var registry = TestContentHelper.LoadAllPacks();

        // Seed catalog should have weapons, armor, shields, wondrous, and rings.
        Assert.True(registry.TryGetEquipment("weapon:longsword", out var longsword));
        Assert.Equal("1d8", longsword!.Weapon!.Damage);
        Assert.True(registry.TryGetEquipment("armor:full_plate", out var fp));
        Assert.Equal(8, fp!.Armor!.ArmorBonus);
        Assert.True(registry.TryGetEquipment("shield:heavy_steel", out _));
        Assert.True(registry.TryGetEquipment("wondrous:cloak_of_resistance_3", out _));
        Assert.True(registry.TryGetEquipment("ring:protection_2", out _));
        Assert.True(registry.TryGetEquipment("wondrous:gauntlets_ogre_power", out _));
    }

    [Fact]
    public void SrdGoodsAndServices_LoadIntoTheGearCategory()
    {
        // goodsAndServices.html tables 2-8, extracted 2026-07-29. Before this, the "gear"
        // category was empty in every pack — a character could buy a longsword but not a
        // backpack. Prices are stored in copper (1 gp = 100 cp).
        var registry = TestContentHelper.LoadAllPacks();

        var gear = registry.GetAllEquipment()
            .Where(e => e.Category == EquipmentCategory.Gear)
            .ToList();
        Assert.True(gear.Count >= 150, $"expected the full goods table, found {gear.Count}");

        Assert.True(registry.TryGetEquipment("gear:backpack_empty", out var backpack));
        Assert.Equal(200, backpack!.PriceCp);   // 2 gp
        Assert.Equal(2, backpack.WeightLbs);

        Assert.True(registry.TryGetEquipment("gear:spyglass", out var spyglass));
        Assert.Equal(100_000, spyglass!.PriceCp); // 1,000 gp — the priciest piece of gear

        Assert.True(registry.TryGetEquipment("gear:galley", out var galley));
        Assert.Equal(3_000_000, galley!.PriceCp); // 30,000 gp, and why price is copper-denominated

        // Sub-priced rows are qualified with their parent row, not left as bare "Amazing".
        Assert.True(registry.TryGetEquipment("gear:lock_amazing", out var amazing));
        Assert.Equal("Lock, amazing", amazing!.Name);
        Assert.Equal(15_000, amazing.PriceCp);   // 150 gp

        // Sub-pound items floor to 0, matching the existing convention (weapon:dart, 1/2 lb).
        Assert.True(registry.TryGetEquipment("gear:piton", out var piton));
        Assert.Equal(0, piton!.WeightLbs);

        // Holly and mistletoe is priced "-" in the SRD because it is free, not unpriced.
        Assert.True(registry.TryGetEquipment("gear:holly_and_mistletoe", out var holly));
        Assert.Equal(0, holly!.PriceCp);
    }

    [Fact]
    public void SrdAmmunition_AndTheRemainingMundaneWeaponGaps_Load()
    {
        // weapons.html and armor.html, extracted 2026-07-29. Every base weapon, armor and shield
        // was already present; what was missing was all ammunition (the category held nothing at
        // all, so no bow was usable), the net, and the three armor "Extras" rows.
        var registry = TestContentHelper.LoadAllPacks();

        // The four mundane ammunition rows off the weapon table. Magic ammunition (screaming
        // bolt, slaying/sleep arrows) also lands in this category, so count only the mundane set.
        var ammo = registry.GetAllEquipment()
            .Where(e => e.Category == EquipmentCategory.Ammunition && !e.Tags.Contains("magic"))
            .ToList();
        Assert.Equal(4, ammo.Count);

        Assert.True(registry.TryGetEquipment("ammunition:arrows_20", out var arrows));
        Assert.Equal(100, arrows!.PriceCp);      // 1 gp
        Assert.Equal(3, arrows.WeightLbs);

        Assert.True(registry.TryGetEquipment("ammunition:sling_bullets_10", out var bullets));
        Assert.Equal(10, bullets!.PriceCp);      // 1 sp — the one ammunition priced in silver
        Assert.Equal(5, bullets.WeightLbs);

        // "Net ... 20 gp ... 10 ft. ... 6 lb." — an exotic weapon that deals no damage.
        Assert.True(registry.TryGetEquipment("weapon:net", out var net));
        Assert.Equal(2000, net!.PriceCp);
        Assert.Equal(10, net.Weapon!.RangeFt);
        Assert.Equal("exotic", net.Weapon.Proficiency);

        // Armor table "Extras": armor spikes +50 gp/+10 lb., shield spikes +10 gp/+5 lb.,
        // locked gauntlet 8 gp/+5 lb. Stored as the increment, since each rides on a host item.
        Assert.True(registry.TryGetEquipment("gear:armor_spikes", out var aspikes));
        Assert.Equal(5000, aspikes!.PriceCp);
        Assert.Equal(10, aspikes.WeightLbs);

        Assert.True(registry.TryGetEquipment("gear:shield_spikes", out var sspikes));
        Assert.Equal(1000, sspikes!.PriceCp);
        Assert.Equal(5, sspikes.WeightLbs);

        Assert.True(registry.TryGetEquipment("gear:locked_gauntlet", out var lg));
        Assert.Equal(800, lg!.PriceCp);
        Assert.Equal(5, lg.WeightLbs);
    }

    [Fact]
    public void SrdRingsRodsAndStaffs_Load()
    {
        // magicItemsPRR.html and magicItemsSSW.html, extracted 2026-07-29. The rod and staff
        // categories held nothing at all; rings held only the +1..+10 protection ladder.
        var registry = TestContentHelper.LoadAllPacks();

        var rods = registry.GetAllEquipment().Where(e => e.Category == EquipmentCategory.Rod).ToList();
        var staffs = registry.GetAllEquipment().Where(e => e.Category == EquipmentCategory.Staff).ToList();
        Assert.Equal(35, staffs.Count);
        Assert.True(rods.Count >= 30, $"expected the metamagic tiers too, found {rods.Count}");

        Assert.True(registry.TryGetEquipment("staff:power", out var power));
        Assert.Equal(211_000 * 100, power!.PriceCp);

        // Prices embedded after an in-description table still parse (these three sit after a
        // "table-..." anchor, which naive slicing cuts the entry off before).
        Assert.True(registry.TryGetEquipment("rod:wonder", out var wonder));
        Assert.Equal(12_000 * 100, wonder!.PriceCp);
        Assert.True(registry.TryGetEquipment("ring:elemental_command", out var elemental));
        Assert.Equal(200_000 * 100, elemental!.PriceCp);

        // A tiered price clause becomes one item per tier, not one item at the cheapest price.
        Assert.True(registry.TryGetEquipment("rod:metamagic_extend_lesser", out var lesser));
        Assert.True(registry.TryGetEquipment("rod:metamagic_extend_normal", out var normal));
        Assert.True(registry.TryGetEquipment("rod:metamagic_extend_greater", out var greater));
        Assert.Equal(3_000 * 100, lesser!.PriceCp);
        Assert.Equal(11_000 * 100, normal!.PriceCp);
        Assert.Equal(24_500 * 100, greater!.PriceCp);

        Assert.True(registry.TryGetEquipment("ring:wizardry_iv", out var wiz4));
        Assert.Equal(100_000 * 100, wiz4!.PriceCp);

        // The generic "Ring of Protection +1" row is deliberately not re-extracted — the
        // existing ring:protection_1..5 / ring:ring_of_protection_6..10 ladder supersedes it.
        Assert.False(registry.TryGetEquipment("ring:protection", out _));
        Assert.True(registry.TryGetEquipment("ring:protection_1", out _));
    }

    [Fact]
    public void SrdWondrousItems_Load()
    {
        // magicItemsWI.html, extracted 2026-07-29 — the largest single batch. The packs held
        // 69 wondrous items, almost all tiered stat ladders; the page describes ~190 distinct
        // ones. Existing entries are matched by name and left alone rather than duplicated.
        var registry = TestContentHelper.LoadAllPacks();

        var wondrous = registry.GetAllEquipment()
            .Where(e => e.Category == EquipmentCategory.Wondrous)
            .ToList();
        Assert.True(wondrous.Count >= 300, $"expected the SRD page plus the existing ladder, found {wondrous.Count}");

        Assert.True(registry.TryGetEquipment("wondrous:boots_of_striding_and_springing", out var boots));
        Assert.Equal(5_500 * 100, boots!.PriceCp);
        Assert.Equal("feet", boots.Slot);

        Assert.True(registry.TryGetEquipment("wondrous:circlet_of_persuasion", out var circlet));
        Assert.Equal(4_500 * 100, circlet!.PriceCp);
        Assert.Equal("head", circlet.Slot);

        // "Bracers of Armor" is a wrists item; a naive keyword match calls it torso because
        // the name contains "armor".
        Assert.True(registry.TryGetEquipment("wondrous:bracers_of_armor_2", out var bracers));
        Assert.Equal("wrists", bracers!.Slot);
        Assert.Equal(4_000 * 100, bracers.PriceCp);

        // The packs carried Bracers of Armor +1/+3/+5/+8 only; the even tiers were missing
        // because the enhancement bonus is part of the item's identity, not a suffix to strip.
        foreach (var n in new[] { 1, 2, 3, 4, 5, 6, 7, 8 })
            Assert.True(registry.TryGetEquipment($"wondrous:bracers_of_armor_{n}", out _)
                        || registry.TryGetEquipment($"wondrous:bracers_armor_{n}", out _),
                        $"Bracers of Armor +{n} missing");

        // Items priced by a variant table rather than a "Price N gp" clause.
        Assert.True(registry.TryGetEquipment("wondrous:bag_of_holding_type_iv", out var bag));
        Assert.Equal(10_000 * 100, bag!.PriceCp);
        Assert.Equal(60, bag.WeightLbs);

        Assert.True(registry.TryGetEquipment("wondrous:ioun_stone_lavender_and_green", out var ioun));
        Assert.Equal(40_000 * 100, ioun!.PriceCp);

        Assert.True(registry.TryGetEquipment("wondrous:necklace_of_fireballs_type_vii", out var necklace));
        Assert.Equal(8_700 * 100, necklace!.PriceCp);
        Assert.Equal("neck", necklace.Slot);
    }

    [Fact]
    public void SrdArmsAndArmor_ReplaceTheRetiredPcgenPack()
    {
        // weapons.html, magicItemsAW.html, epicMagicItems.html and epicArtifacts.html, extracted
        // 2026-07-29 to retire pcgen_srd. That pack's 178 weapon/armor/shield entries carried base
        // stats only — no enhancement bonuses, no prices for the magic items, no descriptions.
        var registry = TestContentHelper.LoadAllPacks();

        // The pack is gone: nothing in the catalog may come from a PCGen LST conversion.
        Assert.DoesNotContain(registry.GetAllEquipment(), e => e.Id == "weapon:flurry_of_blows");

        // --- mundane weapons off Table: Weapons ---
        Assert.True(registry.TryGetEquipment("weapon:falchion", out var falchion));
        Assert.Equal(75 * 100, falchion!.PriceCp);
        Assert.Equal("2d4", falchion.Weapon!.Damage);
        Assert.Equal(18, falchion.Weapon.CritRangeLow);

        // A double weapon stores its primary head only; the off-head lives in the description.
        Assert.True(registry.TryGetEquipment("weapon:hammer_gnome_hooked", out var hooked));
        Assert.Equal("1d8", hooked!.Weapon!.Damage);
        Assert.True(hooked.Weapon.DoubleWeapon);
        Assert.Contains("1d4", hooked.Description);

        // The composite bows collapse to one item each — pcgen_srd carried a garbled "+0" twin.
        Assert.True(registry.TryGetEquipment("weapon:longbow_composite", out var comp));
        Assert.Equal(110, comp!.Weapon!.RangeFt);
        Assert.False(registry.TryGetEquipment("weapon:longbow_composite_0", out _));

        // --- special materials are named SRD entries, priced with the material surcharge ---
        // pcgen_srd priced this at the bare breastplate's 200 gp and never applied the DR.
        Assert.True(registry.TryGetEquipment("armor:adamantine_breastplate", out var adam));
        Assert.Equal(10_200 * 100, adam!.PriceCp);
        Assert.Equal(-3, adam.Armor!.CheckPenalty);   // adamantine is masterwork: -4 lessened by 1
        Assert.Contains(adam.GrantedPermabuffs, p => p is GrantDR { Value: 2 });

        Assert.True(registry.TryGetEquipment("armor:mithral_shirt", out var mithral));
        Assert.Equal(1_100 * 100, mithral!.PriceCp);
        Assert.Equal(6, mithral.Armor!.MaxDex);
        Assert.Equal(0, mithral.Armor.CheckPenalty);
        Assert.Equal(10, mithral.Armor.ArcaneFailurePct);

        // --- specific magic armor folds the enhancement into the armor bonus ---
        Assert.True(registry.TryGetEquipment("armor:celestial_armor", out var celestial));
        Assert.Equal(22_400 * 100, celestial!.PriceCp);
        Assert.Equal(8, celestial.Armor!.ArmorBonus);      // +3 on chainmail's +5
        Assert.Equal(ArmorKind.Light, celestial.Armor.Kind);

        // A tiered price clause becomes one item per tier.
        foreach (var (id, gp) in new[] { ("weapon:luck_blade_0_wishes", 22_060),
                                         ("weapon:luck_blade_1_wish", 62_360),
                                         ("weapon:luck_blade_2_wishes", 102_660),
                                         ("weapon:luck_blade_3_wishes", 142_960) })
        {
            Assert.True(registry.TryGetEquipment(id, out var blade), id);
            Assert.Equal(gp * 100, blade!.PriceCp);
        }

        // --- epic items ---
        // Dragonskin armor is +5 full plate (heavy), not the medium armor pcgen_srd recorded.
        Assert.True(registry.TryGetEquipment("armor:dragonskin_armor_red", out var dragonskin));
        Assert.Equal(564_550 * 100, dragonskin!.PriceCp);
        Assert.Equal(ArmorKind.Heavy, dragonskin.Armor!.Kind);
        Assert.Equal(13, dragonskin.Armor.ArmorBonus);
        Assert.Contains(dragonskin.GrantedPermabuffs, p => p is GrantImmunity { Immunity: "fire" });

        // All ten colour variants of both dragon items are present.
        foreach (var colour in new[] { "black", "blue", "brass", "bronze", "copper",
                                       "gold", "green", "red", "silver", "white" })
        {
            Assert.True(registry.TryGetEquipment($"armor:dragonskin_armor_{colour}", out _), colour);
            Assert.True(registry.TryGetEquipment($"shield:bulwark_of_the_great_dragon_{colour}", out _), colour);
        }

        // Artifacts have no market price in the SRD, so they store 0 rather than a guess.
        Assert.True(registry.TryGetEquipment("weapon:axe_of_the_dwarvish_lords", out var axe));
        Assert.Equal(0, axe!.PriceCp);

        // PCGen modelled a rod's melee attack as a separate weapon; the catalog has one rod.
        Assert.False(registry.TryGetEquipment("weapon:rod_besiegement", out _));
        Assert.True(registry.TryGetEquipment("rod:besiegement", out _));
    }

    [Fact]
    public void RetiredPcgenEquipmentNames_StillResolveForPcgImport()
    {
        // PcgIdMapper resolves equipment by display name, so retiring pcgen_srd could silently
        // start dropping items on import. Every LST-style name that pack used to answer must
        // still resolve, either by name in srd_core or through an explicit override.
        var registry = TestContentHelper.LoadAllPacks();
        var mapper = new PcgIdMapper();

        string[] names =
        {
            "Shield, Light Wood", "Shield, Light Metal", "Shield, Heavy Wood",
            "Shield, Heavy Metal", "Shield, Tower Wood", "Shieldbash (Light)",
            "Shieldbash (Heavy)", "Sword, Short", "Shuriken", "Longbow (Composite)",
            "Shortbow (Composite)", "Cold Iron Longsword", "Silver Dagger",
            "Rod (Besiegement)", "Rod of the Black Wyrm", "Staff (Fiery Power)",
            "Demon Armor Claw Attack", "Flurry of Blows",
            "Armor Spikes", "Gauntlet, Locked", "Sun Blade", "Elven Chain", "Oathbow",
        };

        foreach (var name in names)
        {
            var id = mapper.MapEquipment(name, registry);
            Assert.False(string.IsNullOrEmpty(id), $"'{name}' no longer resolves to any item");
            Assert.True(registry.TryGetEquipment(id!, out _), $"'{name}' resolves to missing id '{id}'");
        }
    }

    [Fact]
    public void BrokenRacialHDReference_ProducesError()
    {
        var registry = new ContentRegistry();
        registry.RegisterRace(new RaceDefinition
        {
            Id = "race:test_race",
            Name = "Test",
            Type = CreatureType.Humanoid,
            Size = Size.Medium,
            RacialHDDriverId = "racial_hd:nonexistent"
        });
        registry.Validate();

        Assert.True(registry.HasErrors);
        Assert.Contains(registry.Errors, e =>
            e.Kind == ContentErrorKind.BrokenReference &&
            e.Message.Contains("racial_hd:nonexistent"));
    }

    [Fact]
    public void BrokenHasFeatPrerequisite_ProducesError()
    {
        var registry = new ContentRegistry();
        registry.RegisterFeat(new FeatDefinition
        {
            Id = "feat:bad_feat",
            Name = "Bad Feat",
            Prerequisites = new List<Prerequisite>
            {
                new HasFeat { FeatId = "feat:nonexistent_feat" }
            }
        });
        registry.Validate();

        Assert.True(registry.HasErrors);
        Assert.Contains(registry.Errors, e =>
            e.Kind == ContentErrorKind.BrokenReference &&
            e.Message.Contains("nonexistent_feat"));
    }

    [Fact]
    public void BrokenMinClassLevel_ProducesError()
    {
        var registry = new ContentRegistry();
        registry.RegisterFeat(new FeatDefinition
        {
            Id = "feat:bad_feat",
            Name = "Bad Feat",
            Prerequisites = new List<Prerequisite>
            {
                new MinClassLevel { ClassId = "class:nonexistent", Value = 4 }
            }
        });
        registry.Validate();

        Assert.True(registry.HasErrors);
        Assert.Contains(registry.Errors, e =>
            e.Kind == ContentErrorKind.BrokenReference &&
            e.Message.Contains("class:nonexistent"));
    }

    [Fact]
    public void BrokenGrantBonusFeat_ProducesError()
    {
        var registry = new ContentRegistry();
        registry.RegisterDriver(new HDDriver
        {
            Kind = DriverKind.Class,
            Id = "class:test",
            Name = "Test",
            HitDie = 10,
            BABProgression = BABProgression.Good,
            SaveProgression = new SaveProgression { Fort = ProgressionRate.Good, Ref = ProgressionRate.Poor, Will = ProgressionRate.Poor },
            LevelPermabuffs = new Dictionary<int, List<Permabuff>>
            {
                { 1, new List<Permabuff> { new GrantBonusFeat { FeatId = "feat:nonexistent_feat" } } }
            }
        });
        registry.Validate();

        Assert.True(registry.HasErrors);
        Assert.Contains(registry.Errors, e =>
            e.Kind == ContentErrorKind.BrokenReference &&
            e.Message.Contains("nonexistent_feat"));
    }

    [Fact]
    public void ValidContent_NoErrors()
    {
        var registry = new ContentRegistry();
        registry.RegisterRace(new RaceDefinition
        {
            Id = "race:human",
            Name = "Human",
            Type = CreatureType.Humanoid,
            Size = Size.Medium
        });
        registry.RegisterDriver(new HDDriver
        {
            Kind = DriverKind.Class,
            Id = "class:fighter",
            Name = "Fighter",
            HitDie = 10,
            BABProgression = BABProgression.Good,
            SaveProgression = new SaveProgression { Fort = ProgressionRate.Good, Ref = ProgressionRate.Poor, Will = ProgressionRate.Poor }
        });
        registry.RegisterFeat(new FeatDefinition
        {
            Id = "feat:power_attack",
            Name = "Power Attack"
        });
        registry.Validate();

        Assert.False(registry.HasErrors);
    }

    [Fact]
    public void MultipleContentRoots_MergeCorrectly()
    {
        var registry = TestContentHelper.LoadAllPacks();

        var countBefore = registry.GetAllFeats().Count();

        // Simulate a homebrew feat by registering directly
        registry.RegisterFeat(new FeatDefinition
        {
            Id = "feat:custom_feat",
            Name = "Custom Feat"
        });

        var countAfter = registry.GetAllFeats().Count();
        Assert.Equal(countBefore + 1, countAfter);
        Assert.NotNull(registry.GetFeat("feat:custom_feat"));

        // Original feats still present
        Assert.NotNull(registry.GetFeat("feat:power_attack"));
    }

    [Fact]
    public void SameIdOverride_LaterWins()
    {
        var registry = new ContentRegistry();
        registry.RegisterFeat(new FeatDefinition { Id = "feat:test_feat", Name = "Version 1" });
        registry.RegisterFeat(new FeatDefinition { Id = "feat:test_feat", Name = "Version 2" });

        Assert.Equal("Version 2", registry.GetFeat("feat:test_feat").Name);
    }
}
