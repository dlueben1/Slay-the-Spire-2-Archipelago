# Guided wizard maintenance

The wizard deliberately separates what the player means from what Archipelago accepts:

1. `generated/optionCatalog.ts` types and imports the generated technical schema.
2. `WizardAnswers.ts` stores only player-facing intent.
3. `WizardStep.ts` declares step order, question copy, and conditional visibility.
4. `components/wizard` renders those questions and emits answer-model changes.
5. `compiler/compileWizardAnswers.ts` starts with every generated default.
6. Section compilers, such as `compiler/applyCharacterOptions.ts`, translate answers into the option keys they own.
7. `validation/validateOptions.ts` checks the complete result against the generated schema.
8. `review.ts` and `services/YamlService.ts` independently turn valid answers and options into review output.

## Adding a guided question

When a new question only affects an existing section:

1. Add the semantic answer field and type to that section's interface in `WizardAnswers.ts`.
2. Initialize it in `createDefaultWizardAnswers`.
3. Add its ID, player-facing title, and optional visibility predicate to the section in `WizardStep.ts`.
4. Add the control to the matching component under `components/wizard`. Use `WizardQuestion.vue` for top-level prompts and the shared classes in `wizard.css` for established visual patterns.
5. Translate the answer in that section's compiler. Do not write Archipelago keys from the component or question definition.
6. Add tests for the mapping and any conditional visibility.

For a new section, also create a dedicated answer interface, step component, section compiler, and review-summary builder. Register its compiler in `compileWizardAnswers` before final validation.

## Compiler versus validation

A compiler understands meaning. For example, it knows that the player's “all characters must finish” answer becomes `num_chars_goal: 0`, and that fixed character availability affects both `lock_characters` and `unlocked_character`.

Validation understands the generated schema. It knows that a choice must use a generated choice name, a range must stay between generated boundaries, every output key must exist, and the final configuration must contain every generated option. It should not decide what a player's answer means.

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
