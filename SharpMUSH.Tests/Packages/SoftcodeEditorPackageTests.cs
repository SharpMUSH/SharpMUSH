using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Packages;

public class SoftcodeEditorPackageTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	private static string ManifestPath()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory is not null)
		{
			var candidate = Path.Join(directory.FullName, "examples", "packages", "softcode-editor", "package.yaml");
			if (File.Exists(candidate)) return candidate;
			directory = directory.Parent;
		}
		throw new FileNotFoundException("Could not find the softcode-editor package manifest.");
	}

	[Test]
	public async Task PackageRegistersEditorAndRoutesEveryEditorKeyThroughInput()
	{
		var yaml = await File.ReadAllTextAsync(ManifestPath());
		var manifest = new PackageManifestService().ParseManifest(yaml)
			.Expect<ParsedPackageManifest>().Manifest;
		var editor = manifest.Objects.Single(item => item.Ref == "editor");
		var startup = editor.Attributes["STARTUP"].Value;
		var command = editor.Attributes["CMD`EDITOR"].Value;
		var input = editor.Attributes["INPUT`START"].Value;
		var save = editor.Attributes["INC`SAVE"].Value;

		await Assert.That(startup).Contains("@command/add").And.Contains("@hook/override/inline");
		await Assert.That(command).Contains("@include %!/INPUT`START");
		await Assert.That(input).Contains("@input/start");
		foreach (var route in new[] { ".q!", ".sq", ".s", ".q", ".c", ".l", ".u", ".h", "*" })
			await Assert.That(input + command + editor.Attributes["INPUT`HELP"].Value).Contains(route);
		await Assert.That(save.IndexOf("controls(%#,%q<obj>)", StringComparison.Ordinal))
			.IsLessThan(save.IndexOf("@force/inline", StringComparison.Ordinal));
		await Assert.That(save).Contains("=!%q<other>");
		await Assert.That(command).Contains("ORIGINALCMD").And.Contains("ORIGINALFUN");
		await Assert.That(save).Contains("get(%!/DATA`%d`ORIGINALCMD)")
			.And.Contains("get(%!/DATA`%d`ORIGINALFUN)");
		await Assert.That(editor.Attributes["INPUT`EXIT"].Value).Contains("@include/args %!/INC`SAVE=mode,exit");
		await Assert.That(save).Contains("strmatch(%<mode>,exit)=1,@include %!/INPUT`START");
	}

	[Test]
	public async Task PackageShipsInTheOfflineCatalogueWithoutInstallingItAutomatically()
	{
		var descriptor = BundledPackages.All.Single(item => item.PackageId == "softcode-editor");

		await Assert.That(descriptor.InstallAtFirstBoot).IsFalse();
		await Assert.That(descriptor.Requires).IsEqualTo(BundledPackageHandler.None);
	}

	[Test, NotInParallel]
	public async Task InstalledPackageEditsAndSavesThroughAnInputSession()
	{
		var yaml = await File.ReadAllTextAsync(ManifestPath());
		var manifest = new PackageManifestService().ParseManifest(yaml)
			.Expect<ParsedPackageManifest>().Manifest;
		var installer = Factory.Services.GetRequiredService<IPackageInstallService>();
		var applied = await installer.ApplyAsync(manifest, new PackageApplyRequest(
			new PackageApplySource("https://github.com/SharpMUSH/SharpMUSH-Packages", "softcode-editor/", "test", "main"),
			new Dictionary<string, string>(), []));
		var installed = applied.Expect<PackageApplyResult>("the editor package installs");

		var connections = Factory.Services.GetRequiredService<IConnectionService>();
		var parser = Factory.Services.GetRequiredService<IMUSHCodeParser>();
		var database = Factory.Services.GetRequiredService<ISharpDatabase>();
		var sessions = Factory.Services.GetRequiredService<IInputSessionService>();
		var commands = Factory.Services.GetRequiredService<ILibraryProvider<CommandDefinition>>().Get();
		var hooks = Factory.Services.GetRequiredService<IHookService>();
		await Assert.That(commands.ContainsKey("@EDITOR")).IsTrue();
		await Assert.That((await hooks.GetAllHooksAsync("@EDITOR")).Keys).Contains("OVERRIDE");
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			Factory.Services, Factory.Services.GetRequiredService<IMediator>(), connections, "SoftcodeEditor");
		try
		{
			var opened = await parser.CommandParse(player.Handle, connections, MarkupText.Plain("@editor me/EDITME=command"));
			var editor = DBRef.Parse(installed.CreatedObjects["editor"]);
			var state = await database.GetAttributesAsync(editor, $"DATA`{player.Handle}`**", CancellationToken.None)
				.Select(attribute => $"{attribute.LongName}={attribute.Value.ToPlainText()}").ToListAsync();
			await Assert.That(sessions.GetCapturing(player.Handle)).IsNotNull()
				.Because($"result={opened.Message.ToPlainText()}; state={string.Join(" | ", state)}; notifications={string.Join(" | ", Factory.Notifications.For(player.DbRef))}");
			await Assert.That(sessions.GetCapturing(player.Handle)!.Routes.Last().Attribute).IsEqualTo("INPUT`LINE");
			const string source = "think [add(1,2)]";
			var unexpected = sessions.GetCapturing(player.Handle)!.Routes.SkipLast(1)
				.Where(route => route.Matcher?.IsMatch(source) == true)
				.Select(route => $"{route.Pattern}->{route.Attribute}").ToList();
			await Assert.That(unexpected).IsEmpty();
			var callback = await database.GetAttributeAsync(editor, ["INPUT", "LINE"], CancellationToken.None).LastOrDefaultAsync();
			await Assert.That(callback?.Value.ToPlainText()).Contains("%0");
			var line = await sessions.DeliverAsync(parser, sessions.GetCapturing(player.Handle)!, MarkupText.Plain(source));
			await Assert.That(line).IsNotNull();
			await Assert.That(line?.HadErrors).IsFalse().Because(line?.Message.ToPlainText() ?? "no callback result");
			var draft = await database.GetAttributeAsync(editor, ["DATA", player.Handle.ToString(), "DRAFT"], CancellationToken.None)
				.LastOrDefaultAsync();
			var afterLine = await database.GetAttributesAsync(editor, "DATA`**", CancellationToken.None)
				.Select(attribute => $"{attribute.LongName}={attribute.Value.ToPlainText()}").ToListAsync();
			await Assert.That(draft?.Value.ToPlainText()).IsEqualTo(source)
				.Because(string.Join(" | ", afterLine));
			var diagnostics = await Factory.FunctionParser.EvaluateAsync(
				MarkupText.Plain($"[syntaxdiagnostics({editor}/DATA`{player.Handle}`DRAFT,command)]"));
			await Assert.That(diagnostics.ToPlainText()).IsEqualTo("[]");
			await Assert.That(sessions.GetCapturing(player.Handle)).IsNotNull();
			var exit = await sessions.DeliverAsync(parser, sessions.GetCapturing(player.Handle)!, MarkupText.Plain(".sq"));
			await Assert.That(exit?.HadErrors).IsFalse().Because(exit?.Message.ToPlainText() ?? "no callback result");
			await Assert.That(sessions.GetCapturing(player.Handle)).IsNull()
				.Because(string.Join(" | ", Factory.Notifications.For(player.DbRef)));

			var saved = await database.GetAttributeAsync(player.DbRef, ["EDITME"], CancellationToken.None)
				.LastOrDefaultAsync();
			var afterExit = await database.GetAttributesAsync(editor, "DATA`**", CancellationToken.None)
				.Select(attribute => $"{attribute.LongName}={attribute.Value.ToPlainText()}").ToListAsync();
			await Assert.That(saved).IsNotNull().Because(
				$"state={string.Join(" | ", afterExit)}; notifications={string.Join(" | ", Factory.Notifications.For(player.DbRef))}");
			await Assert.That(saved!.Value.ToPlainText()).IsEqualTo(source);
			await Assert.That(saved.Flags.Select(flag => flag.Name)).Contains("cmdsyntax")
				.Because(string.Join(",", saved.Flags.Select(flag => flag.Name)));
			await Assert.That(saved.Flags.Select(flag => flag.Name)).DoesNotContain("funsyntax");

			var functionPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
				Factory.Services, Factory.Services.GetRequiredService<IMediator>(), connections, "SoftcodeFunctionEditor");
			try
			{
				await parser.CommandParse(functionPlayer.Handle, connections,
					MarkupText.Plain("@editor me/EDITFUNCTION=function"));
				await sessions.DeliverAsync(parser, sessions.GetCapturing(functionPlayer.Handle)!, MarkupText.Plain("add(1,2)"));
				await sessions.DeliverAsync(parser, sessions.GetCapturing(functionPlayer.Handle)!, MarkupText.Plain(".sq"));
				var functionAttribute = await database.GetAttributeAsync(functionPlayer.DbRef, ["EDITFUNCTION"], CancellationToken.None)
					.LastOrDefaultAsync();
				await Assert.That(functionAttribute).IsNotNull();
				await Assert.That(functionAttribute!.Flags.Select(flag => flag.Name)).Contains("funsyntax");
				await Assert.That(functionAttribute.Flags.Select(flag => flag.Name)).DoesNotContain("cmdsyntax");
			}
			finally
			{
				await connections.Disconnect(functionPlayer.Handle);
			}

			var blankPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
				Factory.Services, Factory.Services.GetRequiredService<IMediator>(), connections, "SoftcodeBlankLineEditor");
			try
			{
				await parser.CommandParse(blankPlayer.Handle, connections,
					MarkupText.Plain("@editor me/EDITBLANK=command"));
				await sessions.DeliverAsync(parser, sessions.GetCapturing(blankPlayer.Handle)!, MarkupText.Empty);
				await sessions.DeliverAsync(parser, sessions.GetCapturing(blankPlayer.Handle)!, MarkupText.Plain("think after blank"));
				var blankDraft = await database.GetAttributeAsync(editor,
					["DATA", blankPlayer.Handle.ToString(), "DRAFT"], CancellationToken.None).LastOrDefaultAsync();
				await Assert.That(blankDraft?.Value.ToPlainText()).IsEqualTo("\nthink after blank");
				await sessions.DeliverAsync(parser, sessions.GetCapturing(blankPlayer.Handle)!, MarkupText.Plain(".q"));
			}
			finally
			{
				await connections.Disconnect(blankPlayer.Handle);
			}

			var failurePlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
				Factory.Services, Factory.Services.GetRequiredService<IMediator>(), connections, "SoftcodeFailedSaveEditor");
			try
			{
				await parser.CommandParse(failurePlayer.Handle, connections,
					MarkupText.Plain("@editor me/EDITFAIL=function"));
				await sessions.DeliverAsync(parser, sessions.GetCapturing(failurePlayer.Handle)!, MarkupText.Plain("add(1,2"));
				await sessions.DeliverAsync(parser, sessions.GetCapturing(failurePlayer.Handle)!, MarkupText.Plain(".sq"));
				await Assert.That(sessions.GetCapturing(failurePlayer.Handle)).IsNotNull()
					.Because("a failed save-and-quit must restart capture with the draft intact");
				var failedDraft = await database.GetAttributeAsync(editor,
					["DATA", failurePlayer.Handle.ToString(), "DRAFT"], CancellationToken.None).LastOrDefaultAsync();
				await Assert.That(failedDraft?.Value.ToPlainText()).IsEqualTo("add(1,2");
				await sessions.DeliverAsync(parser, sessions.GetCapturing(failurePlayer.Handle)!, MarkupText.Plain(".q"));
			}
			finally
			{
				await connections.Disconnect(failurePlayer.Handle);
			}
		}
		finally
		{
			await connections.Disconnect(player.Handle);
			await installer.UninstallAsync(manifest.Name);
		}
	}
}
