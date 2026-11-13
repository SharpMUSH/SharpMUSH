import json
import tempfile
import unittest
import unittest.mock as mock
from pathlib import Path

import agent

JOB_ID = '0f8fad5b-d9cb-469f-a165-70867728950e'
FOUND = {'renderer': {'service': 'renderer'}, 'server': {'service': 'sharpmush-server'},
         'socketserver': {'service': 'socketserver'}}


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

    def test_env_values_lose_their_quotes(self):
        with tempfile.TemporaryDirectory() as directory:
            env = Path(directory) / '.env'
            env.write_text('SHARPMUSH_IMAGE_TAG="1.0.0"\nOTHER=\'x\'\nPLAIN=y\n')
            self.assertEqual(agent.read_env(env), {'SHARPMUSH_IMAGE_TAG': '1.0.0', 'OTHER': 'x', 'PLAIN': 'y'})

    def test_inspect_keeps_what_it_found_when_a_container_is_gone(self):
        result = mock.Mock(returncode=1, stdout='[{"Id": "a"}]', stderr='Error: No such container: b')
        with mock.patch.object(agent, 'docker', return_value=result):
            self.assertEqual(agent.present('container', ['a', 'b']), [{'Id': 'a'}])

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
                mock.patch.object(agent, 'components', return_value=found), \
                mock.patch.object(self.agent, 'wait_ready') as wait_ready:
            self.agent.run(None)
        self.assertEqual([call.args for call in project.compose.call_args_list], [
            ('pull', '--quiet', 'renderer', 'sharpmush-server'),
            ('up', '-d', '--no-deps', 'renderer'),
            ('up', '-d', '--no-deps', 'sharpmush-server'),
        ])
        self.assertEqual([call.args[1] for call in wait_ready.call_args_list], ['renderer', 'server'])
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
                mock.patch.object(agent, 'components', return_value=FOUND), \
                mock.patch.object(self.agent, 'read_registry') as read_registry, \
                mock.patch.object(agent, 'write_tag') as write_tag:
            self.agent.run(None)
        read_registry.assert_called_once()
        write_tag.assert_not_called()
        self.assertIn('no release tagged 9.9.9', self.agent.job['message'])
        project.compose.assert_not_called()
        self.assertEqual(self.agent.job['state'], 'failed')

    def run_with(self, project, found=FOUND, wait_ready=None):
        with mock.patch.object(agent, 'own_project', return_value=project), \
                mock.patch.object(agent, 'components', return_value=found), \
                mock.patch.object(self.agent, 'wait_ready', side_effect=wait_ready) as waited:
            self.agent.run(None)
        return waited

    def test_a_failed_pull_replaces_nothing_and_puts_the_tag_back(self):
        self.accept({'id': JOB_ID, 'components': ['renderer', 'server', 'socketserver'], 'tag': '1.1.0'})
        env = Path(self.directory.name) / '.env'
        env.write_text('SHARPMUSH_IMAGE_TAG=1.0.0\nOTHER=1\n')
        project = mock.Mock(env_file=env)
        project.tag_from_env.return_value = '1.0.0'
        project.compose.return_value = mock.Mock(returncode=1, stderr='manifest for sharpmush-socketserver:1.1.0 not found\n')
        self.agent.registry['tags'] = ['1.1.0', '1.0.0']
        self.run_with(project)
        self.assertEqual([call.args[0] for call in project.compose.call_args_list], ['pull'])
        self.assertEqual(env.read_text(), 'SHARPMUSH_IMAGE_TAG=1.0.0\nOTHER=1\n')
        self.assertEqual(self.agent.job['state'], 'failed')

    def test_a_server_that_does_not_come_back_keeps_the_socket_server(self):
        self.accept({'id': JOB_ID, 'components': ['renderer', 'server', 'socketserver'], 'tag': None})
        project = mock.Mock()
        project.compose.return_value = mock.Mock(returncode=0, stderr='')

        def ready(_, name, service):
            if name == 'server':
                raise RuntimeError('The server did not come back.')

        self.run_with(project, wait_ready=ready)
        ups = [call.args[-1] for call in project.compose.call_args_list if call.args[0] == 'up']
        self.assertEqual(ups, ['renderer', 'sharpmush-server'])
        self.assertIn('did not come back', self.agent.job['message'])

    RUNNING = {'state': 'running', 'health': None}

    def test_waiting_for_the_server_reads_its_ready_endpoint_until_the_deadline(self):
        with mock.patch.object(agent, 'components', return_value={'server': self.RUNNING}), \
                mock.patch.object(agent, 'http_ready', side_effect=[False, False, True]) as ready, \
                mock.patch.object(agent.time, 'sleep'):
            self.agent.wait_ready(mock.Mock(), 'server', 'sharpmush-server')
        self.assertEqual(ready.call_count, 3)
        ready.assert_called_with('http://sharpmush-server:8080/ready')
        clock = iter([0, 0, 1000])
        with mock.patch.object(agent, 'components', return_value={'server': self.RUNNING}), \
                mock.patch.object(agent, 'http_ready', return_value=False), \
                mock.patch.object(agent.time, 'sleep'), \
                mock.patch.object(agent.time, 'monotonic', side_effect=lambda: next(clock)):
            with self.assertRaises(RuntimeError):
                self.agent.wait_ready(mock.Mock(), 'server', 'sharpmush-server')

    def test_a_running_renderer_is_not_ready_until_its_healthcheck_passes(self):
        states = [{'renderer': {'state': 'running', 'health': 'starting'}},
                  {'renderer': {'state': 'running', 'health': 'healthy'}}]
        with mock.patch.object(agent, 'components', side_effect=states) as found, \
                mock.patch.object(agent.time, 'sleep'):
            self.agent.wait_ready(mock.Mock(), 'renderer', 'renderer')
        self.assertEqual(found.call_count, 2)

    def test_an_unhealthy_renderer_stops_the_update_before_the_socket_server(self):
        self.accept({'id': JOB_ID, 'components': ['renderer', 'server', 'socketserver'], 'tag': None})
        project = mock.Mock()
        project.compose.return_value = mock.Mock(returncode=0, stderr='')
        unhealthy = {name: {**status, 'state': 'running', 'health': 'unhealthy'} for name, status in FOUND.items()}
        clock = iter([0, 0, 1000])
        with mock.patch.object(agent, 'own_project', return_value=project), \
                mock.patch.object(agent, 'components', return_value=unhealthy), \
                mock.patch.object(agent, 'http_ready', return_value=True), \
                mock.patch.object(agent.time, 'sleep'), \
                mock.patch.object(agent.time, 'monotonic', side_effect=lambda: next(clock)):
            self.agent.run(None)
        ups = [call.args[-1] for call in project.compose.call_args_list if call.args[0] == 'up']
        self.assertEqual(ups, ['renderer'])
        self.assertEqual(self.agent.job['state'], 'failed')
        self.assertIn('renderer did not come back', self.agent.job['message'])
        self.assertIn('unhealthy', self.agent.job['message'])

    def test_the_socket_server_is_ready_when_its_own_endpoint_answers(self):
        with mock.patch.object(agent, 'components', return_value={'socketserver': self.RUNNING}), \
                mock.patch.object(agent, 'http_ready', return_value=True) as ready:
            self.agent.wait_ready(mock.Mock(), 'socketserver', 'socketserver')
        ready.assert_called_once_with('http://socketserver:4202/ready')

    def test_the_socket_time_is_checked_and_held_to_half_an_hour(self):
        thread = self.accept({'id': JOB_ID, 'components': ['socketserver'], 'tag': None, 'socketNotBefore': 'soon'})
        thread.return_value.start.assert_not_called()
        self.assertEqual(self.agent.job['message'], 'That is not a time.')
        other = '1f8fad5b-d9cb-469f-a165-70867728950e'
        thread = self.accept({'id': other, 'components': ['socketserver'], 'tag': None, 'socketNotBefore': '2999-01-01T00:00:00Z'})
        when = thread.call_args.kwargs['args'][0]
        self.assertLessEqual(when, agent.now() + agent.MAX_SOCKET_DELAY)

    def test_the_most_recent_ids_are_kept(self):
        self.agent.done = [f'{index:08x}-0000-4000-8000-000000000000' for index in range(200)]
        self.accept({'id': JOB_ID, 'components': ['renderer'], 'tag': None})
        self.assertEqual(self.agent.done[-1], JOB_ID)
        self.assertEqual(len(self.agent.done), 200)
        self.assertNotIn('00000000-0000-4000-8000-000000000000', self.agent.done)


if __name__ == '__main__':
    unittest.main()
