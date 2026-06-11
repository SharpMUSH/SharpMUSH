using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Implementation.Commands.WikiCommand;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	/// <summary>
	/// @WIKI — the in-game face of the shared wiki. Pages live in the same store
	/// the web portal serves, so edits made here appear on the website immediately
	/// (and vice versa). Page targets accept a namespace prefix: "Help:Markdown Guide".
	/// </summary>
	[SharpCommand(Name = "@WIKI",
		Switches =
		[
			"VIEW", "LIST", "SEARCH", "RECENT", "HISTORY", "CREATE", "EDIT", "APPEND", "ROLLBACK",
			"DELETE", "PROTECT", "UNPROTECT", "CATEGORY", "TAG", "PUBLISH", "UNPUBLISH", "NOEVAL"
		],
		Behavior = CB.Default | CB.EqSplit | CB.NoParse, MinArgs = 0, MaxArgs = 2,
		ParameterNames = ["page", "content"])]
	public static async ValueTask<Option<CallState>> Wiki(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		parser.CurrentState.Arguments.TryGetValue("0", out var arg0CallState);
		parser.CurrentState.Arguments.TryGetValue("1", out var arg1CallState);
		MString? arg0, arg1;
		var switches = parser.CurrentState.Switches.ToArray();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		// NOEVAL only affects argument evaluation; everything else is the action.
		var actions = switches.Where(s => s != "NOEVAL").ToArray();
		if (actions.Length > 1)
		{
			await NotifyService!.Notify(executor, "WIKI: Too many switches passed to @wiki.");
			return new CallState(ErrorMessages.Returns.TooManySwitches);
		}

		if (!switches.Contains("NOEVAL"))
		{
			arg0 = await (arg0CallState?.ParsedMessage() ?? ValueTask.FromResult<MString?>(null));
			arg1 = await (arg1CallState?.ParsedMessage() ?? ValueTask.FromResult<MString?>(null));
		}
		else
		{
			arg0 = arg0CallState?.Message;
			arg1 = arg1CallState?.Message;
		}

		var wikiService = parser.ServiceProvider.GetRequiredService<IWikiService>();
		var hasArg0 = (arg0?.Length ?? 0) != 0;
		var hasArg1 = (arg1?.Length ?? 0) != 0;
		var action = actions.Length == 1 ? actions[0] : (hasArg0 ? "VIEW" : "RECENT");

		var response = action switch
		{
			"LIST" when !hasArg1
				=> await ListWiki.List(parser, Mediator!, wikiService, NotifyService!, arg0),
			"SEARCH" when hasArg0 && !hasArg1
				=> await ListWiki.Search(parser, Mediator!, wikiService, NotifyService!, arg0!),
			"RECENT" when !hasArg1
				=> await ListWiki.Recent(parser, Mediator!, wikiService, NotifyService!, arg0),
			"HISTORY" when hasArg0 && !hasArg1
				=> await ViewWiki.History(parser, Mediator!, wikiService, NotifyService!, arg0!),
			"CREATE" when hasArg0 && hasArg1
				=> await EditWiki.Create(parser, Mediator!, wikiService, NotifyService!, arg0!, arg1!),
			"EDIT" when hasArg0 && hasArg1
				=> await EditWiki.Edit(parser, Mediator!, wikiService, NotifyService!, arg0!, arg1!, append: false),
			"APPEND" when hasArg0 && hasArg1
				=> await EditWiki.Edit(parser, Mediator!, wikiService, NotifyService!, arg0!, arg1!, append: true),
			"ROLLBACK" when hasArg0 && hasArg1
				=> await EditWiki.Rollback(parser, Mediator!, wikiService, NotifyService!, arg0!, arg1!),
			"DELETE" when hasArg0 && !hasArg1
				=> await ManageWiki.Handle(parser, Mediator!, wikiService, NotifyService!, arg0!, arg1, ManageWiki.Operation.Delete),
			"PROTECT" when hasArg0 && !hasArg1
				=> await ManageWiki.Handle(parser, Mediator!, wikiService, NotifyService!, arg0!, arg1, ManageWiki.Operation.Protect),
			"UNPROTECT" when hasArg0 && !hasArg1
				=> await ManageWiki.Handle(parser, Mediator!, wikiService, NotifyService!, arg0!, arg1, ManageWiki.Operation.Unprotect),
			"PUBLISH" when hasArg0 && !hasArg1
				=> await ManageWiki.Handle(parser, Mediator!, wikiService, NotifyService!, arg0!, arg1, ManageWiki.Operation.Publish),
			"UNPUBLISH" when hasArg0 && !hasArg1
				=> await ManageWiki.Handle(parser, Mediator!, wikiService, NotifyService!, arg0!, arg1, ManageWiki.Operation.Unpublish),
			"CATEGORY" when hasArg0
				=> await ManageWiki.Handle(parser, Mediator!, wikiService, NotifyService!, arg0!, arg1, ManageWiki.Operation.Category),
			"TAG" when hasArg0
				=> await ManageWiki.Handle(parser, Mediator!, wikiService, NotifyService!, arg0!, arg1, ManageWiki.Operation.Tag),
			"VIEW" when hasArg0 && !hasArg1
				=> await ViewWiki.Handle(parser, Mediator!, wikiService, NotifyService!, arg0!),
			_ => MModule.single(ErrorMessages.Returns.BadArgumentsToWikiCommand),
		};

		return new CallState(response);
	}
}
