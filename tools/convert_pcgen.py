#!/usr/bin/env python3
"""
PCGen LST → NotOnlyFiendsStudio JSON Converter

Reads PCGen .lst files and outputs JSON matching our content schemas.
Usage:
    python tools/convert_pcgen.py skills <lst_path> [--output <path>]
    python tools/convert_pcgen.py feats <lst_path> [--output <path>]
    python tools/convert_pcgen.py spells <lst_path> [--output <path>]
    python tools/convert_pcgen.py domains <lst_path> [--output <path>]
    python tools/convert_pcgen.py all <rsrd_basics_dir> [--output-dir <dir>]
"""

import argparse
import json
import re
import sys
from pathlib import Path


# ─── Helpers ────────────────────────────────────────────────────────────────

def to_snake_case(name: str) -> str:
    """Convert PCGen name to our snake_case ID convention."""
    s = name.strip()
    # Handle parenthetical subspecialties: "Knowledge (Arcana)" → "knowledge_arcana"
    m = re.match(r'^(.+?)\s*\((.+?)\)$', s)
    if m:
        base = m.group(1).strip()
        sub = m.group(2).strip()
        s = f"{base} {sub}"
    # Replace special chars
    s = s.replace("'s ", "s_").replace("'", "")
    s = re.sub(r'[^a-zA-Z0-9]+', '_', s)
    s = s.strip('_').lower()
    # Collapse multiple underscores
    s = re.sub(r'_+', '_', s)
    return s


def parse_lst_line(line: str) -> dict[str, str]:
    """Parse a tab-delimited LST line into tag dict. First field is 'NAME'."""
    parts = line.split('\t')
    tags = {}
    if parts:
        # First field before any tag is the name
        first = parts[0].strip()
        if not first.startswith('#') and ':' not in first.split('|')[0]:
            tags['NAME'] = first
        elif first.startswith('CLASS:'):
            tags['NAME'] = first.split('CLASS:')[1].strip()
            tags['_IS_CLASS_DEF'] = 'true'
        else:
            # Could be a level line like "1\tABILITY:..."
            tags['_RAW_FIRST'] = first

    for part in parts[1:]:
        part = part.strip()
        if not part or part.startswith('#'):
            continue
        if ':' in part:
            key, _, val = part.partition(':')
            # Some tags appear multiple times (BONUS, etc.) — collect them
            if key in tags:
                if isinstance(tags[key], list):
                    tags[key].append(val)
                else:
                    tags[key] = [tags[key], val]
            else:
                tags[key] = val
    return tags


def read_lst_lines(path: str) -> list[str]:
    """Read non-comment, non-empty lines from an LST file."""
    lines = []
    with open(path, 'r', encoding='utf-8', errors='ignore') as f:
        for line in f:
            line = line.rstrip('\n\r')
            stripped = line.strip()
            if not stripped or stripped.startswith('#') or stripped.startswith('SOURCE'):
                continue
            lines.append(line)
    return lines


# ─── Skills ─────────────────────────────────────────────────────────────────

ABILITY_MAP = {'STR': 'str', 'DEX': 'dex', 'CON': 'con', 'INT': 'int', 'WIS': 'wis', 'CHA': 'cha'}

# Map PCGen skill names to our existing engine IDs where they differ
SKILL_ID_OVERRIDES = {
    'knowledge_architecture_and_engineering': 'knowledge_architecture',
    'knowledge_nobility_and_royalty': 'knowledge_nobility',
    'knowledge_the_planes': 'knowledge_planes',
    'sleight_of_hand': 'sleight_of_hand',
    'use_magic_device': 'use_magic_device',
}

SKILL_PARENT_MAP = {
    'Craft': 'craft',
    'Knowledge': 'knowledge',
    'Perform': 'perform',
    'Profession': 'profession',
}


def parse_synergies(tags: dict) -> list[dict]:
    """Extract synergy bonuses from BONUS:SKILL tags."""
    synergies = []
    bonus_vals = tags.get('BONUS', [])
    if isinstance(bonus_vals, str):
        bonus_vals = [bonus_vals]

    for bonus in bonus_vals:
        # Pattern: SKILL|TargetSkill1,TargetSkill2|SynergyBonus|PRESKILL:1,SourceSkill=5|TYPE=Synergy.STACK
        if 'SynergyBonus' in bonus and 'Synergy' in bonus:
            parts = bonus.split('|')
            if len(parts) >= 2 and parts[0] == 'SKILL':
                targets = parts[1].split(',')
                for target in targets:
                    target_id = to_snake_case(target.strip())
                    target_id = SKILL_ID_OVERRIDES.get(target_id, target_id)
                    synergies.append({
                        'targetSkillId': target_id,
                        'bonus': 2,
                    })
    return synergies


def convert_skills(path: str) -> list[dict]:
    """Convert rsrd_skills.lst to SkillDefinition JSON."""
    lines = read_lst_lines(path)
    skills = []

    for line in lines:
        tags = parse_lst_line(line)
        name = tags.get('NAME')
        if not name:
            continue

        # Skip internal/export-only entries
        visible = tags.get('VISIBLE', '')
        if visible == 'EXPORT':
            continue
        # Skip Literacy and Speak Language (not standard skills)
        if name in ('Literacy', 'Speak Language'):
            continue

        raw_id = to_snake_case(name)
        skill_id = SKILL_ID_OVERRIDES.get(raw_id, raw_id)

        key_stat = tags.get('KEYSTAT', '')
        key_ability = ABILITY_MAP.get(key_stat)
        if not key_ability:
            continue  # Skip skills without a key ability

        trained_only = tags.get('USEUNTRAINED') == 'NO'
        acheck = tags.get('ACHECK', '') in ('YES', 'DOUBLE')

        # Determine parent skill
        parent_skill = None
        for parent_name, parent_id in SKILL_PARENT_MAP.items():
            if name.startswith(f'{parent_name} (') and name != f'{parent_name} (Untrained)':
                parent_skill = parent_id
                break

        # Build skill object
        skill = {
            'id': skill_id,
            'name': name,
            'keyAbility': key_ability,
            'trainedOnly': trained_only,
            'armorCheckPenalty': acheck,
            'description': '',  # LST doesn't have skill descriptions
        }
        if parent_skill:
            skill['parentSkill'] = parent_skill

        synergies = parse_synergies(tags)
        if synergies:
            skill['synergies'] = synergies

        skills.append(skill)

    # Sort alphabetically
    skills.sort(key=lambda s: s['id'])
    return skills


