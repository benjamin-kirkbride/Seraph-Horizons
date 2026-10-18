"""tools/prwatch.py: when the merge queue watcher says a PR has stopped moving.

The fixtures are trimmed `gh pr view --json state,mergeable,labels,statusCheckRollup,comments`
output, shaped like PR #1157's (dequeued after a failed queue run, requeued, merged).

Run with `python3 -m unittest discover -s tools/tests -p test_prwatch.py`.
"""

import datetime as dt
import importlib.util
import io
import unittest
from contextlib import redirect_stdout
from pathlib import Path
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
_spec = importlib.util.spec_from_file_location("prwatch", ROOT / "tools" / "prwatch.py")
prwatch = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(prwatch)

ASK = "2026-10-17T20:42:09Z"
NOW = dt.datetime(2026, 10, 17, 20, 50, tzinfo=dt.timezone.utc)


def check(name, conclusion="SUCCESS", started="2026-10-17T20:43:00Z", status="COMPLETED"):
    return {"__typename": "CheckRun", "name": name, "conclusion": conclusion, "status": status,
            "startedAt": started, "completedAt": started, "workflowName": "CI"}


def comment(login, body, created):
    return {"author": {"login": login}, "body": body, "createdAt": created}


def status_comment(state, created, extra=""):
    return comment("mergify", "<!---\nDO NOT EDIT\n-*- Mergify Payload -*-\n"
                   f'{{"version": 1, "state": "{state}"}}\n-*- Mergify Payload End -*-\n-->\n\n'
                   f"# Merge Queue Status\n\n- Entered queue{extra}", created)


def pr(state="OPEN", mergeable="MERGEABLE", labels=(), checks=None, comments=None):
    return {"state": state, "mergeable": mergeable,
            "labels": [{"name": n} for n in labels],
            "statusCheckRollup": [check("ci-ok"), check("Lock is consistent")] if checks is None else checks,
            "comments": [comment("benjamin-kirkbride", "@mergifyio queue", ASK),
                         status_comment("queued", "2026-10-17T20:42:16Z")] if comments is None else comments}


