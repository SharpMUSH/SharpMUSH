using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Packages;

public class SoftcodeEditorPackageTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	private IConnectionService Connections => Factory.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => Factory.Services.GetRequiredService<IMUSHCodeParser>();
	private ISharpDatabase Database => Factory.Services.GetRequiredService<ISharpDatabase>();
	private IInputSessionService Sessions => Factory.Services.GetRequiredService<IInputSessionService>();

	private static async Task<PackageManifest> Manifest()
	{
		for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
		{
			var candidate = Path.Join(directory.FullName, "examples", "packages", "softcode-editor", "package.yaml");
			if (File.Exists(candidate))
				return new PackageManifestService().ParseManifest(await File.ReadAllTextAsync(candidate))
					.Expect<ParsedPackageManifest>().Manifest;
		}
		throw new FileNotFoundException("Could not find the softcode-editor package manifest.");
	}

	[Test]
	public async Task PackageShipsInTheOfflineCatalogueWithoutInstallingItAutomatically()
	{
		var descriptor = BundledPackages.All.First(item => item.PackageId == "softcode-editor");

		await Assert.That(descriptor.InstallAtFirstBoot).IsFalse();
		await Assert.That(descriptor.Requires).IsEqualTo(BundledPackageHandler.None);
	}

	[Test, NotInParallel]
	public async Task InstalledPackageEditsAndSavesThroughAnInputSession()
	{
		var manifest = await Manifest();
		var installer = Factory.Services.GetRequiredService<IPackageInstallService>();
		var installed = (await installer.ApplyAsync(manifest, new PackageApplyRequest(
			new PackageApplySource("https://github.com/SharpMUSH/SharpMUSH-Packages", "softcode-editor/", "test", "main"),
			new Dictionary<string, string>(), []))).Expect<PackageApplyResult>("the editor package installs");
		var editor = DBRef.Parse(installed.CreatedObjects["editor"]);
		try
		{
			await CommandSavesWithItsSyntaxFlag(editor);
			await FunctionSavesWithItsSyntaxFlag(editor);
			await BlankAndDottedLinesAreCodeAndQuitKeepsUnsavedWork(editor);
			await UndoSwapsWithTheDraftBeforeTheLastLine(editor);
			await FailedSaveAndQuitKeepsTheEditorOpen(editor);
			await ConcurrentChangeRefusesTheSave(editor);
			await OpeningDropsBranchesOfConnectionsThatAreGone(editor);
		}
		finally
		{
			await installer.UninstallAsync(manifest.Name);
		}
	}

	private async Task CommandSavesWithItsSyntaxFlag(DBRef editor)
	{
		const string source = "think [add(1,2)]; @pemit me=a, b";
		await Edit("SoftcodeCommandEditor", async player =>
		{
			await Open(player, "@editor me/EDITME=command");
			await Assert.That(Sessions.GetCapturing(player.Handle)?.Routes.Last().Attribute).IsEqualTo("INPUT`LINE");
			await Send(player, source);
			await Assert.That(await Draft(editor, player)).IsEqualTo(source);

			await Send(player, ".sq");

			await Assert.That(Sessions.GetCapturing(player.Handle)).IsNull().Because(Heard(player));
			var saved = await Attribute(player.DbRef, "EDITME");
			await Assert.That(saved?.Value.ToPlainText()).IsEqualTo(source);
			await Assert.That(Flags(saved)).Contains("cmdsyntax").And.DoesNotContain("funsyntax");
			await Assert.That(await Database.GetAttributesAsync(editor, $"DATA`{player.Handle}**", CancellationToken.None)
				.CountAsync()).IsEqualTo(0).Because("closing the editor removes the connection's branch");
		});
	}

	private async Task FunctionSavesWithItsSyntaxFlag(DBRef editor)
		=> await Edit("SoftcodeFunctionEditor", async player =>
		{
			await Open(player, "@editor me/EDITFUNCTION=function");
			await Send(player, "add(1,2)");
			var heard = Factory.Notifications.CountForHandle(player.Handle);
			await Send(player, ".sq");

			// The save sets the syntax flags quietly: the player hears that it saved, not each flag being set or reset.
			var told = Factory.Notifications.ForHandle(player.Handle).Skip(heard).ToList();
			await Assert.That(told).Contains(line => line.Contains("EDITFUNCTION saved.")).Because(string.Join(" | ", told));
			await Assert.That(told).DoesNotContain(line => line.Contains("syntax")).Because(string.Join(" | ", told));
			var saved = await Attribute(player.DbRef, "EDITFUNCTION");
			await Assert.That(saved?.Value.ToPlainText()).IsEqualTo("add(1,2)");
			await Assert.That(Flags(saved)).Contains("funsyntax").And.DoesNotContain("cmdsyntax");
		});

	private async Task BlankAndDottedLinesAreCodeAndQuitKeepsUnsavedWork(DBRef editor)
		=> await Edit("SoftcodeBlankLineEditor", async player =>
		{
			await Open(player, "@editor me/EDITBLANK=command");
			await Send(player, "");
			await Send(player, "..s");
			await Assert.That(await Draft(editor, player)).IsEqualTo("\n.s");

			var heard = Factory.Notifications.CountForHandle(player.Handle);
			await Send(player, ".l");
			await Assert.That(Factory.Notifications.ForHandle(player.Handle).Skip(heard)).Contains(line => line.Contains("  2: .s")).Because(Heard(player));

			await Send(player, ".q");
			await Assert.That(Sessions.GetCapturing(player.Handle)).IsNotNull()
				.Because(".q refuses to leave while the draft is unsaved");
			await Assert.That(await Draft(editor, player)).IsEqualTo("\n.s");

			await Send(player, ".q!");
			await Assert.That(Sessions.GetCapturing(player.Handle)).IsNull().Because(Heard(player));
			await Assert.That(await Attribute(player.DbRef, "EDITBLANK")).IsNull();
		});

	private async Task UndoSwapsWithTheDraftBeforeTheLastLine(DBRef editor)
		=> await Edit("SoftcodeUndoEditor", async player =>
		{
			await Open(player, "@editor me/EDITUNDO=function");
			await Send(player, "first");
			await Send(player, "second");

			await Send(player, ".u");
			await Assert.That(await Draft(editor, player)).IsEqualTo("first");
			await Send(player, ".u");
			await Assert.That(await Draft(editor, player)).IsEqualTo("first\nsecond");

			await Send(player, ".q!");
		});

	private async Task FailedSaveAndQuitKeepsTheEditorOpen(DBRef editor)
		=> await Edit("SoftcodeFailedSaveEditor", async player =>
		{
			await Open(player, "@editor me/EDITFAIL=function");
			await Send(player, "add(1,2");
			await Send(player, ".sq");

			await Assert.That(Sessions.GetCapturing(player.Handle)).IsNotNull()
				.Because("a failed save-and-quit opens the editor again with the draft");
			await Assert.That(await Draft(editor, player)).IsEqualTo("add(1,2");
			await Assert.That(await Attribute(player.DbRef, "EDITFAIL")).IsNull();
			await Send(player, ".q!");
		});

	private async Task ConcurrentChangeRefusesTheSave(DBRef editor)
		=> await Edit("SoftcodeConflictEditor", async player =>
		{
			await SetAttribute(player.DbRef, "EDITCONFLICT", "add(1,1)");
			await Open(player, "@editor me/EDITCONFLICT=function");
			await SetAttribute(player.DbRef, "EDITCONFLICT", "add(2,2)");
			await Send(player, "add(3,3)");

			await Send(player, ".s");

			await Assert.That((await Attribute(player.DbRef, "EDITCONFLICT"))?.Value.ToPlainText()).IsEqualTo("add(2,2)");
			await Assert.That(Sessions.GetCapturing(player.Handle)).IsNotNull();
			await Send(player, ".q!");
		});

	private async Task OpeningDropsBranchesOfConnectionsThatAreGone(DBRef editor)
		=> await Edit("SoftcodeSweepEditor", async player =>
		{
			var stale = TestIsolationHelpers.GenerateUniqueHandle();
			await SetAttribute(editor, $"DATA`{stale}`DRAFT", "left behind");

			await Open(player, "@editor me/EDITSWEEP=function");

			await Assert.That(await Database.GetAttributesAsync(editor, $"DATA`{stale}**", CancellationToken.None)
				.CountAsync()).IsEqualTo(0);
			await Send(player, ".q!");
		});

	private async Task Edit(string name, Func<TestIsolationHelpers.TestPlayer, Task> scenario)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			Factory.Services, Factory.Services.GetRequiredService<IMediator>(), Connections, name);
		try
		{
			await scenario(player);
		}
		finally
		{
			await Connections.Disconnect(player.Handle);
		}
	}

	private async Task Open(TestIsolationHelpers.TestPlayer player, string command)
	{
		await Parser.CommandParse(player.Handle, Connections, MarkupText.Plain(command));
		await Assert.That(Sessions.GetCapturing(player.Handle)).IsNotNull()
			.Because(Heard(player));
	}

	private async Task Send(TestIsolationHelpers.TestPlayer player, string line)
	{
		if (Sessions.GetCapturing(player.Handle) is not { } session)
			throw new InvalidOperationException($"No editor is open on {player.Handle} to receive '{line}'.");
		var result = await Sessions.DeliverAsync(Parser, session, MarkupText.Plain(line));
		await Assert.That(result?.HadErrors).IsFalse().Because(result?.Message.ToPlainText() ?? "no callback result");
	}

	private string Heard(TestIsolationHelpers.TestPlayer player)
		=> string.Join(" | ", Factory.Notifications.ForHandle(player.Handle));

	private async Task<string?> Draft(DBRef editor, TestIsolationHelpers.TestPlayer player)
		=> (await Attribute(editor, "DATA", player.Handle.ToString(), "DRAFT"))?.Value.ToPlainText();

	private async Task<SharpAttribute?> Attribute(DBRef owner, params string[] path)
		=> await Database.GetAttributeAsync(owner, path, CancellationToken.None).LastOrDefaultAsync();

	private async Task SetAttribute(DBRef owner, string name, string value)
	{
		var mediator = Factory.Services.GetRequiredService<IMediator>();
		var god = (await mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>();
		var target = (await mediator.Send(new GetObjectNodeQuery(owner))).Expect<AnySharpObject>();
		(await Factory.Services.GetRequiredService<IAttributeService>()
			.SetAttributeAsync(god, target, name, MarkupText.Plain(value))).Expect<Success>();
	}

	private static IEnumerable<string> Flags(SharpAttribute? attribute)
		=> attribute?.Flags.Select(flag => flag.Name) ?? [];
}
