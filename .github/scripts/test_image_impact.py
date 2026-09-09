import unittest
from pathlib import Path
from image_impact import analyze, report


class ImageImpactTests(unittest.TestCase):
    def setUp(self):
        self.files = {
            "Dockerfile": "COPY . .\nRUN dotnet publish Game/Game.csproj -c Release",
            "SharpMUSH.ConnectionServer/Dockerfile": "RUN dotnet publish Conn/Conn.csproj",
            "SharpMUSH.SocketServer/Dockerfile": "RUN dotnet publish Socket/Socket.csproj",
            "Socket/Socket.csproj": '<Project><ProjectReference Include="../Shared/Shared.csproj"/></Project>',
            "Game/Game.csproj": '<Project><ProjectReference Include="../Shared/Shared.csproj"/><EmbeddedResource Include="../resources/*.txt"/></Project>',
            "Conn/Conn.csproj": '<Project><ProjectReference Include="../Shared/Shared.csproj"/></Project>',
            "Shared/Shared.csproj": '<Project/>',
        }

    def impact(self, *paths):
        return analyze(paths, [self.files.__getitem__])

    def test_game_only_does_not_restart_connectionserver(self):
        self.assertEqual(self.impact("Game/Program.cs"), {"server": ["Game/Program.cs"], "connectionserver": [], "socketserver": []})

    def test_shared_dependency_restarts_both(self):
        self.assertTrue(all(self.impact("Shared/Message.cs").values()))

    def test_global_build_inputs_restart_both(self):
        for path in ["Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "NuGet.Config", "global.json", ".dockerignore", "SharpMUSH.sln", ".github/scripts/image_impact.py", ".github/workflows/_docker-dev-publish.yml"]:
            with self.subTest(path=path):
                self.assertTrue(all(self.impact(path).values()))

    def test_linked_resource(self):
        self.assertEqual(self.impact("resources/banner.txt"), {"server": ["resources/banner.txt"], "connectionserver": [], "socketserver": []})

    def test_explicit_docker_copy_inputs(self):
        self.files["Dockerfile"] += '\nCOPY ["assets", "/app/assets"]\nCOPY --from=build /app /output'
        self.assertEqual(self.impact("assets/banner.txt"), {"server": ["assets/banner.txt"], "connectionserver": [], "socketserver": []})

    def test_dockerfile_only_affects_its_image(self):
        self.assertEqual(self.impact("Dockerfile"), {"server": ["Dockerfile"], "connectionserver": [], "socketserver": []})

    def test_unrelated_files_do_not_publish(self):
        self.assertFalse(any(self.impact("README.md", "Tests/Test.cs").values()))

    def test_removed_dependency_uses_base_graph(self):
        after = dict(self.files, **{"Conn/Conn.csproj": "<Project/>"})
        result = analyze(["Shared/Deleted.cs"], [self.files.__getitem__, after.__getitem__])
        self.assertTrue(all(result.values()))

    def test_deleted_image_never_publishes_even_for_global_changes(self):
        after = {k: v for k, v in self.files.items() if not k.startswith("SharpMUSH.SocketServer/")}
        removed = []
        result = analyze(["SharpMUSH.SocketServer/Dockerfile", "Directory.Build.props"],
                         [self.files.__getitem__, after.__getitem__], removed)
        self.assertEqual(result["socketserver"], [])
        self.assertEqual(removed, ["socketserver"])
        self.assertIn("socketserver: removed (no publish)", report(result, removed))
        self.assertTrue(result["server"])

    def test_new_dependency_discovered_automatically(self):
        self.files["Shared/Shared.csproj"] = '<Project><ProjectReference Include="..\\New\\New.csproj"/></Project>'
        self.files["New/New.csproj"] = '<Project/>'
        self.assertTrue(all(self.impact("New/Added.cs").values()))

    def test_unresolved_reference_fails_instead_of_underreporting(self):
        self.files["Shared/Shared.csproj"] = '<Project><ProjectReference Include="$(Dependency)"/></Project>'
        with self.assertRaises(ValueError):
            self.impact("New/File.cs")

    def test_unrecognized_docker_publish_fails(self):
        self.files["Dockerfile"] = "RUN ./publish.sh"
        with self.assertRaises(ValueError):
            self.impact("Game/Program.cs")

    def test_report_explains_drops_and_escapes_paths(self):
        summary = report(self.impact("Socket/<unsafe>.cs"))
        self.assertIn("drops: YES", summary)
        self.assertIn("&lt;unsafe&gt;", summary)

    def test_renderer_only_keeps_client_sockets(self):
        result = self.impact("Conn/Renderer.cs")
        self.assertTrue(result["connectionserver"])
        self.assertFalse(result["socketserver"])
        self.assertIn("drops: NO", report(result))

    def test_new_image_missing_from_base_is_supported(self):
        before = {k: v for k, v in self.files.items() if not k.startswith("SharpMUSH.SocketServer/")}
        result = analyze(["Socket/Program.cs"], [before.__getitem__, self.files.__getitem__])
        self.assertEqual(result["socketserver"], ["Socket/Program.cs"])

    def test_real_renderer_sources_do_not_publish_socket_owner(self):
        root = Path(__file__).resolve().parents[2]
        result = analyze(["SharpMUSH.ConnectionServer/Services/MarkupOutputRenderer.cs"],
                         [lambda path: (root / path).read_text()])
        self.assertTrue(result["connectionserver"])
        self.assertFalse(result["socketserver"])

    def test_repository_closure_and_embedded_packages(self):
        root = Path(__file__).resolve().parents[2]
        read = lambda path: (root / path).read_text()
        result = analyze(["examples/packages/plus-help/package.yaml", "SharpMUSH.Messaging/Messages.cs"], [read])
        self.assertEqual(len(result["server"]), 2)
        self.assertEqual(result["connectionserver"], ["SharpMUSH.Messaging/Messages.cs"])


if __name__ == "__main__":
    unittest.main()
