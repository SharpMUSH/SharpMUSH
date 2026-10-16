import unittest
import agent

class WorkerTests(unittest.TestCase):
    def test_snapshot_requires_summary(self):
        self.assertIsNone(agent.snapshot_id('{"snapshot_id":"' + 'a'*64 + '"}'))
    def test_complete_summary(self):
        self.assertEqual(agent.snapshot_id('{"message_type":"summary","snapshot_id":"' + 'a'*64 + '"}'), 'a'*64)
    def test_partial_failure(self):
        self.assertEqual(agent.receipt_for({'id':'job','nonce':'secret'}, 3, 'a'*64)['errorCode'], 'partial-backup')
    def test_fixed_source_mapping_rejects_live_world(self):
        with self.assertRaises(ValueError): agent.copy_path('/app/data/lightning/data.mdb', 'a'*32)
    def test_copy_must_belong_to_job(self):
        with self.assertRaises(ValueError): agent.copy_path('/app/data/backup.remote-jobs/' + 'b'*32 + '/20261016', 'a'*32)

from unittest import mock
import datetime
import os
import json
import subprocess
import tempfile
from pathlib import Path

class ProcessTests(unittest.TestCase):
    def job(self):
        return dict(id='a'*32, nonce='n'*64, repository='b2:bucket:path', password='private',
                    accountId='id', accountKey='key', copyPath='/app/data/backup.remote-jobs/' + 'a'*32 + '/20261016',
                    deadline=(datetime.datetime.now(datetime.timezone.utc) + datetime.timedelta(hours=1)).isoformat())

    def test_environment_drops_command_helpers_and_flags(self):
        with mock.patch.dict(os.environ, {'RESTIC_PASSWORD_COMMAND': 'evil', 'RESTIC_BACKUP_ARGS': '--files-from /data/lightning', 'AWS_PROFILE': 'evil'}):
            env = agent.environment(self.job())
        self.assertNotIn('RESTIC_PASSWORD_COMMAND', env)
        self.assertNotIn('RESTIC_BACKUP_ARGS', env)
        self.assertNotIn('AWS_PROFILE', env)
        self.assertEqual(env['RESTIC_PASSWORD'], 'private')

    def test_executes_fixed_arguments_and_accepts_full_summary(self):
        process = mock.MagicMock()
        process.stdout = iter([json.dumps(dict(message_type='summary', snapshot_id='b'*64))])
        process.wait.return_value = 0
        process.__enter__.return_value = process
        with mock.patch('agent.subprocess.Popen', return_value=process) as popen, mock.patch('agent.Path.is_file', return_value=True), mock.patch('agent.Path.exists', return_value=False), mock.patch('agent.Path.is_dir', return_value=True):
            receipt = agent.run_restic(self.job())
        self.assertEqual(receipt['snapshotId'], 'b'*64)
        args, kwargs = popen.call_args
        self.assertNotIn('shell', kwargs)
        self.assertEqual(args[0][-2:], ['/data/backup.remote-jobs/' + 'a'*32 + '/20261016', '/data/lightning.dataprotection-keys'])
        self.assertEqual(args[0][-3], '--')
        self.assertNotIn('private', str(args[0]))

    def test_missing_required_key_directory_fails_before_starting_restic(self):
        with mock.patch('agent.subprocess.Popen') as popen, mock.patch('agent.Path.is_file', return_value=True), mock.patch('agent.Path.is_dir', return_value=False):
            receipt = agent.run_restic(self.job())
        popen.assert_not_called()
        self.assertEqual(receipt['exitCode'], -3)
        self.assertEqual(receipt['errorCode'], 'required-key-ring-missing')

    def test_reconcile_never_runs_a_second_backup_or_asserts_success(self):
        job = self.job()
        job['reconcileOnly'] = True
        with mock.patch('agent.subprocess.Popen') as popen, mock.patch('agent.Path.is_file', return_value=True):
            receipt = agent.run_restic(job)
        popen.assert_not_called()
        self.assertEqual(receipt['exitCode'], -2)
        self.assertIsNone(receipt['snapshotId'])

    def test_receipt_file_contains_no_repository_credentials(self):
        with tempfile.TemporaryDirectory(prefix='worker-receipt-') as directory, mock.patch.object(agent, 'STATE', Path(directory)):
            agent.save_receipt(agent.receipt_for(self.job(), 0, 'c'*64))
            text = Path(directory, 'receipt.json').read_text()
            self.assertNotIn('private', text)
            self.assertNotIn('b2:', text)
            self.assertEqual(Path(directory, 'receipt.json').stat().st_mode & 0o777, 0o600)

class ResticIntegrationTests(unittest.TestCase):
    @unittest.skipUnless(os.environ.get('RESTIC_INTEGRATION_BINARY'), 'Set RESTIC_INTEGRATION_BINARY to run a disposable local-repository integration')
    def test_real_repository_success_wrong_password_and_partial_snapshot(self):
        binary = Path(os.environ['RESTIC_INTEGRATION_BINARY']).resolve()
        with tempfile.TemporaryDirectory(prefix='restic-integration-') as temporary:
            root = Path(temporary)
            source = root / 'copy'
            source.mkdir()
            (source / 'data.mdb').write_text('consistent completed fixture')
            keys = root / 'keys'
            keys.mkdir()
            (keys / 'key-fixture.xml').write_text('synthetic-test-key')
            job = ProcessTests().job()
            job['repository'] = str(root / 'repo')  # Test-only local backend; admin validation rejects this.
            environment = agent.environment(job)
            environment['RESTIC_CACHE_DIR'] = str(root / 'cache')
            subprocess.run([str(binary), 'init'], env=environment, stdout=subprocess.DEVNULL, stderr=subprocess.PIPE, check=True)
            with mock.patch('agent.copy_path', return_value=str(source)), mock.patch.object(agent, 'KEYS_DIRECTORY', keys), mock.patch.object(agent, 'STATE', root), mock.patch.dict(os.environ, {'PATH': str(binary.parent) + ':' + os.environ['PATH']}):
                receipt = agent.run_restic(job)
                self.assertEqual(receipt['exitCode'], 0)
                self.assertRegex(receipt['snapshotId'], r'^[a-f0-9]{64}$')
                snapshots = subprocess.run([str(binary), 'snapshots', '--json'], env=environment, stdout=subprocess.PIPE, check=True)
                self.assertEqual(json.loads(snapshots.stdout)[0]['id'], receipt['snapshotId'])
                restored_key = subprocess.run([str(binary), 'dump', receipt['snapshotId'], str(keys / 'key-fixture.xml')], env=environment, stdout=subprocess.PIPE, check=True)
                self.assertEqual(restored_key.stdout, b'synthetic-test-key')
                wrong = agent.run_restic(dict(job, password='incorrect-test-password'))
                self.assertEqual(wrong['exitCode'], 12)
                self.assertIsNone(wrong['snapshotId'])
                if os.geteuid() != 0:
                    unreadable = source / 'unreadable'
                    unreadable.write_text('cannot read')
                    unreadable.chmod(0)
                    try:
                        partial = agent.run_restic(job)
                        self.assertEqual(partial['exitCode'], 3)
                        self.assertIsNone(partial['snapshotId'])
                    finally:
                        unreadable.chmod(0o600)
