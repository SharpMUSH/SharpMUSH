using System.Globalization;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.DatabaseConversion;
using SharpMUSH.Library.Services.DatabaseConversion.TinyMux;
using SharpMUSH.Library.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// A TinyMUX game, flatfile, <c>comsys.db</c> and <c>mail.db</c>, read and imported through the same entry
/// point a PennMUSH dump takes. The fixtures are hand-written in TinyMUX's formats
/// (<c>Services/TestData/tinymux*</c>).
/// </summary>
public class TinyMuxImportTests
{
	private static string Fixture(string name) => Path.Join(AppContext.BaseDirectory, "Services", "TestData", name);

	private static readonly string FlatfilePath = Fixture("tinymux-v5.flat");
	private static readonly string ComsysPath = Fixture("tinymux.comsys");
	private static readonly string MailPath = Fixture("tinymux.mail");

	[Test]
	public async Task ReadsTheComsys()
	{
		var comsys = await TinyMuxComsysReader.ReadFileAsync(ComsysPath);

		await Assert.That(comsys.Chat.Channels.Select(c => c.Name)).IsEquivalentTo(["Public", "Staff"]);
		await Assert.That(comsys.Aliases[4]).IsEquivalentTo([("pub", "Public"), ("st", "Staff")]);

		var database = await TinyMuxDatabaseReader.ReadFileAsync(FlatfilePath);
		TinyMuxComsysReader.Apply(database, comsys, 5);

		var @public = database.Chat!.Channels.First(c => c.Name == "Public");
		// PLAYER_JOIN, OBJECT_JOIN and LOUD: open to players and objects, and announces joins.
		await Assert.That(@public.Flags).IsEqualTo(0x1 | 0x2);
		await Assert.That(@public.Creator).IsEqualTo(1);
		await Assert.That(@public.Mogrifier).IsEqualTo(-1);
		await Assert.That(@public.Users.Select(u => (u.DBRef, u.Flags, u.Title)))
			.IsEquivalentTo([(1, 0, ""), (4, 0x1, "The Builder")]);

		var staff = database.Chat.Channels.First(c => c.Name == "Staff");
		await Assert.That(staff.Flags).IsEqualTo(0x1 | 0x8);
		await Assert.That(staff.Mogrifier).IsEqualTo(3);
		await Assert.That(staff.Locks["speak"]).IsEqualTo("#1");
		await Assert.That(staff.Users.Select(u => u.Flags)).IsEquivalentTo([0]);

		await Assert.That(database.Objects.First(o => o.DBRef == 4).Attributes
			.Where(a => a.Name.StartsWith("CHANALIAS`")).Select(a => (a.Name, a.Value)))
			.IsEquivalentTo([("CHANALIAS`PUB", "Public"), ("CHANALIAS`ST", "Staff")]);
		await Assert.That(database.ImportNotes).Contains(note => note.StartsWith("Channel Staff: 1 member(s) had it turned off"));
		await Assert.That(database.ImportNotes).Contains(note => note.StartsWith("Channel Staff: TinyMUX listed it only to its members"));
		await Assert.That(database.ImportNotes).DoesNotContain(note => note.StartsWith("Channel Public: TinyMUX listed it only"));
	}

	[Test]
	public async Task ReadsTheMail()
	{
		var mail = await TinyMuxMailReader.ReadFileAsync(MailPath);

		await Assert.That(mail.Messages.Select(m => (m.To, m.From, m.Subject, m.Body, m.IsRead))).IsEquivalentTo([
			(4, 1, "Hello", "Welcome aboard.\nSee you.", false),
			(1, 4, "Re: \uF501Hello\uF500", "Thanks!", true)
		]);
		// TinyMUX's colour, highlight and reset here, is written as ANSI for the importer.
		await Assert.That(TinyMuxMailReader.WithColour(mail, 5).Messages[1].Subject).IsEqualTo("Re: \u001b[1mHello\u001b[0m");
		await Assert.That(DateTime.ParseExact(mail.Messages[0].Time, "ddd MMM d HH:mm:ss yyyy", CultureInfo.InvariantCulture))
			.IsEqualTo(new DateTime(2026, 9, 4, 12, 1, 45));

		var staff = mail.Aliases.First(a => a.Name == "staff");
		await Assert.That(staff.Members).IsEquivalentTo([1, 4]);
		await Assert.That(staff.UsePrivileges).IsEqualTo((int)MailAliasPrivileges.Everyone);
		var mine = mail.Aliases.First(a => a.Name == "mine");
		await Assert.That(mine.Owner).IsEqualTo(4);
		await Assert.That(mine.UsePrivileges).IsEqualTo((int)(MailAliasPrivileges.Owner | MailAliasPrivileges.Admin));
	}

