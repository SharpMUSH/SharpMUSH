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

	private record ExpandedDataExample(string Word);
	
	[Test, NotInParallel]
	public async Task SetAndGetExpandedData()
	{
		var obj = new ExpandedDataExample("Dog");
		var one = await _database.GetObjectNodeAsync(new DBRef(1), CancellationToken.None);
		await _database.SetExpandedObjectData(one.Object()!.Id!, "ExpandedDataExample", obj, CancellationToken.None);

		var result = await _database.GetExpandedObjectData<ExpandedDataExample>(one.Object()!.Id!, "ExpandedDataExample", CancellationToken.None);
		await Assert.That(result).IsEquivalentTo(obj);
	}

	private record OverwritePartialAndGetExpandedDataExample(string Word, string? Verb);
	
	/// <summary>
	/// This tests exists to illustrate that SetExpandedObjectData overwrites only the values set.
	/// </summary>
	[Test, NotInParallel]
	public async Task OverwritePartialAndGetExpandedData()
	{
		var one = await _database.GetObjectNodeAsync(new DBRef(1));
		await _database.SetExpandedObjectData(one.Object()!.Id!, "OverwritePartialAndGetExpandedDataExample", new OverwritePartialAndGetExpandedDataExample("Dog", "Bark"));
		await _database.SetExpandedObjectData(one.Object()!.Id!, "OverwritePartialAndGetExpandedDataExample", new OverwritePartialAndGetExpandedDataExample("Cat", null));

		var result = await _database.GetExpandedObjectData<OverwritePartialAndGetExpandedDataExample>(one.Object()!.Id!, "OverwritePartialAndGetExpandedDataExample");
		await Assert.That(result as object).IsEqualTo(new OverwritePartialAndGetExpandedDataExample("Cat", "Bark"));
	}

	private record OverwritePartialNullAndGetExpandedDataExample(string? Word, string? Verb);

	/// <summary>
	/// This tests exists to illustrate that SetExpandedObjectData overwrites values when null is explicitly given.
	/// </summary>
	[Test, NotInParallel, Skip("TODO: Failing Behavior. Needs Investigation.")]
	public async Task OverwritePartialNullAndGetExpandedData()
	{
		var one = await _database.GetObjectNodeAsync(new DBRef(1));
		await _database.SetExpandedObjectData(one.Object()!.Id!, "OverwritePartialNullAndGetExpandedDataExample", new OverwritePartialNullAndGetExpandedDataExample("Dog", "Bark"));
		await _database.SetExpandedObjectData(one.Object()!.Id!, "OverwritePartialNullAndGetExpandedDataExample", new OverwritePartialNullAndGetExpandedDataExample(null,"Bark"));

		var result = await _database.GetExpandedObjectData<OverwritePartialNullAndGetExpandedDataExample>(one.Object()!.Id!, "OverwritePartialNullAndGetExpandedDataExample");
		await Assert.That(result as object).IsEqualTo(new OverwritePartialNullAndGetExpandedDataExample(null,"Bark"));
	}

	private record OverwriteUnrelatedTypesAndGetExpandedDataExample(string? Word, string? Verb);
	
	private record OverwriteUnrelatedTypesAndGetExpandedDataExample2(string? Word, string? Verb);

	/// <summary>
	/// This tests exists to illustrate that SetExpandedObjectData safely sets unrelated Keys without wiping the other.
	/// </summary>
	[Test, NotInParallel]
	public async Task OverwriteUnrelatedTypesAndGetExpandedData()
	{
		var one = await _database.GetObjectNodeAsync(new DBRef(1));
		await _database.SetExpandedObjectData(one.Object()!.Id!, "OverwriteUnrelatedTypesAndGetExpandedDataExample", new OverwriteUnrelatedTypesAndGetExpandedDataExample("Dog", "Bark"));
		await _database.SetExpandedObjectData(one.Object()!.Id!, "OverwriteUnrelatedTypesAndGetExpandedDataExample2", new OverwriteUnrelatedTypesAndGetExpandedDataExample2("Cat", null));

		var result = await _database.GetExpandedObjectData<OverwriteUnrelatedTypesAndGetExpandedDataExample>(one.Object()!.Id!, "OverwriteUnrelatedTypesAndGetExpandedDataExample");
		await Assert.That(result as object).IsEqualTo(new OverwriteUnrelatedTypesAndGetExpandedDataExample("Dog", "Bark"));
		var result2 = await _database.GetExpandedObjectData<OverwriteUnrelatedTypesAndGetExpandedDataExample2>(one.Object()!.Id!, "OverwriteUnrelatedTypesAndGetExpandedDataExample2");
		await Assert.That(result2 as object).IsEqualTo(new OverwriteUnrelatedTypesAndGetExpandedDataExample2("Cat", null));
	}
}