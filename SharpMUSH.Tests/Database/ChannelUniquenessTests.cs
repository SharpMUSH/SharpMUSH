using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Tests.Database;

/// <summary>
/// Channel names are a global namespace, and <c>CreateChannelAsync</c> — not the read-before-create in
/// <c>ChannelAdd</c> — is what enforces it.
///
/// <para>Each provider reaches that guarantee differently, because each has a different primitive:
/// ArangoDB decides the name inside the exclusive transaction it already opens, Memgraph relies on a
/// <c>:Channel(name)</c> uniqueness constraint because snapshot isolation lets both writers observe
/// "absent", and SurrealDB relies on the record ID, which is the channel name.</para>
///
/// <para>Losing the race used to mean a duplicate channel on ArangoDB and Memgraph and — worse — a silent
/// overwrite on SurrealDB, where <c>UPSERT</c> reset <c>privs</c> and all five locks and reported success
/// to both callers. These tests run against whichever provider
/// <c>SHARPMUSH_DATABASE_PROVIDER</c> selects, so the matrix covers all three.</para>
/// </summary>
public class ChannelUniquenessTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private ISharpDatabase Database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private static string UniqueChannel(string prefix)
		=> TestIsolationHelpers.GenerateUniqueName(prefix).Replace("_", string.Empty);

	private async Task<SharpPlayer> God()
		=> (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).AsPlayer;

	private async Task<ChannelCreationResult> Create(string name, params string[] privs)
		=> await Mediator.Send(new CreateChannelCommand(MModule.single(name), privs, await God()));

	/// <summary>
	/// Counts straight through <see cref="ISharpDatabase"/> rather than <c>GetChannelListQuery</c>, so a
	/// cached channel list cannot hide a duplicate that is really in storage.
	/// </summary>
	private async Task<int> CountNamed(string name)
		=> await Database.GetAllChannelsAsync().CountAsync(c => c.Name.ToPlainText() == name);

	[Test]
	public async Task SecondCreateOfTheSameNameIsRefusedAndLeavesOneChannel()
	{
		var name = UniqueChannel("UniqSeq");

		var first = await Create(name, "Player");
		var second = await Create(name, "Player");

		await Assert.That(first.IsSuccess).IsTrue();
		await Assert.That(second.IsNameTaken).IsTrue();
		await Assert.That(await CountNamed(name)).IsEqualTo(1);
	}

	[Test]
	public async Task RefusedCreateDoesNotResetPrivilegesOrLocks()
	{
		var name = UniqueChannel("UniqClobber");

		await Assert.That((await Create(name, "Player", "Wizard")).IsSuccess).IsTrue();

		var channel = (await Mediator.Send(new GetChannelQuery(name)))!;
		await Mediator.Send(new UpdateChannelCommand(channel, null, null, null,
			JoinLock: "#1", SpeakLock: "#1", SeeLock: null, HideLock: null, ModLock: null,
			Mogrifier: null, Buffer: null));

		// The privileges the second caller asks for are deliberately weaker. Under the old UPSERT this
		// call took the channel over: privs became ["Player"] and every lock went back to "".
		var loser = await Create(name, "Player");

		await Assert.That(loser.IsNameTaken).IsTrue();

		var survivor = (await Mediator.Send(new GetChannelQuery(name)))!;
		await Assert.That(survivor.Privs).Contains("Wizard");
		await Assert.That(survivor.JoinLock).IsEqualTo("#1");
		await Assert.That(survivor.SpeakLock).IsEqualTo("#1");
		await Assert.That(await CountNamed(name)).IsEqualTo(1);
	}

	[Test]
	public async Task ConcurrentCreatesOfTheSameNameProduceExactlyOneChannel()
	{
		const int racers = 8;
		var name = UniqueChannel("UniqRace");

		var results = await Task.WhenAll(
			Enumerable.Range(0, racers).Select(_ => Task.Run(async () => await Create(name, "Player"))));

		var winners = results.Count(r => r.IsSuccess);
		var refused = results.Count(r => r.IsNameTaken);
		var failed = results.Where(r => r.IsError).Select(r => r.AsError).ToArray();

		await Assert.That(failed).IsEmpty();
		await Assert.That(winners).IsEqualTo(1);
		await Assert.That(refused).IsEqualTo(racers - 1);
		await Assert.That(await CountNamed(name)).IsEqualTo(1);
	}
}
