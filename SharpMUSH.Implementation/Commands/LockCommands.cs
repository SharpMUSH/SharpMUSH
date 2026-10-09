using SharpMUSH.Implementation.Common;
using SharpMUSH.Implementation.Functions;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;
using System.Buffers;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	[SharpCommand(Name = "@LOCK", Switches = ["*"], Behavior = CB.Default | CB.EqSplit | CB.Switches | CB.NoGagged,
		MinArgs = 1, MaxArgs = 2, ParameterNames = ["object", "key"])]
	public ValueTask<Option<CallState>> Lock(IMUSHCodeParser parser, SharpCommandAttribute attribute)
		=> ChangeLockAsync(parser, null, false);

	[SharpCommand(Name = "@UNLOCK", Switches = ["*"], Behavior = CB.Default | CB.EqSplit | CB.Switches | CB.NoGagged,
		MinArgs = 1, MaxArgs = 1, ParameterNames = ["object", "key"])]
	public ValueTask<Option<CallState>> Unlock(IMUSHCodeParser parser, SharpCommandAttribute attribute)
		=> ChangeLockAsync(parser, null, true);

	[SharpCommand(Name = "@ELOCK", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.Switches | CB.NoGagged,
		MinArgs = 1, MaxArgs = 2, ParameterNames = ["object", "key"])]
	public ValueTask<Option<CallState>> ELock(IMUSHCodeParser parser, SharpCommandAttribute attribute)
		=> ChangeLockAsync(parser, "Enter", false);

	[SharpCommand(Name = "@EUNLOCK", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.Switches | CB.NoGagged,
		MinArgs = 1, MaxArgs = 1, ParameterNames = ["object", "key"])]
	public ValueTask<Option<CallState>> EUnlock(IMUSHCodeParser parser, SharpCommandAttribute attribute)
		=> ChangeLockAsync(parser, "Enter", true);

	[SharpCommand(Name = "@ULOCK", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.Switches | CB.NoGagged,
		MinArgs = 1, MaxArgs = 2, ParameterNames = ["object", "key"])]
	public ValueTask<Option<CallState>> ULock(IMUSHCodeParser parser, SharpCommandAttribute attribute)
		=> ChangeLockAsync(parser, "Use", false);

	[SharpCommand(Name = "@UUNLOCK", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.Switches | CB.NoGagged,
		MinArgs = 1, MaxArgs = 1, ParameterNames = ["object", "key"])]
	public ValueTask<Option<CallState>> UUnlock(IMUSHCodeParser parser, SharpCommandAttribute attribute)
		=> ChangeLockAsync(parser, "Use", true);

	/// <summary>
	/// The six lock commands: <c>@lock</c>/<c>@unlock</c> with the type as a switch, and the
	/// <c>@elock</c>/<c>@ulock</c> pairs with it fixed. <c>&lt;object&gt;/&lt;attribute&gt;</c> is
	/// <c>@atrlock</c>, as <c>do_lock</c> and <c>do_unlock</c> make it (<c>src/lock.c:668,710</c>);
	/// anything else is <see cref="LockHelpers"/>, which <c>lock()</c> reaches too.
	/// </summary>
	private async ValueTask<Option<CallState>> ChangeLockAsync(IMUSHCodeParser parser, string? fixedType, bool unlock)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		if (!args.TryGetValue("0", out var targetArg)) return new CallState(ErrorMessages.Returns.InvalidArguments);
		var targetText = targetArg.Message.ToPlainText();
		var type = fixedType ?? parser.CurrentState.Switches.FirstOrDefault();
		var slash = targetText.IndexOf('/');
		if (slash >= 0)
		{
			return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, targetText[..slash],
				LocateFlags.All, async target =>
				{
					var attributeArgs = new Dictionary<string, CallState> { ["1"] = new CallState(unlock ? "off" : "on") };
					return await AttributeLockAsync(executor, target, attributeArgs, targetText[(slash + 1)..]) is CallState state
						? state
						: CallState.Empty;
				});
		}

		var key = !unlock && args.TryGetValue("1", out var keyArg) ? keyArg.Message.ToPlainText() : string.Empty;
		await LockHelpers.LockAsync(parser, LocateService, NotifyService, PermissionService, LockService, executor,
			targetText, key, type);
		return CallState.Empty;
	}

	[SharpCommand(Name = "@LSET", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.NoGagged,
		MinArgs = 2, MaxArgs = 2, ParameterNames = ["object/lock", "flags"])]
	public async ValueTask<Option<CallState>> LockSet(IMUSHCodeParser parser, SharpCommandAttribute attribute)
	{
		if (await RejectIfTooFewArguments(parser, attribute) is { } error) return error;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		await LockHelpers.SetFlagsAsync(parser, LocateService, NotifyService, LockService, executor,
			args["0"].Message.ToPlainText(), args["1"].Message.ToPlainText());
		return CallState.Empty;
	}

	[SharpCommand(Name = "@ATRLOCK", Switches = [], Behavior = CB.Default | CB.EqSplit, MinArgs = 1, MaxArgs = 2, ParameterNames = ["object/attribute", "on-off"])]
	public async ValueTask<Option<CallState>> AttributeLock(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		if (!args.TryGetValue("0", out var objAttrArg))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NeedObjectAttributePair), executor);
			return new CallState(ErrorMessages.Returns.InvalidArguments);
		}

		var objAttrText = objAttrArg.Message.ToPlainText();
		if (HelperFunctions.SplitDbRefAndOptionalAttr(objAttrText) is not { Object: var dbref, Attribute: { } attrName })
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NeedObjectAttributePair), executor);
			return new CallState(ErrorMessages.Returns.InvalidFormat);
		}

		return await LocateService.LocateAndNotifyIfInvalidWithCallState(parser,
		executor, executor, dbref, LocateFlags.All) switch
		{
			AnySharpObject targetObject => await AttributeLockAsync(executor, targetObject, args, attrName),
			Error<CallState> error => error.Value
		};
	}

	private async ValueTask<Option<CallState>> AttributeLockAsync(AnySharpObject executor, AnySharpObject targetObject,
		Dictionary<string, CallState> args, string attrName)
	{
		if (!await PermissionService.Controls(executor, targetObject))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		if (await AttributeService.GetAttributeAsync(executor, targetObject, attrName,
				IAttributeService.AttributeMode.Read, false) is not SharpAttribute[] attribute)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeNotFound), executor);
			return new CallState(ErrorMessages.Returns.NoMatch);
		}

		if (!args.TryGetValue("1", out var valueArg) || string.IsNullOrEmpty(valueArg.Message.ToPlainText()))
		{
			await NotifyService.NotifyLocalized(executor,
				AttributeLockHelpers.IsLocked(attribute)
					? nameof(ErrorMessages.Notifications.AttributeIsLocked)
					: nameof(ErrorMessages.Notifications.AttributeIsUnlocked),
				executor);
			return new CallState(string.Empty);
		}

		if (AttributeLockSwitch.Parse(valueArg.Message.ToPlainText()) is not bool shouldLock)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.InvalidArgument), executor);
			return new CallState(ErrorMessages.Returns.InvalidValue);
		}

		return await AttributeLockHelpers.ChangeAsync(PermissionService, AttributeService, NotifyService, Mediator,
			executor, targetObject, attrName, attribute, shouldLock);
	}
}
