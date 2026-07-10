using NotOnlyFiendsStudio.Studio;
using NotOnlyFiendsStudio.Models;

namespace NotOnlyFiendsStudio.Tests;

public class DomainTests
{
    private (ContentRegistry registry, ReplayStudio engine) CreateStudio()
    {
        var registry = TestContentHelper.LoadAllPacks();
        return (registry, new ReplayStudio(registry));
    }

    [Fact]
    public void ClericWithDomains_GetsDomainAbilities()
    {
        var (_, engine) = CreateStudio();

        var character = new Character
        {
            Name = "Cleric 1",
            RaceId = "human",
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 14, DEX = 10, CON = 14, INT = 10, WIS = 16, CHA = 8
            },
            Ticks = new List<Tick>
            {
                new()
                {
                    DriverId = "class:cleric",
                    Choices = new TickChoices
                    {
                        ClassFeatureChoices = new Dictionary<string, List<string>>
                        {
                            ["domains"] = new() { "domain:knowledge", "domain:war" }
                        }
                    }
                }
            }
        };

        var state = engine.Evaluate(character);

        Assert.Contains(state.Abilities, a => a.Id == "domain_knowledge_power");
        Assert.Contains(state.Abilities, a => a.Id == "domain_war_power");
    }

    [Fact]
    public void ClericWithDomains_DomainsTrackedInState()
    {
        var (_, engine) = CreateStudio();

        var character = new Character
        {
            Name = "Cleric 1",
            RaceId = "human",
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 14, DEX = 10, CON = 14, INT = 10, WIS = 16, CHA = 8
            },
            Ticks = new List<Tick>
            {
                new()
                {
                    DriverId = "class:cleric",
                    Choices = new TickChoices
                    {
                        ClassFeatureChoices = new Dictionary<string, List<string>>
                        {
                            ["domains"] = new() { "domain:knowledge", "domain:war" }
                        }
                    }
                }
            }
        };

        var state = engine.Evaluate(character);

        Assert.Equal(2, state.Domains.Count);
        Assert.Contains("domain:knowledge", state.Domains);
        Assert.Contains("domain:war", state.Domains);
        Assert.Equal(0, state.PendingDomainSelections.Values.Sum());
        // Both domains must be owned by the cleric (the granting class).
        Assert.Equal("class:cleric", state.DomainOwners["domain:knowledge"]);
        Assert.Equal("class:cleric", state.DomainOwners["domain:war"]);
    }

    [Fact]
    public void ClericWithDomains_GetsBonusSpellSlots()
    {
        var (_, engine) = CreateStudio();

        var character = new Character
        {
            Name = "Cleric 5",
            RaceId = "human",
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 14, DEX = 10, CON = 14, INT = 10, WIS = 16, CHA = 8
            },
            Ticks = new List<Tick>
            {
                new()
                {
                    DriverId = "class:cleric",
                    Choices = new TickChoices
                    {
                        ClassFeatureChoices = new Dictionary<string, List<string>>
                        {
                            ["domains"] = new() { "domain:knowledge", "domain:war" }
                        }
                    }
                },
                new() { DriverId = "class:cleric" },
                new() { DriverId = "class:cleric" },
                new() { DriverId = "class:cleric" },
                new() { DriverId = "class:cleric" }
            }
        };

        var state = engine.Evaluate(character);

        var sc = state.Spellcasting["class:cleric"];
        // Cleric 5 has spells at levels 0-3
        // Domain bonus slots: +2 per level 1-3 (2 domains, not at level 0)
        Assert.Equal(2, sc.DomainBonusSlots[1]);
        Assert.Equal(2, sc.DomainBonusSlots[2]);
        Assert.Equal(2, sc.DomainBonusSlots[3]);
        Assert.False(sc.DomainBonusSlots.ContainsKey(0));
    }

    [Fact]
    public void DomainSpellSelection_RoutesToOwningClass()
    {
        // A spell selection with ClassId "domain:war" must be routed to the cleric's
        // spellcasting state (the class that owns the domain), not dropped as unknown.
        var (registry, engine) = CreateStudio();
        // Confirm the spell exists in the corpus before the test depends on it.
        Assert.Contains(registry.GetAllSpells(), s => s.Id == "magic_weapon");

        var character = new Character
        {
            Name = "Cleric 1",
            RaceId = "human",
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 14, DEX = 10, CON = 14, INT = 10, WIS = 16, CHA = 8
            },
            Ticks = new List<Tick>
            {
                new()
                {
                    DriverId = "class:cleric",
                    Choices = new TickChoices
                    {
                        ClassFeatureChoices = new Dictionary<string, List<string>>
                        {
                            ["domains"] = new() { "domain:war" }
                        },
                        SpellSelections = new List<SpellSelection>
                        {
                            new() { ClassId = "domain:war", SpellLevel = 1, SpellId = "magic_weapon" }
                        }
                    }
                }
            }
        };

        var state = engine.Evaluate(character);

        Assert.DoesNotContain(state.Warnings, w => w.Contains("unknown spellcasting class"));
        var sc = state.Spellcasting["class:cleric"];
        Assert.Contains(sc.SelectedSpells, s => s.SpellId == "magic_weapon" && s.ClassId == "domain:war");
    }

    [Fact]
    public void NoDomainSelection_HasPendingSelections()
    {
        var (_, engine) = CreateStudio();

        var character = new Character
        {
            Name = "Cleric 1",
            RaceId = "human",
            BaseAbilityScores = new AbilityScoreSet
            {
                STR = 14, DEX = 10, CON = 14, INT = 10, WIS = 16, CHA = 8
            },
            Ticks = new List<Tick>
            {
                new() { DriverId = "class:cleric" }
            }
        };

        var state = engine.Evaluate(character);

        // Cleric grants 2 domain selections, none were made
        Assert.Equal(2, state.PendingDomainSelections["class:cleric"]);
    }

    [Fact]
    public void ContentRegistry_LoadsDomains()
    {
        var registry = TestContentHelper.LoadAllPacks();

        var allDomains = registry.GetAllDomains().ToList();
        Assert.Equal(35, allDomains.Count); // 23 core + 12 deity domains

        // Spot check SRD
        Assert.True(registry.TryGetDomain("domain:knowledge", out var knowledge));
        Assert.Equal("Knowledge", knowledge!.Name);

        // Deity domains (srd_deity.json) and their new spells
        Assert.True(registry.TryGetDomain("domain:madness", out var madness));
        Assert.Equal("touch_of_madness", madness!.BonusSpells[2]);
        Assert.Contains(registry.GetSpellsForList("domain:creation"), s => s.Id == "genesis");
    }

    [RequiresPrivatePacksFact]
    public void ContentRegistry_LoadsOptionalPrivateDomains_WhenConfigured()
    {
        var registry = TestContentHelper.LoadBundledAndPrivatePacksIfAvailable();

        Assert.True(registry.TryGetDomain("domain:corruption", out var corruption));
        Assert.Equal("Corruption", corruption!.Name);
    }

    [Fact]
    public void GrantDomainSelection_SetsPendingCount()
    {
        var state = new CharacterState();
        var ctx = new PermabuffContext(state, GameRules.Standard35e())
        {
            CurrentDriverId = "class:cleric"
        };

        var buff = new GrantDomainSelection { Count = 2 };
        buff.Apply(ctx);

        Assert.Equal(2, state.PendingDomainSelections["class:cleric"]);

        // Apply again (e.g., prestige class granting one more for the same class)
        var buff2 = new GrantDomainSelection { Count = 1 };
        buff2.Apply(ctx);

        Assert.Equal(3, state.PendingDomainSelections["class:cleric"]);
    }

    [Fact]
    public void GrantDomainSelection_FallsBackToOrphanOwner_WhenNoDriverContext()
    {
        // Race/template permabuffs fire outside the tick loop (no CurrentDriverId).
        // GrantDomainSelection must not throw; it should bucket pending picks under
        // the orphan-owner sentinel so future domain picks mark the domain as orphaned
        // (granted power fires, no bonus slot, no spell picks).
        var state = new CharacterState();
        var ctx = new PermabuffContext(state, GameRules.Standard35e()); // no CurrentDriverId

        new GrantDomainSelection { Count = 1 }.Apply(ctx);

        Assert.Equal(1, state.PendingDomainSelections[GrantDomainSelection.OrphanOwner]);
    }

    [Fact]
    public void GrantDomainSelection_PartitionsByGrantingClass()
    {
        // Two different classes granting domains keep separate pending pools.
        var state = new CharacterState();
        var ctx = new PermabuffContext(state, GameRules.Standard35e());

        ctx.CurrentDriverId = "class:cleric";
        new GrantDomainSelection { Count = 2 }.Apply(ctx);

        ctx.CurrentDriverId = "class:contemplative";
        new GrantDomainSelection { Count = 1 }.Apply(ctx);

        Assert.Equal(2, state.PendingDomainSelections["class:cleric"]);
        Assert.Equal(1, state.PendingDomainSelections["class:contemplative"]);
    }
}
