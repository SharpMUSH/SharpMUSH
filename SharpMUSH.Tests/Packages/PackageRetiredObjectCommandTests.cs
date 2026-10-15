using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Packages;

/// <summary>
/// An object a package lets go of (uninstalled, dropped by an upgrade, or released by a rollback) waits
/// GOING for the purge, and is HALT from the moment it is let go: until it was, a master-room
/// <c>$</c>-command on it kept answering, so reinstalling the package answered every command twice.
/// </summary>
public class PackageRetiredObjectCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IServiceProvider Services => WebAppFactoryArg.Services;
	private IConnectionService ConnectionService => Services.GetRequiredService<IConnectionService>();
	private IPackageInstallService Installer => Services.GetRequiredService<IPackageInstallService>();
	private ISharpDatabase Database => Services.GetRequiredService<ISharpDatabase>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	private static PackageApplyRequest Request(string id, string commit) => new(
		new PackageApplySource("https://github.com/SharpMUSH/SharpMUSH-Packages", $"{id}/", commit, "main"),
		new Dictionary<string, string>(), []);

	/// <summary>A master-room object per ref, each answering <c>+zrt&lt;ref&gt;&lt;tag&gt;</c>.</summary>
	private static PackageManifest Manifest(string id, string version, string tag, params string[] refs) =>
		new PackageManifestService().ParseManifest($"""
			package: {id}
			version: "{version}"
			objects:
			{string.Concat(refs.Select(r => $"  - ref: {r}\n    type: thing\n    name: Retire Probe {r} {tag}\n    location: \"{{{{$master_room}}}}\"\n    attributes:\n      CMD`RUN: |-\n        $+zrt{r}{tag}:@pemit %#=ran-{r}-{tag}\n"))}
			""") switch
		{
			ParsedPackageManifest parsed => parsed.Manifest,
			PackageManifestFailure failure => throw new InvalidOperationException(string.Join("; ", failure.Issues))
		};

	private static string NewTag()
		=> new string([.. TestIsolationHelpers.GenerateUniqueName("T").Where(char.IsAsciiLetterOrDigit)]).ToLowerInvariant();

	private async Task<IReadOnlyDictionary<string, string>> ApplyAsync(PackageManifest manifest, string commit)
		=> (await Installer.ApplyAsync(manifest, Request(manifest.Name, commit)))
			.Expect<PackageApplyResult>("the package applies").CreatedObjects;

	/// <summary>How many times God hears <c>+zrt&lt;ref&gt;&lt;tag&gt;</c> answered.</summary>
	private async Task<int> AnswersAsync(string reference, string tag)
	{
		var reply = $"ran-{reference}-{tag}";
		var god = (await Database.GetObjectNodeAsync(new DBRef(1))).Expect<AnySharpObject>().Object().DBRef;
		int Heard() => WebAppFactoryArg.Notifications.For(god).Count(line => line.Contains(reply));
		var before = Heard();
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"+zrt{reference}{tag}"));
		await WebAppFactoryArg.QueueBarrierAsync();
		return Heard() - before;
	}

	private async Task<bool> HasFlagAsync(string objid, string flag)
		=> await (await Database.GetObjectNodeAsync(DBRef.Parse(objid))).Expect<AnySharpObject>()
			.Object().Flags.Value.AnyAsync(f => f.Name == flag);

	[Test]
	public async Task Uninstall_StopsItsObjectsAnsweringAtOnce_SoAReinstallAnswersOnce()
	{
		var tag = NewTag();
		var id = $"retire-{tag}";
		var manifest = Manifest(id, "1.0", tag, "cmd");

		var first = await ApplyAsync(manifest, "commit-1");
		await Assert.That(await AnswersAsync("cmd", tag)).IsEqualTo(1);

		await Assert.That((await Installer.UninstallAsync(id)).Value).IsTypeOf<Success>();
		await Assert.That(await AnswersAsync("cmd", tag)).IsEqualTo(0);
		await Assert.That(await HasFlagAsync(first["cmd"], "GOING")).IsTrue();
		await Assert.That(await HasFlagAsync(first["cmd"], "HALT")).IsTrue();

		await ApplyAsync(manifest, "commit-2");
		await Assert.That(await AnswersAsync("cmd", tag)).IsEqualTo(1);

		await Assert.That((await Installer.UninstallAsync(id)).Value).IsTypeOf<Success>();
	}

	[Test]
	public async Task UpgradeAndRollback_HaltWhatTheyReleaseAndWakeWhatTheyRevive()
	{
		var tag = NewTag();
		var id = $"retire-up-{tag}";

		var first = await ApplyAsync(Manifest(id, "1.0", tag, "keep", "drop"), "commit-1");
		await ApplyAsync(Manifest(id, "2.0", tag, "keep"), "commit-2");
		await Assert.That(await AnswersAsync("drop", tag)).IsEqualTo(0);
		await Assert.That(await HasFlagAsync(first["drop"], "HALT")).IsTrue();
		await Assert.That(await AnswersAsync("keep", tag)).IsEqualTo(1);

		await Assert.That((await Installer.RollbackAsync(id, 1)).Value).IsTypeOf<PackageRollbackResult>();
		await Assert.That(await HasFlagAsync(first["drop"], "GOING")).IsFalse();
		await Assert.That(await HasFlagAsync(first["drop"], "HALT")).IsFalse();
		await Assert.That(await AnswersAsync("drop", tag)).IsEqualTo(1);

		await Assert.That((await Installer.UninstallAsync(id)).Value).IsTypeOf<Success>();
	}
}
