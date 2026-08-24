using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Wires <c>SoftcodeFormatter.Format</c> (Task 5) into <c>@examine</c>'s attribute loop: an attribute
/// carrying <c>cmdsyntax</c>/<c>funsyntax</c> renders as a highlighted header followed by a formatted,
/// wrapped code block, while an unflagged attribute keeps today's exact single-line rendering.
/// Fixture pattern copied from <see cref="ExamineNullOwnerTests"/>.
/// </summary>
public class ExamineSyntaxFormattingTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	// Comfortably longer than the 78-column fallback width, so it must break.
	private const string LongCode =
		"switch(words(%0),0,you said absolutely nothing at all,1,you said just one word,many words indeed here)";

	private ValueTask Expect(string fragment) => NotifyService.Received().Notify(
		Arg.Any<AnySharpObject>(),
		Arg.Is<OneOf<MString, string>>(m => TestHelpers.MessageContains(m, fragment)),
		Arg.Any<AnySharpObject?>(),
		Arg.Any<INotifyService.NotificationType>());

	[Test]
	public async ValueTask FlaggedAttribute_IsBrokenAcrossLines()
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "ExamFmtOn");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"&LONGFN {obj}={LongCode}"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {obj}/LONGFN=funsyntax"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"examine {obj}/LONGFN"));

		// A newline followed by indentation is the formatter's signature.
		await Expect("\n  ");
	}

	[Test]
	public async ValueTask UnflaggedAttribute_RendersVerbatim()
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "ExamFmtOff");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"&LONGFN {obj}={LongCode}"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"examine {obj}/LONGFN"));

		await Expect(LongCode);
	}

	[Test]
	public async ValueTask FlaggedAttribute_LosesNoCharacters()
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "ExamFmtIntact");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"&LONGFN {obj}={LongCode}"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {obj}/LONGFN=funsyntax"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"examine {obj}/LONGFN"));

		// Whitespace moves; nothing else may.
		await Expect("many words indeed here)");
	}
}
