using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Services.DatabaseConversion;

namespace SharpMUSH.Tests.Services;

public class PennMUSHDatabaseParserTests
{
	[ClassDataSource<TestClassFactory>(Shared = SharedType.PerClass)]
	public required TestClassFactory Factory { get; init; }

	private PennMUSHDatabaseParser GetParser()
	{
		var logger = Factory.Services.GetRequiredService<ILogger<PennMUSHDatabaseParser>>();
		return new PennMUSHDatabaseParser(logger);
	}

	[Test]
	public async ValueTask ParserCanBeInstantiated()
	{
		var parser = GetParser();
		await Assert.That(parser).IsNotNull();
	}

	[Test]
	public async ValueTask ParserCanParseEmptyDatabase()
	{
		var parser = GetParser();
		var databaseContent = "V:PennMUSH v1.8.8p0\n";
		
		using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(databaseContent));
		var database = await parser.ParseAsync(stream);

		await Assert.That(database).IsNotNull();
		await Assert.That(database.Version).Contains("PennMUSH");
		await Assert.That(database.Objects.Count).IsEqualTo(0);
	}

	[Test]
	public async ValueTask ParserCanParseSimpleRoom()
	{
		var parser = GetParser();
		var databaseContent = @"V:PennMUSH v1.8.8p0
!0
Room Zero
-1
-1
-1
-1
-1
-1
1
-1
0
TYPE_ROOM VISUAL
0
0
1234567890
1234567890
";
		
		using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(databaseContent));
		var database = await parser.ParseAsync(stream);

		await Assert.That(database).IsNotNull();
		await Assert.That(database.Objects.Count).IsEqualTo(1);
		
		var room = database.Objects[0];
		await Assert.That(room.DBRef).IsEqualTo(0);
		await Assert.That(room.Name).IsEqualTo("Room Zero");
		await Assert.That(room.Type).IsEqualTo(PennMUSHObjectType.Room);
		await Assert.That(room.Owner).IsEqualTo(1);
	}

	[Test]
	public async ValueTask ParserCanParseObjectWithAttribute()
	{
		var parser = GetParser();
		var databaseContent = @"V:PennMUSH v1.8.8p0
!1
Player One
0
-1
-1
-1
-1
-1
1
-1
1000
TYPE_PLAYER
0
0
1234567890
1234567890
<DESCRIBE>^-1^visual^0
This is the description.
";
		
		using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(databaseContent));
		var database = await parser.ParseAsync(stream);

		await Assert.That(database).IsNotNull();
		await Assert.That(database.Objects.Count).IsEqualTo(1);
		
		var player = database.Objects[0];
		await Assert.That(player.DBRef).IsEqualTo(1);
		await Assert.That(player.Name).IsEqualTo("Player One");
		await Assert.That(player.Type).IsEqualTo(PennMUSHObjectType.Player);
		await Assert.That(player.Attributes.Count).IsEqualTo(1);
		
		var attr = player.Attributes[0];
		await Assert.That(attr.Name).IsEqualTo("DESCRIBE");
		await Assert.That(attr.Value).Contains("description");
	}

	[Test]
	public async ValueTask ParserHandlesMultipleObjects()
	{
		var parser = GetParser();
		var databaseContent = @"V:PennMUSH v1.8.8p0
!0
Room Zero
-1
-1
-1
-1
-1
-1
1
-1
0
TYPE_ROOM
0
0
1234567890
1234567890
!1
Player One
0
-1
-1
-1
-1
-1
1
-1
1000
TYPE_PLAYER
0
0
1234567890
1234567890
";
		
		using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(databaseContent));
		var database = await parser.ParseAsync(stream);

		await Assert.That(database).IsNotNull();
		await Assert.That(database.Objects.Count).IsEqualTo(2);
		await Assert.That(database.GetObjectsByType(PennMUSHObjectType.Room).Count()).IsEqualTo(1);
		await Assert.That(database.GetObjectsByType(PennMUSHObjectType.Player).Count()).IsEqualTo(1);
	}
}
