using System.Runtime.CompilerServices;
using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Tests.Services;

public class AliveHelperTests
{
	private static SharpObjectFlag Flag(string name) => new()
	{
		Name = name, Symbol = "", System = false, SetPermissions = [], UnsetPermissions = [], TypeRestrictions = []
	};

	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task PlayersAndPuppetsDoNotEnumerateAttributes(bool player)
	{
		var objects = new TestObjectFactory();
		var obj = player ? objects.CreatePlayer(7, "player") : objects.CreateThing(8, "puppet");
		obj.Object().Flags = new(() => player ? AsyncEnumerable.Empty<SharpObjectFlag>() : new[] { Flag("PUPPET") }.ToAsyncEnumerable());
		obj.Object().LazyAttributes = new(() => throw new InvalidOperationException("Attributes must not be read"));
		obj.Object().LazyAllAttributes = new(() => throw new InvalidOperationException("Attribute descendants must not be read"));
		await Assert.That(await obj.IsAlive()).IsTrue();
	}

	[Test]
	public async Task AliveChecksOnlyRootNamesWithoutEvaluatingAttributeValues()
	{
		var obj = new TestObjectFactory().CreateThing(8, "audible");
		obj.Object().Flags = new(() => new[] { Flag("AUDIBLE") }.ToAsyncEnumerable());
		var attribute = new LazySharpAttribute("attribute", "attribute", "forwardlist", [], null, "forwardlist",
			new(_ => Task.FromResult(AsyncEnumerable.Empty<LazySharpAttribute>())),
			new(_ => Task.FromResult<SharpPlayer?>(null)), new(_ => Task.FromResult<SharpAttributeEntry?>(null)),
			new(_ => Task.FromException<MString>(new InvalidOperationException("Value must not be evaluated"))));
		obj.Object().LazyAttributes = new(() => new[] { attribute }.ToAsyncEnumerable());
		obj.Object().LazyAllAttributes = new(() => throw new InvalidOperationException("Descendants must not be read"));
		await Assert.That(await obj.IsAlive()).IsTrue();
	}

	[Test]
	public async Task CancellationReachesRootEnumerationWithoutProducingAnAliveResult()
	{
		var obj = new TestObjectFactory().CreateThing(8, "audible");
		obj.Object().Flags = new(() => new[] { Flag("AUDIBLE") }.ToAsyncEnumerable());
		using var cancellation = new CancellationTokenSource();
		var entered = false;
		async IAsyncEnumerable<LazySharpAttribute> Attributes([EnumeratorCancellation] CancellationToken token = default)
		{
			await Task.CompletedTask;
			entered = true;
			await Assert.That(token).IsEqualTo(cancellation.Token);
			cancellation.Cancel();
			token.ThrowIfCancellationRequested();
			yield break;
		}
		obj.Object().LazyAttributes = new(() => Attributes());
		await Assert.That(async () => await obj.IsAlive(cancellation.Token)).Throws<OperationCanceledException>();
		await Assert.That(entered).IsTrue();
	}
}
