using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// The COMMAND and LISTEN search classes, which ask "who would respond to this text?". PennMUSH
/// (<c>src/wiz.c</c>, <c>raw_search</c>) hands the restriction to <c>atr_comm_match</c> as the text
/// to match and uses each object's stored <c>$</c>/<c>^</c> pattern and <c>@listen</c> as the
/// wildcard, so the object's pattern is the glob and the restriction is the subject. Each test pairs
/// a match with a control whose pattern refuses the same text.
/// </summary>
public class SearchCommandListenClassTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	[Test]
	public async Task Command_FindsObjectsWhoseDollarPatternAcceptsTheCommand()
	{
		var token = UniqueToken("cmd");
		var match = await CreateThingAsync($"{token}_Match");
		var control = await CreateThingAsync($"{token}_Control");
		await CommandAsync($"&CMD {match}=$+{token}*:think matched");
		await CommandAsync($"&CMD {control}=$+{token}other:think matched");

		var found = ReturnedDbRefs(await SearchAsync($"lsearch(all,type,thing,command,+{token}frob)"));

		await Assert.That(found).Contains(match.Number);
		await Assert.That(found).DoesNotContain(control.Number);
	}

	[Test]
	public async Task Command_QuestionMarkInTheStoredPatternMatchesOneCharacter()
	{
		var token = UniqueToken("cmdq");
		var match = await CreateThingAsync($"{token}_Match");
		var control = await CreateThingAsync($"{token}_Control");
		await CommandAsync($"&CMD {match}=$+{token}z?p:think matched");
		await CommandAsync($"&CMD {control}=$+{token}z?:think matched");

		var found = ReturnedDbRefs(await SearchAsync($"lsearch(all,type,thing,command,+{token}zap)"));

		await Assert.That(found).Contains(match.Number);
		await Assert.That(found).DoesNotContain(control.Number);
	}

	[Test]
	public async Task Listen_FindsObjectsWhoseListenAttributeAcceptsTheText()
	{
		var token = UniqueToken("lis");
		var match = await CreateThingAsync($"{token}_Match");
		var control = await CreateThingAsync($"{token}_Control");
		await CommandAsync($"@listen {match}=*{token}*");
		await CommandAsync($"@listen {control}={token}");

		var found = ReturnedDbRefs(await SearchAsync($"lsearch(all,type,thing,listen,well {token} there)"));

		await Assert.That(found).Contains(match.Number);
		await Assert.That(found).DoesNotContain(control.Number);
	}

	[Test]
	public async Task Listen_FindsObjectsWhoseCaretPatternAcceptsTheText()
	{
		var token = UniqueToken("car");
		var match = await CreateThingAsync($"{token}_Match");
		var control = await CreateThingAsync($"{token}_Control");
		await CommandAsync($"&HEAR {match}=^*{token}?:think heard");
		await CommandAsync($"&HEAR {control}=^{token}:think heard");

		var found = ReturnedDbRefs(await SearchAsync($"lsearch(all,type,thing,listen,oh {token}!)"));

		await Assert.That(found).Contains(match.Number);
		await Assert.That(found).DoesNotContain(control.Number);
	}

	/// <summary>Letters and hex digits only, so it is a literal inside a glob.</summary>
	private static string UniqueToken(string prefix) => prefix + Guid.NewGuid().ToString("N")[..10];

	private async Task<DBRef> CreateThingAsync(string name)
	{
		var result = await TestIsolationHelpers.CreateObjectCommandAsync(WebAppFactoryArg.CommandParser, ConnectionService, name);
		return DBRef.Parse(result.Message!.ToPlainText().Trim());
	}

	private ValueTask<CallState> CommandAsync(string command) =>
		WebAppFactoryArg.CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain(command));

	private async Task<string> SearchAsync(string expression) =>
		(await Parser.FunctionParse(MarkupText.Plain(expression)))!.Message!.ToPlainText();

	private static int[] ReturnedDbRefs(string searchResult) =>
		[.. searchResult
			.Split(' ', StringSplitOptions.RemoveEmptyEntries)
			.Select(entry => DBRef.TryParse(entry, out var parsed) ? parsed!.Value.Number : (int?)null)
			.Where(number => number.HasValue)
			.Select(number => number!.Value)];
}
