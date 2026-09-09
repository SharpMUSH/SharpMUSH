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
        bases = dict(server='a' * 40, connectionserver='b' * 40, socketserver=None)
        def compare(base, head):
            return ({'server': ['Game/change'] if base == bases['server'] else [],
                     'connectionserver': ['Conn/change'] if base == bases['connectionserver'] else [],
                     'socketserver': ['Socket/change'] if base == '0' * 40 else []}, [])
        with patch('docker_dev.published_revision', side_effect=lambda image: bases[image]), \
             patch('docker_dev.compare', side_effect=compare):
            result = docker_dev.published_impact('head')
        self.assertEqual(result, {'server': ['Game/change'], 'connectionserver': ['Conn/change'],
                                  'socketserver': ['Socket/change']})


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
                                      'connectionserver': ['Conn/Program.cs'], 'socketserver': []})
            with patch('docker_dev.published_revision', side_effect=lambda image:
                       game_published if image == 'server' else published):
                result = docker_dev.published_impact(head)
            self.assertEqual(result, {'server': [], 'connectionserver': ['Conn/Program.cs'],
                                      'socketserver': []})


if __name__ == '__main__':
    unittest.main()
