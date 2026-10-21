"""Print the APWorld's power-logic tables from world/spire2/logic_table.py as plain text.

Usage: python scripts/print_logic_table.py
"""
from __future__ import annotations

import importlib.util
import sys
from pathlib import Path
from types import ModuleType

TABLE_PATH = Path(__file__).resolve().parents[1] / "world" / "spire2" / "logic_table.py"


def load_table() -> ModuleType:
    # Load the file on its own: importing the spire2 package would require Archipelago.
    spec = importlib.util.spec_from_file_location("spire2_logic_table", TABLE_PATH)
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


def tiers(count: int) -> str:
    if count <= 0:
        return "-"
    return "1" if count == 1 else f"1-{count}"


def values(numbers, number_format: str = "g") -> str:
    return " / ".join(format(number, number_format) for number in numbers)


def format_columns(rows: list[tuple[str, ...]]) -> list[str]:
    widths = [max(len(row[column]) for row in rows) for column in range(len(rows[0]))]
    lines = []
    for row in rows:
        cells = [row[0].ljust(widths[0])] + [cell.rjust(width) for cell, width in zip(row[1:], widths[1:])]
        lines.append("  ".join(cells).rstrip())
    return lines


def format_tables(table: ModuleType) -> str:
    rows = [("Region", "Act", "Power", "Card rewards", "Vanilla cards*", "Min cards",
             "Rest tier", "Smith tiers", "Removal tiers", "Shop scale")]
    for region, check in table.REGIONS.items():
        rows.append((
            region, str(check.act), f"{check.power:g}", str(check.card_rewards),
            f"{check.vanilla_card_strength:g}", f"{check.minimum_cards:g}",
            str(check.rest_tier) if check.rest_tier else "-", tiers(check.smith_tiers),
            tiers(check.removal_tiers), f"{check.shop_scale:.3g}" if check.shop_scale else "-",
        ))
    lines = [
        "Regions: power >= base power + missing support, and card strength >= min cards",
        *format_columns(rows),
        "* Half shuffle only: vanilla card rewards already in the run, counted as card strength.",
        "  Rest, smith and removal tiers count only when that support is shuffled.",
        "",
        "Card strength and power from AP items",
        f"  Card Reward: first {table.FULL_VALUE_CARD_REWARDS} worth 1 each "
        f"({table.FULL_VALUE_CARD_REWARDS_ALL_SHUFFLED} with all card rewards shuffled), "
        f"then {table.LATER_CARD_POWER:g} each",
        f"  Rare Card Reward: {table.RARE_CARD_POWER:g}",
        f"  Relic: {table.RELIC_POWER:g}",
        "  Starter card and relic upgrades: per character in characters.py",
        "",
        "Missing support added to the requirement",
        f"  Progressive Rest, by act: {values(table.REST_POWER_BY_ACT)}",
        f"  Progressive Smith, by tier: {values(table.SMITH_POWER_BY_TIER)}",
        f"  Progressive Shop Remove, by tier: {values(table.SHOP_REMOVE_POWER_BY_TIER)}",
        f"  Progressive Ancient, by tier: {values(table.ANCIENT_POWER_BY_TIER)}",
        f"  Shop slots, scaled by act ({values(table.SHOP_SCALE_BY_ACT, '.3g')}):",
        *(f"    {item}: {values(slot_values)}" for item, slot_values in table.SHOP_SLOT_VALUES),
        "",
        f"Shop gold by act: {values(table.SHOP_GOLD_BY_ACT)} "
        f"(Elite Gold {table.ELITE_GOLD}, Boss Gold {table.BOSS_GOLD})",
    ]
    return "\n".join(lines)


def main() -> None:
    print(format_tables(load_table()))


if __name__ == "__main__":
    main()
