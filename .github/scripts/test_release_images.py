import shutil
import tempfile
import unittest
from pathlib import Path

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


class PinTests(unittest.TestCase):
    def setUp(self):
        self.root = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, self.root)
        for path in release_images.PINS:
            (self.root / path).parent.mkdir(parents=True, exist_ok=True)
            shutil.copy(release_images.ROOT / path, self.root / path)
        shutil.copy(release_images.ROOT / 'Directory.Build.props', self.root / 'Directory.Build.props')

    def test_repository_pins_its_own_version(self):
        version = release_images.project_version(self.root)
        self.assertEqual(release_images.mismatches(version, self.root), [])

    def test_another_version_names_every_file(self):
        problems = release_images.mismatches('9.9.9', self.root)
        self.assertEqual({p.split(':')[0] for p in problems}, set(release_images.PINS))

    def test_one_stale_pin_is_reported(self):
        version = release_images.project_version(self.root)
        toml = self.root / 'deploy/fly/server.fly.toml'
        toml.write_text(toml.read_text(encoding='utf-8').replace(f':{version}"', ':dev"'), encoding='utf-8')
        self.assertEqual(release_images.mismatches(version, self.root),
                         [f'deploy/fly/server.fly.toml: pins dev, not {version}'])

    def test_a_file_that_names_no_tag_is_reported(self):
        (self.root / 'deploy/fly/Dockerfile').write_text('FROM scratch\n', encoding='utf-8')
        self.assertIn('deploy/fly/Dockerfile: names no image tag',
                      release_images.mismatches(release_images.project_version(self.root), self.root))


if __name__ == '__main__':
    unittest.main()
