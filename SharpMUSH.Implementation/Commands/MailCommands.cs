using SharpMUSH.Implementation.Commands.MailCommand;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using System.Collections.Immutable;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;
using System.Buffers;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	[SharpCommand(Name = "@MAIL",
		Switches =
		[
			"NOEVAL", "NOSIG", "STATS", "CSTATS", "DSTATS", "FSTATS", "DEBUG", "NUKE", "FOLDERS", "FOLDER", "UNFOLDER",
			"LIST", "READ", "UNREAD", "CLEAR", "UNCLEAR", "STATUS", "PURGE", "FILE", "TAG", "UNTAG", "FWD", "FORWARD",
			"SEND", "SILENT", "URGENT", "REVIEW", "RETRACT"
		], Behavior = CB.Default | CB.EqSplit | CB.NoParse, MinArgs = 0, MaxArgs = 2, ParameterNames = ["player", "subject"])]
	public async ValueTask<Option<CallState>> Mail(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		parser.CurrentState.Arguments.TryGetValue("0", out var arg0CallState);
		parser.CurrentState.Arguments.TryGetValue("1", out var arg1CallState);
		MString? arg0, arg1;
		var switches = parser.CurrentState.Switches.ToArray();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var caller = await parser.CurrentState.KnownCallerObject(Mediator);
		string[] sendSwitches = ["SEND", "URGENT", "NOSIG", "SILENT", "NOEVAL"];

		if (switches.Except(sendSwitches).Any() && switches.Length > 1)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.MailTooManySwitches), executor);
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

		var response = switches.AsSpan() switch
		{
			// PennMUSH declares FOLDERS (src/command.c:206-207) and no FOLDER; the singular is kept as an
			// intentional alias. Switch validation is exact-match, so both must be declared.
			[.., "FOLDERS"] or [.., "FOLDER"] when executor.IsPlayer => await FolderMail.Handle(parser, ObjectDataService,
				Mediator, NotifyService, arg0, arg1, switches),
			[.., "UNFOLDER"] when executor.IsPlayer => await FolderMail.Handle(parser, ObjectDataService, Mediator,
				NotifyService, arg0, arg1, switches),
			[.., "FILE"] when executor.IsPlayer => await FolderMail.Handle(parser, ObjectDataService, Mediator,
				NotifyService, arg0, arg1, switches),
			[.., "CLEAR"] when executor.IsPlayer => await StatusMail.Handle(parser, ObjectDataService, Mediator,
				NotifyService, arg0, arg1, "CLEAR"),
			[.., "UNCLEAR"] when executor.IsPlayer => await StatusMail.Handle(parser, ObjectDataService, Mediator,
				NotifyService, arg0, arg1, "UNCLEAR"),
			[.., "TAG"] when executor.IsPlayer => await StatusMail.Handle(parser, ObjectDataService, Mediator,
				NotifyService, arg0, arg1, "TAG"),
			[.., "UNTAG"] when executor.IsPlayer => await StatusMail.Handle(parser, ObjectDataService, Mediator,
				NotifyService, arg0, arg1, "UNTAG"),
			[.., "UNREAD"] when executor.IsPlayer => await StatusMail.Handle(parser, ObjectDataService, Mediator,
				NotifyService, arg0, arg1, "UNREAD"),
			[.., "STATUS"] when executor.IsPlayer => await StatusMail.Handle(parser, ObjectDataService, Mediator,
				NotifyService, arg0, arg1, "STATUS"),
			[.., "CSTATS"] when executor.IsPlayer => await StatsMail.Handle(parser, ObjectDataService, LocateService,
				Mediator, NotifyService, arg0, switches),
			[.., "STATS"] when executor.IsPlayer => await StatsMail.Handle(parser, ObjectDataService, LocateService,
				Mediator, NotifyService, arg0, switches),
			[.., "DSTATS"] when executor.IsPlayer => await StatsMail.Handle(parser, ObjectDataService, LocateService,
				Mediator, NotifyService, arg0, switches),
			[.., "FSTATS"] when executor.IsPlayer => await StatsMail.Handle(parser, ObjectDataService, LocateService,
				Mediator, NotifyService, arg0, switches),
			[.., "DEBUG"] => await AdminMail.Handle(parser, Mediator, NotifyService, switches),
			[.., "NUKE"] => await AdminMail.Handle(parser, Mediator, NotifyService, switches),
			[.., "REVIEW"] => await ReviewMail.Handle(parser, LocateService, Mediator, NotifyService, arg0, arg1, switches),
			[.., "RETRACT"] when (arg0?.Length ?? 0) != 0 && (arg1?.Length ?? 0) != 0
				=> await RetractMail.Handle(parser, ObjectDataService, LocateService, Mediator, NotifyService,
					arg0!.ToPlainText(), arg1!.ToPlainText()),
			[.., "FWD"] when executor.IsPlayer && int.TryParse(arg0?.ToPlainText(), out var number) &&
											 (arg1?.Length ?? 0) != 0
				=> await ForwardMail.Handle(parser, ObjectDataService, LocateService, Mediator, NotifyService, MailDeliveryServices,
					number, arg1!.ToPlainText()),
			[.., "SEND"] or [.., "URGENT"] or [.., "SILENT"] or [.., "NOSIG"] or []
				when (arg0?.Length ?? 0) != 0 && (arg1?.Length ?? 0) != 0
				=> await SendMail.Handle(parser, LocateService, Mediator, NotifyService, MailDeliveryServices, arg0!, arg1!,
					switches),
			[.., "READ"] or [] when executor.IsPlayer && (arg1?.Length ?? 0) == 0 &&
															int.TryParse(arg0?.ToPlainText(), out var number)
				=> await ReadMail.Handle(parser, ObjectDataService, Mediator, NotifyService, Math.Max(0, number - 1),
					switches),
			[.., "LIST"] or [] when executor.IsPlayer && (arg1?.Length ?? 0) == 0
				=> await ListMail.Handle(parser, ObjectDataService, Mediator, NotifyService, arg0, arg1, switches),
			_ => await NotifyAndReturnBadMailArguments(executor)
		};

		return new CallState(response);
	}

	private MailDelivery.Services MailDeliveryServices
		=> new(PermissionService, Mediator, NotifyService, DidItService, AttributeService, ObjectDataService,
			Configuration);

	private async ValueTask<MString> NotifyAndReturnBadMailArguments(AnySharpObject executor)
	{
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.MailBadArguments), executor);
		return MarkupText.Plain(ErrorMessages.Returns.BadArgumentsToMailCommand);
	}

	[SharpCommand(Name = "@MALIAS",
		Switches =
		[
			"SET", "CREATE", "DESTROY", "DESCRIBE", "RENAME", "STATS", "CHOWN", "NUKE", "ADD", "REMOVE", "LIST", "ALL", "WHO",
			"MEMBERS", "USEFLAG", "SEEFLAG"
		], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 2, ParameterNames = ["alias", "list"])]
	public async ValueTask<Option<CallState>> MailAlias(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var left = args.TryGetValue("0", out var leftArg) ? leftArg.Message?.ToPlainText() ?? string.Empty : string.Empty;
		var right = args.TryGetValue("1", out var rightArg) ? rightArg.Message?.ToPlainText() ?? string.Empty : string.Empty;

		await MailAliases.Handle(MailAliasServices, executor, [.. parser.CurrentState.Switches], left.Trim(), right.Trim());
		return CallState.Empty;
	}

	private MailAliases.Services MailAliasServices => new(Mediator, NotifyService, PermissionService);
}
