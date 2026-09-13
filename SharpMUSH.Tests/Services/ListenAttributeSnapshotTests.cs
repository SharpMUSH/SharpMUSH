using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

public class ListenAttributeSnapshotTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }
	private IMediator Mediator => Factory.Services.GetRequiredService<IMediator>();
	private IConnectionService Connections => Factory.Services.GetRequiredService<IConnectionService>();
	private async Task<AnySharpObject> Thing()
	{
		var reference = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "ListenSnapshot");
		return (await Mediator.Send(new GetObjectNodeQuery(reference))).Expect<AnySharpObject>();
	}

	[Test]
	public async Task CompleteLocalSnapshotRetainsEmptyPlainFlagsAndDescendants()
	{
		var thing = await Thing();
		var reference = thing.Object().DBRef;
		var attributes = Factory.Services.GetRequiredService<IAttributeService>();
		foreach (var (name, value) in new[] { ("EMPTY", ""), ("PLAIN", "mask"), ("TREE", "root"), ("TREE`ACTION", "^hello *:action") })
			await attributes.SetAttributeAsync(thing, thing, name, MarkupText.Plain(value));
		await Factory.CommandParser.CommandParse(1, Connections, MarkupText.Plain($"@set {reference}/TREE=NO_COMMAND"));
		await Factory.CommandParser.CommandParse(1, Connections, MarkupText.Plain($"@set {reference}/PLAIN=NO_INHERIT"));
		var snapshot = await Mediator.Send(new GetListenAttributeSnapshotQuery(reference));
		await Assert.That(snapshot.Select(attribute => attribute.LongName)).IsEquivalentTo(["EMPTY", "PLAIN", "TREE", "TREE`ACTION"]);
		await Assert.That(snapshot.Single(attribute => attribute.LongName == "EMPTY").Value.Length).IsEqualTo(0);
		await Assert.That(snapshot.Single(attribute => attribute.LongName == "PLAIN").Value.ToPlainText()).IsEqualTo("mask");
		await Assert.That(snapshot.Single(attribute => attribute.LongName == "PLAIN").IsNoInherit()).IsTrue();
		await Assert.That(snapshot.Single(attribute => attribute.LongName == "TREE").Flags.Any(flag => flag.Name.Equals("no_command", StringComparison.OrdinalIgnoreCase))).IsTrue();
		await Assert.That(snapshot.Single(attribute => attribute.LongName == "TREE`ACTION").Value.ToPlainText()).IsEqualTo("^hello *:action");
	}

	[Test]
	public async Task StaleStampedIdentityCannotReadCurrentObjectsAttributes()
	{
		var thing = await Thing();
		var reference = thing.Object().DBRef;
		await Factory.Services.GetRequiredService<IAttributeService>().SetAttributeAsync(thing, thing, "ACTION", MarkupText.Plain("^hello:current"));
		await Assert.That((await Mediator.Send(new GetListenAttributeSnapshotQuery(reference))).Length).IsEqualTo(1);
		var stale = new DBRef(reference.Number, reference.CreationMilliseconds!.Value - 1);
		await Assert.That(await Mediator.Send(new GetListenAttributeSnapshotQuery(stale))).IsEmpty();
	}

	[Test]
	public async Task MissingObjectHasNoAttributeSnapshot()
		=> await Assert.That(await Mediator.Send(new GetListenAttributeSnapshotQuery(new DBRef(int.MaxValue)))).IsEmpty();
}
