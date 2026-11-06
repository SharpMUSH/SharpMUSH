using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.DatabaseConversion;
using SharpMUSH.Library.Services.DatabaseConversion.TinyMux;
using SharpMUSH.Server.Services.Imports;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// TinyMUX 2.14's <c>netmux.sqlite</c>. The fixture (<c>Services/TestData/tinymux.sqlite.sql</c>) is the
/// game of <c>tinymux-v5.flat</c>, <c>tinymux.comsys</c> and <c>tinymux.mail</c> written as TinyMUX keeps it
/// in SQLite, so the two imports must agree.
/// </summary>
public class TinyMuxSqliteImportTests
{
	private static string Fixture(string name) => Path.Join(AppContext.BaseDirectory, "Services", "TestData", name);

	/// <summary>A SQLite database built from SQL, deleted again when disposed.</summary>
	private sealed class SqliteFile : IDisposable
	{
		public string Path { get; } = System.IO.Path.Join(System.IO.Path.GetTempPath(), $"netmux-{Guid.NewGuid():N}.sqlite");

		public SqliteFile(string sql)
		{
			using (var connection = new SqliteConnection($"Data Source={Path};Pooling=False"))
			{
				connection.Open();
				using var command = connection.CreateCommand();
				// TinyMUX itself only logs a broken reference between its tables, so a test may write one.
				command.CommandText = "PRAGMA foreign_keys = OFF;\n" + sql;
				command.ExecuteNonQuery();
			}
		}

		public void Dispose() => File.Delete(Path);
	}

	private static SqliteFile Game(Func<string, string>? change = null)
	{
		var sql = File.ReadAllText(Fixture("tinymux.sqlite.sql"));
		return new SqliteFile(change is null ? sql : change(sql));
	}

	/// <summary>A database holding only the objects, attributes and attrnames tables, with the rows given.</summary>
	private const string MinimalSchema = """
		CREATE TABLE objects (dbref INTEGER PRIMARY KEY, location INTEGER, contents INTEGER, exits INTEGER, next INTEGER,
		  link INTEGER, owner INTEGER, parent INTEGER, zone INTEGER, pennies INTEGER, flags1 INTEGER, flags2 INTEGER,
		  flags3 INTEGER, powers1 INTEGER, powers2 INTEGER);
		CREATE TABLE attrnames (attrnum INTEGER PRIMARY KEY, name TEXT NOT NULL, flags INTEGER NOT NULL DEFAULT 0);
		INSERT INTO objects VALUES (0, -1, 1, -1, -1, -1, 1, -1, -1, 0, 0, 0, 0, 0, 0);
		INSERT INTO objects VALUES (1, 0, -1, -1, -1, 0, 1, -1, -1, 0, 19, 0, 0, 0, 0);
		""";

	[Test]
	public async Task IsToldFromItsHeader()
	{
		using var game = Game();

		await Assert.That(TinyMuxDatabaseReader.IsSqlite(game.Path)).IsTrue();
		await Assert.That(DatabaseFormatSniffer.SniffFile(game.Path)).IsEqualTo(DatabaseDialect.TinyMUX);
		await Assert.That(TinyMuxDatabaseReader.IsSqlite(Fixture("tinymux-v5.flat"))).IsFalse();
		await Assert.That(DatabaseFormatSniffer.SniffFile(Fixture("tinymux-v5.flat"))).IsEqualTo(DatabaseDialect.TinyMUX);
	}