# ─── Feats ──────────────────────────────────────────────────────────────────

FEAT_TYPE_MAP = {
    'General': 'general',
    'Fighter': 'fighterBonus',
    'Metamagic': 'metamagic',
    'ItemCreation': 'itemCreation',
    'Epic': 'epic',
    'Divine': 'divine',
    'Vile': 'vile',
    'Exalted': 'exalted',
    'Tactical': 'tactical',
}


def parse_feat_type(type_str: str) -> str:
    """Map PCGen TYPE tag to our feat type enum."""
    types = type_str.split('.')
    # Priority order: specific types first
    for pcgen_type, our_type in FEAT_TYPE_MAP.items():
        if pcgen_type in types:
            # ItemCreation and Metamagic take priority over General
            if our_type in ('itemCreation', 'metamagic', 'epic'):
                return our_type
    # Fallback: check for Fighter bonus
    if 'Fighter' in types:
        return 'fighterBonus'
    return 'general'


def parse_feat_prerequisites(tags: dict) -> list[dict]:
    """Extract prerequisites from PCGen feat tags."""
    prereqs = []

    # PREABILITY — has feat or has feat of type
    preability = tags.get('PREABILITY', [])
    if isinstance(preability, str):
        preability = [preability]
    for pa in preability:
        # Pattern: 1,CATEGORY=FEAT,Power Attack,Cleave
        # or: 2,CATEGORY=FEAT,Weapon Focus,Greater Weapon Focus
        # or: 1,CATEGORY=FEAT,TYPE.Metamagic  (any feat of that type)
        # or: 1,CATEGORY=FEAT,TYPE=Metamagic  (same, alternate syntax)
        parts = pa.split(',')
        if len(parts) >= 3 and 'CATEGORY=FEAT' in parts[1]:
            for feat_name in parts[2:]:
                feat_name = feat_name.strip()
                if not feat_name:
                    continue
                # TYPE.X or TYPE=X means "any feat of type X"
                type_match = re.match(r'^TYPE[.=](\w+)$', feat_name)
                if type_match:
                    pcgen_type = type_match.group(1)
                    our_type = FEAT_TYPE_MAP.get(pcgen_type, pcgen_type.lower())
                    prereqs.append({
                        '$type': 'HasFeatOfType',
                        'featType': our_type,
                        'minCount': 1,
                    })
                else:
                    # For feats with selections like "Spell Focus (Conjuration)",
                    # strip the selection — our engine stores these as "spell_focus"
                    # with selectionRequired, not separate IDs per selection.
                    base_feat = re.sub(r'\s*\(.+\)$', '', feat_name)
                    prereqs.append({
                        '$type': 'HasFeat',
                        'featId': to_snake_case(base_feat),
                    })

    # PRESTAT — minimum ability score
    prestat = tags.get('PRESTAT', [])
    if isinstance(prestat, str):
        prestat = [prestat]
    for ps in prestat:
        # Pattern: 1,STR=13
        parts = ps.split(',')
        for part in parts[1:]:
            if '=' in part:
                stat, val = part.split('=', 1)
                ability = ABILITY_MAP.get(stat.strip())
                if ability:
                    prereqs.append({
                        '$type': 'MinAbility',
                        'ability': ability,
                        'value': int(val),
                    })

    # PRETOTALAB — minimum BAB
    pretotalab = tags.get('PRETOTALAB')
    if pretotalab:
        val = pretotalab if isinstance(pretotalab, str) else pretotalab[-1]
        prereqs.append({
            '$type': 'MinBAB',
            'value': int(val),
        })

    # PRECLASS — minimum class/caster level
    preclass = tags.get('PRECLASS', [])
    if isinstance(preclass, str):
        preclass = [preclass]
    for pc in preclass:
        # Pattern: 1,SPELLCASTER.Arcane=3,SPELLCASTER.Divine=3
        # or: 1,Fighter=4
        parts = pc.split(',')
        for part in parts[1:]:
            if '=' in part:
                class_spec, val = part.rsplit('=', 1)
                if class_spec.startswith('SPELLCASTER'):
                    prereqs.append({
                        '$type': 'MinCasterLevel',
                        'value': int(val),
                    })
                else:
                    class_id = f'class:{to_snake_case(class_spec)}'
                    # Only emit MinClassLevel for classes present in engine
                    KNOWN_CLASSES = {
                        'class:barbarian', 'class:fighter', 'class:sorcerer',
                        'class:cleric', 'class:eldritch_knight',
                    }
                    if class_id in KNOWN_CLASSES:
                        prereqs.append({
                            '$type': 'MinClassLevel',
                            'classId': class_id,
                            'value': int(val),
                        })

    # PRELEVEL — minimum character level (treated as MinHD)
    prelevel = tags.get('PRELEVEL')
    if prelevel:
        val = prelevel if isinstance(prelevel, str) else prelevel[-1]
        m = re.search(r'MIN=(\d+)', val)
        if m:
            prereqs.append({
                '$type': 'MinHD',
                'value': int(m.group(1)),
            })

    # PREVARGTEQ — variable minimums (e.g., WeapSpecQualify, GreatWeapFocusQualify)
    # These are internal PCGen vars; we map known ones
    prevargteq = tags.get('PREVARGTEQ', [])
    if isinstance(prevargteq, str):
        prevargteq = [prevargteq]
    for pv in prevargteq:
        # AllowExtraTurning,1 — means "has turn undead"
        if 'AllowExtraTurning' in pv:
            prereqs.append({
                '$type': 'HasAbility',
                'abilityId': 'turn_undead',
            })
        elif 'WildShape' in pv:
            prereqs.append({
                '$type': 'HasAbility',
                'abilityId': 'wild_shape',
            })

    # PRESKILL — minimum skill ranks
    preskill = tags.get('PRESKILL', [])
    if isinstance(preskill, str):
        preskill = [preskill]
    for ps in preskill:
        # Pattern: 1,Knowledge (Arcana)=5
        parts = ps.split(',')
        for part in parts[1:]:
            if '=' in part:
                skill_name, val = part.rsplit('=', 1)
                try:
                    skill_id = to_snake_case(skill_name.strip())
                    skill_id = SKILL_ID_OVERRIDES.get(skill_id, skill_id)
                    prereqs.append({
                        '$type': 'MinSkillRanks',
                        'skillId': skill_id,
                        'value': int(val),
                    })
                except ValueError:
                    pass

    # Deduplicate (some feats list same prereq via different paths)
    seen = set()
    unique = []
    for p in prereqs:
        key = json.dumps(p, sort_keys=True)
        if key not in seen:
            seen.add(key)
            unique.append(p)

    return unique


