using System.Buffers;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Common;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	[SharpCommand(Name = "@UNRECYCLE", Switches = [], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0, ParameterNames = ["object"])]
	public async ValueTask<Option<CallState>> UnRecycle(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		if (!await executor.IsWizard())
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		await NotifyService.Notify(executor, "@UNRECYCLE: Object recovery system not yet implemented.", executor);
		await NotifyService.Notify(executor, "This command would restore objects from the recycle bin.", executor);

		return CallState.Empty;
	}

	[SharpCommand(Name = "BRIEF", Switches = ["OPAQUE"], Behavior = CB.Default, MinArgs = 0, MaxArgs = 1, ParameterNames = [])]
	public async ValueTask<Option<CallState>> Brief(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();
		var enactor = await parser.CurrentState.KnownEnactorObject(Mediator);
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		AnyOptionalSharpObject viewing;

		if (args.Count == 1)
		{
			var argText = args["0"].Message!.ToPlainText();

			var locate = await LocateService.LocateAndNotifyIfInvalid(
				parser,
				executor,
				executor,
				argText,
				LocateFlags.All);

			if (locate is not AnySharpObject located)
			{
				return new None();
			}

			viewing = located;
		}
		else
		{
			viewing = (await Mediator.Send(new GetLocationQuery(enactor.Object().DBRef))).WithExitOption();
		}

		if (viewing is not AnySharpObject viewingKnown)
		{
			return new None();
		}

		var canExamine = await PermissionService.CanExamine(executor, viewingKnown);

		if (!canExamine)
		{
			var limitedObj = viewingKnown.Object();
			var limitedOwnerObj = (await limitedObj.Owner.WithCancellation(CancellationToken.None)).Object;
			await NotifyService.Notify(enactor, $"{limitedObj.Name} is owned by {limitedOwnerObj.Name}.", enactor);
			return new CallState(limitedObj.DBRef.ToString());
		}

		var perceive = await ObserveRealityAsync(parser, executor);
		var contents = (switches.Contains("OPAQUE") || viewing.IsExit)
			? []
			: await Mediator.CreateStream(new GetContentsQuery(viewingKnown.AsContainer), ExecutionBudget.CurrentToken)
				.Where((item, ct) => perceive(item.Object().DBRef, ct))
				.ToArrayAsync(ExecutionBudget.CurrentToken);

		var obj = viewingKnown.Object()!;
		var ownerObj = (await obj.Owner.WithCancellation(CancellationToken.None)).Object;
		var name = obj.Name;
		var ownerName = ownerObj.Name;
		var objFlags = await obj.Flags.Value.ToArrayAsync();
		var objPowers = obj.Powers.Value;
		var objParent = await obj.Parent.WithCancellation(CancellationToken.None);

		var outputSections = new List<MString>();

		var showFlags = Configuration.CurrentValue.Cosmetic.FlagsOnExamine;
		var nameRow = showFlags
			? MarkupText.Concat([
				name.Hilight(),
				MarkupText.Space,
				MarkupText.Plain($"(#{obj.DBRef.Number}{MessageFormatting.FlagSymbols(objFlags)})")
			])
			: MarkupText.Concat(name.Hilight(), MarkupText.Plain($" (#{obj.DBRef.Number})"));

		outputSections.Add(nameRow);

		if (showFlags)
		{
			outputSections.Add(MarkupText.Plain($"Type: {obj.Type} Flags: {string.Join(" ", objFlags.Select(x => x.Name))}"));
		}
		else
		{
			outputSections.Add(MarkupText.Plain($"Type: {obj.Type}"));
		}

		var ownerRow = showFlags
			? MarkupText.Plain($"Owner: {ownerName.Hilight()}" +
											 $"(#{ownerObj.DBRef.Number}{await MessageFormatting.FlagSymbolsAsync(ownerObj)})")
			: MarkupText.Plain($"Owner: {ownerName.Hilight()}(#{ownerObj.DBRef.Number})");
		outputSections.Add(ownerRow);

		outputSections.Add(MarkupText.Plain($"Parent: {objParent.Object()?.Name ?? "*NOTHING*"}"));

		foreach (var (lockName, lockData) in obj.Locks.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
			outputSections.Add(MarkupText.Plain(await FormatLockLineAsync(executor, lockName, lockData)));

		var powersList = await objPowers.Select(x => x.Name).ToArrayAsync();
		if (powersList.Length > 0)
		{
			outputSections.Add(MarkupText.Plain($"Powers: {string.Join(" ", powersList)}"));
		}

		if (viewingKnown.IsPlayer || viewingKnown.IsThing)
		{
			if (await viewingKnown.MinusRoom().Home() is not AnySharpContainer homeObj)
			{
				throw new InvalidOperationException("Players and things always have a home.");
			}

			outputSections.Add(MarkupText.Plain($"Home: {homeObj.Object().Name}(#{homeObj.Object().DBRef.Number})"));

			var locationObj = await viewingKnown.Where();
			outputSections.Add(MarkupText.Plain($"Location: {locationObj.Object().Name}(#{locationObj.Object().DBRef.Number})"));
		}

		outputSections.Add(MarkupText.Plain($"Created: {DateTimeOffset.FromUnixTimeMilliseconds(obj.CreationTime):F}"));

		await NotifyService.Notify(enactor, MarkupText.Join(MarkupText.Plain("\n"), outputSections), enactor);

		if (!switches.Contains("OPAQUE") && contents.Length > 0)
		{
			var contentNames = contents.Select(x => x.Object().Name);
			await NotifyService.Notify(enactor, $"Contents:", enactor);
			foreach (var contentName in contentNames)
			{
				await NotifyService.Notify(enactor, $"  {contentName}", enactor);
			}
		}

		return new CallState(obj.DBRef.ToString());
	}

	[SharpCommand(Name = "WARN_ON_MISSING", Switches = [], Behavior = CB.Default | CB.NoParse | CB.Internal | CB.NoOp,
		MinArgs = 0, MaxArgs = 0, ParameterNames = [])]
	public async ValueTask<Option<CallState>> WarnOnMissing(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// Internal no-op command for warning system
		await ValueTask.CompletedTask;
		return new None();
	}

	[SharpCommand(Name = "UNIMPLEMENTED_COMMAND", Switches = [],
		Behavior = CB.Default | CB.NoParse | CB.Internal | CB.NoOp, MinArgs = 0, MaxArgs = 0, ParameterNames = [])]
	public async ValueTask<Option<CallState>> UnimplementedCommand(IMUSHCodeParser parser,
		SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownEnactorObject(Mediator);
		await NotifyService.Notify(executor, "Huh?  (Type \"help\" for help.)", executor);
		return new None();
	}
}
