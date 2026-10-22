using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary><c>@mail/debug</c>, PennMUSH <c>do_mail_debug</c> (<c>extmail.c:1743</c>).</summary>
public class MailDebugTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private Task AsGod(string command) =>
		WebAppFactoryArg.CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain(command)).AsTask();

	private async Task<List<string>> GodHearsWhile(Func<Task> action)
	{
		var god = WebAppFactoryArg.ExecutorDBRef;
		var before = WebAppFactoryArg.Notifications.CountFor(god);
		await action();
		return [.. WebAppFactoryArg.Notifications.For(god).Skip(before)];
	}

	private async Task<int> MailCount(DBRef player)
		=> await Mediator.Send(new GetObjectNodeQuery(player)) is AnySharpObject and SharpPlayer found
			? await Mediator.CreateStream(new GetAllMailListQuery(found)).CountAsync()
			: -1;

	/// <summary>A stored message addressed to <paramref name="recipient"/>, which no command can send to.</summary>
	private async Task<string> StoreMailTo(int recipient)
	{
		var id = 900_000_000L + Random.Shared.Next(1_000_000);
		await WebAppFactoryArg.Services.GetRequiredService<LightningDatabase>().Store.WriteAsync(tx =>
			tx.Put(Tables.Mail, Keys.Dbref(id), Codec.Serialize(new MailRecord { Sender = 1, Recipient = recipient, Folder = "INBOX" })));
		return $"Mail/{id}";
	}

	private async Task<bool> Stored(string mailId)
		=> await Mediator.CreateStream(new GetAllMailRecipientsQuery()).AnyAsync(mail => mail.Id == mailId);

	[Test]
	public async ValueTask ClearDeletesEveryFolder()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "MailDebugClear");
		await AsGod($"@mail {player.DbRef}=One/First.");
		await AsGod($"@mail {player.DbRef}=Two/Second.");
		await WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle)
			.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("@mail/file 1=1"));
		await Assert.That(await MailCount(player.DbRef)).IsEqualTo(2);

		var heard = await GodHearsWhile(() => AsGod($"@mail/debug clear={player.Name}"));

		await Assert.That(heard).Contains($"Mail cleared for {player.Name}(#{player.DbRef.Number}).");
		await Assert.That(await MailCount(player.DbRef)).IsEqualTo(0);
	}

	[Test]
	public async ValueTask ClearNamesAMissingPlayer()
	{
		var name = TestIsolationHelpers.GenerateUniqueName("NoMailPlayer");

		var heard = await GodHearsWhile(() => AsGod($"@mail/debug clear={name}"));

		await Assert.That(heard).Contains($"{name}: No such player.");
	}

	[Test]
	public async ValueTask SanityReportsAndFixRemovesMailToANonPlayer()
	{
		var thingName = TestIsolationHelpers.GenerateUniqueName("MailDebugThing");
		var thing = (await WebAppFactoryArg.FunctionParser.EvaluateAsync(MarkupText.Plain($"[create({thingName})]"))).ToPlainText();
		var number = int.Parse(thing.TrimStart('#'));
		var mailId = await StoreMailTo(number);

		var heard = await GodHearsWhile(() => AsGod("@mail/debug sanity"));

		await Assert.That(heard).Contains($"{thingName}(#{number}) has mail but is not a player.");
		await Assert.That(heard).Contains("Mail sanity check completed.");
		await Assert.That(await Stored(mailId)).IsTrue();

		heard = await GodHearsWhile(() => AsGod("@mail/debug fix"));

		await Assert.That(heard).Contains($"Fixing mail for #{number}.");
		await Assert.That(heard).Contains("Mail sanity fix completed.");
		await Assert.That(await Stored(mailId)).IsFalse();
	}

	[Test]
	public async ValueTask UnknownActionIsRefused()
	{
		var heard = await GodHearsWhile(() => AsGod("@mail/debug frobnicate"));

		await Assert.That(heard).Contains("That is not a debugging option.");
	}
}