def parse_feat_selection(tags: dict) -> str | None:
    """Determine if a feat requires a selection choice."""
    choose = tags.get('CHOOSE', '')
    if isinstance(choose, list):
        choose = choose[0]
    if 'WEAPONPROFICIENCY' in choose:
        return 'weapon'
    if 'SCHOOLS' in choose:
        return 'school'
    if 'SKILL' in choose:
        return 'skill'
    return None


def convert_feats(path: str) -> list[dict]:
    """Convert rsrd_feats.lst to FeatDefinition JSON."""
    lines = read_lst_lines(path)
    feats = []

    for line in lines:
        tags = parse_lst_line(line)
        name = tags.get('NAME')
        if not name:
            continue
        # Skip CATEGORY != FEAT lines
        category = tags.get('CATEGORY', 'FEAT')
        if category != 'FEAT':
            continue

        feat_id = to_snake_case(name)
        feat_type = parse_feat_type(tags.get('TYPE', 'General'))
        prereqs = parse_feat_prerequisites(tags)
        repeatable = tags.get('MULT') == 'YES'
        selection = parse_feat_selection(tags)

        feat = {
            'id': feat_id,
            'name': name,
            'type': feat_type,
            'prerequisites': prereqs,
            'grantedPermabuffs': [],
            'repeatable': repeatable,
        }
        if selection:
            feat['selectionRequired'] = selection

        feats.append(feat)

    feats.sort(key=lambda f: f['id'])
    return feats


# ─── Spells ─────────────────────────────────────────────────────────────────

CLASS_ID_MAP = {
    'Sorcerer': 'class:sorcerer',
    'Wizard': 'class:wizard',
    'Cleric': 'class:cleric',
    'Druid': 'class:druid',
    'Bard': 'class:bard',
    'Paladin': 'class:paladin',
    'Ranger': 'class:ranger',
    'Adept': 'class:adept',
}

RANGE_MAP = {
    'Close': 'close (25 ft. + 5 ft./2 levels)',
    'Medium': 'medium (100 ft. + 10 ft./level)',
    'Long': 'long (400 ft. + 40 ft./level)',
    'Personal': 'personal',
    'Touch': 'touch',
    'Unlimited': 'unlimited',
}


def parse_class_levels(classes_str: str) -> dict[str, int]:
    """Parse CLASSES tag into classLevels dict."""
    # Pattern: Sorcerer,Wizard=3|Cleric=2|Ranger=4|Druid,Sorcerer,Wizard=5
    class_levels = {}
    groups = classes_str.split('|')
    for group in groups:
        if '=' not in group:
            continue
        names_part, level = group.rsplit('=', 1)
        try:
            level = int(level)
        except ValueError:
            continue
        for cls_name in names_part.split(','):
            cls_name = cls_name.strip()
            cls_id = CLASS_ID_MAP.get(cls_name)
            if cls_id:
                class_levels[cls_id] = level
    return class_levels


def parse_components(comps_str: str) -> dict:
    """Parse COMPS tag into components object."""
    comps = {
        'verbal': False,
        'somatic': False,
        'divineFocus': False,
    }
    parts = [c.strip() for c in comps_str.split(',')]
    for part in parts:
        if part == 'V':
            comps['verbal'] = True
        elif part == 'S':
            comps['somatic'] = True
        elif part == 'M' or part == 'M/DF':
            comps['material'] = True  # Placeholder — LST doesn't have material text
            if 'DF' in part:
                comps['divineFocus'] = True
        elif part == 'F' or part == 'F/DF':
            comps['focus'] = True
            if 'DF' in part:
                comps['divineFocus'] = True
        elif part == 'DF':
            comps['divineFocus'] = True
        elif part == 'XP':
            comps['xpCost'] = True

    # Clean up: remove boolean placeholders for material/focus
    if comps.get('material') is True:
        comps['material'] = 'see text'
    if comps.get('focus') is True:
        comps['focus'] = 'see text'
    return comps


def expand_range(range_str: str) -> str:
    """Expand PCGen range abbreviations."""
    r = range_str.strip()
    return RANGE_MAP.get(r, r)


def classify_target_area(ta: str) -> dict:
    """Classify TARGETAREA into target, area, or effect."""
    ta = ta.strip()
    result = {}
    lower = ta.lower()

    # Heuristics for area
    area_keywords = ['radius', 'emanation', 'spread', 'cone', 'burst', 'line', 'cylinder',
                     'cube', 'ft. high', 'ft. radius', 'wall']
    # Heuristics for effect
    effect_keywords = ['ray', 'missile', 'arrow', 'bolt', 'beam', 'wall of']
    # Heuristics for self/personal
    if lower == 'you':
        result['target'] = 'you'
    elif any(kw in lower for kw in area_keywords):
        result['area'] = ta
    elif any(kw in lower for kw in effect_keywords):
        result['effect'] = ta
    else:
        result['target'] = ta

    return result


