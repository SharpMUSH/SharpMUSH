using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Case in <c>$</c>-command patterns follows the attribute's <c>CASE</c> flag, for wildcards and
/// regexps alike: PennMUSH's <c>atr_single_match_r</c> passes <c>AF_Case(ptr)</c> to both
/// <c>regexp_match_case_r</c> and <c>wild_match_case_r</c> (<c>src/attrib.c:1813-1821</c>), so a pattern
/// is caseless unless <c>CASE</c> is set. <c>^</c>-listen patterns already do this
/// (<c>ListenAttributeSearch</c>).
/// </summary>
[NotInParallel]
public class CommandPatternCaseTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.Services.GetRequiredService<IMUSHCodeParser>();

	[Test]
	public async Task RegexpPattern_WithoutCase_MatchesWhateverCaseIsTyped()
	{
		var (obj, token) = await CommandAsync("CmdCaseRx", "$^{0} go$", "regexp");

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"{token.ToUpperInvariant()} GO"));

		await AssertEmitted(obj, token, times: 1);
	}

	[Test]
	public async Task RegexpPattern_WithCase_MatchesOnlyTheCaseItWasWritten()
	{
		var (obj, token) = await CommandAsync("CmdCaseRxC", "$^{0} go$", "regexp", "case");

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"{token} GO"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"{token} go"));

		await AssertEmitted(obj, token, times: 1);
	}

	[Test]
	public async Task WildcardPattern_WithCase_MatchesOnlyTheCaseItWasWritten()
	{
		var (obj, token) = await CommandAsync("CmdCaseWild", "${0} go *", "case");

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"{token} GO now"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"{token} go now"));

		await AssertEmitted(obj, token, times: 1);
	}

	/// <summary>A thing carrying one $-command whose action emits the token, with the given attribute flags.</summary>
	private async Task<(DBRef Obj, string Token)> CommandAsync(string name, string patternFormat, params string[] flags)
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, name);
		var token = TestIsolationHelpers.GenerateUniqueName("cc").ToLowerInvariant();
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&CMD {obj}={string.Format(patternFormat, token)}:@emit {token} fired"));
		foreach (var flag in flags)
			await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {obj}/CMD={flag}"));
		return (obj, token);
	}

	private async Task AssertEmitted(DBRef obj, string token, int times)
		=> await NotifyService
			.Received(times)
			.Notify(TestHelpers.MatchingObject(WebAppFactoryArg.ExecutorDBRef),
				Arg.Is<SharpMessage>(s => TestHelpers.MessagePlainTextEquals(s, $"{token} fired")),
				TestHelpers.MatchingObject(obj), INotifyService.NotificationType.Emit);
}
