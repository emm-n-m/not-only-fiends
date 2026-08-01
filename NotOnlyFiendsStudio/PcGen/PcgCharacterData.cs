namespace NotOnlyFiendsStudio.PcGen;

public class PcgCharacterData
{
    public string FileName { get; set; } = "";
    public string CharacterName { get; set; } = "";
    public string Race { get; set; } = "";
    public string Alignment { get; set; } = "";
    public string Deity { get; set; } = "";
    public Dictionary<string, int> BaseStats { get; set; } = new();

    public List<PcgClassEntry> Classes { get; set; } = new();
    public List<PcgLevelEntry> Levels { get; set; } = new();
    public List<PcgSkillEntry> Skills { get; set; } = new();
    public List<PcgFeatEntry> Feats { get; set; } = new();
    public List<PcgTemplateEntry> Templates { get; set; } = new();
    public List<PcgSpellEntry> Spells { get; set; } = new();
    public List<PcgDomainEntry> Domains { get; set; } = new();
    /// <summary>Language names as PCGen writes them ("Abyssal", "Draconic"), in file order.</summary>
    public List<string> Languages { get; set; } = new();
    public List<PcgEquipmentRaw> Equipment { get; set; } = new();
    public List<PcgFollowerEntry> Followers { get; set; } = new();
    public PcgMasterEntry? Master { get; set; }
    public List<string> TemporaryBonuses { get; set; } = new();

    public int TotalClassLevels => Classes.Sum(c => c.Level);
}

public class PcgClassEntry
{
    public string Name { get; set; } = "";
    public int Level { get; set; }
    public string? Subclass { get; set; }
    public List<string> ProhibitedSchools { get; set; } = new();
    public string? SpellBase { get; set; }
}

public class PcgLevelEntry
{
    public string ClassName { get; set; } = "";
    public int ClassLevel { get; set; }
    public int HitPoints { get; set; }
    public int SkillsGained { get; set; }
    public string? AbilityIncrease { get; set; }
    public List<string> SpellcasterChoices { get; set; } = new();
}

public class PcgSkillEntry
{
    public string Name { get; set; } = "";
    public double Ranks { get; set; }
    public string? BoughtClass { get; set; }
}

public class PcgFeatEntry
{
    public string Key { get; set; } = "";
    public string? AppliedTo { get; set; }
    public List<string> Types { get; set; } = new();
}

public class PcgTemplateEntry
{
    public string Name { get; set; } = "";
    public bool IsInternal { get; set; }
}

public class PcgSpellEntry
{
    public string Name { get; set; } = "";
    public string ClassName { get; set; } = "";
    public int SpellLevel { get; set; }
    public string Book { get; set; } = "";
}

public class PcgDomainEntry
{
    public string Name { get; set; } = "";
    public string SourceClass { get; set; } = "";
    public int SourceLevel { get; set; }
}

public class PcgFollowerEntry
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string Race { get; set; } = "";
    public string File { get; set; } = "";
}

public class PcgMasterEntry
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string File { get; set; } = "";
    public int HitDice { get; set; }
    public int Adjustment { get; set; }
}
