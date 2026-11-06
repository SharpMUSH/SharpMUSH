using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.DatabaseConversion;
using SharpMUSH.Library.Services.DatabaseConversion.Rhost;
using SharpMUSH.Library.Services.DatabaseConversion.TinyMux;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// TinyMUX and RhostMUSH keep a failed lock's messages in attributes of their own (DFAIL, GFAIL, ...). The
/// import names each as the attribute SharpMUSH's failed lock reads (<see cref="LockMessages"/>), so the
/// message still shows; TOFAIL has no teleport-out lock to read it and keeps its name. RhostMUSH never uses
/// its PFAIL (attribute 78), and the import skips it.
/// </summary>
public class ImportedLockFailureAttributeTests
{
	public static IEnumerable<(int Mux, int? Rhost, LockType Lock, int Which)> Renames()
	{
		yield return (135, 165, LockType.Drop, 0);
		yield return (136, 166, LockType.Drop, 1);
		yield return (137, 167, LockType.Drop, 2);
		yield return (129, 159, LockType.Give, 0);
		yield return (130, 160, LockType.Give, 1);
		yield return (131, 161, LockType.Give, 2);
		yield return (132, 162, LockType.Receive, 0);
		yield return (133, 163, LockType.Receive, 1);
		yield return (134, 164, LockType.Receive, 2);
		yield return (138, 168, LockType.Teleport, 0);
		yield return (139, 169, LockType.Teleport, 1);
		yield return (140, 170, LockType.Teleport, 2);
		yield return (128, RhostNameTables.InlineStart + 2, LockType.Mail, 0);
		yield return (78, null, LockType.Page, 0);
	}

	[Test]
	[MethodDataSource(nameof(Renames))]
	public async Task EachMessageGetsTheNameItsLockReads(int mux, int? rhost, LockType lockType, int which)
	{
		var (what, owhat, awhat) = LockMessages.FailureAttributes(lockType);
		var expected = which switch { 0 => what, 1 => owhat, _ => awhat };

		await Assert.That(TinyMuxNames.Attributes[mux]).IsEqualTo(expected);
		if (rhost is int rhostNumber)
		{
			await Assert.That(RhostNameTables.RenamedAttributes[rhostNumber]).IsEqualTo(expected);
		}
	}

	[Test]
	public async Task RhostsUnusedPageFailureIsSkipped()
		=> await Assert.That(RhostNameTables.SkippedAttributes.ContainsKey(78)).IsTrue();

	[Test]
	public async Task TheTeleportOutMessagesKeepTheirNames()
	{
		await Assert.That(TinyMuxNames.Attributes[141]).IsEqualTo("TOFAIL");
		await Assert.That(RhostNameTables.RenamedAttributes.ContainsKey(171)).IsFalse();
	}

	/// <summary>A lock key or softcode naming DFAIL finds the renamed attribute too.</summary>
	[Test]
	public async Task ALockKeyNamingTheOldNameFindsTheNewOne()
		=> await Assert.That(TinyMuxNames.AttributeName("dfail")).IsEqualTo("DROP_LOCK`FAILURE");

	/// <summary>The renamed message is a leaf whose branch the source never had; the import creates the branch.</summary>
	[Test]
	public async Task ALeafWithoutItsBranchArrives()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();
		var result = await world.Converter.ConvertDatabaseAsync(new PennMUSHDatabase
		{
			Version = "Lock Failure Fixture",
			Objects =
			[
				new PennMUSHObject { DBRef = 0, Name = "Room Zero", Type = PennMUSHObjectType.Room, Owner = 1 },
				new PennMUSHObject { DBRef = 1, Name = "One", Type = PennMUSHObjectType.Player, Owner = 1, Location = 0, Link = 0, Flags = ["WIZARD"] },
				new PennMUSHObject { DBRef = 2, Name = "Master Room", Type = PennMUSHObjectType.Room, Owner = 1 },
				new PennMUSHObject
				{
					DBRef = 3, Name = "Sticky Widget", Type = PennMUSHObjectType.Thing, Owner = 1, Location = 0, Link = 0,
					Attributes = [new PennMUSHAttribute { Name = "DROP_LOCK`FAILURE", Owner = 1, Value = "It will not let go.", Flags = [] }]
				}
			]
		});
		await Assert.That(result.Errors).IsEmpty();

		var key = (await world.Database.GetAllObjectsAsync().SingleAsync(o => o.Name == "Sticky Widget")).Key;
		var widget = (await world.Mediator.Send(new GetObjectNodeQuery(new DBRef(key)))).Expect<AnySharpObject>();
		var read = await world.Mediator.CreateStream(new GetAttributeQuery(widget.Object().DBRef, ["DROP_LOCK", "FAILURE"])).ToArrayAsync();
		await Assert.That(read[^1].Value.ToPlainText()).IsEqualTo("It will not let go.");
	}
}
