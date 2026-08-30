#!/usr/bin/env python3
"""Extract salient divine ability catalogue data from the bundled local SRD mirror."""

from __future__ import annotations

import argparse
import html
import json
import re
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
DEFAULT_SOURCE = ROOT / "NotOnlyFiendsStudio/Content/srd_html/salientAbilities.html"
DEFAULT_OUTPUT = ROOT / "NotOnlyFiendsStudio/Content/packs/srd_epic/salient_divine_abilities/srd.json"


def plain(fragment: str) -> str:
    fragment = re.sub(r"<(?:br|/p|/li|/h\d)\b[^>]*>", " ", fragment, flags=re.I)
    fragment = re.sub(r"<[^>]+>", " ", fragment)
    return " ".join(html.unescape(fragment).replace("\xa0", " ").split())


def title_case(source: str) -> str:
    return source.title().replace(" And ", " and ").replace(" Of ", " of ")


FEATS = {
    "Spell Mastery": "feat:spell_mastery",
    "Point Blank Shot": "feat:point_blank_shot",
    "Far Shot": "feat:far_shot",
    "Armor Proficiency (light)": "feat:armor_proficiency_light",
    "Armor Proficiency (medium)": "feat:armor_proficiency_medium",
    "Combat Reflexes": "feat:combat_reflexes",
    "Dodge": "feat:dodge",
    "Expertise": "feat:combat_expertise",
    "Mobility": "feat:mobility",
    "Spring Attack": "feat:spring_attack",
    "Whirlwind Attack": "feat:whirlwind_attack",
    "Craft Magic Arms and Armor": "feat:craft_magic_arms_and_armor",
    "Craft Rod": "feat:craft_rod",
    "Craft Staff": "feat:craft_staff",
    "Craft Wand": "feat:craft_wand",
    "Craft Wondrous Item": "feat:craft_wondrous_item",
    "Forge Ring": "feat:forge_ring",
    "Scribe Scroll": "feat:scribe_scroll",
    "Weapon Focus": "feat:weapon_focus",
    "Improved Critical": "feat:improved_critical",
    "Spell Focus": "feat:spell_focus",
    "Greater Spell Focus": "feat:greater_spell_focus",
    "Improved Initiative": "feat:improved_initiative",
}

CLASSES = {
    name: f"class:{name.lower()}"
    for name in ("Bard", "Fighter", "Druid", "Monk", "Paladin", "Barbarian", "Ranger", "Rogue", "Wizard", "Cleric")
}

DOMAINS = ("Air", "Earth", "Fire", "Water", "War", "Trickery", "Sun", "Travel", "Strength", "Luck", "Death", "Magic", "Knowledge")

SALIENT_NAMES = {
    "Alter Size": "salient:alter_size",
    "Alter Form": "salient:alter_form",
    "Create Object": "salient:create_object",
    "Create Greater Object": "salient:create_greater_object",
    "Divine Shield": "salient:divine_shield",
    "Divine Celerity": "salient:divine_celerity",
    "Divine Blast": "salient:divine_blast",
    "Divine Fast Healing": "salient:divine_fast_healing",
    "Divine Weapon Focus": "salient:divine_weapon_focus",
    "Gift of Life": "salient:gift_of_life",
    "Hand of Death": "salient:hand_of_death",
    "Life and Death": "salient:life_and_death",
    "Arcane Mastery": "salient:arcane_mastery",
    "Shapechange": "salient:shapechange",
}