def convert_spells(path: str) -> list[dict]:
    """Convert rsrd_spells.lst to SpellDefinition JSON."""
    lines = read_lst_lines(path)
    spells = []

    for line in lines:
        tags = parse_lst_line(line)
        name = tags.get('NAME')
        if not name:
            continue

        # Handle OUTPUTNAME for display
        output_name = tags.get('OUTPUTNAME', name)

        spell_id = to_snake_case(name)
        class_levels = parse_class_levels(tags.get('CLASSES', ''))
        if not class_levels:
            continue  # Skip spells with no class assignments

        school = tags.get('SCHOOL', '').lower()
        subschool = tags.get('SUBSCHOOL', '')
        subschool = subschool.lower() if subschool else None

        descriptors = []
        desc_str = tags.get('DESCRIPTOR', '')
        if desc_str:
            descriptors = [d.strip().lower() for d in desc_str.split('|')]

        components = parse_components(tags.get('COMPS', ''))
        casting_time = tags.get('CASTTIME', '1 standard action')
        range_val = expand_range(tags.get('RANGE', ''))

        # Target/area/effect
        target_area = tags.get('TARGETAREA', '')
        ta_result = classify_target_area(target_area) if target_area else {}

        # Duration — strip PCGen formulas, keep readable text
        duration = tags.get('DURATION', 'instantaneous')
        # Simplify PCGen formula durations like "(CASTERLEVEL) rounds"
        duration = re.sub(r'\(CASTERLEVEL\)', '1/level', duration)
        duration = re.sub(r'\(CASTERLEVEL\*(\d+)\)', r'\1/level', duration)
        duration = re.sub(r'\(min\(CASTERLEVEL,\d+\)\)', '1/level', duration)
        duration = duration.replace('[D]', '(D)')

        save_info = tags.get('SAVEINFO', 'none')
        spell_res = tags.get('SPELLRES', 'no')

        desc = tags.get('DESC', '')
        # Strip PCGen conditional display tags
        desc = re.sub(r'\|!PRERULE:\d+,\w+', '', desc)

        spell = {
            'id': spell_id,
            'name': output_name if output_name != name else name,
            'school': school,
            'classLevels': class_levels,
            'components': components,
            'castingTime': casting_time,
            'range': range_val,
            'duration': duration,
            'savingThrow': save_info,
            'spellResistance': spell_res.lower(),
            'description': desc,
        }
        if subschool:
            spell['subschool'] = subschool
        if descriptors:
            spell['descriptors'] = descriptors
        # Add target/area/effect
        for key in ('target', 'area', 'effect'):
            if key in ta_result:
                spell[key] = ta_result[key]

        spells.append(spell)

    spells.sort(key=lambda s: s['id'])
    return spells


# ─── Domains ────────────────────────────────────────────────────────────────

def parse_domain_spells(spell_level_str: str, domain_name: str) -> dict[str, str]:
    """Parse SPELLLEVEL:DOMAIN tag into bonusSpells dict."""
    bonus_spells = {}
    # Pattern: DOMAIN|Air=1|Obscuring Mist|Air=2|Wind Wall|...
    parts = spell_level_str.split('|')
    i = 1  # Skip "DOMAIN"
    while i < len(parts) - 1:
        level_part = parts[i]
        spell_name = parts[i + 1] if i + 1 < len(parts) else ''
        # Parse "Air=1" or "Knowledge=5"
        if '=' in level_part:
            _, level_str = level_part.rsplit('=', 1)
            try:
                level = int(level_str)
                bonus_spells[str(level)] = to_snake_case(spell_name)
            except ValueError:
                pass
        i += 2
    return bonus_spells


def convert_domains(path: str) -> list[dict]:
    """Convert rsrd_domains.lst to DomainDefinition JSON."""
    lines = read_lst_lines(path)
    domains = []

    for line in lines:
        tags = parse_lst_line(line)
        name = tags.get('NAME')
        if not name:
            continue

        domain_id = f'domain:{to_snake_case(name)}'
        description = tags.get('DESC', '')
        ability_id = f'domain_{to_snake_case(name)}_power'

        # Parse bonus spells
        spell_level = tags.get('SPELLLEVEL', '')
        bonus_spells = parse_domain_spells(spell_level, name) if spell_level else {}

        domain = {
            'id': domain_id,
            'name': name,
            'description': description,
            'grantedPermabuffs': [
                {
                    '$type': 'GrantAbility',
                    'ability': {
                        'id': ability_id,
                        'name': f'{name} Domain Power',
                        'description': description,
                    }
                }
            ],
            'bonusSpells': bonus_spells,
        }

        domains.append(domain)

    domains.sort(key=lambda d: d['id'])
    return domains


# ─── Classes ───────────────────────────────────────────────────────────────

ALIGN_MAP = {
    'LG': 'lG', 'NG': 'nG', 'CG': 'cG',
    'LN': 'lN', 'TN': 'n',  'CN': 'cN',
    'LE': 'lE', 'NE': 'nE', 'CE': 'cE',
}

TYPE_SKILL_MAP = {
    'Craft': 'craft',
    'Knowledge': 'knowledge',
    'Perform': 'perform',
    'Profession': 'profession',
}


def parse_bab_progression(bonus_tags) -> str:
    """Determine BAB progression from BONUS:COMBAT tags."""
    if isinstance(bonus_tags, str):
        bonus_tags = [bonus_tags]
    for b in bonus_tags:
        if 'BASEAB' in b:
            if '*3/4' in b or '3/4' in b:
                return 'average'
            if '/2' in b or '*1/2' in b:
                return 'poor'
            return 'good'
    return 'average'


def parse_save_progression(bonus_tags) -> dict:
    """Determine save progression from BONUS:SAVE tags."""
    saves = {'fort': 'poor', 'ref': 'poor', 'will': 'poor'}
    if isinstance(bonus_tags, str):
        bonus_tags = [bonus_tags]
    for b in bonus_tags:
        if 'BASE.' not in b:
            continue
        is_good = '/2+2' in b
        # Format: "SAVE|BASE.Fortitude,BASE.Will|classlevel(...)/2+2"
        parts = b.split('|')
        if len(parts) >= 2:
            save_part = parts[1]  # e.g., "BASE.Fortitude,BASE.Will"
            for s in save_part.split(','):
                s = s.strip()
                if 'Fortitude' in s and is_good:
                    saves['fort'] = 'good'
                if 'Reflex' in s and is_good:
                    saves['ref'] = 'good'
                if 'Will' in s and is_good:
                    saves['will'] = 'good'
    return saves