	/// <summary>The same game read from SQLite and from the flatfile with its comsys.db and mail.db is the same model.</summary>
	[Test]
	public async Task ReadsTheSameGameAsTheFlatfile()
	{
		using var game = Game();
		var fromSqlite = await TinyMuxDatabaseReader.ReadSqliteFileAsync(game.Path);

		var fromFlatfile = await TinyMuxDatabaseReader.ReadFileAsync(Fixture("tinymux-v5.flat"));
		fromFlatfile.Mail = TinyMuxMailReader.WithColour(await TinyMuxMailReader.ReadFileAsync(Fixture("tinymux.mail")), 5);
		TinyMuxComsysReader.Apply(fromFlatfile, await TinyMuxComsysReader.ReadFileAsync(Fixture("tinymux.comsys")), 5);

		// The flatfile's #5 carries its money twice, in the record and in a Money attribute; SQLite keeps one.
		var widget = fromFlatfile.Objects.Single(o => o.DBRef == 5);
		await Assert.That(widget.Pennies).IsEqualTo(1);

		await Assert.That(Canonical(fromSqlite.Objects).ToJsonString()).IsEqualTo(Canonical(fromFlatfile.Objects).ToJsonString());
		await Assert.That(Json(fromSqlite.Mail.Messages)).IsEqualTo(Json(fromFlatfile.Mail.Messages));
		await Assert.That(Json(fromSqlite.Mail.Aliases)).IsEqualTo(Json(fromFlatfile.Mail.Aliases));
		await Assert.That(Json(fromSqlite.Chat.Channels)).IsEqualTo(Json(fromFlatfile.Chat.Channels));
		await Assert.That(Json(fromSqlite.FlagDefinitions)).IsEqualTo(Json(fromFlatfile.FlagDefinitions));
		await Assert.That(Json(fromSqlite.PowerDefinitions)).IsEqualTo(Json(fromFlatfile.PowerDefinitions));
		await Assert.That(Json(fromSqlite.SoftcodeRewrites)).IsEqualTo(Json(fromFlatfile.SoftcodeRewrites));
		await Assert.That(Json(fromSqlite.SourceOptions)).IsEqualTo(Json(fromFlatfile.SourceOptions));
		await Assert.That(fromSqlite.Dialect).IsEqualTo(DatabaseDialect.TinyMUX);
		await Assert.That(fromSqlite.ImportNotes.Texts().Select(Unsourced))
			.IsEquivalentTo(fromFlatfile.ImportNotes.Texts().Select(Unsourced), TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(fromSqlite.ImportNotes.Texts()).Contains(note => note.StartsWith("Imported from a TinyMUX SQLite database"));
		await Assert.That(fromSqlite.Version).IsEqualTo("TinyMUX SQLite database, schema version 13");
		await Assert.That(fromSqlite.Mail.ReadError).IsNull();
		await Assert.That(fromSqlite.Chat.ReadError).IsNull();
	}

	/// <summary>An object's name and money are its Name and Money attributes, which TinyMUX keeps them in.</summary>
	[Test]
	public async Task NameAndMoneyComeFromTheirAttributes()
	{
		using var game = Game(sql => sql.Replace(
			"INSERT INTO attributes (object, attrnum, value) VALUES (4, 25, CAST('50' AS BLOB));",
			"INSERT INTO attributes (object, attrnum, value) VALUES (4, 25, CAST('1234' AS BLOB));"));
		var database = await TinyMuxDatabaseReader.ReadSqliteFileAsync(game.Path);
		var builder = database.Objects.Single(o => o.DBRef == 4);

		await Assert.That(builder.Name).IsEqualTo("Builder");
		await Assert.That(builder.Pennies).IsEqualTo(1234);
		await Assert.That(builder.Attributes.Select(a => a.Name)).DoesNotContain("NAME").And.DoesNotContain("MONEY");
		await Assert.That(database.RoutineNotes).DoesNotContain(note => note.Contains("Attribute number 43") || note.Contains("Attribute number 25"));
	}

	/// <summary>
	/// The earliest databases had no owner or flags columns: an attribute's own owner and flags rode in its
	/// value, as in a flatfile.
	/// </summary>
	[Test]
	public async Task ReadsTheOwnerAndFlagsPrefixOfTheEarliestDatabases()
	{
		using var game = new SqliteFile(MinimalSchema + "\n" + """
			CREATE TABLE attributes (object INTEGER NOT NULL, attrnum INTEGER NOT NULL, value BLOB NOT NULL, PRIMARY KEY (object, attrnum));
			INSERT INTO attrnames VALUES (256, 'cmd_hello', 0);
			INSERT INTO attributes VALUES (0, 43, CAST('Limbo' AS BLOB));
			INSERT INTO attributes VALUES (1, 43, CAST('Wizard' AS BLOB));
			INSERT INTO attributes VALUES (0, 256, X'01313A323034383A2468656C6C6F3A4070656D69742025233D4869');
			""");
		var database = await TinyMuxDatabaseReader.ReadSqliteFileAsync(game.Path);
		var hello = database.Objects.Single(o => o.DBRef == 0).Attributes.Single(a => a.Name == "CMD_HELLO");

		await Assert.That(hello.Value).IsEqualTo("$hello:@pemit %#=Hi");
		// The prefix named #1 as its owner, which is also #0's owner, and the visual flag (0x800).
		await Assert.That(hello.Owner).IsNull();
		await Assert.That(hello.Flags).Contains("visual");
		await Assert.That(database.Mail.Messages).IsEmpty();
		await Assert.That(database.Chat.Channels).IsEmpty();
		await Assert.That(database.Mail.ReadError).IsNull();
		await Assert.That(database.Chat.ReadError).IsNull();
	}

	/// <summary>
	/// TinyMUX keeps the database in WAL mode. The copy is read as it is, without the -wal and -shm files a
	/// reader would otherwise create beside it.
	/// </summary>
	[Test]
	public async Task ReadsAWalDatabaseWithoutWritingBesideIt()
	{
		using var game = Game(sql => "PRAGMA journal_mode = WAL;\n" + sql);

		var database = await TinyMuxDatabaseReader.ReadSqliteFileAsync(game.Path);

		await Assert.That(database.Objects.Count).IsEqualTo(10);
		await Assert.That(File.Exists(game.Path + "-wal")).IsFalse();
		await Assert.That(File.Exists(game.Path + "-shm")).IsFalse();
	}

	/// <summary>Mail that cannot be read is the mail's read error; the game is still read.</summary>
	[Test]
	public async Task MailThatCannotBeReadIsReportedApart()
	{
		using var game = Game(sql => sql.Replace("INSERT INTO mail_bodies VALUES (1, 'Thanks!');", ""));
		var database = await TinyMuxDatabaseReader.ReadSqliteFileAsync(game.Path);

		await Assert.That(database.Objects).IsNotEmpty();
		await Assert.That(database.Mail.ReadError).IsNotNull().And.Contains("names body 1");
		await Assert.That(database.Chat.Channels.Select(c => c.Name)).IsEquivalentTo(["Public", "Staff"]);
	}

	[Test]
	public async Task RefusesASqliteDatabaseThatIsNotTinyMuxs()
	{
		using var other = new SqliteFile("CREATE TABLE notes (id INTEGER PRIMARY KEY, text TEXT);");

		await Assert.That(async () => await TinyMuxDatabaseReader.ReadSqliteFileAsync(other.Path))
			.Throws<FormatException>().WithMessageContaining("not TinyMUX's");
	}

	[Test]
	public async Task RefusesADatabaseWithNoGame()
	{
		using var empty = new SqliteFile(MinimalSchema.Replace("INSERT INTO objects", "-- INSERT INTO objects") +
			"\nCREATE TABLE attributes (object INTEGER, attrnum INTEGER, value BLOB, owner INTEGER, flags INTEGER);");

		await Assert.That(async () => await TinyMuxDatabaseReader.ReadSqliteFileAsync(empty.Path))
			.Throws<FormatException>().WithMessageContaining("holds no game");
	}

	/// <summary>The SQLite database holds the mail and comsys, so a mail.db or comsys.db beside it is refused, not dropped.</summary>
	[Test]
	public async Task PreflightRefusesSideFiles()
	{
		using var game = Game();
		var preflight = new DatabaseImportPreflight(new PennMUSHDatabaseParser(NullLogger<PennMUSHDatabaseParser>.Instance));

		var database = await preflight.ReadAsync(new DatabaseImportFiles(game.Path));
		await Assert.That(database.Mail.Messages.Count).IsEqualTo(2);
		await Assert.That(database.Chat.Channels.Count).IsEqualTo(2);

		await Assert.That(async () => await preflight.ReadAsync(new DatabaseImportFiles(game.Path, Mail: Fixture("tinymux.mail"))))
			.Throws<FormatException>().WithMessageContaining("holds its own mail and comsys");
	}

	/// <summary>Through the converter, as the upload endpoint takes it: one file brings the game, its channels and its mail.</summary>
	[Test]
	public async Task AGameArrivesWhole()
	{
		using var game = Game();
		await using var world = await IsolatedImportWorld.CreateAsync();

		var result = await world.Converter.ConvertDatabaseAsync(new DatabaseImportFiles(game.Path), new Progress<ConversionProgress>());

		await Assert.That(result.Errors).IsEmpty();
		await Assert.That(result.ChannelsConverted).IsEqualTo(2);
		await Assert.That(result.MailMessagesConverted).IsEqualTo(2);
		await Assert.That(result.Warnings.Texts()).Contains(w => w.StartsWith("Imported from a TinyMUX SQLite database"));

		var widget = (await world.Mediator.Send(new GetObjectNodeQuery(new DBRef(5)))).Expect<AnySharpObject>();
		await Assert.That(widget.Object().Name).IsEqualTo("Widget");
		var hello = (await world.Database.GetAttributeAsync(new DBRef(5), ["CMD_HELLO"]).ToListAsync()).Last();
		await Assert.That(hello.Value.ToPlainText()).IsEqualTo("$hello:@pemit %#=Hi");

		var wizard = (await world.Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<SharpPlayer>();
		await Assert.That(world.Passwords.PasswordIsValid("potrzebie", wizard.PasswordHash)).IsTrue();

		var builder = (await world.Mediator.Send(new GetObjectNodeQuery(new DBRef(4)))).Expect<SharpPlayer>();
		var inbox = await world.Mediator.CreateStream(new GetAllMailListQuery(builder)).ToListAsync();
		await Assert.That(inbox.Select(m => m.Content.ToPlainText())).IsEquivalentTo(["Welcome aboard.\nSee you."]);
		var staff = await world.Mediator.Send(new GetChannelQuery("Staff")) ?? throw new InvalidOperationException("No Staff channel");
		await Assert.That(LockKeyReferences.WithoutCreationTimes(staff.SpeakLock)).IsEqualTo("#1");
	}

	/// <summary>A side file given to the converter with a SQLite database is passed over, and the report says so.</summary>
	[Test]
	public async Task TheConverterNotesSideFilesItDidNotRead()
	{
		using var game = Game();
		await using var world = await IsolatedImportWorld.CreateAsync();

		var result = await world.Converter.ConvertDatabaseAsync(
			new DatabaseImportFiles(game.Path, Fixture("tinymux.mail"), Fixture("tinymux.comsys")), new Progress<ConversionProgress>());

		await Assert.That(result.Errors).IsEmpty();
		await Assert.That(result.MailMessagesConverted).IsEqualTo(2);
		await Assert.That(result.Warnings.Texts()).Contains(w => w.StartsWith("tinymux.mail was not read"));
		await Assert.That(result.Warnings.Texts()).Contains(w => w.StartsWith("tinymux.comsys was not read"));
	}

	/// <summary>
	/// The objects with their attributes and locks in name order: SQLite gives the attributes by number, a
	/// flatfile in the order it holds them, and neither order means anything.
	/// </summary>
	private static JsonArray Canonical(IEnumerable<PennMUSHObject> objects)
	{
		var array = new JsonArray();
		foreach (var o in objects)
		{
			var node = JsonSerializer.SerializeToNode(o)!.AsObject();
			node["Attributes"] = JsonSerializer.SerializeToNode(o.Attributes.OrderBy(a => a.Name, StringComparer.Ordinal).ToList());
			node["Locks"] = JsonSerializer.SerializeToNode(new SortedDictionary<string, PennMUSHLock>(o.Locks, StringComparer.Ordinal));
			array.Add(node);
		}

		return array;
	}

	private static string Unsourced(string note) => note.Replace("TinyMUX SQLite database", "TinyMUX flatfile");

	private static string Json<T>(T value) => JsonSerializer.Serialize(value);
}
