import unittest
from package_version_bumps import changed_packages, check, version_key


def manifest(version):
    return f'format: "1.20"\npackage: demo\nversion: {version}\n'


class PackageVersionBumpTests(unittest.TestCase):
    def run_check(self, paths, base, head):
        return check(paths, base.get, head.get)

    def test_change_with_a_bump_passes(self):
        path = "examples/packages/demo/package.yaml"
        self.assertEqual(self.run_check(["examples/packages/demo/core.mush"], {path: manifest("1.0.0")},
                                        {path: manifest("1.0.1")}), [])

    def test_change_without_a_bump_fails(self):
        path = "examples/packages/demo/package.yaml"
        problems = self.run_check(["examples/packages/demo/README.md"], {path: manifest("1.0.0")},
                                  {path: manifest("1.0.0")})
        self.assertEqual(len(problems), 1)
        self.assertIn("demo", problems[0])

    def test_lower_version_fails(self):
        path = "examples/packages/demo/package.yaml"
        self.assertTrue(self.run_check([path], {path: manifest("1.2.0")}, {path: manifest("1.1.9")}))

    def test_new_and_removed_packages_need_no_bump(self):
        path = "examples/packages/demo/package.yaml"
        self.assertEqual(self.run_check([path], {}, {path: manifest("1.0.0")}), [])
        self.assertEqual(self.run_check([path], {path: manifest("1.0.0")}, {}), [])

    def test_index_alone_belongs_to_no_package(self):
        self.assertEqual(changed_packages(["examples/packages/index.yaml", "docs/x.md"]), [])

    def test_quoted_version_is_read(self):
        path = "examples/packages/demo/package.yaml"
        self.assertEqual(self.run_check([path], {path: 'version: "1.0.0"\n'}, {path: 'version: "1.0.1"\n'}), [])

    def test_prerelease_sorts_before_its_release(self):
        self.assertLess(version_key("1.0.0-beta.2"), version_key("1.0.0"))
        self.assertLess(version_key("1.0.0-beta.2"), version_key("1.0.0-beta.10"))
        self.assertLess(version_key("0.9.9"), version_key("1.0.0-alpha"))

    def test_version_already_taken_on_the_base_branch_fails(self):
        path = "examples/packages/demo/package.yaml"
        problems = check([path], {path: manifest("1.0.0")}.get, {path: manifest("1.0.1")}.get,
                         {path: manifest("1.0.1")}.get)
        self.assertEqual(len(problems), 1)

    def test_invalid_version_fails(self):
        path = "examples/packages/demo/package.yaml"
        self.assertTrue(self.run_check([path], {path: manifest("1.0.0")}, {path: manifest("next")}))


if __name__ == "__main__":
    unittest.main()
