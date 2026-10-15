from __future__ import annotations

import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import textwrap
import unittest


@unittest.skipUnless(sys.platform == "linux" and shutil.which("jq"), "Linux CI shell and jq")
class CiEventTests(unittest.TestCase):
    def test_actual_workflow_selector_preserves_coverage(self):
        workflow = Path(__file__).resolve().parents[2] / ".github/workflows/ci-event.yml"
        # Execute the workflow's single shell step, rather than a Python reimplementation.
        script = textwrap.dedent(workflow.read_text().split("run: |\n", 1)[1])
        current = "a" * 40

        def pr(mergeable, sha=current):
            return {"mergeable": mergeable, "head": {"sha": sha}}

        cases = [
            ("push", {}, "", "true"),
            ("push", {"7": pr(True)}, "", "false"),
            ("push", {"7": pr(False)}, "", "true"),
            ("push", {"7": pr(None)}, "", "true"),
            ("push", {"7": pr(True, "b" * 40)}, "", "true"),
            ("push", {"7": pr(False), "8": pr(True)}, "", "false"),
            ("pull_request", {"7": pr(True)}, "", "true"),
            ("workflow_dispatch", {"7": pr(True)}, "", "true"),
            ("push", {}, "pulls", None),
            ("push", {"7": pr(True)}, "7", None),
        ]
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            gh = root / "gh"
            gh.write_text(textwrap.dedent('''\
                #!/usr/bin/env python3
                import json, os, sys
                from pathlib import Path
                endpoint = next(arg for arg in sys.argv if arg.startswith('repos/'))
                with Path(os.environ['CALL_LOG']).open('a') as log:
                    log.write(json.dumps(sys.argv[1:]) + '\\n')
                suffix = endpoint.rsplit('/', 1)[1]
                if suffix == os.environ['FAIL_ENDPOINT']:
                    sys.exit(1)
                details = json.loads(os.environ['PR_DETAILS'])
                if suffix == 'pulls':
                    print('\\n'.join(details))
                else:
                    print(json.dumps(details[suffix]))
                '''))
            gh.chmod(0o755)
            for event, details, fail_endpoint, expected in cases:
                with self.subTest(event=event, details=details, fail_endpoint=fail_endpoint):
                    output, calls = root / "output", root / "calls"
                    output.unlink(missing_ok=True)
                    calls.unlink(missing_ok=True)
                    env = dict(os.environ, PATH=f"{root}:{os.environ['PATH']}",
                               GH_TOKEN="test-token", GITHUB_REPOSITORY="owner/project",
                               GITHUB_REPOSITORY_OWNER="owner", EVENT_NAME=event,
                               SOURCE_BRANCH="feature/multiplayer", SOURCE_SHA=current,
                               GITHUB_OUTPUT=str(output), CALL_LOG=str(calls),
                               PR_DETAILS=json.dumps(details), FAIL_ENDPOINT=fail_endpoint)
                    result = subprocess.run(["bash", "-e", "-o", "pipefail", "-c", script],
                                            env=env, capture_output=True, text=True)
                    if expected is None:
                        self.assertNotEqual(result.returncode, 0)
                        self.assertFalse(output.exists())
                    else:
                        self.assertEqual(result.returncode, 0, result.stderr)
                        self.assertEqual(output.read_text().strip(), f"should_run={expected}")
                    self.assertEqual(calls.exists(), event == "push")
                    if calls.exists():
                        recorded = [json.loads(line) for line in calls.read_text().splitlines()]
                        self.assertIn("repos/owner/project/pulls", recorded[0])
                        self.assertIn("head=owner:feature/multiplayer", recorded[0])


if __name__ == "__main__":
    unittest.main()
