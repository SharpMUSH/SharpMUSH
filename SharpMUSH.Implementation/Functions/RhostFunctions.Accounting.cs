using System.Globalization;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Database.Seed;
using SharpMUSH.Implementation.Commands.MailCommand;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Functions;

/// <summary>
/// RhostMUSH's counting and listing functions: how many attributes or commands something has, which
/// flags and commands exist, and who is cloaked. Each answers as RhostMUSH does, in the terms SharpMUSH has.
/// </summary>
public partial class Functions
{
	/// <summary>The attributes SharpMUSH defines itself. Any other name on an object is one somebody made.</summary>
	private static readonly HashSet<string> BuiltInAttributeNames =
		new(AttributeEntrySeed.Entries.Select(entry => entry.Name), StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// <c>vattrcnt(&lt;object&gt;[, &lt;recount&gt;])</c>: how many attributes on the object are not built in.
	/// RhostMUSH keeps that count on the object and <c>&lt;recount&gt;</c> rebuilds it; SharpMUSH counts every
	/// time, so the second argument changes nothing. An object the caller cannot examine gives <c>-1</c>.
	/// </summary>
	[SharpFunction(Name = "vattrcnt", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["object", "recount"])]
	public async ValueTask<CallState> UserAttributeCount(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var name = parser.CurrentState.Arguments["0"].Message.ToPlainText();

		if (await LocateService.LocateAndNotifyIfInvalid(parser, executor, executor, name, LocateFlags.All)
				is not AnySharpObject target
				|| !await PermissionService.CanExamine(executor, target))
		{
			return new CallState("-1");
		}

		if (await AttributeService.LazilyGetAttributePatternAsync(executor, target, "**", false,
					IAttributeService.AttributePatternMode.Wildcard) is not IAsyncEnumerable<LazySharpAttribute> attributes)
		{
			return new CallState("-1");
		}

		var count = await attributes.CountAsync(attribute => !BuiltInAttributeNames.Contains(attribute.LongName),
			ExecutionBudget.CurrentToken);
		return new CallState(count);
	}

	/// <summary>
	/// <c>size(&lt;player&gt;[, &lt;type&gt;])</c>: RhostMUSH's memory use of a player and what they own.
	/// SharpMUSH does not measure memory, so it answers 0 as <c>objmem()</c> and <c>playermem()</c> do, once
	/// the target is found and the caller controls it. Types 0 to 2 name a player and 3 any object.
	/// </summary>
	[SharpFunction(Name = "size", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.WizardOnly,
		ParameterNames = ["player", "type"])]
	public async ValueTask<CallState> Size(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var name = parser.CurrentState.Arguments["0"].Message.ToPlainText();
		var type = parser.CurrentState.Arguments.TryGetValue("1", out var typeArg)
			&& int.TryParse(typeArg.Message.ToPlainText(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
			&& parsed is >= 0 and <= 3
				? parsed
				: 0;

		var found = type == 3
			? await LocateService.Locate(parser, executor, executor, name, LocateFlags.All)
			: await LocateService.LocateConnectionTarget(parser, executor, executor, name);

		return found is AnySharpObject target && await PermissionService.Controls(executor, target)
			? new CallState("0")
			: new CallState("#-1");
	}

	/// <summary>
	/// <c>cloak(&lt;viewer&gt;, &lt;target&gt;)</c>: 1 when <c>&lt;target&gt;</c> is hidden from
	/// <c>&lt;viewer&gt;</c>'s WHO and location listings. RhostMUSH's cloak is a wizard who has hidden; here
	/// that is a DARK wizard or royalty, or a player who has used <c>@hide</c>. A viewer who may see
	/// hidden players (wizard, royalty or See_All) sees through it.
	/// </summary>
	[SharpFunction(Name = "cloak", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.WizardOnly,
		ParameterNames = ["viewer", "target"])]
	public async ValueTask<CallState> Cloak(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;

		if (await LocateService.Locate(parser, executor, executor, args["0"].Message.ToPlainText(), LocateFlags.All)
				is not AnySharpObject viewer
				|| await LocateService.Locate(parser, executor, executor, args["1"].Message.ToPlainText(), LocateFlags.All)
				is not AnySharpObject target)
		{
			return new CallState("#-1");
		}

		var cloaked = (await target.HasFlag("DARK") && (await target.IsWizard() || await target.IsRoyalty()))
			|| (target.IsPlayer && await ConnectionService.IsPlayerHiddenAsync(target.Object().DBRef));
		var seesThrough = await viewer.IsWizard() || await viewer.IsRoyalty() || await viewer.IsSee_All();

		return new CallState(cloaked && !seesThrough ? "1" : "0");
	}

	/// <summary>
	/// <c>mailalias(&lt;alias&gt;[, &lt;type&gt;])</c>: the members of a mail alias as dbrefs. Type 1 asks
	/// RhostMUSH for the alias's lock; SharpMUSH aliases have who-may-use settings rather than a lock, so it
	/// gives nothing. An alias that does not exist gives nothing too.
	/// </summary>
	[SharpFunction(Name = "mailalias", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi | FunctionFlags.WizardOnly,
		ParameterNames = ["alias", "type"])]
	public async ValueTask<CallState> MailAlias(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		if (args.TryGetValue("1", out var typeArg) && typeArg.Message.ToPlainText().Trim() == "1")
		{
			return CallState.Empty;
		}

		var services = new MailAliases.Services(Mediator, NotifyService, PermissionService, ConnectionService, HouseLook);
		return new CallState(await MailAliases.MembersAsync(services, args["0"].Message.ToPlainText().Trim()) ?? string.Empty);
	}

	/// <summary>
	/// <c>strlenraw(&lt;string&gt;)</c>: the length of the string with its colour written out as ANSI
	/// escape codes, in bytes of UTF-8, as RhostMUSH counts the raw buffer.
	/// </summary>
	[SharpFunction(Name = "strlenraw", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["string"])]
	public ValueTask<CallState> StringLengthRaw(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult(new CallState(
			Encoding.UTF8.GetByteCount(parser.CurrentState.Arguments["0"].Message.Render(MarkupFormat.Ansi))));

	/// <summary>
	/// <c>totcmds(&lt;player&gt;)</c>: every command the player has typed, the ended sessions' total
	/// (<c>TOTCMDS</c>) plus each connection's count. A caller who may not see a hidden player's connections
	/// gets <c>-1</c> while that player is connected, and so does a name that matches no player.
	/// </summary>
	[SharpFunction(Name = "totcmds", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["player"])]
	public async ValueTask<CallState> TotalCommandCount(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var name = parser.CurrentState.Arguments["0"].Message.ToPlainText();

		if (await LocateService.LocateConnectionTarget(parser, executor, executor, name) is not AnySharpObject player)
		{
			return new CallState("-1");
		}

		var dbref = player.Object().DBRef;
		var total = await TotalCommands.StoredAsync(Mediator, dbref);

		var connections = await ConnectionService.Get(dbref).ToListAsync();
		if (connections.Count > 0)
		{
			var seesHidden = dbref == executor.Object().DBRef
				|| await executor.IsWizard() || await executor.IsRoyalty() || await executor.IsSee_All();
			// A hidden connection's count is left out for a caller who may not see it, so the answer does
			// not change when a hidden player connects.
			total += connections
				.Where(connection => seesHidden || !connection.IsHidden)
				.Sum(connection => (long)connection.CommandCount);
		}

		return new CallState(Math.Min(total, 2_000_000_000L));
	}

	/// <summary>
	/// <c>listcommands([&lt;type&gt;])</c>: the commands the caller may run, sorted and in lower case.
	/// Type 0 lists the built-in ones, 1 those added with <c>@command/add</c>, 2 both (built-in first), and
	/// 3 the <c>@&lt;attribute&gt;</c> commands that set an attribute.
	/// </summary>
	[SharpFunction(Name = "listcommands", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["type"])]
	public async ValueTask<CallState> ListCommandTable(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var type = parser.CurrentState.Arguments.TryGetValue("0", out var typeArg)
			&& int.TryParse(typeArg.Message.ToPlainText(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
			&& parsed is >= 0 and <= 3
				? parsed
				: 0;

		if (type == 3)
		{
			var attributeCommands = await Mediator.CreateStream(new GetAllAttributeEntriesQuery())
				.Select(entry => $"@{entry.Name.ToLowerInvariant()}")
				.ToListAsync();
			attributeCommands.Sort(StringComparer.Ordinal);
			return new CallState(string.Join(' ', attributeCommands));
		}

		var permission = parser.ServiceProvider.GetRequiredService<ICommandPermission>();
		var parts = new List<string>();
		if (type is 0 or 2) parts.Add(await RunnableCommandsAsync(parser, permission, executor, system: true));
		if (type is 1 or 2) parts.Add(await RunnableCommandsAsync(parser, permission, executor, system: false));

		return new CallState(string.Join(' ', parts.Where(part => part.Length > 0)));
	}

	private static async ValueTask<string> RunnableCommandsAsync(IMUSHCodeParser parser, ICommandPermission permission,
		AnySharpObject executor, bool system)
	{
		var names = new SortedSet<string>(StringComparer.Ordinal);
		foreach (var (_, (definition, isSystem)) in parser.CommandLibrary)
		{
			var name = definition.Attribute.Name.ToLowerInvariant();
			if (isSystem != system || names.Contains(name)) continue;
			if (await permission.CanRunAsync(executor, definition.Attribute.Name)) names.Add(name);
		}

		return string.Join(' ', names);
	}

	/// <summary>
	/// <c>listflags()</c>: every flag the caller may see, sorted, each as <c>NAME(&lt;letter&gt;)</c>. A dark
	/// flag is listed only to God and an mdark one only to those who may see all; disabled flags are left out.
	/// </summary>
	[SharpFunction(Name = "listflags", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular)]
	public async ValueTask<CallState> ListFlagTable(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var god = executor.IsGod();
		var seeAll = god || await executor.IsWizard() || await executor.IsRoyalty() || await executor.IsSee_All();

		var flags = await Mediator.CreateStream(new GetAllObjectFlagsQuery()).ToListAsync();
		var listed = flags
			.Where(flag => !flag.Disabled)
			.Where(flag => god || !flag.SetPermissions.Contains("dark", StringComparer.OrdinalIgnoreCase))
			.Where(flag => seeAll || !flag.SetPermissions.Contains("mdark", StringComparer.OrdinalIgnoreCase))
			.Select(flag => $"{flag.Name.ToUpperInvariant()}({flag.Symbol})")
			.Order(StringComparer.Ordinal);

		return new CallState(string.Join(' ', listed));
	}
}