def parse_class_skills(cskill_str: str) -> list[str]:
    """Parse CSKILL pipe-separated list to skill IDs."""
    skills = []
    for part in cskill_str.split('|'):
        part = part.strip()
        if not part:
            continue
        # TYPE=Craft → parent skill
        if part.startswith('TYPE='):
            type_name = part[5:]
            parent = TYPE_SKILL_MAP.get(type_name)
            if parent:
                skills.append(parent)
        else:
            skill_id = to_snake_case(part)
            skill_id = SKILL_ID_OVERRIDES.get(skill_id, skill_id)
            skills.append(skill_id)
    return sorted(set(skills))


def parse_alignment_prereqs(prealign_str: str) -> list[str]:
    """Parse PREALIGN tag into engine alignment codes."""
    codes = []
    for a in prealign_str.split(','):
        a = a.strip()
        if a in ALIGN_MAP:
            codes.append(ALIGN_MAP[a])
    return sorted(codes)


def parse_ability_name(ability_str: str) -> tuple[str, str] | None:
    """Extract feature name from ABILITY tag. Returns (id, display_name)."""
    # Pattern: "Barbarian Class Feature|AUTOMATIC|Barbarian ~ Fast Movement|..."
    parts = ability_str.split('|')
    for part in parts:
        if '~' in part:
            # "Barbarian ~ Fast Movement" → "Fast Movement"
            _, _, feature = part.partition('~')
            feature = feature.strip()
            if feature:
                return to_snake_case(feature), feature
    return None


def convert_classes(path: str) -> list[dict]:
    """Convert rsrd_classes.lst to HDDriver JSON."""
    lines = read_lst_lines(path)
    classes: dict[str, dict] = {}  # name → class data
    current_class: str | None = None
    blocked_classes: set[str] = set()  # classes to skip (monster types, Ex, .MOD)

    for line in lines:
        stripped = line.strip()

        # Skip SUBCLASS/SUBCLASSLEVEL lines
        if stripped.startswith('SUBCLASS:') or stripped.startswith('SUBCLASSLEVEL:'):
            continue

        tags = parse_lst_line(line)

        # CLASS: definition line
        if tags.get('_IS_CLASS_DEF'):
            name = tags['NAME']

            # Skip if previously blocked
            if name in blocked_classes:
                current_class = None
                continue

            # Skip "Ex ..." classes, hidden classes, and .MOD entries
            if (name.startswith('Ex ') or tags.get('VISIBLE') == 'NO'
                    or name.endswith('.MOD')):
                blocked_classes.add(name)
                current_class = None
                continue

            # Skip monster/creature type classes (only want Base.PC, Base.NPC, Prestige)
            class_type = tags.get('TYPE', '')
            if class_type and not any(t in class_type for t in ('Base.PC', 'Base.NPC', 'Prestige')):
                blocked_classes.add(name)
                current_class = None
                continue

            current_class = name

            if name not in classes:
                classes[name] = {
                    'name': name,
                    'hd': 8,
                    'skill_pts': 2,
                    'class_skills': [],
                    'bab': 'average',
                    'saves': {'fort': 'poor', 'ref': 'poor', 'will': 'poor'},
                    'spellcasting': None,
                    'cast_table': {},   # level → CAST values
                    'known_table': {},  # level → KNOWN values
                    'abilities': {},    # level → [(id, name)]
                    'prealign': None,
                    'max_level': 20,
                    'type': 'Base',
                }

            cls = classes[name]

            # HD
            if 'HD' in tags:
                cls['hd'] = int(tags['HD'])

            # Max level
            if 'MAXLEVEL' in tags:
                try:
                    cls['max_level'] = int(tags['MAXLEVEL'])
                except ValueError:
                    cls['max_level'] = 20  # NOLIMIT → treat as 20

            # Type (Base.PC, Base.NPC, Prestige)
            if 'TYPE' in tags:
                cls['type'] = tags['TYPE']

            # FACT tags (SpellType, etc.) — may be on a different line than SPELLSTAT
            fact_tags = tags.get('FACT', [])
            if isinstance(fact_tags, str):
                fact_tags = [fact_tags]
            for f in fact_tags:
                if f.startswith('SpellType|'):
                    cls['spell_type'] = f.split('|')[1].lower()

            # Skill points
            if 'STARTSKILLPTS' in tags:
                cls['skill_pts'] = int(tags['STARTSKILLPTS'])

            # Class skills
            if 'CSKILL' in tags:
                cls['class_skills'] = parse_class_skills(tags['CSKILL'])

            # BAB from BONUS tags
            bonus_tags = tags.get('BONUS', [])
            if isinstance(bonus_tags, str):
                bonus_tags = [bonus_tags]
            combat_bonuses = [b for b in bonus_tags if b.startswith('COMBAT')]
            if combat_bonuses:
                cls['bab'] = parse_bab_progression(combat_bonuses)

            # Saves from BONUS tags
            save_bonuses = [b for b in bonus_tags if b.startswith('SAVE')]
            if save_bonuses:
                cls['saves'] = parse_save_progression(save_bonuses)

            # Alignment
            if 'PREALIGN' in tags:
                cls['prealign'] = parse_alignment_prereqs(tags['PREALIGN'])

            # Spellcasting info
            spell_stat = tags.get('SPELLSTAT')
            if spell_stat:
                cls['spellcasting'] = {
                    'stat': spell_stat.lower(),
                    'type': cls.get('spell_type', 'divine'),
                    'spontaneous': tags.get('MEMORIZE') == 'NO',
                }

            continue

        if current_class is None:
            continue

        cls = classes.get(current_class)
        if cls is None:
            continue

        # Level lines: start with a number
        first_field = tags.get('_RAW_FIRST') or tags.get('NAME', '')
        try:
            level = int(first_field)
        except (ValueError, TypeError):
            continue

        # CAST line
        if 'CAST' in tags:
            cast_vals = [int(x) for x in tags['CAST'].split(',')]
            cls['cast_table'][level] = cast_vals

        # KNOWN line
        if 'KNOWN' in tags:
            known_vals = [int(x) for x in tags['KNOWN'].split(',')]
            cls['known_table'][level] = known_vals

        # ABILITY line — class features
        ability_tag = tags.get('ABILITY')
        if ability_tag:
            if isinstance(ability_tag, list):
                ability_tag = ability_tag[0]
            parsed = parse_ability_name(ability_tag)
            if parsed:
                feat_id, feat_name = parsed
                if level not in cls['abilities']:
                    cls['abilities'][level] = []
                cls['abilities'][level].append((feat_id, feat_name))

    # Build output JSON
    results = []
    for name, cls in classes.items():
        class_id = f"class:{to_snake_case(name)}"

        driver: dict = {
            '$type': 'HDDriver',
            'kind': 'Class',
            'id': class_id,
            'name': name,
            'hitDie': cls['hd'],
            'skillPointsPerLevel': cls['skill_pts'],
            'classSkills': cls['class_skills'],
            'babProgression': cls['bab'],
            'saveProgression': cls['saves'],
        }

        # Spellcasting
        if cls['spellcasting'] and cls['cast_table']:
            sc: dict = {
                'castingType': cls['spellcasting']['type'],
                'castingStat': cls['spellcasting']['stat'],
                'spellsPerDay': {},
            }
            for level, casts in sorted(cls['cast_table'].items()):
                sc['spellsPerDay'][str(level)] = {
                    str(i): v for i, v in enumerate(casts)
                }
            if cls['known_table']:
                sc['spellsKnown'] = {}
                for level, knowns in sorted(cls['known_table'].items()):
                    sc['spellsKnown'][str(level)] = {
                        str(i): v for i, v in enumerate(knowns)
                    }
            driver['spellcasting'] = sc

        # Level permabuffs (class features as GrantAbility)
        level_perms: dict = {}
        for level, abilities in sorted(cls['abilities'].items()):
            perms = []
            for feat_id, feat_name in abilities:
                perms.append({
                    '$type': 'GrantAbility',
                    'ability': {
                        'id': feat_id,
                        'name': feat_name,
                        'description': f'{name} class feature.',
                    }
                })
            level_perms[str(level)] = perms
        driver['levelPermabuffs'] = level_perms
        driver['perLevelPermabuffs'] = []

        # Prerequisites
        prereqs = []
        if cls['prealign']:
            prereqs.append({
                '$type': 'AlignmentReq',
                'allowed': cls['prealign'],
            })
        driver['prerequisites'] = prereqs

        # Max level (only if not default 20)
        if cls['max_level'] != 20:
            driver['maxLevel'] = cls['max_level']

        results.append(driver)

    results.sort(key=lambda d: d['id'])
    return results


