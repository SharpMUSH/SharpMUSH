using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using SharpMUSH.Tools.RhostExport;

namespace SharpMUSH.Tests.Services;

public class RhostOfflineExportTests
{
	[Test]
	[Arguments("cat >/dev/null; printf '+V7\\n+S0\\n+N256\\n***END OF DUMP***\\n'; exit 12", "exit")]
	[Arguments("cat >/dev/null; printf invalid", "malformed")]
	[Arguments("cat >/dev/null; yes flood", "stdout")]
	[Arguments("cat >/dev/null; yes flood >&2", "stderr")]
	[Arguments("sleep 30", "timeout")]
	[Arguments("cat >/dev/null; printf '+V7\\n+S0\\n+N256\\n***END OF DUMP***\\n'; echo unknown >&2", "diagnostic")]
	public async Task FailureDoesNotPublishOrAlterSource(string body, string reason)
	{
		using var fixture = new Fixture(body);
		var before = File.ReadAllBytes(fixture.Checkpoint);
		Exception? failure = null;
		try { await new RhostOfflineExporter().ExportAsync(fixture.Options); }
		catch (Exception ex) { failure = ex; }
		if (reason == "timeout") await Assert.That(failure is TimeoutException).IsTrue();
		else await Assert.That(failure is InvalidDataException or IOException).IsTrue();
		await Assert.That(Directory.Exists(fixture.Options.OutputDirectory)).IsFalse();
		await Assert.That(File.ReadAllBytes(fixture.Checkpoint).SequenceEqual(before)).IsTrue();
		await Assert.That(Directory.GetDirectories(fixture.Root).Length).IsEqualTo(1);
	}

	[Test]
	public async Task ExportsFromPrivateCopyAndPublishesManifest()
	{
		using var fixture = new Fixture("cat >/dev/null; test \"$2\" = x || exit 21; test -f \"$1.db\" || exit 22; printf '+V7\\n+S0\\n+N256\\n***END OF DUMP***\\n'");
		var result = await new RhostOfflineExporter().ExportAsync(fixture.Options with { SourceDirectory = fixture.Source + Path.DirectorySeparatorChar });
		await Assert.That(result.ObjectCount).IsEqualTo(0);
		await Assert.That(File.Exists(Path.Combine(result.OutputDirectory, "manifest.json"))).IsTrue();
		await Assert.That(result.SourceInventory.Count).IsEqualTo(4);
	}

	[Test]
	[Arguments("missing")]
	[Arguments("mixed")]
	[Arguments("profile")]
	[Arguments("hash")]
	[Arguments("abi")]
	[Arguments("backend")]
	[Arguments("checkpoint")]
	[Arguments("source-limit")]
	[Arguments("collision")]
	[Arguments("symlink")]
	[Arguments("overlap")]
	[Arguments("mail")]
	public async Task RefusesInvalidSourceBeforeLaunch(string defect)
	{
		using var fixture = new Fixture("touch \"$0.launched\"; exit 0");
		var options = fixture.Options;
		switch (defect)
		{
			case "missing": File.Delete(Path.Combine(fixture.Source, "native.pag")); break;
			case "mixed": File.WriteAllText(Path.Combine(fixture.Source, "native.mdbx"), "mixed"); break;
			case "profile": File.WriteAllText(options.ExporterProfilePath, "{}"); break;
			case "hash":
			case "abi":
			case "backend":
				var profile = JsonNode.Parse(File.ReadAllText(options.ExporterProfilePath))!;
				if (defect == "hash") profile["exporterSha256"] = new string('0', 64);
				if (defect == "abi") profile["pointerSize"] = 4;
				if (defect == "backend") profile["backend"] = "mdbx";
				File.WriteAllText(options.ExporterProfilePath, profile.ToJsonString());
				break;
			case "checkpoint": File.WriteAllText(fixture.Checkpoint, "+V7\n"); break;
			case "source-limit": options = options with { MaxSourceBytes = 1 }; break;
			case "collision": Directory.CreateDirectory(options.OutputDirectory); break;
			case "symlink": File.Delete(fixture.Checkpoint); File.CreateSymbolicLink(fixture.Checkpoint, options.ExporterPath); break;
			case "overlap": options = options with { OutputDirectory = Path.Combine(fixture.Source, "output") }; break;
			case "mail": File.WriteAllText(Path.Combine(fixture.Source, "native.mail.dir"), "mail"); break;
		}
		Exception? failure = null;
		try { await new RhostOfflineExporter().ExportAsync(options); }
		catch (Exception ex) { failure = ex; }
		await Assert.That(failure is InvalidDataException or IOException).IsTrue();
		await Assert.That(File.Exists(options.ExporterPath + ".launched")).IsFalse();
	}

	[Test]
	public async Task CancellationStopsExporter()
	{
		using var fixture = new Fixture("sleep 30");
		using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
		await Assert.ThrowsAsync<OperationCanceledException>(() => new RhostOfflineExporter().ExportAsync(fixture.Options, cancellationToken: cancellation.Token));
		await Assert.That(Directory.Exists(fixture.Options.OutputDirectory)).IsFalse();
	}

	private sealed class Fixture : IDisposable
	{
		public string Root { get; } = Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? Path.Combine(Environment.CurrentDirectory, "work"), "rhost-test-" + Guid.NewGuid().ToString("N"));
		public string Source => Path.Combine(Root, "source");
		public string Checkpoint => Path.Combine(Source, "checkpoint");
		public RhostExportOptions Options { get; }
		public Fixture(string body)
		{
			if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Rhost export requires Linux.");
			Directory.CreateDirectory(Source);
			File.WriteAllText(Checkpoint, "+V1031\n+S0\n+N256\n***END OF DUMP***\n");
			foreach (var suffix in new[] { ".db", ".dir", ".pag" }) File.WriteAllText(Path.Combine(Source, "native" + suffix), "native");
			var executable = Path.Combine(Root, "exporter");
			File.WriteAllText(executable, "#!/bin/sh\n" + body + "\n");
			File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
			var profile = Path.Combine(Root, "profile.json");
			File.WriteAllText(profile, JsonSerializer.Serialize(new { schema = 1, upstreamRevision = "bfd2894c7239d02b8bec15c36939706be371715c", backend = "legacy-qdbm", rid = "linux-x64", littleEndian = true, pointerSize = 8, intSize = 4, offTSize = 8, exporterSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(executable))) }));
			Options = new() { SourceDirectory = Source, DatabaseBase = "native", Checkpoint = "checkpoint", ExporterPath = executable, ExporterProfilePath = profile, OutputDirectory = Path.Combine(Root, "output"), Backend = RhostBackend.LegacyQdbm, Timeout = TimeSpan.FromMilliseconds(500), MaxOutputBytes = 4096, MaxDiagnosticBytes = 4096 };
		}
		public void Dispose() => Directory.Delete(Root, true);
	}
}
