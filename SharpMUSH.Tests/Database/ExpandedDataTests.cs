using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Tests.Database;

public class ExpandedDataTests : BaseUnitTest
{
	private static Infrastructure? _server;
	private static ISharpDatabase? _database;
	
	[Before(Class)]
	public static async Task OneTimeSetup()
	{
		(_database, _server) = await IntegrationServer();
	}
	
	[After(Class)]
	public static async Task OneTimeTearDown()
	{
		_server!.Dispose();
		await Task.CompletedTask;
	}
	
	
	[Test]
	public async Task SetAndGetExpandedData()
	{
		var one = await _database!.GetObjectNodeAsync(new DBRef(1));
		await _database.SetExpandedObjectData(one.Object()!.Id!, "Test", new { Word = "Dog" });

		var result = await _database.GetExpandedObjectData(one.Object()!.Id!, "Test");
		await Assert.That(result).IsEqualTo("{\"Word\":\"Dog\"}");
	}
}