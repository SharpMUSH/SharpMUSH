using System.Text;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.DatabaseConversion;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// The mail aliases in the maildb the same PennMUSH 1.8.8 game wrote beside
/// <see cref="PennMUSHDbrefPreservationTests"/>'s dump: <c>+Team</c> (Alice's; Alice, Bob and Carol;
/// use Owner|Members, see Owner), <c>+Pair</c> (Bob's; Bob and Alice; open to everyone) and
/// <c>+Staff</c> (One's; One; use Admin|Owner, see Owner).
/// </summary>
public class PennMUSHMailAliasImportTests
{
	private static readonly string MailFixturePath =
		Path.Join(AppContext.BaseDirectory, "Services", "TestData", "pennmush-1.8.8p0-holes.maildb");

	[Test]
	public async Task TheMaildbAliasSectionIsRead()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();

		var mail = await world.Parser.ParseMailFileAsync(MailFixturePath);

		await Assert.That(mail.Flags).IsEqualTo(15);
		await Assert.That(string.Join(",", mail.Aliases.Select(a => a.Name))).IsEqualTo("Team,Pair,Staff");
		await Assert.That(string.Join(" ", mail.Aliases[0].Members)).IsEqualTo("3 4 5");
		await Assert.That(mail.Aliases[0].Description).IsEqualTo("The whole team");
		await Assert.That(mail.MessageCount).IsEqualTo(11);
	}

	/// <summary>
	/// Through the path the upload endpoint takes: the dump and the maildb together. Every alias keeps
	/// its owner, members, description and privilege bits, under the dbrefs the dump preserved.
	/// </summary>
	[Test]
	public async Task EveryAliasArrivesAsPennMUSHHadIt()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();

		var progress = new RecordingProgress();
		var result = await world.Converter.ConvertDatabaseAsync(PennMUSHDbrefPreservationTests.FixturePath,
			MailFixturePath, progress);

		await Assert.That(result.Errors).IsEmpty();
		await Assert.That(result.MailAliasesConverted).IsEqualTo(3);
		await Assert.That(result.Warnings).DoesNotContain(w => w.StartsWith("Mail alias"));

		// The admin page stops polling at 100%, so nothing may report it before the aliases are in.
		var phases = progress.Reports.Select(p => p.CurrentPhase).ToList();
		await Assert.That(phases.IndexOf("Mail imported")).IsGreaterThan(phases.IndexOf("Locks created"));
		await Assert.That(progress.Reports[^1].CurrentPhase).IsEqualTo("Complete");
		await Assert.That(progress.Reports.Count(p => p.PercentageComplete >= 100)).IsEqualTo(1);

		var aliases = await world.Mediator.CreateStream(new GetMailAliasesQuery()).ToListAsync();

		// Creation order is the maildb's order, which is the order PennMUSH lists them in.
		await Assert.That(string.Join(",", aliases.Select(a => a.Name))).IsEqualTo("Team,Pair,Staff");

		await Assert.That(Describe(aliases[0])).IsEqualTo("Team|The whole team|#3|3 4 5|Members, Owner|Owner");
		await Assert.That(Describe(aliases[1])).IsEqualTo("Pair|Pair|#4|4 3|Everyone|Everyone");
		await Assert.That(Describe(aliases[2])).IsEqualTo("Staff|Staff only|#1|1|Admin, Owner|Owner");

		// The owners and members are the imported players those numbers name.
		await Assert.That((await PennMUSHDbrefPreservationTests.NodeAsync(world, 3)).Object().Name).IsEqualTo("Alice");
		await Assert.That((await PennMUSHDbrefPreservationTests.NodeAsync(world, 5)).Object().Name).IsEqualTo("Carol");
	}

	/// <summary>
	/// load_malias hands an alias whose owner is no player to the probate judge and drops members that are
	/// no player. Here the owner is #7, a hole in the dump, and one member is #6, the Widget thing.
	/// </summary>
	[Test]
	public async Task AnOwnerOrMemberThatIsNoPlayerIsReportedAndReplaced()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();
		var database = await world.Parser.ParseFileAsync(PennMUSHDbrefPreservationTests.FixturePath);
		const string maildb = "+15\n1\n7\n\"Broken\"\n\"Broken refs\"\n0\n0\n3\n3\n6\n4\n\"*** End of MALIAS ***\"\n0\n***END OF DUMP***\n";
		database.Mail = await world.Parser.ParseMailAsync(new MemoryStream(Encoding.UTF8.GetBytes(maildb)));

		var result = await world.Converter.ConvertDatabaseAsync(database);

		await Assert.That(result.MailAliasesConverted).IsEqualTo(1);
		await Assert.That(result.Warnings).Contains(w => w.StartsWith("Mail alias +Broken: owner #7") && w.EndsWith("given to #1"));
		await Assert.That(result.Warnings).Contains(w => w.StartsWith("Mail alias +Broken: dropped member") && w.Contains("#6"));

		var alias = (await world.Mediator.CreateStream(new GetMailAliasesQuery()).ToListAsync()).Single();
		await Assert.That(alias.Owner).IsEqualTo(1);
		await Assert.That(string.Join(" ", alias.Members)).IsEqualTo("3 4");
	}

	/// <summary>A dump imported without its maildb imports no aliases and says nothing about them.</summary>
	[Test]
	public async Task WithoutAMaildbThereAreNoAliases()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();

		var result = await world.Converter.ConvertDatabaseAsync(
			await world.Parser.ParseFileAsync(PennMUSHDbrefPreservationTests.FixturePath));

		await Assert.That(result.MailAliasesConverted).IsEqualTo(0);
		await Assert.That(await world.Mediator.CreateStream(new GetMailAliasesQuery()).CountAsync()).IsEqualTo(0);
	}

	/// <summary>The maildb is optional: one that cannot be read costs its aliases, not the world, and says so.</summary>
	[Test]
	public async Task AnUnreadableMaildbStillImportsTheWorld()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();
		var badMaildb = Path.Join(Path.GetTempPath(), $"bad-{Guid.NewGuid():N}.maildb");
		// An alias section cut off after its owner.
		await File.WriteAllTextAsync(badMaildb, "+15\n1\n3\n");

		try
		{
			var result = await world.Converter.ConvertDatabaseAsync(PennMUSHDbrefPreservationTests.FixturePath,
				badMaildb, new Progress<ConversionProgress>());

			await Assert.That(result.Errors).IsEmpty();
			await Assert.That(result.PlayersConverted).IsGreaterThan(0);
			await Assert.That(result.MailAliasesConverted).IsEqualTo(0);
			await Assert.That(result.Warnings).Contains(w => w.StartsWith("The maildb could not be read"));
			await Assert.That((await PennMUSHDbrefPreservationTests.NodeAsync(world, 3)).Object().Name).IsEqualTo("Alice");
		}
		finally
		{
			File.Delete(badMaildb);
		}
	}

	/// <summary>malias_cleanup: a destroyed player leaves every alias, and one it owned passes on.</summary>
	[Test]
	public async Task ReleasingAPlayerTakesItOffAliasesAndHandsOnWhatItOwned()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();
		await world.Converter.ConvertDatabaseAsync(PennMUSHDbrefPreservationTests.FixturePath, MailFixturePath,
			new Progress<ConversionProgress>());
		// Read first so the listing is cached: the release has to invalidate it.
		await Assert.That(await world.Mediator.CreateStream(new GetMailAliasesQuery()).CountAsync()).IsEqualTo(3);

		await world.Mediator.Send(new ReleaseMailAliasesCommand(3, 1));

		var aliases = await world.Mediator.CreateStream(new GetMailAliasesQuery()).ToDictionaryAsync(a => a.Name);
		await Assert.That(aliases["Team"].Owner).IsEqualTo(1);
		await Assert.That(string.Join(" ", aliases["Team"].Members)).IsEqualTo("4 5");
		await Assert.That(aliases["Pair"].Owner).IsEqualTo(4);
		await Assert.That(string.Join(" ", aliases["Pair"].Members)).IsEqualTo("4");
		await Assert.That(aliases["Staff"].Owner).IsEqualTo(1);
	}

	private static string Describe(SharpMailAlias alias)
		=> $"{alias.Name}|{alias.Description}|#{alias.Owner}|{string.Join(" ", alias.Members)}|{alias.UsePrivileges}|{alias.SeePrivileges}";

	/// <summary>Records each report as it is made; <see cref="Progress{T}"/> posts them, so their order is lost.</summary>
	private sealed class RecordingProgress : IProgress<ConversionProgress>
	{
		public List<ConversionProgress> Reports { get; } = [];

		public void Report(ConversionProgress value) => Reports.Add(value);
	}
}
