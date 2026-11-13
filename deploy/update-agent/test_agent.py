import json
import tempfile
import unittest
import unittest.mock as mock
from pathlib import Path

import agent

JOB_ID = '0f8fad5b-d9cb-469f-a165-70867728950e'


class TagTests(unittest.TestCase):
    def test_release_tags_sort_newest_first_with_prereleases_below_their_release(self):
        tags = ['1.0.0', '1.10.0', '1.2.0', '1.2.0-rc.1', 'dev', 'latest', '1', '1.2', 'sha-abc']
        ordered = sorted((tag for tag in tags if agent.RELEASE.fullmatch(tag)), key=agent.release_key, reverse=True)
        self.assertEqual(ordered, ['1.10.0', '1.2.0', '1.2.0-rc.1', '1.0.0'])

    def test_tag_pattern_refuses_anything_a_shell_or_compose_could_read(self):
        for tag in ('1.0.0', '1.0.0-rc.1', 'latest', 'dev'):
            self.assertTrue(agent.TAG.fullmatch(tag), tag)
        for tag in ('', '1.0', '../x', '1.0.0;rm', '${HOME}', 'v1.0.0', '1.0.0 ', 'main'):
            self.assertFalse(agent.TAG.fullmatch(tag), tag)

    def test_docker_times_are_cut_to_microseconds(self):
        self.assertEqual(agent.timestamp('2026-11-13T21:40:00.123456789Z'), '2026-11-13T21:40:00.123456Z')
        self.assertEqual(agent.timestamp('2026-11-13T21:40:00Z'), '2026-11-13T21:40:00Z')
        self.assertIsNone(agent.timestamp('0001-01-01T00:00:00Z'))
        self.assertIsNone(agent.timestamp('nonsense'))


class EnvFileTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.root = Path(self.directory.name)

    def tearDown(self):
        self.directory.cleanup()

    def test_write_tag_replaces_only_the_tag_and_keeps_the_mode(self):
        env = self.root / '.env'
        env.write_text('SHARPMUSH_DOMAIN=example.org\nSHARPMUSH_IMAGE_TAG=1.0.0\nRESTIC_PASSWORD=secret\n')
        env.chmod(0o600)
        agent.write_tag(env, '1.1.0')
        self.assertEqual(env.read_text(), 'SHARPMUSH_DOMAIN=example.org\nSHARPMUSH_IMAGE_TAG=1.1.0\nRESTIC_PASSWORD=secret\n')
        self.assertEqual(env.stat().st_mode & 0o777, 0o600)

    def test_write_tag_adds_a_missing_tag(self):
        env = self.root / '.env'
        env.write_text('SHARPMUSH_DOMAIN=example.org\n')
        agent.write_tag(env, 'latest')
        self.assertIn('SHARPMUSH_IMAGE_TAG=latest', env.read_text().splitlines())

    def test_write_tag_refuses_a_value_that_is_not_a_tag(self):
        env = self.root / '.env'
        env.write_text('SHARPMUSH_IMAGE_TAG=1.0.0\n')
        with self.assertRaises(ValueError):
            agent.write_tag(env, '1.0.0\nEVIL=1')
        self.assertEqual(env.read_text(), 'SHARPMUSH_IMAGE_TAG=1.0.0\n')

    def project(self, compose):
        (self.root / 'compose.yml').write_text(compose)
        return agent.Project({
            'com.docker.compose.project': 'sharpmush',
            'com.docker.compose.project.working_dir': str(self.root),
            'com.docker.compose.project.config_files': str(self.root / 'compose.yml'),
        })

    def test_tag_comes_from_env_then_the_compose_default(self):
        project = self.project('image: sharpmush/sharpmush-server:${SHARPMUSH_IMAGE_TAG:-1.0.0}\n')
        self.assertEqual(project.tag_from_env(), '1.0.0')
        (self.root / '.env').write_text('SHARPMUSH_IMAGE_TAG=latest\n')
        self.assertEqual(project.tag_from_env(), 'latest')

    def test_a_compose_file_that_fixes_its_tag_has_none_to_move(self):
        project = self.project('image: sharpmush/sharpmush-server:1.0.0\n')
        (self.root / '.env').write_text('SHARPMUSH_IMAGE_TAG=latest\n')
        self.assertIsNone(project.tag_from_env())

    def test_a_project_directory_not_mounted_at_its_own_path_is_refused(self):
        with self.assertRaises(RuntimeError):
            agent.Project({
                'com.docker.compose.project': 'sharpmush',
                'com.docker.compose.project.working_dir': '/no/such/directory',
                'com.docker.compose.project.config_files': '/no/such/directory/compose.yml',
            })


class AcceptTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        patcher = mock.patch.object(agent, 'STATE', Path(self.directory.name))
        patcher.start()
        self.addCleanup(patcher.stop)
        self.addCleanup(self.directory.cleanup)
        self.agent = agent.Agent()

    def accept(self, job):
        with mock.patch.object(agent.threading, 'Thread') as thread:
            self.agent.accept(job)
        return thread

    def test_a_job_runs_once_in_update_order(self):
        thread = self.accept({'id': JOB_ID, 'components': ['socketserver', 'renderer'], 'tag': None})
        thread.return_value.start.assert_called_once()
        self.assertEqual(self.agent.job['components'], ['renderer', 'socketserver'])
        self.assertEqual(self.agent.job['state'], 'running')
        self.agent.job['state'] = 'succeeded'
        again = self.accept({'id': JOB_ID, 'components': ['renderer'], 'tag': None})
        again.return_value.start.assert_not_called()

    def test_unknown_components_and_bad_tags_fail_without_running(self):
        for job in ({'id': JOB_ID, 'components': ['nats'], 'tag': None},
                    {'id': '1f8fad5b-d9cb-469f-a165-70867728950e', 'components': ['server'], 'tag': '$(reboot)'},
                    {'id': '2f8fad5b-d9cb-469f-a165-70867728950e', 'components': [], 'tag': None}):
            with self.subTest(job=job):
                thread = self.accept(job)
                thread.return_value.start.assert_not_called()
                self.assertEqual(self.agent.job['state'], 'failed')

    def test_a_malformed_id_is_ignored(self):
        thread = self.accept({'id': '../../etc', 'components': ['server'], 'tag': None})
        thread.return_value.start.assert_not_called()
        self.assertIsNone(self.agent.job)

    def test_a_second_job_waits_while_one_runs(self):
        self.accept({'id': JOB_ID, 'components': ['renderer'], 'tag': None})
        thread = self.accept({'id': '1f8fad5b-d9cb-469f-a165-70867728950e', 'components': ['server'], 'tag': None})
        thread.return_value.start.assert_not_called()
        self.assertEqual(self.agent.job['id'], JOB_ID)

    def test_a_job_running_when_the_agent_stopped_is_reported_failed(self):
        self.accept({'id': JOB_ID, 'components': ['renderer'], 'tag': None})
        restarted = agent.Agent()
        restarted.resume()
        self.assertEqual(restarted.job['state'], 'failed')
        self.assertIn(JOB_ID, restarted.done)

    def test_run_replaces_each_component_with_pull_then_up(self):
        self.accept({'id': JOB_ID, 'components': ['renderer', 'server'], 'tag': None})
        project = mock.Mock()
        project.compose.return_value = mock.Mock(returncode=0, stderr='')
        found = {'renderer': {'service': 'renderer'}, 'server': {'service': 'sharpmush-server'}}
        with mock.patch.object(agent, 'own_project', return_value=project), \
                mock.patch.object(agent, 'components', return_value=found):
            self.agent.run(None)
        self.assertEqual([call.args for call in project.compose.call_args_list], [
            ('pull', '--quiet', 'renderer'), ('up', '-d', '--no-deps', 'renderer'),
            ('pull', '--quiet', 'sharpmush-server'), ('up', '-d', '--no-deps', 'sharpmush-server'),
        ])
        self.assertEqual(self.agent.job['state'], 'succeeded')
        self.assertEqual(json.loads((Path(self.directory.name) / 'job.json').read_text())['state'], 'succeeded')

    def test_run_stops_at_the_first_failure(self):
        self.accept({'id': JOB_ID, 'components': ['renderer', 'server'], 'tag': None})
        project = mock.Mock()
        project.compose.return_value = mock.Mock(returncode=1, stderr='pull access denied\n')
        found = {'renderer': {'service': 'renderer'}, 'server': {'service': 'sharpmush-server'}}
        with mock.patch.object(agent, 'own_project', return_value=project), \
                mock.patch.object(agent, 'components', return_value=found):
            self.agent.run(None)
        self.assertEqual(project.compose.call_count, 1)
        self.assertEqual(self.agent.job['state'], 'failed')
        self.assertIn('pull access denied', self.agent.job['message'])

    def test_run_refuses_a_tag_the_registry_does_not_have(self):
        self.accept({'id': JOB_ID, 'components': ['renderer', 'server', 'socketserver'], 'tag': '9.9.9'})
        project = mock.Mock()
        project.tag_from_env.return_value = '1.0.0'
        self.agent.registry['tags'] = ['1.0.0']
        with mock.patch.object(agent, 'own_project', return_value=project), \
                mock.patch.object(agent, 'components', return_value={}), \
                mock.patch.object(self.agent, 'read_registry') as read_registry, \
                mock.patch.object(agent, 'write_tag') as write_tag:
            self.agent.run(None)
        read_registry.assert_called_once()
        write_tag.assert_not_called()
        self.assertIn('no release tagged 9.9.9', self.agent.job['message'])
        project.compose.assert_not_called()
        self.assertEqual(self.agent.job['state'], 'failed')


if __name__ == '__main__':
    unittest.main()
