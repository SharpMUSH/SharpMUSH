using SharpMUSH.Implementation.Handlers.Database;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Tests.Database;

public class AttributeOwnerPersistenceTests
{
	[Test]
	public async Task OwnerChangePreservesAncestorsValuesAndFlags()
	{
		await using var world = await DefinitionCreationContract.Open();
		var db = world.Database;
		var original = (await db.GetObjectNodeAsync(new DBRef(1))).Expect<SharpPlayer>();
		var playerRef = await db.CreatePlayerAsync("NewOwner", "password", new DBRef(0), new DBRef(0), 100);
		var newOwner = (await db.GetObjectNodeAsync(playerRef)).Expect<SharpPlayer>();
		var target = original.Object.DBRef;
		await db.SetAttributeAsync(target, ["OWNERROOT"], MarkupText.Plain("root value"), original);
		await db.SetAttributeAsync(target, ["OWNERROOT", "LEAF"], MarkupText.Plain("leaf value"), original);
		var before = await db.GetAttributeAsync(target, ["OWNERROOT", "LEAF"]).ToArrayAsync();
		await db.CreateOrUpdateAttributeEntryAsync("OWNERROOT`LEAF", ["visual"]);
		var handler = new SetAttributeOwnerCommandHandler(db);
		await Assert.That(await handler.Handle(new SetAttributeOwnerCommand(target, ["ownerroot", "leaf"], newOwner), CancellationToken.None)).IsTrue();
		var after = await db.GetAttributeAsync(target, ["OWNERROOT", "LEAF"]).ToArrayAsync();
		await Assert.That((await after[0].Owner.WithCancellation(CancellationToken.None))?.Object.DBRef).IsEqualTo(original.Object.DBRef);
		await Assert.That((await after[1].Owner.WithCancellation(CancellationToken.None))?.Object.DBRef).IsEqualTo(newOwner.Object.DBRef);
		for (var i = 0; i < before.Length; i++)
		{
			await Assert.That(after[i].Value.ToPlainText()).IsEqualTo(before[i].Value.ToPlainText());
			await Assert.That(after[i].Flags.Select(flag => flag.Name)).IsEquivalentTo(before[i].Flags.Select(flag => flag.Name));
		}
		await Assert.That(await handler.Handle(new SetAttributeOwnerCommand(target, ["OWNERROOT", "MISSING"], newOwner), CancellationToken.None)).IsFalse();
		await Assert.That(await db.GetAttributeAsync(target, ["OWNERROOT", "MISSING"]).AnyAsync()).IsFalse();
	}
}
