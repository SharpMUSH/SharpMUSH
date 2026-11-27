using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Packages;

/// <summary>
/// The bundled mail-composer package: +compose prompts for To, Cc, Bcc and Subject over an @input session, takes the
/// message a line at a time with dot commands, and sends it as the player with @mail.
/// </summary>
public class MailComposerPackageTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	private IConnectionService Connections => Factory.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => Factory.Services.GetRequiredService<IMUSHCodeParser>();
	private ISharpDatabase Database => Factory.Services.GetRequiredService<ISharpDatabase>();
	private IInputSessionService Sessions => Factory.Services.GetRequiredService<IInputSessionService>();
	private IMediator Mediator => Factory.Services.GetRequiredService<IMediator>();

	private static async Task<PackageManifest> Manifest()
	{
		for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
		{
			var candidate = Path.Join(directory.FullName, "examples", "packages", "mail-composer", "package.yaml");
			if (File.Exists(candidate))
				return new PackageManifestService().ParseManifest(await File.ReadAllTextAsync(candidate))
					.Expect<ParsedPackageManifest>().Manifest;
		}
		throw new FileNotFoundException("Could not find the mail-composer package manifest.");
	}

	[Test]
	public async Task PackageIsSuggestedAtSetupWithoutInstallingAtFirstBoot()
	{
		var descriptor = BundledPackages.All.First(item => item.PackageId == "mail-composer");

		await Assert.That(descriptor.InstallAtFirstBoot).IsFalse();
		await Assert.That(descriptor.SuggestAtSetup).IsTrue();
		await Assert.That(descriptor.Requires).IsEqualTo(BundledPackageHandler.None);
	}

	[Test, NotInParallel]
	public async Task InstalledPackageComposesAndSendsThroughAnInputSession()
	{
		var manifest = await Manifest();
		var installer = Factory.Services.GetRequiredService<IPackageInstallService>();
		(await installer.ApplyAsync(manifest, new PackageApplyRequest(
			new PackageApplySource("https://github.com/SharpMUSH/SharpMUSH", "examples/packages/mail-composer/", "test", "main"),
			new Dictionary<string, string>(), []))).Expect<PackageApplyResult>("the composer package installs");
		try
		{
			await PromptsForEachPartAndNamesOnlyVisibleReaders();
			await QuickStartEditsLinesAndKeepsThemAsTyped();
			await UnknownNamesAreNamedBackAndAskedAgain();
			await QuitKeepsTheDraftAndANewMessageIsRefusedUntilItIsDone();
			await RefusedMessageKeepsTheComposerOpen();
			await AbortThrowsTheDraftAway();
		}
		finally
		{
			await installer.UninstallAsync(manifest.Name);
		}
	}

	private async Task PromptsForEachPartAndNamesOnlyVisibleReaders()
	{
		var to = await Player("ComposeTo");
		var cc = await Player("ComposeCc");
		var bcc = await Player("ComposeBcc");
		await Compose("ComposePrompted", async writer =>
		{
			await Open(writer, "+compose");
			await Assert.That(await DraftStage(writer)).IsEqualTo("to");
			await Send(writer, to.Name);
			await Assert.That(await DraftStage(writer)).IsEqualTo("cc");
			await Send(writer, cc.Name);
			await Assert.That(await DraftStage(writer)).IsEqualTo("bcc");
			await Send(writer, bcc.Name);
			await Assert.That(await DraftStage(writer)).IsEqualTo("subject");
			await Send(writer, "Plans");
			await Send(writer, "Meet at the docks at nine.");
			await Send(writer, ".send");

			await Assert.That(Sessions.GetCapturing(writer.Handle)).IsNull().Because(Heard(writer));
			var expected = $"To: {to.Name}\nCc: {cc.Name}\n\nMeet at the docks at nine.";
			foreach (var reader in new[] { to, cc, bcc })
			{
				var mail = await LastMail(reader, writer);
				await Assert.That(mail?.Subject.ToPlainText()).IsEqualTo("Plans");
				await Assert.That(mail?.Content.ToPlainText()).IsEqualTo(expected).Because("Bcc readers are never named");
			}
			await Assert.That(await DraftStage(writer)).IsNull().Because("sending empties the draft");
		});
	}

	private async Task QuickStartEditsLinesAndKeepsThemAsTyped()
	{
		var reader = await Player("ComposeQuickTo");
		await Compose("ComposeQuick", async writer =>
		{
			await Open(writer, $"+compose {reader.Name}=Quick");
			await Assert.That(await DraftStage(writer)).IsEqualTo("body");
			await Send(writer, "");
			await Send(writer, "[add(1,2)] 50% of it; @pemit me=hi");
			await Send(writer, "..send is a line");
			await Send(writer, ".edit 1 First");
			await Send(writer, ".insert 3 Third");
			await Send(writer, ".insert 5 Last");
			await Send(writer, ".delete 4");
			await Send(writer, ".sned");
			await Assert.That(Heard(writer)).Contains("There is no .sned command.");
			await Send(writer, ".send");

			var mail = await LastMail(reader, writer);
			await Assert.That(mail?.Content.ToPlainText())
				.IsEqualTo("First\n[add(1,2)] 50% of it; @pemit me=hi\nThird\nLast")
				.Because("one reader gets no To line, and nothing typed is evaluated or run");
		});
	}

	private async Task UnknownNamesAreNamedBackAndAskedAgain()
		=> await Compose("ComposeUnknown", async writer =>
		{
			var missing = TestIsolationHelpers.GenerateUniqueName("Nobody");
			await Parser.CommandParse(writer.Handle, Connections, MarkupText.Plain($"+compose {missing}=Hi"));
			await Assert.That(Sessions.GetCapturing(writer.Handle)).IsNull();
			await Assert.That(Heard(writer)).Contains($"I can't find '{missing}'.");
			await Assert.That(await DraftStage(writer)).IsNull().Because("a refused start keeps no draft");

			await Open(writer, "+compose");
			await Send(writer, missing);
			await Assert.That(await DraftStage(writer)).IsEqualTo("to");
			await Send(writer, ".abort");
		});

	private async Task QuitKeepsTheDraftAndANewMessageIsRefusedUntilItIsDone()
	{
		var reader = await Player("ComposeLaterTo");
		await Compose("ComposeLater", async writer =>
		{
			await Open(writer, $"+compose {reader.Name}=Later");
			await Send(writer, "Kept line.");
			await Send(writer, ".quit");
			await Assert.That(Sessions.GetCapturing(writer.Handle)).IsNull();
			await Assert.That(await DraftStage(writer)).IsEqualTo("body");
			await Assert.That((await Attribute(writer.DbRef, "MAILDRAFT"))?.Flags.Select(flag => flag.Name))
				.Contains("wizard").Because("only the composer writes a character's draft");

			await Parser.CommandParse(writer.Handle, Connections, MarkupText.Plain($"+compose {reader.Name}=Other"));
			await Assert.That(Sessions.GetCapturing(writer.Handle)).IsNull();
			await Assert.That(Heard(writer)).Contains($"You have an unsent draft to {reader.Name} (Later).");

			await Open(writer, "+compose");
			await Send(writer, ".send");
			await Assert.That((await LastMail(reader, writer))?.Content.ToPlainText()).IsEqualTo("Kept line.");
		});
	}

	private async Task RefusedMessageKeepsTheComposerOpen()
	{
		var reader = await Player("ComposeLockedTo");
		await LockMailToGod(reader.DbRef);
		await Compose("ComposeLocked", async writer =>
		{
			await Open(writer, $"+compose {reader.Name}=Locked");
			await Send(writer, "Hello?");
			await Send(writer, ".send");

			await Assert.That(Heard(writer)).Contains("Not sent. Your draft is still open.");
			await Assert.That(Sessions.GetCapturing(writer.Handle)).IsNotNull();
			await Assert.That(await DraftStage(writer)).IsEqualTo("body");
			await Send(writer, ".abort");
		});
	}

	private async Task AbortThrowsTheDraftAway()
		=> await Compose("ComposeAbort", async writer =>
		{
			await Open(writer, "+compose");
			await Send(writer, ".abort");
			await Assert.That(Sessions.GetCapturing(writer.Handle)).IsNull();
			await Assert.That(await DraftStage(writer)).IsNull();
		});

	private async Task Compose(string name, Func<TestIsolationHelpers.TestPlayer, Task> scenario)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, Mediator, Connections, name);
		try
		{
			await scenario(player);
		}
		finally
		{
			await Connections.Disconnect(player.Handle);
		}
	}

	private async Task<TestIsolationHelpers.TestPlayer> Player(string name)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, Mediator, Connections, name);
		await Connections.Disconnect(player.Handle);
		return player;
	}

	private async Task Open(TestIsolationHelpers.TestPlayer player, string command)
	{
		await Parser.CommandParse(player.Handle, Connections, MarkupText.Plain(command));
		await Assert.That(Sessions.GetCapturing(player.Handle)).IsNotNull().Because(Heard(player));
	}

	private async Task Send(TestIsolationHelpers.TestPlayer player, string line)
	{
		if (Sessions.GetCapturing(player.Handle) is not { } session)
			throw new InvalidOperationException($"No composer is open on {player.Handle} to receive '{line}'. {Heard(player)}");
		var result = await Sessions.DeliverAsync(Parser, session, MarkupText.Plain(line));
		await Assert.That(result?.HadErrors).IsFalse().Because(result?.Message.ToPlainText() ?? "no callback result");
	}

	private string Heard(TestIsolationHelpers.TestPlayer player)
		=> string.Join(" | ", Factory.Notifications.ForHandle(player.Handle));

	private async Task<string?> DraftStage(TestIsolationHelpers.TestPlayer player)
		=> (await Attribute(player.DbRef, "MAILDRAFT", "STAGE"))?.Value.ToPlainText();

	private async Task<SharpAttribute?> Attribute(DBRef owner, params string[] path)
		=> await Database.GetAttributeAsync(owner, path, CancellationToken.None).LastOrDefaultAsync();

	/// <summary>The last message <paramref name="reader"/> has from <paramref name="writer"/>.</summary>
	private async Task<SharpMail?> LastMail(TestIsolationHelpers.TestPlayer reader, TestIsolationHelpers.TestPlayer writer)
	{
		var player = (await Mediator.Send(new GetObjectNodeQuery(reader.DbRef))).Expect<SharpPlayer>();
		var mailbox = await Mediator.CreateStream(new GetAllMailListQuery(player)).ToListAsync();
		var fromWriter = new List<SharpMail>();
		foreach (var mail in mailbox)
			if ((await mail.From.WithCancellation(CancellationToken.None)).Object()?.DBRef == writer.DbRef)
				fromWriter.Add(mail);
		return fromWriter.OrderBy(mail => mail.DateSent).LastOrDefault();
	}

	/// <summary>Locks <paramref name="owner"/>'s mail to God alone.</summary>
	private async Task LockMailToGod(DBRef owner)
	{
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>();
		var target = (await Mediator.Send(new GetObjectNodeQuery(owner))).Expect<AnySharpObject>();
		(await Mediator.Send(new SetLockCommand(target.Object(), "Mail", "#1", god))).Expect<Success>();
	}
}