class Decide(unittest.TestCase):
    def test_moving(self):
        self.assertIsNone(prwatch.decide(pr(labels=["queued"]), NOW))

    def test_pending_checks_are_moving(self):
        checks = [check("ci-ok", conclusion="", status="IN_PROGRESS"), check("x", "SKIPPED"), check("y", "NEUTRAL")]
        self.assertIsNone(prwatch.decide(pr(checks=checks), NOW))

    def test_merged_and_closed(self):
        self.assertEqual(prwatch.decide(pr(state="MERGED"), NOW), "STATE MERGED")
        self.assertEqual(prwatch.decide(pr(state="CLOSED"), NOW), "STATE CLOSED")

    def test_merged_wins_over_everything(self):
        self.assertEqual(prwatch.decide(pr(state="MERGED", labels=["dequeued"], comments=[],
                                           checks=[check("ci-ok", "FAILURE")]), NOW), "STATE MERGED")

    def test_failed(self):
        for conclusion in ("FAILURE", "CANCELLED", "TIMED_OUT", "STARTUP_FAILURE"):
            checks = [check("ci-ok", conclusion), check("smoke")]
            self.assertEqual(prwatch.decide(pr(checks=checks), NOW), "FAILED ['ci-ok']", conclusion)

    def test_failed_status_context(self):
        checks = [{"__typename": "StatusContext", "context": "legacy", "state": "ERROR"}]
        self.assertEqual(prwatch.decide(pr(checks=checks), NOW), "FAILED ['legacy']")

    def test_a_rerun_supersedes_its_failure(self):
        checks = [check("smoke", "FAILURE", started="2026-10-17T20:43:00Z"),
                  check("smoke", "", started="2026-10-17T20:48:00Z", status="IN_PROGRESS")]
        self.assertIsNone(prwatch.decide(pr(checks=checks), NOW))
        self.assertEqual(prwatch.decide(pr(checks=list(reversed(checks))), NOW), None)

    def test_a_later_failure_still_counts(self):
        checks = [check("smoke", "SUCCESS", started="2026-10-17T20:43:00Z"),
                  check("smoke", "FAILURE", started="2026-10-17T20:48:00Z")]
        self.assertEqual(prwatch.decide(pr(checks=checks), NOW), "FAILED ['smoke']")

    def test_conflict(self):
        self.assertEqual(prwatch.decide(pr(mergeable="CONFLICTING"), NOW), "CONFLICT")

    def test_unknown_mergeable_is_moving(self):
        self.assertIsNone(prwatch.decide(pr(mergeable="UNKNOWN"), NOW))

    def test_dequeued(self):
        self.assertEqual(prwatch.decide(pr(labels=["dequeued"]), NOW), "DEQUEUED")

    def test_requeued_keeps_the_old_dequeued_label(self):
        # #1157 after `@mergifyio queue` again: Mergify adds `queued` and leaves `dequeued`.
        comments = [comment("benjamin-kirkbride", "@mergifyio queue", "2026-10-17T20:42:09Z"),
                    status_comment("dequeued", "2026-10-17T20:42:16Z",
                                   "\n- Left the queue\n\nRequeued — the merge queue status continues"),
                    comment("benjamin-kirkbride", "@mergifyio queue", "2026-10-17T21:51:28Z"),
                    status_comment("queued", "2026-10-17T21:51:33Z")]
        later = dt.datetime(2026, 10, 17, 22, 30, tzinfo=dt.timezone.utc)
        self.assertIsNone(prwatch.decide(pr(labels=["dequeued", "queued"], comments=comments), later))
        self.assertEqual(prwatch.decide(pr(labels=["dequeued"], comments=comments), later), "DEQUEUED")

    def test_not_queued(self):
        self.assertEqual(prwatch.decide(pr(comments=[]), NOW), "NOTQUEUED no queue command")
        others = [comment("benjamin-kirkbride", "LGTM, will @mergifyio queue later", ASK)]
        self.assertEqual(prwatch.decide(pr(comments=others), NOW), "NOTQUEUED no queue command")

    def test_requeue_command_counts(self):
        comments = [comment("benjamin-kirkbride", "@mergifyio requeue", ASK),
                    status_comment("queued", "2026-10-17T20:42:16Z")]
        self.assertIsNone(prwatch.decide(pr(comments=comments), NOW))

    def test_cancelled(self):
        for text in ("The queue request has been cancelled: new commits were pushed",
                     "This pull request has been removed from the queue"):
            comments = [comment("benjamin-kirkbride", "@mergifyio queue", ASK),
                        comment("mergify", text, "2026-10-17T20:42:30Z")]
            self.assertEqual(prwatch.decide(pr(comments=comments), NOW), "NOTQUEUED cancelled", text)

    def test_cancellation_of_an_earlier_request_does_not_count(self):
        comments = [comment("benjamin-kirkbride", "@mergifyio queue", "2026-10-17T20:30:00Z"),
                    comment("mergify", "The queue request has been cancelled", "2026-10-17T20:31:00Z"),
                    comment("benjamin-kirkbride", "@mergifyio queue", ASK),
                    status_comment("queued", "2026-10-17T20:42:16Z")]
        self.assertIsNone(prwatch.decide(pr(comments=comments), NOW))

    def test_no_reply(self):
        comments = [comment("benjamin-kirkbride", "@mergifyio queue", ASK)]
        soon = dt.datetime(2026, 10, 17, 20, 44, tzinfo=dt.timezone.utc)
        self.assertIsNone(prwatch.decide(pr(comments=comments), soon))
        self.assertEqual(prwatch.decide(pr(comments=comments), NOW), "NOTQUEUED no reply from Mergify")


class Cli(unittest.TestCase):
    def run_cli(self, responses, *args):
        out = io.StringIO()
        with mock.patch.object(prwatch, "fetch", side_effect=responses) as fetch, \
                mock.patch.object(prwatch.time, "sleep"), redirect_stdout(out):
            code = prwatch.main(["1157", "--delay", "0", "--interval", "0", *args])
        return code, out.getvalue().strip(), fetch.call_count

    def test_merged_exits_zero(self):
        self.assertEqual(self.run_cli([pr(state="MERGED")]), (0, "STATE MERGED", 1))

    def test_polls_through_gh_failures_until_a_result(self):
        moving = pr(labels=["queued"])
        self.assertEqual(self.run_cli([None, moving, pr(mergeable="CONFLICTING")]), (1, "CONFLICT", 3))

    def test_timeout(self):
        moving = pr(labels=["queued"])
        with mock.patch.object(prwatch.time, "monotonic", side_effect=[0, 5, 50]):
            code, out, calls = self.run_cli([moving, moving], "--interval", "20", "--timeout", "60")
        self.assertEqual((code, out, calls), (1, "timeout", 2))


if __name__ == "__main__":
    unittest.main()