# ─── Equipment ──────────────────────────────────────────────────────────────

WEAPON_DAMAGE_TYPES = {
    'Slashing': 'slashing',
    'Piercing': 'piercing',
    'Bludgeoning': 'bludgeoning',
}


def _parse_cost_to_cp(cost_raw: str) -> int:
    """Parse PCGen COST (gp by default) → copper pieces."""
    cost = cost_raw.strip().rstrip('*')
    if not cost:
        return 0
    try:
        # PCGen mostly uses gp; some items use "0" for "negligible"
        return int(float(cost) * 100)
    except ValueError:
        return 0


def _critrange_low(critrange_raw: str) -> int:
    """PCGen CRITRANGE: 1 = 20, 2 = 19-20, 3 = 18-20, etc. → return critRangeLow."""
    try:
        n = int(critrange_raw)
        return 21 - n
    except (TypeError, ValueError):
        return 20


def _critmult(critmult_raw: str) -> int:
    """Parse 'x2' / 'x3' / 'x4' → integer multiplier."""
    s = critmult_raw.strip().lower().lstrip('x')
    try:
        return int(s) if s else 2
    except ValueError:
        return 2


def _equipment_id(name: str, prefix: str) -> str:
    return f"{prefix}:{to_snake_case(name)}"


def _classify_armor(type_chain: str) -> tuple[str, str | None]:
    """
    Inspect a TYPE chain and return (category, armor_kind).
    category ∈ {'armor', 'shield'}; armor_kind ∈ {'light','medium','heavy','shield','towerShield'} or None.
    """
    parts = type_chain.split('.')
    if 'Shield' in parts:
        if 'Tower' in parts:
            return 'shield', 'towerShield'
        return 'shield', 'shield'
    if 'Armor' in parts:
        if 'Heavy' in parts:
            return 'armor', 'heavy'
        if 'Medium' in parts:
            return 'armor', 'medium'
        if 'Light' in parts:
            return 'armor', 'light'
        return 'armor', 'light'
    return 'armor', None


def _classify_weapon(type_chain: str) -> tuple[str, dict]:
    """Inspect a Weapon TYPE chain and return ('weapon', extras dict)."""
    parts = type_chain.split('.')
    extras = {}
    if 'Ranged' in parts:
        extras['ranged'] = True
    if 'Thrown' in parts:
        extras['thrown'] = True
    if 'Reach' in parts:
        extras['reach'] = True
    if 'Double' in parts:
        extras['doubleWeapon'] = True
    if 'Simple' in parts:
        extras['proficiency'] = 'simple'
    elif 'Martial' in parts:
        extras['proficiency'] = 'martial'
    elif 'Exotic' in parts:
        extras['proficiency'] = 'exotic'
    damage_types = [WEAPON_DAMAGE_TYPES[p] for p in parts if p in WEAPON_DAMAGE_TYPES]
    if damage_types:
        extras['damageType'] = '+'.join(damage_types)
    return 'weapon', extras


def _parse_combat_ac_bonus(bonuses, expected_type: str) -> int:
    """Find a BONUS:COMBAT|AC|N|TYPE=<expected_type> entry and return N."""
    if isinstance(bonuses, str):
        bonuses = [bonuses]
    for b in bonuses or []:
        # Format: 'COMBAT|AC|2|TYPE=Armor'
        segments = b.split('|')
        if len(segments) < 3 or segments[0] != 'COMBAT' or segments[1] != 'AC':
            continue
        if any(seg.upper() == f"TYPE={expected_type.upper()}" for seg in segments[3:]):
            try:
                return int(segments[2])
            except ValueError:
                continue
    return 0


