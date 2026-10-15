"""Select the logic implementation; keep each model's rules in its own module."""
from . import rules_new, rules_old
# Preserve imports used by the progression-analysis tools and existing tests.
from .rules_new import MID_ACT_1_POWER, SpireHasPower, SpireHasGold

__all__ = ["MID_ACT_1_POWER", "SpireHasPower", "SpireHasGold", "FALLBACK_REASON", "select_logic", "set_rules"]

FALLBACK_REASON = (
    "Floor, gold, and potion checks are all disabled; "
    "use old logic to avoid known sparse-fill failures."
)


def select_logic(world) -> None:
    options = world.options
    world.effective_logic = "new" if options.use_new_logic else "old"
    world.logic_fallback_reason = None

    # fallback to previous logic to prevent generation issues in restricted options
    if world.effective_logic == "new" and not any((
        options.include_floor_checks,
        options.gold_sanity, options.potion_sanity,
    )):
        world.effective_logic = "old"
        world.logic_fallback_reason = FALLBACK_REASON


def set_rules(world) -> None:
    implementation = rules_old if world.effective_logic == "old" else rules_new
    implementation.set_rules(world)