	/// <summary>
	/// Through the path the upload endpoint takes: the converter sniffs the flatfile as TinyMUX and reads its
	/// comsys and mail beside it.
	/// </summary>
	[Test]
	public async Task AGameArrivesWhole()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();

		var result = await world.Converter.ConvertDatabaseAsync(FlatfilePath, MailPath, ComsysPath,
			new Progress<ConversionProgress>());

		await Assert.That(result.Errors).IsEmpty();
		await Assert.That(result.ChannelsConverted).IsEqualTo(2);
		await Assert.That(result.MailMessagesConverted).IsEqualTo(2);
		await Assert.That(result.Warnings).Contains(w => w.StartsWith("Imported from a TinyMUX flatfile") && w.Contains("rhost_list"));
		// %m is a register in TinyMUX as here, so its softcode is not warned about.
		await Assert.That(result.Warnings).DoesNotContain(w => w.Contains("%m"));

		var widget = await NodeAsync(world, 5);
		await Assert.That(widget.Object().Name).IsEqualTo("Widget");
		await Assert.That(widget.Object().Locks["Basic"].LockString).IsEqualTo("#4|DESCRIBE:*widget*");
		await Assert.That(widget.Object().Locks["Enter"].LockString).IsEqualTo("@#7");
		await Assert.That((await NodeAsync(world, 7)).Object().Locks["Zone"].LockString).IsEqualTo("#4");
		await Assert.That(await AttributeAsync(world, 5, "CMD_HELLO")).IsEqualTo("$hello:@pemit %#=Hi");

		foreach (var (dbref, password) in (IEnumerable<(int, string)>)[(1, "potrzebie"), (4, "hunter2"), (9, "U*U*U*U*"), (10, "pennpass")])
		{
			var player = (await NodeAsync(world, dbref)).Expect<SharpPlayer>();
			await Assert.That(world.Passwords.PasswordIsValid(password, player.PasswordHash)).IsTrue()
				.Because($"#{dbref} does not log in with its TinyMUX password");
			await Assert.That(world.Passwords.NeedsRehash(player.PasswordHash)).IsTrue();
		}

		var roles = world.Services.GetRequiredService<IRoleRegistryService>();
		await Assert.That((await roles.GetObjectOverridesAsync(4))["control.all"]).IsEqualTo(PermissionState.Allow);
		await Assert.That((await roles.GetObjectOverridesAsync(10))["server.operate"]).IsEqualTo(PermissionState.Allow);

		var stored = (await world.ExpandedData.GetExpandedServerData<SharpMUSHOptions>(nameof(SharpMUSHOptions)))!;
		await Assert.That(stored.Compatibility.MuxLogic).IsTrue();
		await Assert.That(stored.Compatibility.RhostList).IsTrue();
		await Assert.That(stored.Compatibility.InnermostTokens).IsTrue();
		await Assert.That(stored.Compatibility.ParenGroups).IsTrue();
		await Assert.That(stored.Limit.FunctionRecursionLimit).IsGreaterThanOrEqualTo(500u);

		var staff = await world.Mediator.Send(new GetChannelQuery("Staff")) ?? throw new InvalidOperationException("No Staff channel");
		await Assert.That(staff.SpeakLock).IsEqualTo("#1");
		await Assert.That(await AttributeAsync(world, 4, "CHANALIAS`ST")).IsEqualTo("Staff");

		var builder = (await NodeAsync(world, 4)).Expect<SharpPlayer>();
		var inbox = await world.Mediator.CreateStream(new GetAllMailListQuery(builder)).ToListAsync();
		await Assert.That(inbox.Select(m => m.Subject.ToPlainText())).IsEquivalentTo(["Hello"]);
	}

	/// <summary>A format chosen by the caller is the one the file is read in, with mail.db and comsys.db beside it.</summary>
	[Test]
	public async Task AnExplicitFormatIsHonoured()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();

		var result = await world.Converter.ConvertDatabaseAsync(
			new DatabaseImportFiles(FlatfilePath, MailPath, ComsysPath, Format: DatabaseImportFormat.TinyMUX),
			new Progress<ConversionProgress>());

		await Assert.That(result.Errors).IsEmpty();
		await Assert.That(result.ChannelsConverted).IsEqualTo(2);
		await Assert.That(result.MailMessagesConverted).IsEqualTo(2);
	}

	private static async Task<AnySharpObject> NodeAsync(IsolatedImportWorld world, int dbref)
		=> (await world.Mediator.Send(new GetObjectNodeQuery(new DBRef(dbref)))).Expect<AnySharpObject>();

	private static async Task<string> AttributeAsync(IsolatedImportWorld world, int dbref, string name)
		=> (await world.Database.GetAttributeAsync(new DBRef(dbref), [name]).ToListAsync()).Last().Value.ToPlainText();
}