def structured_prerequisites(anchor: str, text: str | None) -> tuple[list[dict[str, object]], bool]:
    if not text:
        return [], False
    requirements: list[dict[str, object]] = []

    for ability, value in re.findall(r"\b(Str|Dex|Con|Int|Wis|Cha)\s+(\d+)", text):
        if anchor != "divine-fast-healing":
            requirements.append({"$type": "MinAbility", "ability": ability.lower(), "value": int(value)})
    if match := re.search(r"base attack bonus \+(\d+)", text, flags=re.I):
        requirements.append({"$type": "MinBAB", "value": int(match.group(1))})
    if match := re.search(r"Spellcaster level (\d+)", text, flags=re.I):
        requirements.append({"$type": "MinCasterLevel", "value": int(match.group(1))})

    for name, class_id in CLASSES.items():
        if match := re.search(rf"\b{name} level (\d+)", text, flags=re.I):
            requirements.append({"$type": "MinClassLevel", "classId": class_id, "value": int(match.group(1))})
    for name in DOMAINS:
        if re.search(rf"\b{name} domain\b", text, flags=re.I):
            requirements.append({"$type": "HasDivineDomain", "domainId": f"domain:{name.lower()}"})

    if "Good alignment" in text:
        requirements.append({"$type": "AlignmentReq", "allowed": ["lg", "ng", "cg"]})
    if "Chaotic alignment" in text:
        requirements.append({"$type": "AlignmentReq", "allowed": ["cg", "cn", "ce"]})

    dependency_ids = [ability_id for name, ability_id in SALIENT_NAMES.items()
                      if f"{name} salient divine ability" in text]
    if anchor in {"life-and-death", "mass-life-and-death"}:
        alternatives = ["salient:gift_of_life", "salient:hand_of_death"]
        requirements.append({"$type": "AnyOf", "options": [
            {"$type": "HasSalientDivineAbility", "abilityId": value} for value in alternatives
        ]})
        dependency_ids = [value for value in dependency_ids if value not in alternatives]
    for ability_id in dict.fromkeys(dependency_ids):
        requirements.append({"$type": "HasSalientDivineAbility", "abilityId": ability_id})

    for name, feat_id in FEATS.items():
        feat_text = text.replace("Divine Weapon Focus salient divine ability", "")
        if name.casefold() in feat_text.casefold():
            requirements.append({"$type": "HasFeat", "featId": feat_id})

    skills = {
        r"Bluff (\d+) ranks": "skill:bluff",
        r"(?:Wilderness Lore|Survival) (\d+) ranks": "skill:survival",
        r"Knowledge \(nature\) (\d+) ranks": "skill:knowledge_nature",
    }
    for pattern, skill_id in skills.items():
        if match := re.search(pattern, text, flags=re.I):
            requirements.append({"$type": "MinSkillRanks", "skillId": skill_id, "value": int(match.group(1))})

    if anchor == "divine-fast-healing":
        requirements.append({"$type": "AnyOf", "options": [
            {"$type": "MinAbility", "ability": "con", "value": 29},
            {"$type": "HasFastHealing"},
        ]})
    if anchor == "master-crafter":
        requirements.append({
            "$type": "MinSkillRanksAcross",
            "skillIds": [
                "skill:craft_alchemy", "skill:craft_armorsmithing", "skill:craft_blacksmithing",
                "skill:craft_bowmaking", "skill:craft_carpentry", "skill:craft_gemcutting",
                "skill:craft_leatherworking", "skill:craft_metalworking", "skill:craft_painting",
                "skill:craft_pottery", "skill:craft_sculpting", "skill:craft_shipmaking",
                "skill:craft_stonemasonry", "skill:craft_trapmaking", "skill:craft_weaponsmithing",
                "skill:craft_woodworking",
            ],
            "value": 23,
            "minCount": 2,
        })

    # These clauses rely on a selected parameter or a class feature/special quality that the
    # generic prerequisite grammar cannot compare exactly. Preserve and surface them explicitly.
    manual = anchor in {
        "divine-blessing", "divine-rogue", "divine-skill-focus", "divine-sneak-attack",
        "divine-weapon-specialization", "extra-energy-immunity", "irresistible-blows",
        "irresistible-performance", "true-knowledge",
    }
    return requirements, manual


def extract(source: Path) -> list[dict[str, object]]:
    document = source.read_text(encoding="utf-8")
    marker = '<h2 class="subtitle">In Alphabetical Order</h2>'
    body = document[document.index(marker) + len(marker):]
    pieces = re.split(r'<h5><a id="([^"]+)"></a>', body)
    abilities: list[dict[str, object]] = []

    for index in range(1, len(pieces), 2):
        anchor = pieces[index]
        block = pieces[index + 1]
        heading, _, remainder = block.partition("</h5>")
        name = title_case(plain(heading))
        text = plain(remainder)

        prerequisite = None
        match = re.search(r"Prerequisites?:\s*(.*?)(?=\s+Benefit:)", text, flags=re.I)
        if match:
            prerequisite = match.group(1).strip().rstrip(".") + "."

        benefit = re.search(
            r"Benefit:\s*(.*?)(?=\s+(?:Notes|Rest|Suggested Portfolio Elements):|$)",
            text,
            flags=re.I,
        )
        description = benefit.group(1).strip() if benefit else text
        notes_match = re.search(
            r"Notes:\s*(.*?)(?=\s+(?:Rest|Suggested Portfolio Elements):|$)", text, flags=re.I
        )
        rest_match = re.search(
            r"Rest:\s*(.*?)(?=\s+(?:Notes|Suggested Portfolio Elements):|$)", text, flags=re.I
        )

        portfolio_match = re.search(r"Suggested Portfolio Elements:\s*(.*?)(?=$)", text, flags=re.I)
        portfolios = []
        if portfolio_match:
            portfolios = [
                value.strip().rstrip(".")
                for value in portfolio_match.group(1).split(",")
                if value.strip()
            ]

        rank_match = re.search(r"Divine rank\s+(\d+)", prerequisite or "", flags=re.I)
        minimum_rank = int(rank_match.group(1)) if rank_match else 1
        prerequisites, manual_review = structured_prerequisites(anchor, prerequisite)
        repeatable = bool(re.search(
            r"(?:taken|selected|choose) (?:this ability )?(?:more than once|multiple times)|"
            r"each time (?:the deity )?(?:takes|selects|chooses) this ability|"
            r"can have this ability multiple times",
            text,
            flags=re.I,
        ))

        ability: dict[str, object] = {
            "id": f"salient:{anchor.replace('-', '_')}",
            "name": name,
            "description": description,
            "minimumDivineRank": minimum_rank,
            "prerequisites": prerequisites,
            "requiresManualReview": manual_review,
            "suggestedPortfolioElements": portfolios,
            "repeatable": repeatable,
        }
        if prerequisite:
            ability["prerequisiteText"] = prerequisite
        if notes_match:
            ability["notes"] = notes_match.group(1).strip()
        if rest_match:
            ability["rest"] = rest_match.group(1).strip()
        abilities.append(ability)

    return abilities


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, default=DEFAULT_SOURCE)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    args = parser.parse_args()
    abilities = extract(args.source)
    if len(abilities) != 99:
        raise SystemExit(f"Expected 99 abilities, extracted {len(abilities)}")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(abilities, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
