import io
import json
import os
import shutil
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import release_images


class TagTests(unittest.TestCase):
    def test_first_release_moves_every_tag(self):
        self.assertEqual(release_images.tags('1.0.0', []), ['1.0.0', '1.0', '1', 'latest'])

    def test_newer_release_moves_every_tag(self):
        self.assertEqual(release_images.tags('1.1.0', ['1.0.0', '1.0.1']), ['1.1.0', '1.1', '1', 'latest'])

    def test_patch_to_an_older_line_moves_only_its_minor(self):
        self.assertEqual(release_images.tags('1.0.2', ['1.0.1', '1.1.0']), ['1.0.2', '1.0'])

    def test_patch_to_an_older_major_leaves_latest(self):
        self.assertEqual(release_images.tags('1.2.0', ['1.1.0', '2.0.0']), ['1.2.0', '1.2', '1'])

    def test_republishing_the_newest_release_keeps_its_tags(self):
        self.assertEqual(release_images.tags('1.0.0', ['1.0.0']), ['1.0.0', '1.0', '1', 'latest'])

    def test_prerelease_pushes_only_its_own_tag(self):
        self.assertEqual(release_images.tags('1.1.0-rc.1', ['1.0.0']), ['1.1.0-rc.1'])

    def test_prereleases_do_not_hold_back_a_release(self):
        self.assertEqual(release_images.tags('1.1.0', ['1.0.0', '1.2.0-rc.1']), ['1.1.0', '1.1', '1', 'latest'])

    def test_not_a_version_is_refused(self):
        for version in ('v1.0.0', '1.0', 'main', '1.0.0+build'):
            with self.subTest(version=version), self.assertRaises(ValueError):
                release_images.tags(version, [])


class ReleasedVersionTests(unittest.TestCase):
    def test_only_published_stable_v_releases_count(self):
        page = [
            {'tag_name': 'v1.0.0', 'draft': False, 'prerelease': False},
            {'tag_name': 'v2.0.0', 'draft': False, 'prerelease': True},
            {'tag_name': 'v1.1.0', 'draft': True, 'prerelease': False},
            {'tag_name': 'plugin-nuget/v3.0.0', 'draft': False, 'prerelease': False},
        ]
        env = {'GITHUB_TOKEN': 't', 'GITHUB_REPOSITORY': 'o/r', 'GITHUB_API_URL': 'https://api.example'}
        with patch.dict(os.environ, env), \
                patch('urllib.request.urlopen', return_value=io.BytesIO(json.dumps(page).encode())) as urlopen:
            self.assertEqual(release_images.released_versions(), ['1.0.0'])
        self.assertEqual(urlopen.call_args[0][0].full_url,
                         'https://api.example/repos/o/r/releases?per_page=100&page=1')


class PinTests(unittest.TestCase):
    def setUp(self):
        self.root = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, self.root)
        for path in release_images.PINS:
            (self.root / path).parent.mkdir(parents=True, exist_ok=True)
            shutil.copy(release_images.ROOT / path, self.root / path)

    def tag(self):
        return release_images.pins(self.root)[0][1]

    def test_repository_pins_one_tag(self):
        self.assertEqual(release_images.problems(self.root), [])

    def test_every_file_names_the_tag(self):
        named = {path for path, *_ in release_images.pins(self.root)}
        self.assertEqual(named, set(release_images.PINS))

    def test_one_stale_pin_is_reported(self):
        toml = self.root / 'deploy/fly/server.fly.toml'
        toml.write_text(toml.read_text(encoding='utf-8').replace(f':{self.tag()}"', ':9.9.9"'), encoding='utf-8')
        found = release_images.problems(self.root)
        self.assertIn('deploy/fly/server.fly.toml: pins 9.9.9', found)
        self.assertTrue(found[0].startswith('the deploy files pin different tags'))

    def test_an_image_written_another_way_is_still_checked(self):
        compose = self.root / 'deploy/oneclick/compose.yml'
        compose.write_text(compose.read_text(encoding='utf-8') + '  extra:\n    image: "sharpmush/sharpmush-server:old"\n',
                           encoding='utf-8')
        self.assertIn(f'deploy/oneclick/compose.yml: pins {self.tag()}, old', release_images.problems(self.root))

    def test_a_file_that_names_no_tag_is_reported(self):
        (self.root / 'deploy/fly/Dockerfile').write_text('FROM scratch\n', encoding='utf-8')
        self.assertIn('deploy/fly/Dockerfile: names no image tag', release_images.problems(self.root))

    def test_bump_moves_every_pin(self):
        before = len(release_images.pins(self.root))
        release_images.bump('2.3.4', self.root)
        after = release_images.pins(self.root)
        self.assertEqual(len(after), before)
        self.assertEqual({tag for _, tag, *_ in after}, {'2.3.4'})
        self.assertEqual(release_images.problems(self.root), [])

    def test_bump_refuses_a_prerelease(self):
        with self.assertRaises(ValueError):
            release_images.bump('2.0.0-rc.1', self.root)


if __name__ == '__main__':
    unittest.main()
