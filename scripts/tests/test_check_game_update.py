import sys
import tempfile
import unittest
from pathlib import Path


# Appended so scripts/test_multiplayer_local.py does not shadow the test module of that name.
sys.path.append(str(Path(__file__).resolve().parents[1]))

from check_game_update import (
    REPO,
    find_game_data,
    parse_compile_errors,
    render_report,
    short_name,
    supported_versions,
    version_branches,
)


def findings(**overrides):
    return {
        "appliedClasses": 3,
        "failures": [],
        "typeLoadErrors": [],
        "patchedMethods": [],
        "contentChanges": [],
    } | overrides


class CompileErrorTests(unittest.TestCase):
    def test_keeps_each_error_once_with_repository_paths(self):
        line = f"{REPO}/client/StS2AP/Foo.cs(12,5): error CS0117: 'Bar' has no member 'Baz' [{REPO}/x.csproj]"
        self.assertEqual(
            parse_compile_errors(f"{line}\nBuild FAILED.\n{line}\n"),
            ["client/StS2AP/Foo.cs:12: CS0117: 'Bar' has no member 'Baz'"],
        )

    def test_ignores_warnings(self):
        self.assertEqual(parse_compile_errors("Foo.cs(1,1): warning CS0436: duplicate [x.csproj]"), [])


class VersionBranchTests(unittest.TestCase):
    def test_finds_directives_but_not_mentions(self):
        with tempfile.TemporaryDirectory() as temporary:
            repo = Path(temporary)
            client = repo / "client/StS2AP/Patches"
            client.mkdir(parents=True)
            (client / "A.cs").write_text(
                "// STS2_AT_LEAST_0_100_0 in a comment\n#if STS2_AT_LEAST_0_111_0\n#endif\n#if !STS2_AT_LEAST_0_111_0\n",
                encoding="utf-8",
            )
            (client / "obj").mkdir()
            (client / "obj/B.cs").write_text("#if STS2_AT_LEAST_0_112_0\n", encoding="utf-8")
            self.assertEqual(
                version_branches(repo),
                {(0, 111, 0): ["client/StS2AP/Patches/A.cs:2", "client/StS2AP/Patches/A.cs:4"]},
            )

    def test_every_symbol_in_the_client_is_defined_by_the_project(self):
        # An undefined symbol is silently false, so its branch would never compile in.
        project = (REPO / "client/StS2AP/StS2AP.csproj").read_text(encoding="utf-8-sig")
        for version in version_branches(REPO):
            symbol = "STS2_AT_LEAST_" + "_".join(str(part) for part in version)
            self.assertIn(f"$(DefineConstants);{symbol}", project)

    def test_reads_supported_versions(self):
        props = "<Sts2MainApiCompat>0.107.1</Sts2MainApiCompat><Sts2BetaApiCompat>0.111.0</Sts2BetaApiCompat>"
        self.assertEqual(supported_versions(props), [(0, 107, 1), (0, 111, 0)])

    def test_reads_extra_supported_versions(self):
        props = ("<Sts2MainApiCompat>0.107.1</Sts2MainApiCompat><Sts2BetaApiCompat>0.113.0</Sts2BetaApiCompat>"
                 "<Sts2ExtraApiCompats>0.111.0; 0.112.0</Sts2ExtraApiCompats>")
        self.assertEqual(supported_versions(props), [(0, 107, 1), (0, 113, 0), (0, 111, 0), (0, 112, 0)])


class GameDataTests(unittest.TestCase):
    def test_uses_the_game_path_from_local_props(self):
        with tempfile.TemporaryDirectory() as temporary:
            game = Path(temporary) / "Slay the Spire 2"
            (game / "data_sts2_linuxbsd_x86_64").mkdir(parents=True)
            props = Path(temporary) / "local.props"
            props.write_text(f"<Project><STS2GamePath>{game}</STS2GamePath></Project>", encoding="utf-8")
            self.assertEqual(find_game_data(props), game / "data_sts2_linuxbsd_x86_64")

    def test_missing_props_finds_nothing(self):
        self.assertIsNone(find_game_data(Path("does/not/exist.props")))


class ReportTests(unittest.TestCase):
    def test_short_names_drop_namespaces_and_spell_generics(self):
        self.assertEqual(
            short_name("Game.Relics.Candy.Modify(Game.Player, System.Collections.Generic.List`1[[Game.Card]])"),
            "Candy.Modify(Player, List<Card>)",
        )

    def test_lists_failures_and_largest_change_first(self):
        small = {"name": "A.Small()", "status": "changed", "previous": "x", "current": "y"}
        large = {"name": "A.Large()", "status": "changed", "previous": "a\nb\nc", "current": "d\ne\nf"}
        same = {"name": "A.Same()", "status": "same", "previous": None, "current": None}
        report = render_report(
            "0.1.0", "0.2.0",
            findings(failures=[{"patchClass": "PatchA", "error": "gone"}], patchedMethods=[small, large, same]),
            [], {}, [],
        )
        self.assertIn("Patch classes that no longer attach: **1** of 4", report)
        self.assertIn("Patched game methods to read: **2** of 3", report)
        self.assertIn("- `PatchA`: gone", report)
        self.assertLess(report.index("`A.Large()` (6 changed lines)"), report.index("`A.Small()` (2 changed lines)"))
        self.assertNotIn("A.Same()", report)
        self.assertIn("+d", report)

    def test_compile_errors_explain_the_rerun(self):
        report = render_report("0.1.0", "0.2.0", findings(), ["a.cs:1: CS0117: nope"], {}, [])
        self.assertIn("- `a.cs:1: CS0117: nope`", report)
        self.assertIn("Rerun after fixing these", report)

    def test_flags_branches_every_supported_version_has_passed(self):
        branches = {(0, 111, 0): ["a.cs:1"], (0, 115, 0): ["b.cs:2"]}
        report = render_report("0.1.0", "0.2.0", findings(), [], branches, [(0, 111, 0), (0, 112, 0)])
        self.assertIn("**0.111.0** (true on every supported version", report)
        self.assertIn("**0.115.0**: `b.cs:2`", report)


if __name__ == "__main__":
    unittest.main()