# PCGen STAT abbreviations → engine BonusTarget enum names (camelCase).
_STAT_TO_TARGET = {
    'STR': 'abilityStr', 'DEX': 'abilityDex', 'CON': 'abilityCon',
    'INT': 'abilityInt', 'WIS': 'abilityWis', 'CHA': 'abilityCha',
}

# PCGen SAVE token sets that mean "all three saves" → use the engine's AllSaves target instead
# of emitting three permabuffs.
_ALL_SAVES_TOKENS = {
    'ALL',
    'FORTITUDE,REFLEX,WILL',
    'FORT,REF,WILL',
}

# PCGen SAVE single tokens → engine save target.
_SAVE_TO_TARGET = {
    'FORTITUDE': 'saveFort', 'FORT': 'saveFort',
    'REFLEX': 'saveRef', 'REF': 'saveRef',
    'WILL': 'saveWill',
}

# Engine BonusType vocabulary (lowercase camelCase as emitted in JSON).
_BONUS_TYPE_NORMALIZED = {
    'ARMOR': 'armor', 'SHIELD': 'shield', 'NATURAL': 'natural',
    'NATURALARMOR': 'natural', 'NATURALARMORENHANCEMENT': 'naturalEnhancement',
    'NATURALENHANCEMENT': 'naturalEnhancement',
    'ENHANCEMENT': 'enhancement', 'DEFLECTION': 'deflection', 'DODGE': 'dodge',
    'INSIGHT': 'insight', 'LUCK': 'luck', 'SACRED': 'sacred', 'PROFANE': 'profane',
    'MORALE': 'morale', 'COMPETENCE': 'competence', 'RESISTANCE': 'resistance',
    'CIRCUMSTANCE': 'circumstance', 'SIZE': 'size', 'RACIAL': 'racial',
}


def _extract_bonus_type(segments: list[str]) -> str:
    """Pull 'enhancement' out of segments like ['…','TYPE=Enhancement']. Default 'untyped'."""
    for seg in segments:
        if seg.upper().startswith('TYPE='):
            raw = seg.split('=', 1)[1].strip()
            return _BONUS_TYPE_NORMALIZED.get(raw.upper(), 'untyped')
    return 'untyped'


def _parse_bonus_permabuffs(bonuses) -> list[dict]:
    """
    Convert PCGen BONUS tags into engine `GrantTypedBonus` permabuffs.
    Recognized shapes (all others are silently skipped — VAR-based BONUSes encode psionic
    points, energy resistance, etc., which the engine doesn't model as typed bonuses):
      BONUS:STAT|<STAT>|N|TYPE=<Type>            → ability score bonus
      BONUS:COMBAT|AC|N|TYPE=<Type>              → AC bonus
      BONUS:SAVE|<SaveSet>|N|TYPE=<Type>         → save bonus (single save or all-saves)
    """
    if isinstance(bonuses, str):
        bonuses = [bonuses]
    permabuffs: list[dict] = []
    for b in bonuses or []:
        segments = b.split('|')
        if len(segments) < 3:
            continue
        kind = segments[0].upper()

        try:
            value = int(segments[2])
        except ValueError:
            continue

        target: str | None = None
        if kind == 'STAT':
            target = _STAT_TO_TARGET.get(segments[1].upper())
        elif kind == 'COMBAT' and segments[1].upper() == 'AC':
            target = 'ac'
        elif kind == 'SAVE':
            save_token = segments[1].upper()
            if save_token in _ALL_SAVES_TOKENS:
                target = 'allSaves'
            else:
                target = _SAVE_TO_TARGET.get(save_token)

        if target is None:
            continue

        permabuffs.append({
            '$type': 'GrantTypedBonus',
            'target': target,
            'bonusType': _extract_bonus_type(segments),
            'value': {'expression': str(value)},
        })

    return permabuffs


# PCGen TYPE subtype (Cape/Bracer/Amulet/…) → engine body-slot string + ID prefix.
# Items whose TYPE chain is just `Magic.Epic.Wondrous` (no further subtype) get no slot.
_WONDROUS_SUBTYPE_TO_SLOT = {
    'Cape': 'shoulders',
    'Bracer': 'wrists',
    'Amulet': 'neck',
    'Belt': 'waist',
    'Headgear': 'head',
    'Glove': 'hands',
    'Boot': 'feet',
    'Periapt': 'neck',
    'Eyegear': 'eyes',
    'Cloak': 'shoulders',
}


