using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Tests.Database;

public class ExpandedDataTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private ISharpDatabase _database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();

	[Test, NotInParallel]
	public async Task SetAndGetExpandedData()
	{
		var one = await _database.GetObjectNodeAsync(new DBRef(1));
		await _database.SetExpandedObjectData(one.Object()!.Id!, "SetAndGetExpandedData", new { Word = "Dog" });

		var result = await _database.GetExpandedObjectData(one.Object()!.Id!, "SetAndGetExpandedData");
		await Assert.That(result).IsEqualTo("{\"Word\":\"Dog\"}");
	}

	/// <summary>
	/// This tests exists to illustrate that SetExpandedObjectData overwrites only the values set.
	/// </summary>
	[Test, NotInParallel]
	public async Task OverwritePartialAndGetExpandedData()
	{
		var one = await _database.GetObjectNodeAsync(new DBRef(1));
		await _database.SetExpandedObjectData(one.Object()!.Id!, "OverwritePartialAndGetExpandedData", new { Word = "Dog", Verb = "Bark" });
		await _database.SetExpandedObjectData(one.Object()!.Id!, "OverwritePartialAndGetExpandedData", new { Word = "Cat" });

		var result = await _database.GetExpandedObjectData(one.Object()!.Id!, "OverwritePartialAndGetExpandedData");
		await Assert.That(result).IsEqualTo("{\"Verb\":\"Bark\",\"Word\":\"Cat\"}");
	}


	/// <summary>
	/// This tests exists to illustrate that SetExpandedObjectData overwrites values when null is explicitly given.
	/// </summary>
	[Test, NotInParallel]
	public async Task OverwritePartialNullAndGetExpandedData()
	{
		var one = await _database.GetObjectNodeAsync(new DBRef(1));
		await _database.SetExpandedObjectData(one.Object()!.Id!, "OverwritePartialAndGetExpandedData", new { Word = "Dog", Verb = "Bark" });
		await _database.SetExpandedObjectData(one.Object()!.Id!, "OverwritePartialAndGetExpandedData", new { Word = (string?)null });

		var result = await _database.GetExpandedObjectData(one.Object()!.Id!, "OverwritePartialAndGetExpandedData");
		await Assert.That(result).IsEqualTo("{\"Verb\":\"Bark\",\"Word\":null}");
	}


	/// <summary>
	/// This tests exists to illustrate that SetExpandedObjectData safely sets unrelated Keys without wiping the other..
	/// </summary>
	[Test, NotInParallel]
	public async Task OverwriteUnrelatedTypesAndGetExpandedData()
	{
		var one = await _database.GetObjectNodeAsync(new DBRef(1));
		await _database.SetExpandedObjectData(one.Object()!.Id!, "OverwriteUnrelatedTypesAndGetExpandedData", new { Word = "Dog", Verb = "Bark" });
		await _database.SetExpandedObjectData(one.Object()!.Id!, "OverwriteUnrelatedTypesAndGetExpandedData2", new { Word = "Cat" });

		var result = await _database.GetExpandedObjectData(one.Object()!.Id!, "OverwriteUnrelatedTypesAndGetExpandedData");
		await Assert.That(result).IsEqualTo("{\"Word\":\"Dog\",\"Verb\":\"Bark\"}");
		var result2 = await _database.GetExpandedObjectData(one.Object()!.Id!, "OverwriteUnrelatedTypesAndGetExpandedData2");
		await Assert.That(result2).IsEqualTo("{\"Word\":\"Cat\"}");
	}
}