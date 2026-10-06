using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// The portal's formatted input sends a styled draft as <c>decompose()</c> writes it, after <c>say</c> or
/// <c>@emit</c> (the Scene plugin's verbs take it the same way). What the speaker hears must be the draft
/// itself: its colours, and the brackets, commas, semicolons, percent signs and spaces typed as text.
/// </summary>
public class ComposedTextRoundTripTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private static MarkupText Styled(string codes, string text) => MarkupText.Wrap(AnsiCodeParser.Parse(codes), text);

	[Test]
	[Arguments("say")]
	[Arguments("@emit")]
	public async Task AStyledDraft_IsHeardAsComposed(string verb)
	{
		var marker = TestIsolationHelpers.GenerateUniqueName("Composed");
		var draft = MarkupText.Concat(
		[
			Styled("hr", "Well met"),
			MarkupText.Plain($", {marker} [OOC] 100%; a  gap"),
			MarkupText.Plain(" "),
			Styled("ug", "friend"),
			MarkupText.Plain("\n  next (line)"),
		]);
		var softcode = SoftcodeDecomposer.Decompose(draft);

		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "Composer");
		var before = WebAppFactoryArg.Notifications.RawCountFor(player.DbRef);
		await WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle)
			.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"{verb} {softcode}"));

		var heard = WebAppFactoryArg.Notifications.RawFor(player.DbRef).Skip(before)
			.Select(message => message switch { MString markup => markup, string text => MarkupText.Plain(text) })
			.Single(message => message.Text.Contains(marker, StringComparison.Ordinal));
		var start = heard.Text.IndexOf("Well met", StringComparison.Ordinal);

		await Assert.That(start).IsGreaterThanOrEqualTo(0);
		await Assert.That(SoftcodeDecomposer.Decompose(heard.Substring(start, draft.Length))).IsEqualTo(softcode)
			.Because($"{verb} must hand back the draft as it was composed, colours and literal text alike");
	}
}
