"""Select the logic implementation; keep each model's rules in its own module."""
from . import rules_new, rules_old
# Preserve imports used by existing tests.
from .rules_new import SpireHasPower, SpireHasGold

__all__ = ["SpireHasPower", "SpireHasGold", "FALLBACK_REASON", "is_sparse", "select_logic", "set_rules"]

FALLBACK_REASON = (
    "Floor, gold, and potion checks are all disabled in a single-player generation; "
    "use old logic to avoid known sparse-fill failures."
)


def is_sparse(options) -> bool:
    """Whether a world has about one location per item: floor, gold and potion checks all off."""
    return not any((options.include_floor_checks, options.gold_sanity, options.potion_sanity))


def select_logic(world) -> None:
    options = world.options
    world.effective_logic = "new" if options.use_new_logic else "old"
    world.logic_fallback_reason = None

    # A sparse world generated alone has nowhere to put its items but its own few locations, and
    # new logic's late gates then leave fill too little room. Other players' locations give it
    # that room, as fuzzing confirms.
    if world.effective_logic == "new" and is_sparse(options) and world.multiworld.players == 1:
        world.effective_logic = "old"
        world.logic_fallback_reason = FALLBACK_REASON


def set_rules(world) -> None:
    implementation = rules_old if world.effective_logic == "old" else rules_new
    implementation.set_rules(world)
