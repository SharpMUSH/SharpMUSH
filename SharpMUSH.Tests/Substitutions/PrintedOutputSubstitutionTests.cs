using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Requests;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Substitutions;

/// <summary>
/// TinyMUX's command piping: a command followed by <c>;|</c> has what it tells its executor taken
/// instead of shown, and the next command reads it as <c>%|</c>. Without <c>;|</c>, <c>%|</c> is empty.
/// </summary>
public class PrintedOutputSubstitutionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	private IConnectionService Connections => Factory.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => Factory.Services.GetRequiredService<IMediator>();
	private TestIsolationHelpers.TestPlayer _actor = null!;
	private DBRef _room;

	[Before(Test)]
	public async Task CreateActor()
	{
		_room = DBRef.Parse((await Factory.CommandParser.CommandParse(1, Connections, MarkupText.Plain($"@dig {Guid.NewGuid():N}")))
			.Message!.ToPlainText().Trim());
		_actor = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, Mediator, Connections, "PipePrint", _room);
	}

	[After(Test)]
	public async Task DisconnectActor() => await Connections.Disconnect(_actor.Handle);

	/// <summary>Runs <paramref name="list"/> as the actor's own queue entry, and what the actor heard.</summary>
	private async Task<List<string>> Queued(string list)
	{
		var before = Factory.Notifications.CountFor(_actor.DbRef);
		await Mediator.Send(new AdmitCommandListRequest(MarkupText.Plain(list),
			Factory.CommandParserFor(_actor.DbRef, _actor.Handle).CurrentState,
			new DbRefAttribute(_actor.DbRef, ["PIPEPRINT"]), -1));
		await Factory.Services.GetRequiredService<ITaskScheduler>().SettleForTestsAsync();
		return Factory.Notifications.For(_actor.DbRef).Skip(before).ToList();
	}

	private static string Unique(string prefix) => TestIsolationHelpers.GenerateUniqueName(prefix);

	[Test]
	public async Task PipedOutputIsPassedOnInsteadOfShown()
	{
		var marker = Unique("Piped");
		var heard = await Queued($"think {marker} ;| @pemit me=got=%|");
		await Assert.That(heard).Contains($"got={marker}");
		await Assert.That(heard).DoesNotContain(marker);
	}

	[Test]
	public async Task EveryLineIsKept()
	{
		var marker = Unique("Lines");
		var heard = await Queued($"@dolist/inline a b=think {marker}##;| @pemit me=got=[edit(%|,%r,+)]");
		await Assert.That(heard).Contains($"got={marker}a+{marker}b");
	}

	[Test]
	public async Task PipesChain()
	{
		var marker = Unique("Chain");
		var heard = await Queued($"think {marker} ;| think %|-two ;| @pemit me=got=%|");
		await Assert.That(heard).Contains($"got={marker}-two");
	}

	[Test]
	public async Task OnlyTheCommandAfterThePipeReadsIt()
	{
		var marker = Unique("Next");
		var heard = await Queued($"think piped ;| think {marker}=%|;@pemit me=after=[strlen(%|)]");
		await Assert.That(heard).Contains($"{marker}=piped");
		await Assert.That(heard).Contains("after=0");
	}

	[Test]
	public async Task ASpaceAfterTheSemicolonIsNotAPipe()
	{
		var marker = Unique("Spaced");
		var heard = await Queued($"think {marker}; |think x;@pemit me=after=[strlen(%|)]");
		await Assert.That(heard).Contains(marker);
		await Assert.That(heard).Contains("after=0");
	}

	[Test]
	public async Task WithoutAPipeItIsEmptyAndOutputIsShown()
	{
		var marker = Unique("Plain");
		var heard = await Queued($"think {marker};@pemit me=plain=[strlen(%|)]");
		await Assert.That(heard).Contains(marker);
		await Assert.That(heard).Contains("plain=0");
	}

	[Test]
	public async Task OutputToOthersIsNotTaken()
	{
		var other = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, Mediator, Connections, "PipeOther", _room);
		try
		{
			var marker = Unique("Other");
			var before = Factory.Notifications.CountFor(other.DbRef);
			var heard = await Queued($"@pemit/silent {other.DbRef}={marker} ;| @pemit me=got=[strlen(%|)]");
			await Assert.That(heard).Contains("got=0");
			await Assert.That(Factory.Notifications.For(other.DbRef).Skip(before)).Contains(marker);
		}
		finally
		{
			await Connections.Disconnect(other.Handle);
		}
	}

	[Test]
	public async Task HelpExamplesGiveWhatTheySay()
	{
		await Assert.That(await Queued("think [ansi(hr,Hello)] there ;| @pemit me=Got [words(%|)] words: %|"))
			.Contains("Got 2 words: Hello there");
		await Assert.That(await Queued("@dolist/inline red green=think ##;| think Colors: [edit(%|,%r,%b-%b)]"))
			.Contains("Colors: red - green");
	}

	[Test]
	public async Task MarkupIsKept()
	{
		var heard = await Queued("think [ansi(r,red)] ;| @pemit me=same=[eq(comp(%|,[ansi(r,red)]),0)] plain=[stripansi(%|)] keeps=[neq(strlen(%|),strlen(decompose(%|)))]");
		await Assert.That(heard).Contains("same=1 plain=red keeps=1");
	}

	[Test]
	public async Task QueuedEntryGetsACopy()
	{
		var marker = Unique("Waited");
		var before = Factory.Notifications.CountFor(_actor.DbRef);
		await Queued($"think piped ;| @wait 0=@pemit me={marker}=%|");
		await Factory.Notifications.WaitForAsync(_actor.DbRef, marker, startIndex: before);
		await Assert.That(Factory.Notifications.For(_actor.DbRef).Skip(before)).Contains($"{marker}=piped");
	}
}
