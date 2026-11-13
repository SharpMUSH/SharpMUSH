using SharpMUSH.Implementation.Common;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Common;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.DatabaseConversion.Rhost;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Functions;

/// <summary>RhostMUSH's toggle functions, over the flags its toggles are here (<see cref="RhostNameTables.Toggles"/>).</summary>
public partial class Functions
{
	/// <summary>hastoggle(): 1 when the object has the toggle and the caller may see it; 0 for a name that is no toggle.</summary>
	[SharpFunction(Name = "hastoggle", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["object", "toggle"])]
	public async ValueTask<CallState> HasToggle(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var name = parser.CurrentState.ArgumentList[1].Message.ToPlainText().Trim();
		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor,
			parser.CurrentState.ArgumentList[0].Message.ToPlainText(), LocateFlags.All,
			async found => RhostNameTables.Toggle(name) is { Flag: { } flag }
				? new CallState((await RhostToggleNames.VisibleFlagNamesAsync(executor, found, ConnectionService)).Contains(flag, StringComparer.OrdinalIgnoreCase))
				: new CallState(false));
	}

	/// <summary>ltoggles(): the toggles an object has that the caller may see, by RhostMUSH's names.</summary>
	[SharpFunction(Name = "ltoggles", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["object"])]
	public async ValueTask<CallState> ListToggles(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor,
			parser.CurrentState.ArgumentList[0].Message.ToPlainText(), LocateFlags.All,
			async found => new CallState(string.Join(' ',
				RhostToggleNames.ToggleNamesOf(await RhostToggleNames.VisibleFlagNamesAsync(executor, found, ConnectionService)))));
	}

	/// <summary>
	/// listtoggles(): every toggle this game has, as <c>NAME(letter)</c> by name; one whose flag is dark only to God,
	/// one whose flag is mdark only to those who may see all.
	/// </summary>
	[SharpFunction(Name = "listtoggles", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public async ValueTask<CallState> ListAllToggles(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var god = executor.IsGod();
		var seeAll = god || await executor.IsWizard() || await executor.IsRoyalty() || await executor.IsSee_All();
		var flags = await Mediator.CreateStream(new GetAllObjectFlagsQuery())
			.Where(flag => !flag.Disabled)
			.Where(flag => god || !flag.SetPermissions.Contains("dark", StringComparer.OrdinalIgnoreCase))
			.Where(flag => seeAll || !flag.SetPermissions.Contains("mdark", StringComparer.OrdinalIgnoreCase))
			.Select(flag => flag.Name)
			.ToHashSetAsync(StringComparer.OrdinalIgnoreCase);
		return new CallState(string.Join(' ', RhostNameTables.Toggles
			.Where(toggle => toggle.Flag is { } flag && flags.Contains(flag))
			.OrderBy(toggle => toggle.Name, StringComparer.Ordinal)
			.Select(toggle => $"{toggle.Name}({toggle.Letter})")));
	}

	/// <summary>toggle(): <c>@toggle</c> as a side effect, without its messages; it returns nothing.</summary>
	[SharpFunction(Name = "toggle", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX,
		ParameterNames = ["object", "toggles"])]
	public async ValueTask<CallState> Toggle(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var (flags, unknown) = RhostToggleNames.Translate(parser.CurrentState.ArgumentList[1].Message.ToPlainText(),
			await RhostToggleNames.DefinedFlagsAsync(Mediator));
		foreach (var name in unknown)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NoSuchToggleFormat), executor, name);
		}

		if (flags.Count > 0)
		{
			await SetHelpers.DoSet(parser, LocateService, AttributeService, FlagAndPowerService, NotifyService, PermissionService,
				executor, parser.CurrentState.ArgumentList[0].Message, MString.Plain(string.Join(' ', flags)), quiet: true);
		}

		return CallState.Empty;
	}
}
