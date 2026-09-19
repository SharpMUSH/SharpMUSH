using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// <c>list()</c> as PennMUSH's <c>fun_list</c> (<c>src/funmisc.c:1288-1327</c>): the option may be any
/// prefix of its name except the motds, which are exact; a type other than builtin/local/all, an empty
/// option or an unknown one is <c>#-1</c>; names come back upper-case; and flags and powers are listed
/// <c>NAME (c), NAME</c> with what the caller may not see left out (<c>src/flags.c list_all_flags</c>).
/// </summary>
public class ListFunctionParityTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private async Task<string> ListAsync(string arguments) =>
		(await Parser.FunctionParse(MarkupText.Plain($"list({arguments})")))!.Message!.ToPlainText();

	[Test]
	[Arguments("f")]
	[Arguments("func")]
	[Arguments("FUNCTIONS")]
	public async Task Functions_AnswersToAnyPrefix(string option)
		=> await Assert.That((await ListAsync(option)).Split(' ')).Contains("ADD");

	[Test]
	[Arguments("c", "@EMIT")]
	[Arguments("l", "BASIC")]
	public async Task OtherOptions_AnswerToAPrefixToo(string option, string expected)
		=> await Assert.That((await ListAsync(option)).Split(' ')).Contains(expected);

	[Test]
	public async Task AtFunctions_IsTheLocalList()
		=> await Assert.That((await ListAsync("@functions")).Split(' ')).DoesNotContain("ADD");

	[Test]
	[Arguments("functions,local")]
	[Arguments("functions,LOCAL")]
	public async Task Type_Local_ExcludesBuiltins(string arguments)
		=> await Assert.That((await ListAsync(arguments)).Split(' ')).DoesNotContain("ADD");

	[Test]
	[Arguments("functions,bogus")]
	[Arguments("motd,bogus")]
	[Arguments("bogus")]
	[Arguments("mot")]
	public async Task AnythingElse_IsMinusOne(string arguments)
		=> await Assert.That(await ListAsync(arguments)).IsEqualTo("#-1");

	[Test]
	public async Task Attribs_AreTheTableNamesUpperCased()
	{
		var name = TestIsolationHelpers.GenerateUniqueName("LISTATTR").ToUpperInvariant();
		await WebAppFactoryArg.CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@attribute/access {name}="));

		await Assert.That((await ListAsync("attribs")).Split(' ')).Contains(name);
	}

	[Test]
	public async Task Flags_AreNamesWithTheirLetters()
	{
		var flags = await ListAsync("flags");

		await Assert.That(flags.Split(", ")).Contains("WIZARD (W)");
	}

	/// <summary>
	/// Visibility, so it runs as a mortal: God sees every flag and would pass whatever the filter did.
	/// </summary>
	[Test]
	public async Task Flags_HideMortalDarkFlagsFromMortals()
	{
		var hidden = TestIsolationHelpers.GenerateUniqueName("LISTMDARK").ToUpperInvariant();
		await Mediator.Send(new CreateObjectFlagCommand(hidden, null, string.Empty, false, ["wizard", "mdark"], ["wizard"], ["PLAYER", "THING", "ROOM", "EXIT"]));
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "ListMortal");

		await WebAppFactoryArg.CommandParser.CommandParse(mortal.Handle, ConnectionService,
			MarkupText.Plain($"think [list(flags)] {hidden}-end"));

		var said = NotifyService.ReceivedCalls()
			.Where(call => call.GetMethodInfo().Name == "Notify" && call.GetArguments()[1] is SharpMessage)
			.Where(call => call.GetArguments()[0] is AnySharpObject who && who.Object().DBRef == mortal.DbRef)
			.Select(call => (SharpMessage)call.GetArguments()[1]! switch { MString m => m.ToPlainText(), string t => t })
			.Single(text => text.EndsWith($"{hidden}-end"));

		await Assert.That(said).Contains("WIZARD (W)");
		await Assert.That(said.Split(", ").Any(entry => entry.StartsWith(hidden))).IsFalse();
		await Assert.That((await ListAsync("flags")).Split(", ")).Contains(hidden);
	}
}
