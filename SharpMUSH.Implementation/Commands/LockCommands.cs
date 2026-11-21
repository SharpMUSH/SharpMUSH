using SharpMUSH.Implementation.Common;
using SharpMUSH.Implementation.Functions;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Commands.Database;
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
		var args = parser.CurrentState.ArgumentList;
		if (!args.TryGetValue("0", out var targetArg)) return new CallState(ErrorMessages.Returns.InvalidArguments);
		var targetText = targetArg.Message.ToPlainText();
		var type = fixedType ?? parser.CurrentState.Switches.FirstOrDefault();
		var slash = targetText.IndexOf('/');
		if (slash >= 0)
		{
			return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, targetText[..slash],
				LocateFlags.All | LocateFlags.MatchAccountHolders, async target =>
				{
					var attributeArgs = new Dictionary<string, CallState> { ["1"] = new CallState(unlock ? "off" : "on") };
					return await AttributeLockAsync(executor, target, ArgumentList.From(attributeArgs), targetText[(slash + 1)..]) is CallState state
						? state
						: CallState.Empty;
				});
		}

		var key = !unlock && args.TryGetValue("1", out var keyArg) ? keyArg.Message.ToPlainText() : string.Empty;
		await LockHelpers.LockAsync(parser, LocateService, NotifyService, PermissionService, LockService, executor,
			targetText, key, type, rhostSpelling: fixedType is null);
		return CallState.Empty;
	}

	[SharpCommand(Name = "@LSET", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.NoGagged,
		MinArgs = 2, MaxArgs = 2, ParameterNames = ["object/lock", "flags"])]
	public async ValueTask<Option<CallState>> LockSet(IMUSHCodeParser parser, SharpCommandAttribute attribute)
	{
		if (await RejectIfTooFewArguments(parser, attribute) is { } error) return error;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.ArgumentList;
		await LockHelpers.SetFlagsAsync(parser, LocateService, NotifyService, LockService, executor,
			args["0"].Message.ToPlainText(), args["1"].Message.ToPlainText());
		return CallState.Empty;
	}

	[SharpCommand(Name = "@ATRLOCK", Switches = ["READ", "WRITE"], Behavior = CB.Default | CB.EqSplit, MinArgs = 1, MaxArgs = 2, ParameterNames = ["object/attribute", "on-off"])]
	public async ValueTask<Option<CallState>> AttributeLock(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.ArgumentList;
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

		// /READ and /WRITE are the attribute's keys; without either, @atrlock is PennMUSH's on/off lock.
		AttributeLockKind? kind = parser.CurrentState.Switches.Contains("READ") ? AttributeLockKind.Read
			: parser.CurrentState.Switches.Contains("WRITE") ? AttributeLockKind.Write
			: null;
		return await LocateService.LocateAndNotifyIfInvalidWithCallState(parser,
		executor, executor, dbref, LocateFlags.All | LocateFlags.MatchAccountHolders) switch
		{
			AnySharpObject targetObject when kind is { } key => await AttributeKeyAsync(executor, targetObject, args, attrName, key),
			AnySharpObject targetObject => await AttributeLockAsync(executor, targetObject, args, attrName),
			Error<CallState> error => error.Value
		};
	}

	/// <summary>
	/// <c>@atrlock/read</c> and <c>/write</c>: shows each matching attribute's key to whoever may read the attribute, or,
	/// given <c>=&lt;key&gt;</c>, sets it (an empty key clears it) for whoever may set the attribute.
	/// </summary>
	private async ValueTask<CallState> AttributeKeyAsync(AnySharpObject executor, AnySharpObject targetObject,
		ArgumentList args, string attrName, AttributeLockKind kind)
	{
		var attributes = await OwnAttributeChainsAsync(executor, targetObject, attrName);
		if (attributes.Count == 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeNotFound), executor);
			return new CallState(ErrorMessages.Returns.NoMatch);
		}

		var label = kind is AttributeLockKind.Read ? "read" : "write";
		if (!args.TryGetValue("1", out var keyArg))
		{
			// A key names roles and objects: it is shown, as examine shows it, to whoever may examine the object.
			if (!await PermissionService.CanExamine(executor, targetObject))
			{
				return await NotifyService.PermissionDeniedNotControlled(executor, targetObject);
			}

			foreach (var chain in attributes)
			{
				var leaf = chain.Last();
				await ((kind is AttributeLockKind.Read ? leaf.ReadLock : leaf.WriteLock) is { } shown
					? NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeKeyShowFormat), executor,
						leaf.LongName!, label,
						await BooleanExpressionParser.RenderAsync(shown, executor, LockRenderMode.Readback, ExecutionBudget.CurrentToken))
					: NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeKeyNoneFormat), executor,
						leaf.LongName!, label));
			}

			return CallState.Empty;
		}

		return await AttributeLockHelpers.ChangeKeyAsync(PermissionService, NotifyService, LockService, Mediator,
			executor, targetObject, attributes, kind, keyArg.Message.ToPlainText());
	}

	private async ValueTask<Option<CallState>> AttributeLockAsync(AnySharpObject executor, AnySharpObject targetObject,
		ArgumentList args, string attrName)
	{
		if (await PermissionService.Check(executor, Requires.ControlOf(targetObject)) is PermissionRefusal refused)
		{
			return await NotifyService.PermissionDenied(executor, refused);
		}

		// do_atrlock reads only the object's own attribute (attrib.c:2512, atr_get_noparent). A wildcard
		// pattern, which PennMUSH does not take here, reads or changes every readable match, naming each.
		var attributes = await OwnAttributeChainsAsync(executor, targetObject, attrName);
		if (attributes.Count == 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeNotFound), executor);
			return new CallState(ErrorMessages.Returns.NoMatch);
		}

		var named = !SharpMUSH.Library.Services.AttributeService.IsLiteralPattern(attrName,
			IAttributeService.AttributePatternMode.Wildcard);

		if (!args.TryGetValue("1", out var valueArg) || string.IsNullOrEmpty(valueArg.Message.ToPlainText()))
		{
			foreach (var chain in attributes)
			{
				var locked = AttributeLockHelpers.IsLocked(chain);
				await (named
					? NotifyService.NotifyLocalized(executor, locked
						? nameof(ErrorMessages.Notifications.NamedAttributeIsLockedFormat)
						: nameof(ErrorMessages.Notifications.NamedAttributeIsUnlockedFormat), executor, chain.Last().LongName!)
					: NotifyService.NotifyLocalized(executor, locked
						? nameof(ErrorMessages.Notifications.AttributeIsLocked)
						: nameof(ErrorMessages.Notifications.AttributeIsUnlocked), executor));
			}

			return new CallState(string.Empty);
		}

		if (AttributeLockSwitch.Parse(valueArg.Message.ToPlainText()) is not bool shouldLock)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.InvalidArgument), executor);
			return new CallState(ErrorMessages.Returns.InvalidValue);
		}

		CallState? firstFailure = null;
		var anyChanged = false;
		foreach (var chain in attributes)
		{
			var result = await AttributeLockHelpers.ChangeAsync(PermissionService, NotifyService, Mediator,
				executor, targetObject, chain.Last().LongName!, chain, shouldLock, named);
			if (result.Message.ToPlainText().Length == 0)
			{
				anyChanged = true;
			}
			else
			{
				firstFailure ??= result;
			}
		}

		return anyChanged ? CallState.Empty : firstFailure ?? CallState.Empty;
	}
}
