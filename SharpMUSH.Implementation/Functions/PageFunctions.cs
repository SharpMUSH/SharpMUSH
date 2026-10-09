using SharpMUSH.Implementation.Common;
using SharpMUSH.Implementation.Commands.PageCommand;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Functions;

/// <summary>
/// The page log read from softcode (a SharpMUSH extension; PennMUSH keeps no page log): the same reads as
/// <c>page/recall</c> and <c>page/conversations</c>, of the executor's own log only. Neither writes,
/// notifies or triggers anything.
/// </summary>
public partial class Functions
{
	/// <summary>
	/// <c>pagerecall(&lt;players&gt;[, &lt;lines&gt;[, &lt;delimiter&gt;]])</c>: the lines <c>page/recall</c>
	/// shows, without the frame or timestamps, joined by <c>%r</c> unless told otherwise. An empty player
	/// list is every conversation.
	/// </summary>
	[SharpFunction(Name = "pagerecall", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular,
		ParameterNames = ["players", "lines", "delimiter"])]
	public async ValueTask<CallState> PageRecallFunction(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!Configuration.CurrentValue.Chat.PageLog)
		{
			return new CallState(ErrorMessages.Returns.PageLoggingIsOff);
		}

		var arguments = parser.CurrentState.Arguments;
		var count = ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 1, string.Empty).ToPlainText();
		if (!PageRecall.TryParseLines(count, out var lines))
		{
			return new CallState(ErrorMessages.Returns.Integer);
		}

		var delimiter = ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 2, MarkupText.NewLine);
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		return await PageRecall.MatchAsync(Mediator, ConnectionService, executor.Object().DBRef, arguments["0"].Message.ToPlainText()) switch
		{
			MatchedPartners matched => await RecalledPagesAsync(executor.Object().DBRef, matched.Partners, lines, delimiter),
			UnmatchedPartner { Ambiguous: true } => new CallState(ErrorMessages.Returns.AmbiguousMatch),
			UnmatchedPartner => new CallState(ErrorMessages.Returns.NoSuchPlayer)
		};
	}

	private async ValueTask<CallState> RecalledPagesAsync(DBRef viewer, PagePartner[] partners, int lines,
		MString delimiter)
	{
		var pages = await PageRecall.ReadAsync(Mediator, viewer, partners, lines);
		return new CallState(MarkupText.Join(delimiter, pages.Select(page => PageRecall.Line(page, viewer))));
	}

	/// <summary>
	/// <c>pageconversations([&lt;delimiter&gt;])</c>: the executor's logged conversations, the latest first,
	/// each as the objids of the others in it (space-separated, so each recalls with <c>pagerecall()</c>),
	/// separated by <c>|</c> unless told otherwise.
	/// </summary>
	[SharpFunction(Name = "pageconversations", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular,
		ParameterNames = ["delimiter"])]
	public async ValueTask<CallState> PageConversationsFunction(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!Configuration.CurrentValue.Chat.PageLog)
		{
			return new CallState(ErrorMessages.Returns.PageLoggingIsOff);
		}

		// pageconversations() arrives with one empty argument, which is no delimiter given.
		var delimiter = parser.CurrentState.Arguments.TryGetValue("0", out var delimiterArgument)
			&& delimiterArgument.Message.ToPlainText() is { Length: > 0 } given
				? given
				: "|";
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var conversations = await Mediator.Send(
			new GetPageConversationsQuery(executor.Object().DBRef, PageRecall.MaxConversations));

		return new CallState(string.Join(delimiter,
			conversations.Select(conversation => string.Join(' ', conversation.With))));
	}
}
