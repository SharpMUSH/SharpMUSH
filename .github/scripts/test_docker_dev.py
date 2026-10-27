import unittest
import os
from pathlib import Path
import subprocess
import tempfile
from unittest.mock import patch

import docker_dev


class PublishedRevisionTests(unittest.TestCase):
    def test_single_platform_and_attested_image_config(self):
        config = {'config': {'Labels': {'org.opencontainers.image.revision': 'a' * 40}}}
        self.assertEqual(docker_dev.image_revision(config), 'a' * 40)
        self.assertEqual(docker_dev.image_revision({'linux/amd64': config}), 'a' * 40)

    def test_missing_or_disagreeing_labels_rebuild(self):
        self.assertIsNone(docker_dev.image_revision({}))
        self.assertIsNone(docker_dev.image_revision({'config': {'Labels': {
            'org.opencontainers.image.revision': 'not-a-commit'}}}))
        self.assertIsNone(docker_dev.image_revision({
            'linux/amd64': {'config': {'Labels': {'org.opencontainers.image.revision': 'a' * 40}}},
            'linux/arm64': {'config': {'Labels': {'org.opencontainers.image.revision': 'b' * 40}}},
        }))

    def test_each_image_uses_its_own_published_baseline(self):
        bases = dict(server='a' * 40, renderer='b' * 40, socketserver=None)
        def compare(base, head):
            return ({'server': ['Game/change'] if base == bases['server'] else [],
                     'renderer': ['Conn/change'] if base == bases['renderer'] else [],
                     'socketserver': ['Socket/change'] if base == '0' * 40 else []}, [])
        with patch('docker_dev.published_revision', side_effect=lambda image: bases[image]), \
             patch('docker_dev.compare', side_effect=compare):
            result = docker_dev.published_impact('head')
        self.assertEqual(result, {'server': ['Game/change'], 'renderer': ['Conn/change'],
                                  'socketserver': ['Socket/change']})

    def test_reads_each_image_under_its_current_name(self):
        tags = []
        def inspect(args, text, capture_output):
            tags.append(args[4])
            return subprocess.CompletedProcess(args, 0, '{}', '')
        with patch('subprocess.run', side_effect=inspect):
            for image in ('server', 'renderer', 'socketserver', 'edge', 'allinone'):
                docker_dev.published_revision(image)
        self.assertEqual(tags, ['sharpmush/sharpmush-server:dev', 'sharpmush/sharpmush-renderer:dev',
                                'sharpmush/sharpmush-socketserver:dev', 'sharpmush/sharpmush-edge:dev',
                                'sharpmush/sharpmush-allinone:dev'])

    def test_never_pushed_tag_builds_and_other_registry_errors_fail(self):
        def failing(stderr):
            return lambda args, text, capture_output: subprocess.CompletedProcess(args, 1, '', stderr)
        with patch('subprocess.run', side_effect=failing('ERROR: docker.io/sharpmush/sharpmush-renderer:dev: not found')):
            self.assertIsNone(docker_dev.published_revision('renderer'))
        with patch('subprocess.run', side_effect=failing('ERROR: unexpected status: 503 Service Unavailable')):
            with self.assertRaises(subprocess.CalledProcessError):
                docker_dev.published_revision('renderer')


class FlyImageTests(unittest.TestCase):
    def test_rebuilds_when_its_own_files_change(self):
        def diff(*args):
            return 'deploy/fly/start.sh\0' if args[-2] == 'a' * 40 else 'README.md\0SharpMUSH.Server/x.cs\0'
        with patch('docker_dev.git', side_effect=diff):
            with patch('docker_dev.published_revision', return_value='a' * 40):
                self.assertTrue(docker_dev.own_files_changed('edge', 'head'))
                self.assertTrue(docker_dev.own_files_changed('allinone', 'head'))
            with patch('docker_dev.published_revision', return_value='b' * 40):
                self.assertFalse(docker_dev.own_files_changed('edge', 'head'))
                self.assertFalse(docker_dev.own_files_changed('allinone', 'head'))

    def test_missing_or_unlabelled_image_rebuilds(self):
        with patch('docker_dev.published_revision', return_value=None):
            self.assertTrue(docker_dev.own_files_changed('edge', 'head'))
        with patch('docker_dev.published_revision',
                   side_effect=subprocess.CalledProcessError(1, 'docker')):
            self.assertTrue(docker_dev.own_files_changed('allinone', 'head'))


class BatchedChangesTests(unittest.TestCase):
    def test_skipped_push_and_partial_publication_use_real_git_history(self):
        from test_image_impact import ImageImpactTests
        fixture = ImageImpactTests()
        fixture.setUp()
        with tempfile.TemporaryDirectory() as directory:
            previous = os.getcwd()
            self.addCleanup(os.chdir, previous)
            os.chdir(directory)
            def git(*args):
                return subprocess.check_output(['git', *args], stderr=subprocess.DEVNULL,
                                               text=True).strip()
            git('init')
            git('config', 'user.name', 'Test')
            git('config', 'user.email', 'test@example.invalid')
            for name, content in fixture.files.items():
                path = Path(name)
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(content)
            def commit(message):
                git('add', '.')
                git('commit', '-m', message)
                return git('rev-parse', 'HEAD')
            published = commit('Initial published images')
            Path('Game/Program.cs').write_text('first push')
            game_published = commit('Game image published, other images not yet updated')
            Path('Conn/Program.cs').write_text('second push')
            commit('Skipped intermediate push')
            Path('README.md').write_text('third push')
            head = commit('Documentation-only final push')
            with patch('docker_dev.published_revision', return_value=published):
                result = docker_dev.published_impact(head)
            self.assertEqual(result, {'server': ['Game/Program.cs'],
                                      'renderer': ['Conn/Program.cs'], 'socketserver': []})
            with patch('docker_dev.published_revision', side_effect=lambda image:
                       game_published if image == 'server' else published):
                result = docker_dev.published_impact(head)
            self.assertEqual(result, {'server': [], 'renderer': ['Conn/Program.cs'],
                                      'socketserver': []})


if __name__ == '__main__':
    unittest.main()
