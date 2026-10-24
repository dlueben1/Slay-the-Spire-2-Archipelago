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
    relics: dict[str, list[int]] = {}
    for number, region in table.RELIC_REGIONS.items():
        relics.setdefault(region, []).append(number)
    rows = [("Region", "Act", "Power", "Seeded", "Card rewards", "Vanilla cards*", "Min cards",
             "Rest tier", "Smith tiers", "Own smith share", "Removal tiers", "Shop scale", "Relic checks")]
    for region, check in table.REGIONS.items():
        rows.append((
            region, str(check.act), f"{check.power:g}",
            f"{check.seeded_power:g}" if check.seeded_power is not None else "-", str(check.card_rewards),
            f"{check.vanilla_card_strength:g}", f"{check.minimum_cards:g}",
            str(check.rest_tier) if check.rest_tier else "-", tiers(check.smith_tiers),
            f"{check.smith:g}" if check.smith is not None else "-",
            tiers(check.removal_tiers), f"{check.shop_scale:.3g}" if check.shop_scale else "-",
            ", ".join(map(str, relics.get(region, []))) or "-",
        ))
    lines = [
        "Regions: power >= base power + missing support, and card strength >= min cards",
        *format_columns(rows),
        "* Half shuffle only: vanilla card rewards already in the run, counted as card strength.",
        "  Seeded: power with Seeded Run, when it differs.",
        "  Rest, smith and removal tiers count only when that support is shuffled.",
        "  Own smith share: the share of this act's rest sites passed, which its own smith tier",
        "  counts for; earlier acts' tiers count in full.",
        "  Each elite is entered from the one before; Act 2 and 3 chests and first elites need",
        "  the previous act's last elite.",
        "",
        *format_claimable_relics(table),
        "",
        "Card strength and power from AP items",
        "  Card strength by ordinary Card Rewards held (each later one adds the last step):",
        f"    Half shuffle: {values(table.CARD_REWARD_STRENGTH)}",
        f"    All shuffled: {values(table.CARD_REWARD_STRENGTH_ALL_SHUFFLED)}",
        f"  Vanilla card reward (half shuffle): {table.VANILLA_CARD_POWER:g}",
        f"  Rare Card Reward: {table.RARE_CARD_POWER:g}",
        f"  Relic: {table.RELIC_POWER:g}",
        "  Starter card and relic upgrades: per character in characters.py",
        "",
        "Missing support added to the requirement",
        f"  Progressive Rest, by act: {values(table.REST_POWER_BY_ACT)}"
        f" (x{table.REST_SHARE_WHILE_SMITHING:g} while that act's smith tier works)",
        f"  Progressive Smith, by tier: {values(table.SMITH_POWER_BY_TIER)}",
        f"  Progressive Shop Remove, by tier: {values(table.SHOP_REMOVE_POWER_BY_TIER)}",
        f"  Progressive Ancient, by tier: {values(table.ANCIENT_POWER_BY_TIER)}",
        f"  Shop slots, scaled by act ({values(table.SHOP_SCALE_BY_ACT, '.3g')}):",
        *(f"    {item}: {values(slot_values)}" for item, slot_values in table.SHOP_SLOT_VALUES),
        "  With Gold Sanity, the offered choices count only as far as a run's gold reaches a",
        f"  normal run's ({values(table.normal_run_gold(act) for act in (1, 2, 3))} by Acts 1 / 2 / 3).",
        f"  A run has {values(table.gold_sanity_run_gold(act, 0, 0) for act in (1, 2, 3))} before Elite Gold"
        f" ({table.ELITE_GOLD}) and Boss Gold ({table.BOSS_GOLD}), with every Combat Gold assumed.",
        "",
        *format_shop_gold(table),
    ]
    return "\n".join(lines)


def format_claimable_relics(table: ModuleType) -> list[str]:
    rows = [("Region", "Walking around elites", "After an earlier act's elites")]
    for region in table.REGIONS:
        alternatives = table.ELITE_RELIC_REWARDS.get(region, {})
        rows.append((region, str(table.PATH_RELIC_REWARDS[region]),
                     ", ".join(f"{elites} {count}" for elites, count in alternatives.items()) or "-"))
    return [
        "Claimable relics: power counts at most Relic Rewards Available Anytime + these relic rewards",
        *format_columns(rows),
    ]


# Shop Sanity Costs choices, in option value order.
SHOP_COST_MODES = ("Fixed", "Super Discount", "Discount", "Tiered")
# Shop pages to show: (label, card, neutral, relic and potion slots, removal checks).
SHOP_PAGES = (("Default page 2/1/2/2", 2, 1, 2, 2, False), ("Full page 5/2/3/3 + removals", 5, 2, 3, 3, True))


def format_shop_gold(table: ModuleType) -> list[str]:
    prices = ", ".join(f"{category} {price}" for category, price in table.SHOP_TIERED_PRICES.items())
    shares = ", ".join(f"{mode} {share:g}" for mode, share in zip(SHOP_COST_MODES, table.SHOP_PRICE_SHARE)
                       if share is not None)
    per_visit = ", ".join(f"{mode} {checks}" for mode, checks in zip(SHOP_COST_MODES, table.SHOP_CHECKS_PER_VISIT))
    rows = [("Elite and Boss Gold needed", *SHOP_COST_MODES)]
    for label, *slots in SHOP_PAGES:
        page = table.shop_page(*slots)
        rows.append((label, *(values(table.shop_gold_needed(page, act, mode) for act in (1, 2, 3))
                              for mode in range(len(SHOP_COST_MODES)))))
    return [
        "Shop gold (Shop and Gold Sanity), by act",
        f"  Elite Gold {table.ELITE_GOLD}, Boss Gold {table.BOSS_GOLD}; a run already has "
        f"{table.STARTING_GOLD} plus {table.CHEST_GOLD} per earlier chest.",
        f"  Dearest Tiered check by page position: {prices}.",
        f"  Fixed checks cost {table.FIXED_SHOP_PRICE}; other modes charge a share of Tiered: {shares}.",
        f"  Checks one visit should afford: {per_visit}.",
        *(f"  {line}" for line in format_columns(rows)),
    ]


def main() -> None:
    print(format_tables(load_table()))


if __name__ == "__main__":
    main()
