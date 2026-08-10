# Guided wizard maintenance

The wizard deliberately separates what the player means from what Archipelago accepts:

1. `generated/optionCatalog.ts` types and imports the generated technical schema.
2. `WizardAnswers.ts` stores player-facing intent plus document-only input such as the player name.
3. `WizardOptionKey.ts` records the generated keys owned by each guided section.
4. `WizardStep.ts` declares step order, question copy, and conditional visibility.
5. `components/wizard` renders those questions and emits answer-model changes.
6. `compiler/compileWizardAnswers.ts` starts with every generated default.
7. Section compilers, such as `compiler/applyCharacterOptions.ts`, translate answers into the option keys they own.
8. `validation/validateOptions.ts` checks the complete result against the generated schema, while `validation/validateWizardMetadata.ts` validates values outside the game option mapping.
9. `GuidedOption.ts` selects implemented settings for the review YAML.
10. `review.ts` derives the prose summary, and `services/YamlService.ts` wraps selected options in the final Archipelago metadata and game mapping used by preview, copy, and download.

## Current guided coverage

The first-pass flow is organized as:

1. Character Setup
2. Gameplay Modifiers
3. Checks & Rewards
4. Death Link
5. Progression
6. Review

Character Setup presents one roster model for built-in and modded characters. Shared Ascensions compile through `characters`, `modded_characters`, `ascension`, and `ascension_down`. Individual Ascensions compile through `use_advanced_characters` and `advanced_characters`; ignored standard fields are removed from guided YAML. Random roster selection, unlock behavior, starting character, and completion goal apply to both modes.

Gameplay Modifiers owns Relic choice count and seeded runs. Checks & Rewards begins with the two Progressive Ancient choices, then provides the additional checks and rewards including Neow Sanity, floor, campfire, gold, potion, and card-reward shuffling. It also owns the Shop Slots toggle, the conditional Shop Sanity subsection, and the existing Filler Items table at the bottom of the step. Shop details remain hidden until Shop Slots is enabled.

Progression contains the shared Archipelago settings: progression balancing and accessibility. Progression balancing is an integral 0-99 value; the named Disabled, Normal, and Extreme buttons are presentation shortcuts for 0, 50, and 99 rather than separate compiler concepts.

Death Link hides received-effect controls until its controlling option is enabled. The wizard requires one received effect: Death Fragment, nonlethal Max HP damage, or Die. Selecting Die clears both nonlethal effects. Python exposes only `enable_death_fragments` and `death_link_damage_percent`, so `applyDeathLinkOptions.ts` maps disabled damage to 0 and Die to 100 while suppressing the mutually exclusive fragment setting. Do not reproduce that technical coupling in the Vue component.

The player name is not a game option and must never be added to a section compiler or `GuidedOption.ts`. `buildWizardYaml` validates it, adds the fixed builder description and `Slay the Spire II` game identifier, and nests the selected guided settings under the game-name mapping. `WizardView.vue` computes one shared document, and Review uses that exact string for preview, clipboard, and download behavior.

Inherited Archipelago template fields such as item links, plando, start inventory, and location overrides remain generated defaults because the guided UI does not ask about them yet.

## Adding a guided question

When a new question only affects an existing section:

1. Add the semantic answer field and type to that section's interface in `WizardAnswers.ts`.
2. Initialize it in `createDefaultWizardAnswers`.
3. Add its ID, player-facing title, and optional visibility predicate to the section in `WizardStep.ts`.
4. Add the control to the matching component under `components/wizard`. Use `WizardQuestion.vue` for top-level prompts and the shared classes in `wizard.css` for established visual patterns.
5. Translate the answer in that section's compiler. Do not write Archipelago keys from the component or question definition.
6. Add tests for the mapping and any conditional visibility.

For a new section, also create a dedicated answer interface, step component, section compiler, and review-summary builder. Register its generated keys in `WizardOptionKey.ts`, register its compiler in `compileWizardAnswers` before final validation, and include its keys in `GuidedOption.ts`. When one visible step contains several meaningful option families, follow Checks & Rewards: keep focused family compilers behind one step-level compiler facade.

The Filler Items subsection is a concrete example of this pattern: `FillerItem.ts` owns the semantic-ID-to-option-key mapping and schema-derived display data, `FillerStep.vue` edits only `FillerAnswers`, and `compiler/applyFillerOptions.ts` converts its four slider levels to canonical generated choice names. `compiler/applyChecksAndRewardsOptions.ts` composes that focused compiler with the ordinary-check and Shop compilers. The filler compiler tests compare the mapping with the generated `Filler Items` group so newly generated filler options require an explicit UX decision.

Character Setup demonstrates one player model targeting competing generated systems. `AscensionModifier.ts` documents the A1-A10 display catalog and canonical option names. `CharacterRoster.ts` merges built-in and modded entries for shared questions. `compiler/applyCharacterOptions.ts` selects the standard or advanced YAML representation, while `GuidedOption.ts` removes fields ignored by the active mode from the review YAML. `ModdedCharacterTable.vue` loads its player instructions from `docs/modded-characters.md` through the same sanitized Markdown pipeline and Vite public-doc sync as the setup guides.

## Compiler versus validation

A compiler understands meaning. For example, it knows that the player's “all characters must finish” answer becomes `num_chars_goal: 0`, and that fixed character availability affects both `lock_characters` and `unlocked_character`.

Validation understands accepted shapes. Generated-option validation knows that a choice must use a generated choice name, a range must be a whole number between generated boundaries, every output key must exist, and the final configuration must contain every generated option. Metadata validation separately knows that the final player name is non-empty and single-line. Neither validator should decide what a gameplay answer means.

## Styling questions

Shared question styles live in `components/wizard/wizard.css` and use `wizard-*` class names. Reuse these classes before adding new ones. Add a class to the shared stylesheet when it represents a reusable question pattern; keep truly step-specific layout in that component's scoped style block.

## Regenerating the option catalog

The generated `web/src/generated/options_compiled.json` file must be refreshed whenever option metadata changes in `world/spire2/options.py` or option grouping changes in `world/spire2/web_world.py`.

The generator expects an Archipelago source checkout containing `Options.py` in an `Archipelago` directory beside this repository. From the repository root, run:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\generate_options_for_web.ps1
```

The script uses `python` by default. To select another Python executable, pass it explicitly:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\generate_options_for_web.ps1 -PythonExecutable py
```

The script checks for its required Python packages and installs missing dependencies with `pip`. It then executes the current Python world definitions, writes `web/src/generated/options_compiled.json`, and includes source hashes so consumers can detect schema drift.

After regeneration, review the JSON diff and run the website verification commands:

```powershell
Set-Location .\web
npm test
npm run build
```