def convert_equipment(path: str) -> list[dict]:
    """Convert a PCGen equipment LST (rsrd_equip_arms_and_armor.lst style) to EquipmentDefinition JSON."""
    results = []
    seen_ids = set()
    for line in read_lst_lines(path):
        tags = parse_lst_line(line)
        name = tags.get('NAME', '').strip()
        if not name:
            continue
        # Skip barding for now — PCGen lists them under same chain, we omit until we have mount support.
        if name.startswith('Barding'):
            continue
        type_chain = tags.get('TYPE', '')
        if not type_chain:
            continue

        # Use OUTPUTNAME for display when present.
        display_name = tags.get('OUTPUTNAME', name).replace('[NAME]', name).strip()
        weight_raw = (tags.get('WT', '0') or '0').strip()
        try:
            weight_lbs = max(0, int(round(float(weight_raw))))
        except ValueError:
            weight_lbs = 0
        price_cp = _parse_cost_to_cp(tags.get('COST', '0'))

        item: dict = {}

        type_parts = type_chain.split('.')
        if 'Weapon' in type_parts:
            category, extras = _classify_weapon(type_chain)
            damage = tags.get('DAMAGE', '').strip()
            if not damage:
                continue  # Skip ammo-without-damage entries
            weapon: dict = {'damage': damage}
            critrange = tags.get('CRITRANGE', '')
            if critrange:
                weapon['critRangeLow'] = _critrange_low(critrange)
            critmult = tags.get('CRITMULT', '')
            if critmult:
                weapon['critMultiplier'] = _critmult(critmult)
            range_ft = tags.get('RANGE', '')
            if range_ft:
                try:
                    weapon['rangeFt'] = int(range_ft)
                except ValueError:
                    pass
            wield = tags.get('WIELD', '')
            if wield == 'Light':
                weapon['light'] = True
            elif wield in ('TwoHanded', 'TwoHandsOnly'):
                weapon['twoHanded'] = True
            weapon.update(extras)
            item = {
                'id': _equipment_id(name, 'weapon'),
                'name': display_name,
                'category': 'weapon',
                'slot': 'weapon',
                'weightLbs': weight_lbs,
                'priceCp': price_cp,
                'weapon': weapon,
            }
        elif 'Armor' in type_parts or 'Shield' in type_parts:
            category, kind = _classify_armor(type_chain)
            bonuses = tags.get('BONUS', '')
            armor_type = 'Shield' if category == 'shield' else 'Armor'
            ac_bonus = _parse_combat_ac_bonus(bonuses, armor_type)
            armor_block: dict = {
                'kind': kind or 'light',
                'armorBonus': ac_bonus,
            }
            accheck = tags.get('ACCHECK', '')
            if accheck:
                try:
                    armor_block['checkPenalty'] = int(accheck)
                except ValueError:
                    pass
            maxdex = tags.get('MAXDEX', '')
            if maxdex:
                try:
                    armor_block['maxDex'] = int(maxdex)
                except ValueError:
                    pass
            spellfail = tags.get('SPELLFAILURE', '')
            if spellfail:
                try:
                    armor_block['arcaneFailurePct'] = int(spellfail)
                except ValueError:
                    pass
            slot = 'shield' if category == 'shield' else 'body'
            item = {
                'id': _equipment_id(name, 'armor' if category == 'armor' else 'shield'),
                'name': display_name,
                'category': category,
                'slot': slot,
                'weightLbs': weight_lbs,
                'priceCp': price_cp,
                'armor': armor_block,
            }
        elif 'Wondrous' in type_parts or 'Ring' in type_parts:
            # Map the most specific wondrous subtype to a body slot, falling back to None for
            # generic "Magic.Epic.Wondrous" items (e.g. Boots of Speed listed without a subtype).
            slot: str | None = None
            id_prefix = 'ring' if 'Ring' in type_parts else 'wondrous'
            if id_prefix == 'ring':
                slot = 'ring'
            else:
                for part in type_parts:
                    if part in _WONDROUS_SUBTYPE_TO_SLOT:
                        slot = _WONDROUS_SUBTYPE_TO_SLOT[part]
                        break

            permabuffs = _parse_bonus_permabuffs(tags.get('BONUS', ''))
            # Skip items we can't represent yet — extracting a name with no mechanical effect
            # just gives the importer a match that does nothing, which is misleading.
            if not permabuffs:
                continue

            category = 'ring' if id_prefix == 'ring' else 'wondrous'
            item = {
                'id': _equipment_id(name, id_prefix),
                'name': display_name,
                'category': category,
                'weightLbs': weight_lbs,
                'priceCp': price_cp,
                'grantedPermabuffs': permabuffs,
            }
            if slot is not None:
                item['slot'] = slot
        else:
            continue  # Skip mounts/accessories/rod/staff/artifact types

        if item['id'] in seen_ids:
            continue
        seen_ids.add(item['id'])
        results.append(item)

    results.sort(key=lambda e: (e['category'], e['id']))
    return results


# ─── Main ───────────────────────────────────────────────────────────────────

def write_json(data: list, output_path: str | None, default_name: str):
    """Write JSON array to file or stdout."""
    content = json.dumps(data, indent=2, ensure_ascii=False)
    if output_path:
        Path(output_path).parent.mkdir(parents=True, exist_ok=True)
        with open(output_path, 'w', encoding='utf-8') as f:
            f.write(content + '\n')
        print(f"Wrote {len(data)} entries to {output_path}", file=sys.stderr)
    else:
        print(content)


CONVERTERS = {
    'skills': (convert_skills, 'rsrd_skills.lst', 'srd_skills.json'),
    'feats': (convert_feats, 'rsrd_feats.lst', 'srd_feats.json'),
    'spells': (convert_spells, 'rsrd_spells.lst', 'srd_spells.json'),
    'domains': (convert_domains, 'rsrd_domains.lst', 'srd_domains.json'),
    'classes': (convert_classes, 'rsrd_classes.lst', 'srd_classes.json'),
    'equipment': (convert_equipment, 'rsrd_equip_arms_and_armor.lst', 'srd_equipment.json'),
    'equipment_epic': (convert_equipment, '../epic/rsrd_equip_epic.lst', 'srd_equipment_epic.json'),
}


def main():
    parser = argparse.ArgumentParser(description='Convert PCGen LST files to NotOnlyFiendsStudio JSON')
    parser.add_argument('command', choices=['skills', 'feats', 'spells', 'domains', 'classes', 'equipment', 'equipment_epic', 'all'],
                        help='Content type to convert')
    parser.add_argument('path', help='Path to LST file or RSRD basics directory (for "all")')
    parser.add_argument('--output', '-o', help='Output file path (default: stdout)')
    parser.add_argument('--output-dir', '-d', help='Output directory (for "all" command)')
    args = parser.parse_args()

    if args.command == 'all':
        basics_dir = Path(args.path)
        out_dir = Path(args.output_dir) if args.output_dir else Path('.')
        for cmd, (converter, lst_name, json_name) in CONVERTERS.items():
            lst_path = basics_dir / lst_name
            if lst_path.exists():
                data = converter(str(lst_path))
                write_json(data, str(out_dir / json_name), json_name)
            else:
                print(f"Warning: {lst_path} not found, skipping {cmd}", file=sys.stderr)
    else:
        converter, _, default_name = CONVERTERS[args.command]
        data = converter(args.path)
        write_json(data, args.output, default_name)


if __name__ == '__main__':
    main()
