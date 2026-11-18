import contextlib
import io
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import release_preflight


class ReleasePreflightTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.props = self.root / 'Directory.Build.props'
        self.changelog = self.root / 'CHANGELOG.md'
        self.write('1.0.2')

    def write(self, version):
        self.props.write_text(f'<Project><PropertyGroup><SharpMUSHVersion>{version}</SharpMUSHVersion>'
                              '</PropertyGroup></Project>', encoding='utf-8')
        self.changelog.write_text(f'# Changelog\n\n## [Unreleased]\n\n## [{version}] - 2026-11-16\n'
                                  '\n### Fixed\n\n- A release fix.\n', encoding='utf-8')

    def test_stable_and_prerelease_candidates(self):
        for version in ('1.0.2', '1.1.0-rc.1'):
            with self.subTest(version=version):
                self.write(version)
                self.assertEqual(release_preflight.check(self.root), version)
                self.assertEqual(release_preflight.check(self.root, version), version)

    def test_missing_release_section_despite_unreleased_notes(self):
        self.changelog.write_text('## [Unreleased]\n\n- Pending fix.\n\n## [1.0.1]\n- Old fix.\n')
        with self.assertRaisesRegex(ValueError, 'exactly one'):
            release_preflight.check(self.root)

    def test_intended_version_cannot_silently_use_old_engine_version(self):
        self.write('1.0.1')
        with self.assertRaisesRegex(ValueError, 'intended release is 1.0.2'):
            release_preflight.check(self.root, '1.0.2')

    def test_mentions_and_longer_versions_are_not_release_headings(self):
        for heading in ('text ## [1.0.2]', '### [1.0.2]', '## [1.0.20]', '## [1.0.2-rc.1]',
                        '[1.0.2]: https://example.com', '## [1.0.2] suffix'):
            with self.subTest(heading=heading):
                self.changelog.write_text(heading + '\n- A fix.\n')
                with self.assertRaisesRegex(ValueError, 'exactly one'):
                    release_preflight.check(self.root)

    def test_duplicate_section_is_rejected(self):
        with self.changelog.open('a') as output:
            output.write('\n## [1.0.2]\n- Another fix.\n')
        with self.assertRaisesRegex(ValueError, 'exactly one'):
            release_preflight.check(self.root)

    def test_empty_section_does_not_borrow_next_releases_notes(self):
        self.changelog.write_text('## [1.0.2]\n### Fixed\n\n## [1.0.1]\n- Old fix.\n')
        with self.assertRaisesRegex(ValueError, 'no release notes'):
            release_preflight.check(self.root)

    def test_invalid_candidate_versions(self):
        for version in ('', 'v1.0.2', 'main', '1.0', '01.0.2', '1.0.2-rc..1', '1.0.2-01',
                        '1.0.2+build', '1.0.2\n'):
            with self.subTest(version=version), self.assertRaisesRegex(ValueError, 'not a version'):
                release_preflight.check(self.root, version)

    def test_missing_or_ambiguous_engine_version(self):
        for content in ('<Project/>', '<Project><SharpMUSHVersion/></Project>',
                        '<Project><SharpMUSHVersion>1.0.2</SharpMUSHVersion>'
                        '<SharpMUSHVersion>1.0.3</SharpMUSHVersion></Project>'):
            self.props.write_text(content)
            with self.assertRaisesRegex(ValueError, 'exactly one SharpMUSHVersion'):
                release_preflight.check(self.root)

    def test_cli_success_failure_and_usage(self):
        with patch.object(release_preflight, 'ROOT', self.root), \
                contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
            self.assertEqual(release_preflight.main([]), 0)
            self.assertEqual(release_preflight.main(['1.0.2']), 0)
            self.assertEqual(release_preflight.main(['1.0.3']), 1)
            self.assertEqual(release_preflight.main(['1.0.2', 'extra']), 2)
            self.props.write_text('<broken')
            self.assertEqual(release_preflight.main([]), 1)
            self.props.unlink()
            self.assertEqual(release_preflight.main([]), 1)


if __name__ == '__main__':
    unittest.main()
