"""Regression for #2580: delayed prereleases must not evict pending main builds.

Model GitHub's documented bounded queue, not a live image-publishing workflow:
https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/control-workflow-concurrency
The actual workflow policy and promotion shell are exercised below as well.
"""
import os
from pathlib import Path
import re
import subprocess
import tempfile
import textwrap
import unittest

REPO = Path(__file__).resolve().parents[2]


def job(workflow, name):
    text = (REPO / '.github/workflows' / workflow).read_text()
    return re.search(rf'^  {name}:\n(.*?)(?=^  [\w-]+:|\Z)',
                     text, re.MULTILINE | re.DOTALL)[1]


def concurrency(block):
    body = re.search(r'^    concurrency:\n((?:      .*\n)+)', block, re.MULTILINE)[1]
    return dict(re.findall(r'^      ([\w-]+): (.+)$', body, re.MULTILINE))


class DevPublishQueueTests(unittest.TestCase):
    def setUp(self):
        self.publish = job('docker-dev.yml', 'publish')
        self.promote = job('docker-publish.yml', 'move-dev')

    def test_writers_share_non_cancelling_retaining_lock(self):
        main = concurrency(self.publish)
        main['group'] = main['group'].replace('${{ github.ref }}', 'refs/heads/main')
        self.assertEqual(main, concurrency(self.promote))
        self.assertEqual(main, {'group': 'docker-dev-refs/heads/main',
                                'cancel-in-progress': 'false', 'queue': 'max'})
        self.assertIn('uses: ./.github/workflows/_docker-dev-publish.yml', self.publish)
        # The caller holds the lock for validation AND all base/derived image writes.
        self.assertNotIn('concurrency:', (REPO / '.github/workflows/_docker-dev-publish.yml').read_text())

    def test_quiet_period_remains_cancellable(self):
        policy = concurrency(job('docker-dev.yml', 'quiet-period'))
        self.assertEqual(policy['cancel-in-progress'], 'true')
        self.assertNotIn('queue', policy)
        self.assertNotEqual(policy['group'], concurrency(self.publish)['group'])

    def pending(self, arrivals, policy=None):
        # A is running with the shared lock; new jobs cannot write until A releases it.
        pending = []
        cancelled = []
        for name in arrivals:
            setting = policy or concurrency(self.publish if name == 'C' else self.promote)['queue']
            if setting != 'max':
                cancelled.extend(pending)
                pending = [name]
            elif len(pending) == 100:
                cancelled.append(name)
            else:
                pending.append(name)
        return pending, cancelled

    def test_delayed_prerelease_does_not_lose_newest_main_in_either_order(self):
        for arrivals in [('C', 'B'), ('B', 'C')]:
            with self.subTest(arrivals=arrivals):
                pending, cancelled = self.pending(arrivals)
                self.assertEqual(pending, list(arrivals))
                self.assertEqual(cancelled, [])
                # No later push is needed. Check either possible execution order rather
                # than depending on scheduling order; stale B skips and C publishes.
                for execution in (pending, pending[::-1]):
                    writes = [name for name in execution if name == 'C']
                    self.assertEqual(writes, ['C'])
        self.assertEqual(self.pending(('C', 'B'), policy='single'), (['B'], ['C']))

    def test_queue_cap_rejects_new_arrival_not_existing_pending_main(self):
        arrivals = ['C'] + [f'old-{i}' for i in range(100)]
        pending, cancelled = self.pending(arrivals)
        self.assertEqual(len(pending), 100)
        self.assertIn('C', pending)
        self.assertEqual(cancelled, ['old-99'])
        # Retention is bounded, not an unconditional eventual-publication guarantee.
        _, cancelled = self.pending([f'old-{i}' for i in range(100)] + ['C'])
        self.assertEqual(cancelled, ['C'])

    def run_promotion(self, head, lookup_exit=0):
        # Execute the real workflow shell with network and registry commands stubbed.
        script = textwrap.dedent(self.promote.split('        run: |\n', 1)[1])
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / 'curl').write_text('#!/bin/sh\nprintf \'%s\\n\' "$RESPONSE"\nexit "$LOOKUP_EXIT"\n')
            (root / 'docker').write_text('#!/bin/sh\nprintf \'%s\\n\' "$*" >> "$WRITES"\n')
            for name in ('curl', 'docker'):
                (root / name).chmod(0o755)
            env = dict(os.environ, PATH=f'{root}:{os.environ["PATH"]}',
                       RESPONSE='{"sha": "' + head + '"}', LOOKUP_EXIT=str(lookup_exit),
                       WRITES=str(root / 'writes'), GITHUB_STEP_SUMMARY=str(root / 'summary'),
                       GITHUB_SHA='B', VERSION='1.0.0-rc.1', GITHUB_TOKEN='test',
                       GITHUB_API_URL='https://example.invalid', GITHUB_REPOSITORY='test/test')
            result = subprocess.run(['bash', '--noprofile', '--norc', '-eo', 'pipefail', '-c', script],
                                    env=env, text=True, capture_output=True)
            writes = (root / 'writes').read_text().splitlines() if (root / 'writes').exists() else []
            summary = (root / 'summary').read_text() if (root / 'summary').exists() else ''
            return result, writes, summary

    def test_stale_promotion_skips_without_claiming_main_was_published(self):
        result, writes, summary = self.run_promotion('C')
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(writes, [])
        self.assertIn('without changing :dev', summary)
        self.assertNotIn("stays on main's build", summary)

    def test_current_promotion_moves_all_five_images(self):
        result, writes, _ = self.run_promotion('B')
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(len(writes), 5)
        for image, command in zip(('server', 'renderer', 'socketserver', 'edge', 'allinone'), writes):
            self.assertIn(f'sharpmush/sharpmush-{image}:dev', command)

    def test_lookup_error_fails_closed_even_with_valid_json(self):
        result, writes, _ = self.run_promotion('B', lookup_exit=22)
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(writes, [])


if __name__ == '__main__':
    unittest.main()
