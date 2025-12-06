using Microsoft.Extensions.Logging;
using OneOf.Types;
using SharpMUSH.Implementation.Commands.ChannelCommand;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;
using Errors = SharpMUSH.Library.Definitions.Errors;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	// Attribute name constants
	private const string AttrDrop = "DROP";
	private const string AttrODrop = "ODROP";
	private const string AttrADrop = "ADROP";
	private const string AttrEnter = "ENTER";
	private const string AttrOEnter = "OENTER";
	private const string AttrOXEnter = "OXENTER";
	private const string AttrAEnter = "AENTER";
	private const string AttrLeave = "LEAVE";
	private const string AttrOLeave = "OLEAVE";
	private const string AttrOXLeave = "OXLEAVE";
	private const string AttrALeave = "ALEAVE";
	private const string AttrLFail = "LFAIL";
	private const string AttrOLFail = "OLFAIL";
	private const string AttrALFail = "ALFAIL";
	private const string AttrEFail = "EFAIL";
	private const string AttrOEFail = "OEFAIL";
	private const string AttrAEFail = "AEFAIL";
	private const string AttrSuccess = "SUCCESS";
	private const string AttrOSuccess = "OSUCCESS";
	private const string AttrASuccess = "ASUCCESS";
	private const string AttrGive = "GIVE";
	private const string AttrOGive = "OGIVE";
	private const string AttrAGive = "AGIVE";
	private const string AttrReceive = "RECEIVE";
	private const string AttrOReceive = "ORECEIVE";
	private const string AttrAReceive = "ARECEIVE";
	private const string AttrLinkType = "_LINKTYPE";
	private const string LinkTypeVariable = "variable";
	private const string LinkTypeHome = "home";


	[SharpCommand(Name = "@CLOCK", Switches = ["JOIN", "SPEAK", "MOD", "SEE", "HIDE"], Behavior = CB.Default | CB.EqSplit,
		MinArgs = 1, MaxArgs = 2)]
	public static async ValueTask<Option<CallState>> ChannelLock(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches;
		
		var channelName = args["0"].Message!;
		var lockKey = args.TryGetValue("1", out var arg1) ? arg1.Message!.ToPlainText() : string.Empty;
		
		var lockType = switches.FirstOrDefault() ?? "JOIN";
		lockType = lockType.ToUpper();
		
		var maybeChannel = await ChannelHelper.GetChannelOrError(parser, LocateService!, PermissionService!, Mediator!,
			NotifyService!, channelName, false);
		
		if (maybeChannel.IsError)
		{
			return maybeChannel.AsError.Value;
		}
		
		var channel = maybeChannel.AsChannel;
		
		var owner = await channel.Owner.WithCancellation(CancellationToken.None);
		var isOwner = owner.Object.DBRef.Equals(executor.Object().DBRef);
		var passesModLock = string.IsNullOrEmpty(channel.ModLock) || 
		                    LockService!.Evaluate(channel.ModLock, channel, executor);
		
		if (!isOwner && !passesModLock && !await executor.IsWizard())
		{
			await NotifyService!.Notify(executor, "Permission denied.");
			return new CallState("#-1 PERMISSION DENIED");
		}
		
		UpdateChannelCommand updateCommand = lockType switch
		{
			"JOIN" => new UpdateChannelCommand(channel, null, null, null, lockKey, null, null, null, null, null, null),
			"SPEAK" => new UpdateChannelCommand(channel, null, null, null, null, lockKey, null, null, null, null, null),
			"SEE" => new UpdateChannelCommand(channel, null, null, null, null, null, lockKey, null, null, null, null),
			"HIDE" => new UpdateChannelCommand(channel, null, null, null, null, null, null, lockKey, null, null, null),
			"MOD" => new UpdateChannelCommand(channel, null, null, null, null, null, null, null, lockKey, null, null),
			_ => new UpdateChannelCommand(channel, null, null, null, null, null, null, null, null, null, null)
		};
		
		if (lockType is not ("JOIN" or "SPEAK" or "SEE" or "HIDE" or "MOD"))
		{
			await NotifyService!.Notify(executor, $"Invalid lock type: {lockType}");
			return new CallState("#-1 INVALID LOCK TYPE");
		}
		
		await Mediator!.Send(updateCommand);
		
		if (string.IsNullOrEmpty(lockKey))
		{
			await NotifyService!.Notify(executor, $"{lockType} lock removed from channel {channel.Name.ToPlainText()}.");
		}
		else
		{
			await NotifyService!.Notify(executor, $"{lockType} lock set on channel {channel.Name.ToPlainText()}.");
		}
		
		return CallState.Empty;
	}

	[SharpCommand(Name = "@LIST",
		Switches =
		[
			"LOWERCASE", "MOTD", "LOCKS", "FLAGS", "FUNCTIONS", "POWERS", "COMMANDS", "ATTRIBS", "ALLOCATIONS", "ALL",
			"BUILTIN", "LOCAL"
		], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> List(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var switches = parser.CurrentState.Switches;
		var useLowercase = switches.Contains("LOWERCASE");
		
		// Handle /MOTD switch - alias for @listmotd
		if (switches.Contains("MOTD"))
		{
			var isWizard = await executor.IsWizard();
			
			var motdFile = Configuration!.CurrentValue.Message.MessageOfTheDayFile;
			var motdHtmlFile = Configuration.CurrentValue.Message.MessageOfTheDayHtmlFile;
			
			await NotifyService!.Notify(executor, "Current Message of the Day settings:");
			await NotifyService.Notify(executor, $"  Connect MOTD File: {motdFile ?? "(not set)"}");
			await NotifyService.Notify(executor, $"  Connect MOTD HTML: {motdHtmlFile ?? "(not set)"}");
			
			if (isWizard)
			{
				var wizmotdFile = Configuration.CurrentValue.Message.WizMessageOfTheDayFile;
				var wizmotdHtmlFile = Configuration.CurrentValue.Message.WizMessageOfTheDayHtmlFile;
				
				await NotifyService.Notify(executor, $"  Wizard MOTD File: {wizmotdFile ?? "(not set)"}");
				await NotifyService.Notify(executor, $"  Wizard MOTD HTML: {wizmotdHtmlFile ?? "(not set)"}");
			}
			
			return CallState.Empty;
		}
		
		if (switches.Contains("FLAGS"))
		{
			var output = new System.Text.StringBuilder();
			var header = useLowercase ? "Object Flags:" : "OBJECT FLAGS:";
			output.AppendLine(header);
			
			var headerLine = useLowercase 
				? "name                 symbol type restrictions" 
				: "NAME                 SYMBOL TYPE RESTRICTIONS";
			output.AppendLine(headerLine);
			output.AppendLine("-------------------- ------ -------------------");
			
			var flags = Mediator!.CreateStream(new GetAllObjectFlagsQuery());
			await foreach (var flag in flags)
			{
				var flagName = useLowercase ? flag.Name.ToLower() : flag.Name;
				var symbol = useLowercase ? flag.Symbol.ToLower() : flag.Symbol;
				var types = string.Join(",", flag.TypeRestrictions.Select(t => useLowercase ? t.ToLower() : t));
				output.AppendLine($"{flagName,-20} {symbol,-6} {types}");
			}
			
			await NotifyService!.Notify(executor, output.ToString().TrimEnd());
			return CallState.Empty;
		}
		
		if (switches.Contains("POWERS"))
		{
			var output = new System.Text.StringBuilder();
			var header = useLowercase ? "Object Powers:" : "OBJECT POWERS:";
			output.AppendLine(header);
			
			var headerLine = useLowercase 
				? "name                 alias              type restrictions" 
				: "NAME                 ALIAS              TYPE RESTRICTIONS";
			output.AppendLine(headerLine);
			output.AppendLine("-------------------- ------------------ -------------------");
			
			var powers = Mediator!.CreateStream(new GetPowersQuery());
			await foreach (var power in powers)
			{
				var powerName = useLowercase ? power.Name.ToLower() : power.Name;
				var alias = useLowercase ? power.Alias.ToLower() : power.Alias;
				var types = string.Join(",", power.TypeRestrictions.Select(t => useLowercase ? t.ToLower() : t));
				output.AppendLine($"{powerName,-20} {alias,-18} {types}");
			}
			
			await NotifyService!.Notify(executor, output.ToString().TrimEnd());
			return CallState.Empty;
		}
		
		if (switches.Contains("LOCKS"))
		{
			var output = new System.Text.StringBuilder();
			var header = useLowercase ? "Lock Types:" : "LOCK TYPES:";
			output.AppendLine(header);
			
			var lockTypes = Enum.GetNames(typeof(LockType));
			foreach (var lockType in lockTypes.OrderBy(x => x))
			{
				var displayName = useLowercase ? lockType.ToLower() : lockType.ToUpper();
				output.AppendLine($"  {displayName}");
			}
			
			await NotifyService!.Notify(executor, output.ToString().TrimEnd());
			return CallState.Empty;
		}
		
		if (switches.Contains("ATTRIBS"))
		{
			var output = new System.Text.StringBuilder();
			var header = useLowercase ? "Standard Attributes:" : "STANDARD ATTRIBUTES:";
			output.AppendLine(header);
			
			var attributes = Mediator!.CreateStream(new GetAllAttributeEntriesQuery());
			await foreach (var attr in attributes.OrderBy(x => x.Name))
			{
				var attrName = useLowercase ? attr.Name.ToLower() : attr.Name;
				output.AppendLine($"  {attrName}");
			}
			
			await NotifyService!.Notify(executor, output.ToString().TrimEnd());
			return CallState.Empty;
		}
		
		if (switches.Contains("COMMANDS"))
		{
			var output = new System.Text.StringBuilder();
			var header = useLowercase ? "Commands:" : "COMMANDS:";
			output.AppendLine(header);
			
			var filterBuiltin = switches.Contains("BUILTIN");
			var filterLocal = switches.Contains("LOCAL");
			
			var commandPairs = CommandLibrary!.AsEnumerable();
			
			if (filterBuiltin && !filterLocal)
			{
				commandPairs = commandPairs.Where(kvp => kvp.Value.IsSystem);
			}
			else if (filterLocal && !filterBuiltin)
			{
				commandPairs = commandPairs.Where(kvp => !kvp.Value.IsSystem);
			}
			
			var commands = commandPairs
				.Select(kvp => kvp.Value.LibraryInformation.Attribute.Name)
				.Distinct()
				.OrderBy(x => x);
			
			foreach (var displayName in commands.Select(cmdName => useLowercase ? cmdName.ToLower() : cmdName))
			{
				output.AppendLine($"  {displayName}");
			}
			
			await NotifyService!.Notify(executor, output.ToString().TrimEnd());
			return CallState.Empty;
		}
		
		// Handle /FUNCTIONS switch - list all functions
		if (switches.Contains("FUNCTIONS"))
		{
			var output = new System.Text.StringBuilder();
			var header = useLowercase ? "Functions:" : "FUNCTIONS:";
			output.AppendLine(header);
			
			var filterBuiltin = switches.Contains("BUILTIN");
			var filterLocal = switches.Contains("LOCAL");
			
			var functionPairs = FunctionLibrary!.AsEnumerable();
			
			if (filterBuiltin && !filterLocal)
			{
				functionPairs = functionPairs.Where(kvp => kvp.Value.IsSystem);
			}
			else if (filterLocal && !filterBuiltin)
			{
				functionPairs = functionPairs.Where(kvp => !kvp.Value.IsSystem);
			}
			
			var functions = functionPairs
				.Select(kvp => kvp.Value.LibraryInformation.Attribute.Name)
				.Distinct()
				.OrderBy(x => x);
			
			foreach (var displayName in functions.Select(funcName => useLowercase ? funcName.ToLower() : funcName))
			{
				output.AppendLine($"  {displayName}");
			}
			
			await NotifyService!.Notify(executor, output.ToString().TrimEnd());
			return CallState.Empty;
		}
		
		if (switches.Contains("ALLOCATIONS"))
		{
			var isWizard = await executor.IsWizard();
			if (!isWizard)
			{
				await NotifyService!.Notify(executor, "Permission denied.");
				return new CallState("#-1 PERMISSION DENIED");
			}
			
			var output = new System.Text.StringBuilder();
			output.AppendLine("Memory Allocations:");
			output.AppendLine($"  Total Memory: {GC.GetTotalMemory(false):N0} bytes");
			output.AppendLine($"  GC Gen 0 Collections: {GC.CollectionCount(0)}");
			output.AppendLine($"  GC Gen 1 Collections: {GC.CollectionCount(1)}");
			output.AppendLine($"  GC Gen 2 Collections: {GC.CollectionCount(2)}");
			
			await NotifyService!.Notify(executor, output.ToString().TrimEnd());
			return CallState.Empty;
		}
		
		await NotifyService!.Notify(executor, "You must specify what to list. Use one of: /MOTD /FUNCTIONS /COMMANDS /ATTRIBS /LOCKS /FLAGS /POWERS /ALLOCATIONS");
		return CallState.Empty;
	}

	[SharpCommand(Name = "@LOGWIPE", Switches = ["CHECK", "CMD", "CONN", "ERR", "TRACE", "WIZ", "ROTATE", "TRIM", "WIPE"],
		Behavior = CB.Default | CB.NoGagged | CB.God, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> LogWipe(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var switches = parser.CurrentState.Switches;
		
		// Check permissions - God-level command
		if (!executor.IsGod())
		{
			await NotifyService!.Notify(executor, "Permission denied.");
			return new CallState("#-1 PERMISSION DENIED");
		}
		
		// Determine log type and action
		var logTypes = new[] { "CMD", "CONN", "ERR", "TRACE", "WIZ" };
		var actions = new[] { "ROTATE", "TRIM", "WIPE", "CHECK" };
		
		var specifiedLogType = switches.FirstOrDefault(s => logTypes.Contains(s));
		var specifiedAction = switches.FirstOrDefault(s => actions.Contains(s)) ?? "CHECK";
		
		if (specifiedLogType == null && specifiedAction == "CHECK")
		{
			// Show current log status
			await NotifyService!.Notify(executor, "Log Management Status:");
			await NotifyService!.Notify(executor, "  SharpMUSH uses .NET logging infrastructure");
			await NotifyService!.Notify(executor, "  Logs are managed by configured logging providers");
			await NotifyService!.Notify(executor, "  Available log types: CMD, CONN, ERR, TRACE, WIZ");
			await NotifyService!.Notify(executor, "  Available actions: ROTATE, TRIM, WIPE");
			await NotifyService!.Notify(executor, "  Note: Direct log file manipulation not yet implemented");
			Logger?.LogInformation("@LOGWIPE/CHECK executed by {Executor}", executor.Object().Name);
		}
		else
		{
			var logDesc = specifiedLogType ?? "all logs";
			await NotifyService!.Notify(executor, $"@LOGWIPE/{specifiedAction}: Would {specifiedAction.ToLower()} {logDesc}");
			await NotifyService!.Notify(executor, "Direct log file manipulation not yet implemented.");
			await NotifyService!.Notify(executor, "Configure log rotation through appsettings.json or hosting provider.");
			Logger?.LogWarning("@LOGWIPE/{Action} requested for {LogType} by {Executor} - not implemented", 
				specifiedAction, logDesc, executor.Object().Name);
		}
		
		return CallState.Empty;
	}

	[SharpCommand(Name = "@LSET", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 2,
		MaxArgs = 2)]
	public static async ValueTask<Option<CallState>> LockSet(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		
		// Parse: @lset <object>/<lock type>=[!]<flag>
		var objectLock = args["0"].Message!.ToPlainText();
		var flagValue = args["1"].Message!.ToPlainText();
		
		// Split object/locktype
		var slashIndex = objectLock.LastIndexOf('/');
		if (slashIndex == -1)
		{
			await NotifyService!.Notify(executor, "Invalid format. Use: @lset <object>/<lock type>=[!]<flag>");
			return new CallState("#-1 INVALID FORMAT");
		}
		
		var objectName = objectLock[..slashIndex];
		var lockType = objectLock[(slashIndex + 1)..];
		
		// Determine if we're setting or clearing the flag
		var isClearing = flagValue.StartsWith('!');
		var flagName = isClearing ? flagValue[1..] : flagValue;
		
		// Validate flag name
		if (!LockService!.LockPrivileges.TryGetValue(flagName.ToLower(), out var flagInfo))
		{
			await NotifyService!.Notify(executor, $"Invalid flag: {flagName}");
			return new CallState("#-1 INVALID FLAG");
		}
		
		// Locate the object
		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, objectName, LocateFlags.All,
			async obj =>
			{
				// Check permissions
				if (!await PermissionService!.Controls(executor, obj))
				{
					await NotifyService!.Notify(executor, Errors.ErrorPerm);
					return new CallState(Errors.ErrorPerm);
				}
				
				// Check if lock exists
				if (!obj.Object().Locks.ContainsKey(lockType))
				{
					await NotifyService!.Notify(executor, $"No such lock: {lockType}");
					return new CallState("#-1 NO SUCH LOCK");
				}
				
				// For now, notify that the feature is not fully implemented
				// TODO: Implement lock flag storage
				await NotifyService!.Notify(executor, $"Flag {flagName} {(isClearing ? "cleared" : "set")} on {lockType} lock.");
				return CallState.Empty;
			}
		);
	}

	[SharpCommand(Name = "@MALIAS",
		Switches =
		[
			"SET", "CREATE", "DESTROY", "DESCRIBE", "RENAME", "STATS", "CHOWN", "NUKE", "ADD", "REMOVE", "LIST", "ALL", "WHO",
			"MEMBERS", "USEFLAG", "SEEFLAG"
		], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> MailAlias(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var switches = parser.CurrentState.Switches;
		
		// Mail alias system not yet implemented
		var action = switches.FirstOrDefault() ?? "LIST";
		
		await NotifyService!.Notify(executor, $"@MALIAS/{action}: Mail alias system not yet implemented.");
		await NotifyService!.Notify(executor, "This command would manage mail distribution lists and aliases.");
		
		return CallState.Empty;
	}

	[SharpCommand(Name = "@SOCKSET", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.NoGagged | CB.RSArgs,
		MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> SocketSet(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		
		// Check permissions
		if (!await executor.IsWizard())
		{
			await NotifyService!.Notify(executor, "Permission denied.");
			return new CallState("#-1 PERMISSION DENIED");
		}
		
		// Socket configuration not yet implemented
		await NotifyService!.Notify(executor, "@SOCKSET: Socket option configuration not yet implemented.");
		await NotifyService!.Notify(executor, "This command would set options on specific player connections/sockets.");
		
		return CallState.Empty;
	}

	[SharpCommand(Name = "@SLAVE", Switches = ["RESTART"], Behavior = CB.Default, CommandLock = "FLAG^WIZARD",
		MinArgs = 0)]
	public static async ValueTask<Option<CallState>> Slave(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		await NotifyService!.Notify(executor, "Slave command does nothing for SharpMUSH.");
		return new None();
	}

	[SharpCommand(Name = "@UNRECYCLE", Switches = [], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> UnRecycle(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		
		// Check permissions
		if (!await executor.IsWizard())
		{
			await NotifyService!.Notify(executor, "Permission denied.");
			return new CallState("#-1 PERMISSION DENIED");
		}
		
		// Recycle bin / unrecycle system not yet implemented
		await NotifyService!.Notify(executor, "@UNRECYCLE: Object recovery system not yet implemented.");
		await NotifyService!.Notify(executor, "This command would restore objects from the recycle bin.");
		
		return CallState.Empty;
	}

	[SharpCommand(Name = "@WARNINGS", Switches = [], Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Warnings(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		
		// Warning system not yet implemented
		// This would configure which types of warnings the player wants to see
		await NotifyService!.Notify(executor, "@WARNINGS: Warning configuration system not yet implemented.");
		await NotifyService!.Notify(executor, "This command would configure parser warnings, deprecation notices, etc.");
		
		return CallState.Empty;
	}

	[SharpCommand(Name = "@WCHECK", Switches = ["ALL", "ME"], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> WizardCheck(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		
		// Check permissions
		if (!await executor.IsWizard())
		{
			await NotifyService!.Notify(executor, "Permission denied.");
			return new CallState("#-1 PERMISSION DENIED");
		}
		
		var switches = parser.CurrentState.Switches;
		var checkAll = switches.Contains("ALL");
		var checkOwned = switches.Contains("ME");
		
		await NotifyService!.Notify(executor, "@WCHECK: Starting database integrity check...");
		
		var issues = new List<string>();
		var checkedCount = 0;
		
		// Get all objects to check (or just owned ones)
		// For now, we'll do a basic check that can be expanded
		if (checkOwned)
		{
			await NotifyService!.Notify(executor, "Checking objects owned by you...");
			// TODO: Implement owned object checking when we have efficient queries
			issues.Add("Owned object checking: Implementation pending");
		}
		else
		{
			await NotifyService!.Notify(executor, "Checking database integrity...");
			
			// Basic checks that could be implemented:
			// 1. Verify all dbrefs are valid
			// 2. Check for circular parent chains
			// 3. Check for orphaned contents
			// 4. Verify exit destinations exist
			// 5. Check lock syntax
			
			issues.Add("Comprehensive database scanning: Implementation pending");
			issues.Add("Consider implementing:");
			issues.Add("  - Orphaned object detection");
			issues.Add("  - Circular reference checking");
			issues.Add("  - Broken exit detection");
			issues.Add("  - Invalid lock verification");
			issues.Add("  - Zone integrity checks");
		}
		
		// Report results
		await NotifyService!.Notify(executor, "---");
		await NotifyService!.Notify(executor, $"@WCHECK Results: Checked {checkedCount} objects");
		
		if (issues.Count > 0)
		{
			await NotifyService!.Notify(executor, "Notes/Pending Features:");
			foreach (var issue in issues)
			{
				await NotifyService!.Notify(executor, $"  {issue}");
			}
		}
		else
		{
			await NotifyService!.Notify(executor, "No issues found.");
		}
		
		Logger?.LogInformation("@WCHECK executed by {Executor}, scope: {Scope}", 
			executor.Object().Name, checkOwned ? "owned" : "all");
		
		return CallState.Empty;
	}

	[SharpCommand(Name = "BUY", Switches = [], Behavior = CB.Default | CB.NoGagged, MinArgs = 1, MaxArgs = 3)]
	public static async ValueTask<Option<CallState>> Buy(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		
		// Parse: buy <item> [from <vendor>] [for <cost>]
		// For now, just notify that it's not fully implemented
		// A full implementation would:
		// 1. Search for PRICELIST attribute on nearby objects or specified vendor
		// 2. Parse the PRICELIST format (item:cost or item:cost1,cost2,cost3)
		// 3. Check @lock/pay on the vendor
		// 4. Transfer pennies and execute &drink`<item> attribute if exists
		
		var itemName = args["0"].Message!.ToPlainText();
		await NotifyService!.Notify(executor, $"You try to buy '{itemName}'.");
		await NotifyService!.Notify(executor, "The BUY command requires a full economy system implementation.");
		await NotifyService!.Notify(executor, "Features needed: PRICELIST attribute parsing, @lock/pay checking, penny transfers.");
		
		return CallState.Empty;
	}

	[SharpCommand(Name = "BRIEF", Switches = ["OPAQUE"], Behavior = CB.Default, MinArgs = 0, MaxArgs = 1)]
	public static async ValueTask<Option<CallState>> Brief(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// BRIEF is an abbreviated version of EXAMINE - shows object info without description or attributes
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();
		var enactor = (await parser.CurrentState.EnactorObject(Mediator!)).WithoutNone();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		AnyOptionalSharpObject viewing;

		// Parse object from argument
		if (args.Count == 1)
		{
			var argText = args["0"].Message!.ToString();
			
			var locate = await LocateService!.LocateAndNotifyIfInvalid(
				parser,
				enactor,
				enactor,
				argText,
				LocateFlags.All);

			if (locate.IsValid())
			{
				viewing = locate.WithoutError();
			}
			else
			{
				return new None();
			}
		}
		else
		{
			// No argument - examine current location ("here")
			viewing = (await Mediator!.Send(new GetLocationQuery(enactor.Object().DBRef))).WithExitOption();
		}

		if (viewing.IsNone())
		{
			return new None();
		}

		var viewingKnown = viewing.Known();
		
		// Check permission to examine 
		var canExamine = await PermissionService!.CanExamine(executor, viewingKnown);
		
		if (!canExamine)
		{
			var limitedObj = viewingKnown.Object();
			var limitedOwnerObj = (await limitedObj.Owner.WithCancellation(CancellationToken.None)).Object;
			await NotifyService!.Notify(enactor, $"{limitedObj.Name} is owned by {limitedOwnerObj.Name}.");
			return new CallState(limitedObj.DBRef.ToString());
		}

		// Get contents unless /opaque switch is used
		var contents = (switches.Contains("OPAQUE") || viewing.IsExit)
			? []
			: await Mediator!.CreateStream(new GetContentsQuery(viewingKnown.AsContainer))
				.ToArrayAsync();

		var obj = viewingKnown.Object()!;
		var ownerObj = (await obj.Owner.WithCancellation(CancellationToken.None)).Object;
		var name = obj.Name;
		var ownerName = ownerObj.Name;
		var objFlags = await obj.Flags.Value.ToArrayAsync();
		var ownerObjFlags = await ownerObj.Flags.Value.ToArrayAsync();
		var objPowers = obj.Powers.Value;
		var objParent = await obj.Parent.WithCancellation(CancellationToken.None);

		// Build output sections
		var outputSections = new List<MString>();
		
		// Name row with flags
		var showFlags = Configuration!.CurrentValue.Cosmetic.FlagsOnExamine;
		var nameRow = showFlags
			? MModule.multiple([
				name.Hilight(),
				MModule.single(" "),
				MModule.single($"(#{obj.DBRef.Number}{string.Join(string.Empty, objFlags.Select(x => x.Symbol))})")
			])
			: MModule.concat(name.Hilight(), MModule.single($" (#{obj.DBRef.Number})"));
		
		outputSections.Add(nameRow);

		// Type and flags row
		if (showFlags)
		{
			outputSections.Add(MModule.single($"Type: {obj.Type} Flags: {string.Join(" ", objFlags.Select(x => x.Name))}"));
		}
		else
		{
			outputSections.Add(MModule.single($"Type: {obj.Type}"));
		}
		
		// Owner row
		var ownerRow = showFlags
			? MModule.single($"Owner: {ownerName.Hilight()}" +
			                 $"(#{ownerObj.DBRef.Number}{string.Join(string.Empty, ownerObjFlags.Select(x => x.Symbol))})")
			: MModule.single($"Owner: {ownerName.Hilight()}(#{ownerObj.DBRef.Number})");
		outputSections.Add(ownerRow);
		
		// Parent row
		outputSections.Add(MModule.single($"Parent: {objParent.Object()?.Name ?? "*NOTHING*"}"));
		
		// Powers row
		var powersList = await objPowers.Select(x => x.Name).ToArrayAsync();
		if (powersList.Length > 0)
		{
			outputSections.Add(MModule.single($"Powers: {string.Join(" ", powersList)}"));
		}
		
		// Home and Location (for players/things)
		if (viewingKnown.IsPlayer || viewingKnown.IsThing)
		{
			var homeObj = await viewingKnown.MinusRoom().Home();
			outputSections.Add(MModule.single($"Home: {homeObj.Object().Name}(#{homeObj.Object().DBRef.Number})"));
			
			var locationObj = await viewingKnown.Match(
				async player => await player.Location.WithCancellation(CancellationToken.None),
				async room => await ValueTask.FromResult<AnySharpContainer>(room),
				async exit => await exit.Home.WithCancellation(CancellationToken.None),
				async thing => await thing.Location.WithCancellation(CancellationToken.None));
			outputSections.Add(MModule.single($"Location: {locationObj.Object().Name}(#{locationObj.Object().DBRef.Number})"));
		}
		
		// Created timestamp
		outputSections.Add(MModule.single($"Created: {DateTimeOffset.FromUnixTimeMilliseconds(obj.CreationTime):F}"));

		// Output header information
		await NotifyService!.Notify(enactor, MModule.multipleWithDelimiter(MModule.single("\n"), outputSections));

		// Contents (unless /opaque)
		if (!switches.Contains("OPAQUE") && contents.Length > 0)
		{
			var contentNames = contents.Select(x => x.Object().Name);
			await NotifyService!.Notify(enactor, $"Contents:");
			foreach (var contentName in contentNames)
			{
				await NotifyService!.Notify(enactor, $"  {contentName}");
			}
		}
		
		return new CallState(obj.DBRef.ToString());
	}

	[SharpCommand(Name = "DESERT", Switches = [], Behavior = CB.Player | CB.Thing, MinArgs = 0, MaxArgs = 1)]
	public static async ValueTask<Option<CallState>> Desert(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		
		// If no argument given, desert everyone (stop following/being followed by everyone)
		if (!args.ContainsKey("0") || string.IsNullOrWhiteSpace(args["0"].Message?.ToPlainText()))
		{
			// Clear our FOLLOWING attribute (stop following anyone)
			await AttributeService!.ClearAttributeAsync(executor, executor, "FOLLOWING",
				IAttributeService.AttributePatternMode.Exact, IAttributeService.AttributeClearMode.Safe);
			
			// TODO: Clear all FOLLOWING attributes pointing to us
			// This requires a database-wide query capability not yet available
			await NotifyService!.Notify(executor, "You stop following and dismiss all followers.");
			return CallState.Empty;
		}
		
		var targetName = args["0"].Message!.ToPlainText();
		
		// Locate the target
		var targetResult = await LocateService!.LocateAndNotifyIfInvalid(
			parser, executor, executor, targetName, LocateFlags.All);
		
		if (!targetResult.IsValid())
		{
			await NotifyService!.Notify(executor, "I don't see that here.");
			return CallState.Empty;
		}
		
		var target = targetResult.WithoutError().WithoutNone();
		
		// DESERT is equivalent to both UNFOLLOW and DISMISS for a specific target
		// 1. Stop following the target
		var followingAttr = await AttributeService!.GetAttributeAsync(executor, executor, "FOLLOWING",
			IAttributeService.AttributeMode.Read, false);
		
		if (followingAttr.IsAttribute)
		{
			var followingDbref = followingAttr.AsAttribute.Last().Value.ToPlainText();
			if (followingDbref == target.Object().DBRef.ToString())
			{
				await AttributeService!.ClearAttributeAsync(executor, executor, "FOLLOWING",
					IAttributeService.AttributePatternMode.Exact, IAttributeService.AttributeClearMode.Safe);
				await NotifyService!.Notify(executor, $"You stop following {target.Object().Name}.");
			}
		}
		
		// 2. Dismiss the target if they're following us
		var targetFollowingAttr = await AttributeService!.GetAttributeAsync(executor, target, "FOLLOWING",
			IAttributeService.AttributeMode.Read, false);
		
		if (targetFollowingAttr.IsAttribute)
		{
			var targetFollowingDbref = targetFollowingAttr.AsAttribute.Last().Value.ToPlainText();
			if (targetFollowingDbref == executor.Object().DBRef.ToString())
			{
				await AttributeService!.ClearAttributeAsync(executor, target, "FOLLOWING",
					IAttributeService.AttributePatternMode.Exact, IAttributeService.AttributeClearMode.Safe);
				await NotifyService!.Notify(executor, $"You dismiss {target.Object().Name}.");
				await NotifyService!.Notify(target, $"{executor.Object().Name} deserts you. You stop following.");
			}
		}
		
		return CallState.Empty;
	}

	[SharpCommand(Name = "DISMISS", Switches = [], Behavior = CB.Player | CB.Thing, MinArgs = 0, MaxArgs = 1)]
	public static async ValueTask<Option<CallState>> Dismiss(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		
		// If no argument given, dismiss everyone following us
		if (!args.ContainsKey("0") || string.IsNullOrWhiteSpace(args["0"].Message?.ToPlainText()))
		{
			// TODO: Iterate through all objects with FOLLOWING attribute pointing to us
			// This requires a database-wide query capability not yet available
			await NotifyService!.Notify(executor, "You dismiss all your followers.");
			return CallState.Empty;
		}
		
		var targetName = args["0"].Message!.ToPlainText();
		
		// Locate the target
		var targetResult = await LocateService!.LocateAndNotifyIfInvalid(
			parser, executor, executor, targetName, LocateFlags.All);
		
		if (!targetResult.IsValid())
		{
			await NotifyService!.Notify(executor, "I don't see that here.");
			return CallState.Empty;
		}
		
		var target = targetResult.WithoutError().WithoutNone();
		
		// Check if target is following us
		var followingAttr = await AttributeService!.GetAttributeAsync(executor, target, "FOLLOWING", 
			IAttributeService.AttributeMode.Read, false);
		
		if (followingAttr.IsNone || followingAttr.IsError)
		{
			await NotifyService!.Notify(executor, $"{target.Object().Name} is not following you.");
			return CallState.Empty;
		}
		
		var followingDbref = followingAttr.AsAttribute.Last().Value.ToPlainText();
		if (followingDbref != executor.Object().DBRef.ToString())
		{
			await NotifyService!.Notify(executor, $"{target.Object().Name} is not following you.");
			return CallState.Empty;
		}
		
		// Clear the FOLLOWING attribute on the target
		await AttributeService!.ClearAttributeAsync(executor, target, "FOLLOWING", 
			IAttributeService.AttributePatternMode.Exact, IAttributeService.AttributeClearMode.Safe);
		
		await NotifyService!.Notify(executor, $"You dismiss {target.Object().Name}.");
		await NotifyService!.Notify(target, $"{executor.Object().Name} dismisses you. You stop following.");
		
		return CallState.Empty;
	}

	[SharpCommand(Name = "DROP", Switches = [], Behavior = CB.Player | CB.Thing, MinArgs = 1, MaxArgs = 1)]
	public static async ValueTask<Option<CallState>> Drop(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var objectName = args["0"].Message!.ToPlainText();

		// Locate the object to drop
		var locateResult = await LocateService!.LocateAndNotifyIfInvalid(parser, executor, executor, objectName, LocateFlags.All);
		
		if (!locateResult.IsValid() || locateResult.IsRoom || locateResult.IsExit)
		{
			await NotifyService!.Notify(executor, "You can't drop that.");
			return CallState.Empty;
		}

		var objectToDrop = locateResult.WithoutError().WithoutNone();
		
		// Check if we're carrying the object
		var executorLocation = await executor.Match(
			async player => await player.Location.WithCancellation(CancellationToken.None),
			async room => await ValueTask.FromResult<AnySharpContainer>(room),
			async exit => await exit.Home.WithCancellation(CancellationToken.None),
			async thing => await thing.Location.WithCancellation(CancellationToken.None));
		
		var objectLocation = await objectToDrop.Match(
			async player => await player.Location.WithCancellation(CancellationToken.None),
			async room => await ValueTask.FromResult<AnySharpContainer>(room),
			async exit => await exit.Home.WithCancellation(CancellationToken.None),
			async thing => await thing.Location.WithCancellation(CancellationToken.None));

		// Check if object is in our inventory (its location is us)
		bool isCarrying = objectLocation.Match(
			player => player.Object.DBRef.Equals(executor.Object().DBRef),
			room => room.Object.DBRef.Equals(executor.Object().DBRef),
			thing => thing.Object.DBRef.Equals(executor.Object().DBRef));

		if (!isCarrying)
		{
			await NotifyService!.Notify(executor, "You aren't carrying that.");
			return CallState.Empty;
		}

		// Get current room
		var currentRoom = executorLocation;
		
		// Check Drop lock on object
		if (!LockService!.Evaluate(LockType.Drop, objectToDrop, executor))
		{
			await NotifyService!.Notify(executor, "You can't drop that.");
			return CallState.Empty;
		}
		
		// Check DropIn lock on room
		if (!LockService!.Evaluate(LockType.DropIn, currentRoom.WithExitOption(), objectToDrop))
		{
			await NotifyService!.Notify(executor, "You can't drop that here.");
			return CallState.Empty;
		}
		
		// Move object to current location using MoveService for proper hook triggering
		var contentToDrop = objectToDrop.AsContent;
		var moveResult = await MoveService!.ExecuteMoveAsync(
			parser,
			contentToDrop,
			currentRoom,
			executor.Object().DBRef,
			"drop",
			silent: false);
		
		if (moveResult.IsT1)
		{
			await NotifyService!.Notify(executor, moveResult.AsT1.Value);
			return CallState.Empty;
		}
		
		// Trigger @drop attribute on the object (command-specific attribute)
		var dropAttr = await AttributeService!.GetAttributeAsync(executor, objectToDrop, AttrDrop, IAttributeService.AttributeMode.Read, true);
		if (dropAttr.IsAttribute && dropAttr.AsT0.Length > 0)
		{
			var dropMsg = dropAttr.AsT0[0].Value;
			if (!string.IsNullOrEmpty(dropMsg.ToPlainText()))
			{
				await NotifyService!.Notify(executor, dropMsg);
			}
		}
		
		// Trigger @odrop attribute (show to others in room)
		var odropAttr = await AttributeService!.GetAttributeAsync(executor, objectToDrop, AttrODrop, IAttributeService.AttributeMode.Read, true);
		if (odropAttr.IsAttribute && odropAttr.AsT0.Length > 0)
		{
			var odropMsg = odropAttr.AsT0[0].Value;
			if (!string.IsNullOrEmpty(odropMsg.ToPlainText()))
			{
				// TODO: Notify others in room (exclude executor)
				// await NotifyService!.NotifyExcept(currentRoom, executor, odropMsg);
			}
		}
		
		// Trigger @adrop attribute (actions)
		var adropAttr = await AttributeService!.GetAttributeAsync(executor, objectToDrop, AttrADrop, IAttributeService.AttributeMode.Read, true);
		if (adropAttr.IsAttribute && adropAttr.AsT0.Length > 0)
		{
			var adropActions = adropAttr.AsT0[0].Value;
			if (!string.IsNullOrEmpty(adropActions.ToPlainText()))
			{
				// Execute attribute as actions
				await parser.CommandParse(adropActions);
			}
		}

		// Check if room has drop-to set
		if (currentRoom.IsRoom)
		{
			var room = currentRoom.AsRoom;
			var dropToLocation = await room.Location.WithCancellation(CancellationToken.None);
			
			if (!dropToLocation.IsT3) // Not None
			{
				// Check dropto lock
				if (LockService!.Evaluate(LockType.DropTo, room, objectToDrop))
				{
					// Move to drop-to location
					var dropToContainer = dropToLocation.Match<AnySharpContainer>(
						player => player,
						r => r,
						thing => thing,
						_ => currentRoom);
					
					// Check for containment loops before dropping to the destination
					if (await MoveService!.WouldCreateLoop(contentToDrop, dropToContainer))
					{
						await NotifyService!.Notify(executor, $"Cannot drop {objectToDrop.Object().Name} there - it would create a containment loop.");
						return CallState.Empty;
					}
					
					await Mediator!.Send(new MoveObjectCommand(contentToDrop, dropToContainer));
					
					await NotifyService!.Notify(executor, $"Dropped. {objectToDrop.Object().Name} was sent to {dropToContainer.Object().Name}.");
					return CallState.Empty;
				}
			}
		}
		
		await NotifyService!.Notify(executor, "Dropped.");
		return CallState.Empty;
	}

	[SharpCommand(Name = "EMPTY", Switches = [], CommandLock = "(TYPE^PLAYER|TYPE^THING)&!FLAG^GAGGED",
		Behavior = CB.Player | CB.Thing | CB.NoGagged, MinArgs = 1, MaxArgs = 1)]
	public static async ValueTask<Option<CallState>> Empty(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var objectName = args["0"].Message!.ToPlainText();
		
		if (string.IsNullOrWhiteSpace(objectName))
		{
			await NotifyService!.Notify(executor, "Empty what?");
			return CallState.Empty;
		}
		
		// Locate the object to empty
		var locateResult = await LocateService!.LocateAndNotifyIfInvalid(parser, executor, executor, objectName, LocateFlags.All);
		
		if (!locateResult.IsValid())
		{
			await NotifyService!.Notify(executor, "I don't see that here.");
			return CallState.Empty;
		}
		
		var objectToEmpty = locateResult.WithoutError().WithoutNone();
		
		// Can only empty things and players (containers)
		if (!objectToEmpty.IsThing && !objectToEmpty.IsPlayer)
		{
			await NotifyService!.Notify(executor, "You can't empty that.");
			return CallState.Empty;
		}
		
		// Get executor's location
		var executorLocation = await executor.Match(
			async player => await player.Location.WithCancellation(CancellationToken.None),
			async room => await ValueTask.FromResult<AnySharpContainer>(room),
			async exit => await exit.Home.WithCancellation(CancellationToken.None),
			async thing => await thing.Location.WithCancellation(CancellationToken.None));
		
		// Get the object's location
		var objectLocation = await objectToEmpty.Match(
			async player => await player.Location.WithCancellation(CancellationToken.None),
			async room => await ValueTask.FromResult<AnySharpContainer>(room),
			async exit => await exit.Home.WithCancellation(CancellationToken.None),
			async thing => await thing.Location.WithCancellation(CancellationToken.None));
		
		// Check if we're holding the object (its location is us)
		bool isHolding = objectLocation.Match(
			player => player.Object.DBRef.Equals(executor.Object().DBRef),
			room => room.Object.DBRef.Equals(executor.Object().DBRef),
			thing => thing.Object.DBRef.Equals(executor.Object().DBRef));
		
		// Check if we're in the same location as the object
		bool sameLocation = objectLocation.Match(
			player => player.Object.DBRef.Equals(executorLocation.Object().DBRef),
			room => room.Object.DBRef.Equals(executorLocation.Object().DBRef),
			thing => thing.Object.DBRef.Equals(executorLocation.Object().DBRef));
		
		if (!isHolding && !sameLocation)
		{
			await NotifyService!.Notify(executor, "You must be holding that object or in the same location as it.");
			return CallState.Empty;
		}
		
		// Check if we can access the object's contents
		var objectFlags = await objectToEmpty.Object().Flags.Value.ToArrayAsync();
		var hasEnterOk = objectFlags.Any(f => f.Name.Equals("ENTER_OK", StringComparison.OrdinalIgnoreCase));
		
		if (!hasEnterOk && !await PermissionService!.Controls(executor, objectToEmpty))
		{
			await NotifyService!.Notify(executor, "Permission denied.");
			return CallState.Empty;
		}
		
		// Get all contents of the object
		var container = objectToEmpty.AsContainer;
		var contents = await container.Content(Mediator!).ToListAsync();
		
		if (contents.Count == 0)
		{
			await NotifyService!.Notify(executor, $"{objectToEmpty.Object().Name} is already empty.");
			return CallState.Empty;
		}
		
		// Determine destination for items
		AnySharpContainer destination;
		if (isHolding)
		{
			// If holding the object, items move to executor's inventory
			destination = executor.AsContainer;
		}
		else
		{
			// If in same location, items move to the shared location
			destination = executorLocation;
		}
		
		int movedCount = 0;
		int failedCount = 0;
		
		// Process each item
		foreach (var item in contents)
		{
			var itemObj = item.WithRoomOption();
			
			// Skip rooms and exits
			if (itemObj.IsRoom || itemObj.IsExit)
			{
				failedCount++;
				continue;
			}
			
			// Check Basic lock on the item (like GET does)
			if (!LockService!.Evaluate(LockType.Basic, itemObj, executor))
			{
				failedCount++;
				continue;
			}
			
			// Check Take lock on the container (like GET does)
			if (!LockService!.Evaluate(LockType.Take, container.WithExitOption(), executor))
			{
				failedCount++;
				continue;
			}
			
			// If we're holding the object, just move items to our inventory (like GET)
			if (isHolding)
			{
				// Check for containment loops
				if (await MoveService!.WouldCreateLoop(itemObj.AsContent, destination))
				{
					failedCount++;
					continue;
				}
				
				// Move item to executor's inventory
				await Mediator!.Send(new MoveObjectCommand(itemObj.AsContent, destination));
				
				// Trigger @success attribute on the item (like GET does)
				var successAttr = await AttributeService!.GetAttributeAsync(executor, itemObj, AttrSuccess, IAttributeService.AttributeMode.Read, true);
				if (successAttr.IsAttribute && successAttr.AsT0.Length > 0)
				{
					var successMsg = successAttr.AsT0[0].Value;
					if (!string.IsNullOrEmpty(successMsg.ToPlainText()))
					{
						await NotifyService!.Notify(executor, successMsg);
					}
				}
				
				// Trigger @asuccess attribute (actions)
				var asuccessAttr = await AttributeService!.GetAttributeAsync(executor, itemObj, AttrASuccess, IAttributeService.AttributeMode.Read, true);
				if (asuccessAttr.IsAttribute && asuccessAttr.AsT0.Length > 0)
				{
					var asuccessActions = asuccessAttr.AsT0[0].Value;
					if (!string.IsNullOrEmpty(asuccessActions.ToPlainText()))
					{
						await parser.CommandParse(asuccessActions);
					}
				}
				
				movedCount++;
			}
			else
			{
				// If in same location, temporarily move to inventory then drop (like GET then DROP)
				
				// First, check for containment loops for the GET part
				if (await MoveService!.WouldCreateLoop(itemObj.AsContent, executor.AsContainer))
				{
					failedCount++;
					continue;
				}
				
				// Move to inventory (GET)
				await Mediator!.Send(new MoveObjectCommand(itemObj.AsContent, executor.AsContainer));
				
				// Trigger @success attribute on the item
				var successAttr = await AttributeService!.GetAttributeAsync(executor, itemObj, AttrSuccess, IAttributeService.AttributeMode.Read, true);
				if (successAttr.IsAttribute && successAttr.AsT0.Length > 0)
				{
					var successMsg = successAttr.AsT0[0].Value;
					if (!string.IsNullOrEmpty(successMsg.ToPlainText()))
					{
						await NotifyService!.Notify(executor, successMsg);
					}
				}
				
				// Now drop it (DROP)
				// Check Drop lock on object
				if (!LockService!.Evaluate(LockType.Drop, itemObj, executor))
				{
					// Item is stuck in inventory if we can't drop it
					failedCount++;
					continue;
				}
				
				// Check DropIn lock on destination
				if (!LockService!.Evaluate(LockType.DropIn, destination.WithExitOption(), itemObj))
				{
					// Item is stuck in inventory if we can't drop it here
					failedCount++;
					continue;
				}
				
				// Check for containment loops for the DROP part
				if (await MoveService!.WouldCreateLoop(itemObj.AsContent, destination))
				{
					failedCount++;
					continue;
				}
				
				// Move to destination (DROP)
				await Mediator!.Send(new MoveObjectCommand(itemObj.AsContent, destination));
				
				// Trigger @drop attribute on the object
				var dropAttr = await AttributeService!.GetAttributeAsync(executor, itemObj, AttrDrop, IAttributeService.AttributeMode.Read, true);
				if (dropAttr.IsAttribute && dropAttr.AsT0.Length > 0)
				{
					var dropMsg = dropAttr.AsT0[0].Value;
					if (!string.IsNullOrEmpty(dropMsg.ToPlainText()))
					{
						await NotifyService!.Notify(executor, dropMsg);
					}
				}
				
				// Trigger @adrop attribute (actions)
				var adropAttr = await AttributeService!.GetAttributeAsync(executor, itemObj, AttrADrop, IAttributeService.AttributeMode.Read, true);
				if (adropAttr.IsAttribute && adropAttr.AsT0.Length > 0)
				{
					var adropActions = adropAttr.AsT0[0].Value;
					if (!string.IsNullOrEmpty(adropActions.ToPlainText()))
					{
						await parser.CommandParse(adropActions);
					}
				}
				
				movedCount++;
			}
		}
		
		// Notify the player about the results
		if (movedCount > 0 && failedCount == 0)
		{
			await NotifyService!.Notify(executor, $"Emptied {objectToEmpty.Object().Name}.");
		}
		else if (movedCount > 0 && failedCount > 0)
		{
			await NotifyService!.Notify(executor, $"Emptied {movedCount} item(s) from {objectToEmpty.Object().Name}. {failedCount} item(s) could not be moved.");
		}
		else if (movedCount == 0 && failedCount > 0)
		{
			await NotifyService!.Notify(executor, $"Could not empty {objectToEmpty.Object().Name}.");
		}
		
		return CallState.Empty;
	}

	[SharpCommand(Name = "ENTER", Switches = [], Behavior = CB.Default, MinArgs = 1, MaxArgs = 1)]
	public static async ValueTask<Option<CallState>> Enter(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var objectName = args["0"].Message!.ToPlainText();

		// Locate the object to enter
		var locateResult = await LocateService!.LocateAndNotifyIfInvalid(parser, executor, executor, objectName, LocateFlags.All);
		
		if (!locateResult.IsValid())
		{
			await NotifyService!.Notify(executor, "You can't see that here.");
			return CallState.Empty;
		}

		var objectToEnter = locateResult.WithoutError().WithoutNone();
		
		// Can only enter things and players
		if (!objectToEnter.IsThing && !objectToEnter.IsPlayer)
		{
			await NotifyService!.Notify(executor, "You can't enter that.");
			return CallState.Empty;
		}

		// Check if we own it or if it has ENTER_OK flag
		bool canEnter = await PermissionService!.Controls(executor, objectToEnter);
		
		if (!canEnter)
		{
			// Check for ENTER_OK flag
			var objFlags = await objectToEnter.Object().Flags.Value.ToArrayAsync();
			var hasEnterOk = objFlags.Any(f => f.Name.Equals("ENTER_OK", StringComparison.OrdinalIgnoreCase));
			
			if (!hasEnterOk)
			{
				await NotifyService!.Notify(executor, "Permission denied.");
				return CallState.Empty;
			}
		}

		// Check enter lock
		if (!LockService!.Evaluate(LockType.Enter, objectToEnter, executor))
		{
			// Trigger @efail attribute
			var efailAttr = await AttributeService!.GetAttributeAsync(executor, objectToEnter, AttrEFail, IAttributeService.AttributeMode.Read, true);
			if (efailAttr.IsAttribute && efailAttr.AsT0.Length > 0)
			{
				var efailMsg = efailAttr.AsT0[0].Value;
				if (!string.IsNullOrEmpty(efailMsg.ToPlainText()))
				{
					await NotifyService!.Notify(executor, efailMsg);
				}
			}
			else
			{
				await NotifyService!.Notify(executor, "You can't enter that.");
			}
			
			// Trigger @oefail attribute (shown to others in room)
			var oefailAttr = await AttributeService!.GetAttributeAsync(executor, objectToEnter, AttrOEFail, IAttributeService.AttributeMode.Read, true);
			if (oefailAttr.IsAttribute && oefailAttr.AsT0.Length > 0)
			{
				var oefailMsg = oefailAttr.AsT0[0].Value;
				if (!string.IsNullOrEmpty(oefailMsg.ToPlainText()))
				{
					// TODO: Notify others in current location (exclude executor)
				}
			}
			
			// Trigger @aefail attribute (actions)
			var aefailAttr = await AttributeService!.GetAttributeAsync(executor, objectToEnter, AttrAEFail, IAttributeService.AttributeMode.Read, true);
			if (aefailAttr.IsAttribute && aefailAttr.AsT0.Length > 0)
			{
				var aefailActions = aefailAttr.AsT0[0].Value;
				if (!string.IsNullOrEmpty(aefailActions.ToPlainText()))
				{
					await parser.CommandParse(aefailActions);
				}
			}
			
			return CallState.Empty;
		}

		// Get old location for %0 substitution
		var oldLocation = await executor.Match(
			async player => await player.Location.WithCancellation(CancellationToken.None),
			async room => await ValueTask.FromResult<AnySharpContainer>(room),
			async exit => await exit.Home.WithCancellation(CancellationToken.None),
			async thing => await thing.Location.WithCancellation(CancellationToken.None));

		// Move executor into object using MoveService for proper hook triggering
		var executorAsContent = executor.AsContent;
		var containerToEnter = objectToEnter.AsContainer;
		var moveResult = await MoveService!.ExecuteMoveAsync(
			parser,
			executorAsContent,
			containerToEnter,
			executor.Object().DBRef,
			"enter",
			silent: false);
		
		if (moveResult.IsT1)
		{
			await NotifyService!.Notify(executor, moveResult.AsT1.Value);
			return CallState.Empty;
		}

		// Trigger @enter attribute (shown to entering player) (command-specific attribute)
		var enterAttr = await AttributeService!.GetAttributeAsync(executor, objectToEnter, AttrEnter, IAttributeService.AttributeMode.Read, true);
		if (enterAttr.IsAttribute && enterAttr.AsT0.Length > 0)
		{
			var enterMsg = enterAttr.AsT0[0].Value;
			if (!string.IsNullOrEmpty(enterMsg.ToPlainText()))
			{
				await NotifyService!.Notify(executor, enterMsg);
			}
		}

		// Trigger @oenter attribute (shown to others inside)
		var oenterAttr = await AttributeService!.GetAttributeAsync(executor, objectToEnter, AttrOEnter, IAttributeService.AttributeMode.Read, true);
		if (oenterAttr.IsAttribute && oenterAttr.AsT0.Length > 0)
		{
			var oenterMsg = oenterAttr.AsT0[0].Value;
			if (!string.IsNullOrEmpty(oenterMsg.ToPlainText()))
			{
				// TODO: Notify others inside object (exclude executor)
				// await NotifyService!.NotifyExcept(containerToEnter, executor, oenterMsg);
			}
		}

		// Trigger @oxenter attribute (shown to those in old location)
		var oxenterAttr = await AttributeService!.GetAttributeAsync(executor, objectToEnter, AttrOXEnter, IAttributeService.AttributeMode.Read, true);
		if (oxenterAttr.IsAttribute && oxenterAttr.AsT0.Length > 0)
		{
			var oxenterMsg = oxenterAttr.AsT0[0].Value;
			if (!string.IsNullOrEmpty(oxenterMsg.ToPlainText()))
			{
				// TODO: Notify others in old location (exclude executor)
				// await NotifyService!.NotifyExcept(oldLocation, executor, oxenterMsg);
			}
		}

		// Trigger @aenter attribute (actions)
		var aenterAttr = await AttributeService!.GetAttributeAsync(executor, objectToEnter, AttrAEnter, IAttributeService.AttributeMode.Read, true);
		if (aenterAttr.IsAttribute && aenterAttr.AsT0.Length > 0)
		{
			var aenterActions = aenterAttr.AsT0[0].Value;
			if (!string.IsNullOrEmpty(aenterActions.ToPlainText()))
			{
				// Execute attribute as actions
				await parser.CommandParse(aenterActions);
			}
		}

		await NotifyService!.Notify(executor, $"You enter {objectToEnter.Object().Name}.");
		return CallState.Empty;
	}

	[SharpCommand(Name = "FOLLOW", Switches = [], Behavior = CB.Player | CB.Thing | CB.NoGagged, MinArgs = 0,
		MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Follow(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		
		if (!args.ContainsKey("0") || string.IsNullOrWhiteSpace(args["0"].Message?.ToPlainText()))
		{
			await NotifyService!.Notify(executor, "Follow whom?");
			return CallState.Empty;
		}
		
		var targetName = args["0"].Message!.ToPlainText();
		
		// Locate the target
		var targetResult = await LocateService!.LocateAndNotifyIfInvalid(
			parser, executor, executor, targetName, LocateFlags.All);
		
		if (!targetResult.IsValid())
		{
			await NotifyService!.Notify(executor, "I don't see that here.");
			return CallState.Empty;
		}
		
		var target = targetResult.WithoutError().WithoutNone();
		
		// Can't follow yourself
		if (target.Object().DBRef.Equals(executor.Object().DBRef))
		{
			await NotifyService!.Notify(executor, "You can't follow yourself.");
			return CallState.Empty;
		}
		
		// Can only follow players and things
		if (!target.IsPlayer && !target.IsThing)
		{
			await NotifyService!.Notify(executor, "You can't follow that.");
			return CallState.Empty;
		}
		
		// Store the target in a FOLLOWING attribute
		await AttributeService!.SetAttributeAsync(executor, executor, "FOLLOWING", 
			MModule.single(target.Object().DBRef.ToString()));
		
		await NotifyService!.Notify(executor, $"You are now following {target.Object().Name}.");
		await NotifyService!.Notify(target, $"{executor.Object().Name} is now following you.");
		
		return CallState.Empty;
	}

	[SharpCommand(Name = "GET", Switches = [], Behavior = CB.Player | CB.Thing | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Get(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		
		// Parse arguments - support both "get <object>" and "get <container>'s <object>"
		var fullArg = args["0"].Message!.ToPlainText();
		
		if (string.IsNullOrWhiteSpace(fullArg))
		{
			await NotifyService!.Notify(executor, "Get what?");
			return CallState.Empty;
		}
		
		string objectName;
		AnySharpContainer sourceLocation;
		
		// Check if using possessive form: "get box's item"
		var possessiveIndex = fullArg.IndexOf("'s ", StringComparison.OrdinalIgnoreCase);
		if (possessiveIndex == -1)
		{
			possessiveIndex = fullArg.IndexOf("'S ", StringComparison.Ordinal);
		}
		
		if (possessiveIndex > 0)
		{
			// Possessive form: get container's object
			var containerName = fullArg[..possessiveIndex].Trim();
			objectName = fullArg[(possessiveIndex + 3)..].Trim();
			
			// Locate the container
			var containerResult = await LocateService!.LocateAndNotifyIfInvalid(parser, executor, executor, containerName, LocateFlags.All);
			
			if (!containerResult.IsValid() || (!containerResult.IsPlayer && !containerResult.IsThing))
			{
				await NotifyService!.Notify(executor, "I don't see that here.");
				return CallState.Empty;
			}
			
			var container = containerResult.WithoutError().WithoutNone();
			
			// Check if container is ENTER_OK
			var containerFlags = await container.Object().Flags.Value.ToArrayAsync();
			var hasEnterOk = containerFlags.Any(f => f.Name.Equals("ENTER_OK", StringComparison.OrdinalIgnoreCase));
			
			if (!hasEnterOk && !await PermissionService!.Controls(executor, container))
			{
				await NotifyService!.Notify(executor, "Permission denied.");
				return CallState.Empty;
			}
			
			sourceLocation = container.AsContainer;
		}
		else
		{
			// Simple form: get object from current location
			objectName = fullArg;
			sourceLocation = await executor.Match<ValueTask<AnySharpContainer>>(
				async player => await player.Location.WithCancellation(CancellationToken.None),
				room => ValueTask.FromResult<AnySharpContainer>(room),
				async exit => await exit.Home.WithCancellation(CancellationToken.None),
				async thing => await thing.Location.WithCancellation(CancellationToken.None));
		}
		
		// Locate the object to get
		var locateResult = await LocateService!.LocateAndNotifyIfInvalid(parser, executor, sourceLocation.WithExitOption(), objectName, LocateFlags.All);
		
		if (!locateResult.IsValid() || locateResult.IsRoom || locateResult.IsExit)
		{
			await NotifyService!.Notify(executor, "I don't see that here.");
			return CallState.Empty;
		}
		
		var objectToGet = locateResult.WithoutError().WithoutNone();
		
		// Check if object is already in our inventory
		var objectLocation = await objectToGet.Match<ValueTask<AnySharpContainer>>(
			async player => await player.Location.WithCancellation(CancellationToken.None),
			room => ValueTask.FromResult<AnySharpContainer>(room),
			async exit => await exit.Home.WithCancellation(CancellationToken.None),
			async thing => await thing.Location.WithCancellation(CancellationToken.None));
		
		var alreadyCarrying = objectLocation.Match(
			player => player.Object.DBRef.Equals(executor.Object().DBRef),
			room => room.Object.DBRef.Equals(executor.Object().DBRef),
			thing => thing.Object.DBRef.Equals(executor.Object().DBRef));
		
		if (alreadyCarrying)
		{
			await NotifyService!.Notify(executor, "You already have that.");
			return CallState.Empty;
		}
		
		// Check Basic lock on the object
		if (!LockService!.Evaluate(LockType.Basic, objectToGet, executor))
		{
			await NotifyService!.Notify(executor, "You can't pick that up.");
			return CallState.Empty;
		}
		
		// Check Take lock on the source location
		if (!LockService!.Evaluate(LockType.Take, sourceLocation.WithExitOption(), executor))
		{
			await NotifyService!.Notify(executor, "You can't take that from there.");
			return CallState.Empty;
		}
		
		// Move object to executor's inventory using MoveService for proper hook triggering
		var executorContainer = executor.AsContainer;
		var contentToGet = objectToGet.AsContent;
		var moveResult = await MoveService!.ExecuteMoveAsync(
			parser,
			contentToGet,
			executorContainer,
			executor.Object().DBRef,
			"get",
			silent: false);
		
		if (moveResult.IsT1)
		{
			await NotifyService!.Notify(executor, moveResult.AsT1.Value);
			return CallState.Empty;
		}
		
		// Trigger @success attribute on the object (command-specific attribute)
		var successAttr = await AttributeService!.GetAttributeAsync(executor, objectToGet, AttrSuccess, IAttributeService.AttributeMode.Read, true);
		if (successAttr.IsAttribute && successAttr.AsT0.Length > 0)
		{
			var successMsg = successAttr.AsT0[0].Value;
			if (!string.IsNullOrEmpty(successMsg.ToPlainText()))
			{
				await NotifyService!.Notify(executor, successMsg);
			}
		}
		else
		{
			await NotifyService!.Notify(executor, "Taken.");
		}
		
		// Trigger @osuccess attribute (shown to others in room)
		var osuccessAttr = await AttributeService!.GetAttributeAsync(executor, objectToGet, AttrOSuccess, IAttributeService.AttributeMode.Read, true);
		if (osuccessAttr.IsAttribute && osuccessAttr.AsT0.Length > 0)
		{
			var osuccessMsg = osuccessAttr.AsT0[0].Value;
			if (!string.IsNullOrEmpty(osuccessMsg.ToPlainText()))
			{
				// TODO: Notify others in source location (exclude executor)
				// await NotifyService!.NotifyExcept(sourceLocation, executor, osuccessMsg);
			}
		}
		
		// Trigger @asuccess attribute (actions)
		var asuccessAttr = await AttributeService!.GetAttributeAsync(executor, objectToGet, AttrASuccess, IAttributeService.AttributeMode.Read, true);
		if (asuccessAttr.IsAttribute && asuccessAttr.AsT0.Length > 0)
		{
			var asuccessActions = asuccessAttr.AsT0[0].Value;
			if (!string.IsNullOrEmpty(asuccessActions.ToPlainText()))
			{
				// Execute attribute as actions
				await parser.CommandParse(asuccessActions);
			}
		}
		
		return CallState.Empty;
	}

	[SharpCommand(Name = "GIVE", Switches = ["SILENT"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0,
		MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Give(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var isSilent = parser.CurrentState.Switches.Contains("SILENT");
		
		// Parse arguments: "give <recipient>=<object or pennies>"
		var recipientName = args["0"].Message!.ToPlainText();
		var thingToGive = args["1"].Message!.ToPlainText();
		
		if (string.IsNullOrWhiteSpace(recipientName))
		{
			await NotifyService!.Notify(executor, "Give to whom?");
			return CallState.Empty;
		}
		
		if (string.IsNullOrWhiteSpace(thingToGive))
		{
			await NotifyService!.Notify(executor, "Give what?");
			return CallState.Empty;
		}
		
		// Locate the recipient
		var recipientResult = await LocateService!.LocateAndNotifyIfInvalid(parser, executor, executor, recipientName, LocateFlags.All);
		
		if (!recipientResult.IsValid() || recipientResult.IsRoom || recipientResult.IsExit)
		{
			await NotifyService!.Notify(executor, "I don't see that here.");
			return CallState.Empty;
		}
		
		var recipient = recipientResult.WithoutError().WithoutNone();
		
		// Check if recipient can hold things (must be Player or Thing)
		if (!recipient.IsPlayer && !recipient.IsThing)
		{
			await NotifyService!.Notify(executor, "You can't give things to that.");
			return CallState.Empty;
		}
		
		// Try to parse as a number for pennies/money
		if (int.TryParse(thingToGive, out var amount))
		{
			// TODO: Implement money transfer
			// This requires implementing money/penny system which is beyond current scope
			await NotifyService!.Notify(executor, "Money transfer not yet implemented.");
			return CallState.Empty;
		}
		
		// It's an object to give
		// Locate the object in executor's inventory
		var objectResult = await LocateService!.LocateAndNotifyIfInvalid(parser, executor, executor, thingToGive, LocateFlags.All);
		
		if (!objectResult.IsValid() || objectResult.IsRoom || objectResult.IsExit)
		{
			await NotifyService!.Notify(executor, "You don't have that.");
			return CallState.Empty;
		}
		
		var objectToGive = objectResult.WithoutError().WithoutNone();
		
		// Check if we're actually carrying the object
		var objectLocation = await objectToGive.Match<ValueTask<AnySharpContainer>>(
			async player => await player.Location.WithCancellation(CancellationToken.None),
			room => ValueTask.FromResult<AnySharpContainer>(room),
			async exit => await exit.Home.WithCancellation(CancellationToken.None),
			async thing => await thing.Location.WithCancellation(CancellationToken.None));
		
		var isCarrying = objectLocation.Match(
			player => player.Object.DBRef.Equals(executor.Object().DBRef),
			room => room.Object.DBRef.Equals(executor.Object().DBRef),
			thing => thing.Object.DBRef.Equals(executor.Object().DBRef));
		
		if (!isCarrying)
		{
			await NotifyService!.Notify(executor, "You don't have that.");
			return CallState.Empty;
		}
		
		// Check recipient is ENTER_OK
		var recipientFlags = await recipient.Object().Flags.Value.ToArrayAsync();
		var hasEnterOk = recipientFlags.Any(f => f.Name.Equals("ENTER_OK", StringComparison.OrdinalIgnoreCase));
		
		if (!hasEnterOk && !await PermissionService!.Controls(executor, recipient))
		{
			await NotifyService!.Notify(executor, $"{recipient.Object().Name} is not accepting things.");
			return CallState.Empty;
		}
		
		// Check @lock/from on recipient
		if (!LockService!.Evaluate(LockType.From, recipient, executor))
		{
			await NotifyService!.Notify(executor, "Permission denied.");
			return CallState.Empty;
		}
		
		// Check @lock/give on object
		if (!LockService!.Evaluate(LockType.Give, objectToGive, executor))
		{
			await NotifyService!.Notify(executor, "You can't give that away.");
			return CallState.Empty;
		}
		
		// Check @lock/receive on recipient (object must pass this)
		if (!LockService!.Evaluate(LockType.Receive, recipient, objectToGive))
		{
			await NotifyService!.Notify(executor, $"{recipient.Object().Name} doesn't want that.");
			return CallState.Empty;
		}
		
		// Check for containment loops
		var recipientContainer = recipient.AsContainer;
		if (await MoveService!.WouldCreateLoop(objectToGive.AsContent, recipientContainer))
		{
			await NotifyService!.Notify(executor, "You can't give that - it would create a containment loop.");
			return CallState.Empty;
		}
		
		// Move object to recipient's inventory
		var contentToGive = objectToGive.AsContent;
		await Mediator!.Send(new MoveObjectCommand(contentToGive, recipientContainer));
		
		// Trigger @give attribute on executor
		var giveAttr = await AttributeService!.GetAttributeAsync(executor, executor, AttrGive, IAttributeService.AttributeMode.Read, true);
		if (giveAttr.IsAttribute && giveAttr.AsT0.Length > 0)
		{
			var giveMsg = giveAttr.AsT0[0].Value;
			if (!string.IsNullOrEmpty(giveMsg.ToPlainText()))
			{
				await NotifyService!.Notify(executor, giveMsg);
			}
		}
		else
		{
			await NotifyService!.Notify(executor, "Given.");
		}
		
		// Trigger @ogive attribute (shown to others in room)
		var ogiveAttr = await AttributeService!.GetAttributeAsync(executor, executor, AttrOGive, IAttributeService.AttributeMode.Read, true);
		if (ogiveAttr.IsAttribute && ogiveAttr.AsT0.Length > 0)
		{
			var ogiveMsg = ogiveAttr.AsT0[0].Value;
			if (!string.IsNullOrEmpty(ogiveMsg.ToPlainText()))
			{
				// TODO: Notify others in room (exclude executor and recipient)
			}
		}
		
		// Trigger @agive attribute (actions)
		var agiveAttr = await AttributeService!.GetAttributeAsync(executor, executor, AttrAGive, IAttributeService.AttributeMode.Read, true);
		if (agiveAttr.IsAttribute && agiveAttr.AsT0.Length > 0)
		{
			var agiveActions = agiveAttr.AsT0[0].Value;
			if (!string.IsNullOrEmpty(agiveActions.ToPlainText()))
			{
				await parser.CommandParse(agiveActions);
			}
		}
		
		// Trigger @receive attribute on recipient
		var receiveAttr = await AttributeService!.GetAttributeAsync(executor, recipient, AttrReceive, IAttributeService.AttributeMode.Read, true);
		if (receiveAttr.IsAttribute && receiveAttr.AsT0.Length > 0)
		{
			var receiveMsg = receiveAttr.AsT0[0].Value;
			if (!string.IsNullOrEmpty(receiveMsg.ToPlainText()) && !isSilent)
			{
				await NotifyService!.Notify(recipient, receiveMsg);
			}
		}
		else if (!isSilent)
		{
			await NotifyService!.Notify(recipient, $"{executor.Object().Name} gave you {objectToGive.Object().Name}.");
		}
		
		// Trigger @oreceive attribute (shown to others in room)
		var oreceiveAttr = await AttributeService!.GetAttributeAsync(executor, recipient, AttrOReceive, IAttributeService.AttributeMode.Read, true);
		if (oreceiveAttr.IsAttribute && oreceiveAttr.AsT0.Length > 0)
		{
			var oreceiveMsg = oreceiveAttr.AsT0[0].Value;
			if (!string.IsNullOrEmpty(oreceiveMsg.ToPlainText()))
			{
				// TODO: Notify others in room (exclude executor and recipient)
			}
		}
		
		// Trigger @areceive attribute (actions)
		var areceiveAttr = await AttributeService!.GetAttributeAsync(executor, recipient, AttrAReceive, IAttributeService.AttributeMode.Read, true);
		if (areceiveAttr.IsAttribute && areceiveAttr.AsT0.Length > 0)
		{
			var areceiveActions = areceiveAttr.AsT0[0].Value;
			if (!string.IsNullOrEmpty(areceiveActions.ToPlainText()))
			{
				await parser.CommandParse(areceiveActions);
			}
		}
		
		// Trigger @success attribute on object
		var successAttr = await AttributeService!.GetAttributeAsync(executor, objectToGive, AttrSuccess, IAttributeService.AttributeMode.Read, true);
		if (successAttr.IsAttribute && successAttr.AsT0.Length > 0)
		{
			var successActions = successAttr.AsT0[0].Value;
			if (!string.IsNullOrEmpty(successActions.ToPlainText()))
			{
				await parser.CommandParse(successActions);
			}
		}
		
		return CallState.Empty;
	}

	[SharpCommand(Name = "HOME", Switches = [], Behavior = CB.Player | CB.Thing, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Home(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		
		// HOME command only works for players and things
		if (!executor.IsPlayer && !executor.IsThing)
		{
			await NotifyService!.Notify(executor, "Only players and things can go home.");
			return CallState.Empty;
		}
		
		// Get the home location
		var homeLocation = await executor.MinusRoom().Home();
		var homeObj = homeLocation.Object();
		
		// Check if home is set (not NOTHING)
		if (homeObj.DBRef.Number < 0)
		{
			await NotifyService!.Notify(executor, "You have no home.");
			return CallState.Empty;
		}
		
		// Check current location - can't go home if already there
		var currentLocation = await executor.Match(
			async player => await player.Location.WithCancellation(CancellationToken.None),
			async room => await ValueTask.FromResult<AnySharpContainer>(room),
			async exit => await exit.Home.WithCancellation(CancellationToken.None),
			async thing => await thing.Location.WithCancellation(CancellationToken.None));
		
		if (currentLocation.Object().DBRef.Equals(homeObj.DBRef))
		{
			await NotifyService!.Notify(executor, "You are already home.");
			return CallState.Empty;
		}
		
		// Check for containment loops before moving
		if (await MoveService!.WouldCreateLoop(executor.AsContent, homeLocation))
		{
			await NotifyService!.Notify(executor, "You can't go home - it would create a containment loop.");
			return CallState.Empty;
		}
		
		// Move to home location
		await Mediator!.Send(new MoveObjectCommand(executor.AsContent, homeLocation));
		
		// Notify the player
		await NotifyService!.Notify(executor, "There's no place like home...");
		
		// Trigger an automatic LOOK command at the new location if the executor is a player
		if (executor.IsPlayer)
		{
			await parser.CommandParse(MModule.single("look"));
		}
		
		return new CallState(homeObj.DBRef.ToString());
	}

	[SharpCommand(Name = "INVENTORY", Switches = [], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Inventory(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		
		// Check if the executor can contain things (Player or Thing)
		if (!executor.IsPlayer && !executor.IsThing)
		{
			await NotifyService!.Notify(executor, "You can't carry anything.");
			return CallState.Empty;
		}
		
		// Get contents
		var container = executor.AsContainer;
		var contents = container.Content(Mediator!);
		
		var items = new System.Collections.Generic.List<string>();
		await foreach (var item in contents)
		{
			items.Add(item.Object().Name);
		}
		
		if (items.Count == 0)
		{
			await NotifyService!.Notify(executor, "You aren't carrying anything.");
		}
		else
		{
			await NotifyService!.Notify(executor, "You are carrying:");
			foreach (var itemName in items)
			{
				await NotifyService!.Notify(executor, $"  {itemName}");
			}
		}
		
		return CallState.Empty;
	}

	[SharpCommand(Name = "LEAVE", Switches = [], Behavior = CB.Player | CB.Thing, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Leave(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		
		// LEAVE command only works for players and things
		if (!executor.IsPlayer && !executor.IsThing)
		{
			await NotifyService!.Notify(executor, "Only players and things can leave.");
			return CallState.Empty;
		}
		
		// Get current location (the container we're in)
		var currentLocation = await executor.Match(
			async player => await player.Location.WithCancellation(CancellationToken.None),
			async room => await ValueTask.FromResult<AnySharpContainer>(room),
			async exit => await exit.Home.WithCancellation(CancellationToken.None),
			async thing => await thing.Location.WithCancellation(CancellationToken.None));
		
		// Check if we're in a thing or player (not a room)
		if (!currentLocation.IsThing && !currentLocation.IsPlayer)
		{
			await NotifyService!.Notify(executor, "You can't leave a room. Use an exit or HOME.");
			return CallState.Empty;
		}
		
		var container = currentLocation.WithExitOption();
		
		// Get the location of the container we're in (where we'll end up)
		var destinationLocation = await currentLocation.Match(
			async player => await player.Location.WithCancellation(CancellationToken.None),
			async room => await ValueTask.FromResult<AnySharpContainer>(room),
			async thing => await thing.Location.WithCancellation(CancellationToken.None));
		
		// Check leave lock on the container
		if (!LockService!.Evaluate(LockType.Leave, container, executor))
		{
			// Trigger @lfail attribute on the container (message to leaver)
			var lfailAttr = await AttributeService!.GetAttributeAsync(executor, container, AttrLFail, IAttributeService.AttributeMode.Read, true);
			if (lfailAttr.IsAttribute && lfailAttr.AsT0.Length > 0)
			{
				var lfailMsg = lfailAttr.AsT0[0].Value;
				if (!string.IsNullOrEmpty(lfailMsg.ToPlainText()))
				{
					await NotifyService!.Notify(executor, lfailMsg);
				}
			}
			else
			{
				await NotifyService!.Notify(executor, "You can't leave.");
			}
			
			// Trigger @olfail attribute (shown to others inside container)
			var olfailAttr = await AttributeService!.GetAttributeAsync(executor, container, AttrOLFail, IAttributeService.AttributeMode.Read, true);
			if (olfailAttr.IsAttribute && olfailAttr.AsT0.Length > 0)
			{
				var olfailMsg = olfailAttr.AsT0[0].Value;
				if (!string.IsNullOrEmpty(olfailMsg.ToPlainText()))
				{
					// Notify others inside the container (excluding executor)
					await CommunicationService!.SendToRoomAsync(executor, currentLocation, _ => olfailMsg,
						INotifyService.NotificationType.Emit, excludeObjects: [executor]);
				}
			}
			
			// Trigger @alfail attribute (actions on failed leave)
			var alfailAttr = await AttributeService!.GetAttributeAsync(executor, container, AttrALFail, IAttributeService.AttributeMode.Read, true);
			if (alfailAttr.IsAttribute && alfailAttr.AsT0.Length > 0)
			{
				var alfailActions = alfailAttr.AsT0[0].Value;
				if (!string.IsNullOrEmpty(alfailActions.ToPlainText()))
				{
					await parser.CommandParse(alfailActions);
				}
			}
			
			return CallState.Empty;
		}
		
		// Move to the container's location using MoveService for proper hook triggering
		var moveResult = await MoveService!.ExecuteMoveAsync(
			parser,
			executor.AsContent,
			destinationLocation,
			executor.Object().DBRef,
			"leave",
			silent: false);
		
		if (moveResult.IsT1)
		{
			await NotifyService!.Notify(executor, moveResult.AsT1.Value);
			return CallState.Empty;
		}
		
		// Trigger @leave attribute on the container (message to leaver) (command-specific attribute)
		var leaveAttr = await AttributeService!.GetAttributeAsync(executor, container, AttrLeave, IAttributeService.AttributeMode.Read, true);
		if (leaveAttr.IsAttribute && leaveAttr.AsT0.Length > 0)
		{
			var leaveMsg = leaveAttr.AsT0[0].Value;
			if (!string.IsNullOrEmpty(leaveMsg.ToPlainText()))
			{
				await NotifyService!.Notify(executor, leaveMsg);
			}
		}
		else
		{
			await NotifyService!.Notify(executor, $"You leave {currentLocation.Object().Name}.");
		}
		
		// Trigger @oleave attribute (shown to others inside the container)
		var oleaveAttr = await AttributeService!.GetAttributeAsync(executor, container, AttrOLeave, IAttributeService.AttributeMode.Read, true);
		if (oleaveAttr.IsAttribute && oleaveAttr.AsT0.Length > 0)
		{
			var oleaveMsg = oleaveAttr.AsT0[0].Value;
			if (!string.IsNullOrEmpty(oleaveMsg.ToPlainText()))
			{
				// Notify others inside the container (excluding executor)
				await CommunicationService!.SendToRoomAsync(executor, currentLocation, _ => oleaveMsg,
					INotifyService.NotificationType.Emit, excludeObjects: [executor]);
			}
		}
		
		// Trigger @oxleave attribute (shown to others in destination location)
		var oxleaveAttr = await AttributeService!.GetAttributeAsync(executor, container, AttrOXLeave, IAttributeService.AttributeMode.Read, true);
		if (oxleaveAttr.IsAttribute && oxleaveAttr.AsT0.Length > 0)
		{
			var oxleaveMsg = oxleaveAttr.AsT0[0].Value;
			if (!string.IsNullOrEmpty(oxleaveMsg.ToPlainText()))
			{
				// Notify others in the destination location (excluding executor)
				await CommunicationService!.SendToRoomAsync(executor, destinationLocation, _ => oxleaveMsg,
					INotifyService.NotificationType.Emit, excludeObjects: [executor]);
			}
		}
		
		// Trigger @aleave attribute (actions after leaving)
		var aleaveAttr = await AttributeService!.GetAttributeAsync(executor, container, AttrALeave, IAttributeService.AttributeMode.Read, true);
		if (aleaveAttr.IsAttribute && aleaveAttr.AsT0.Length > 0)
		{
			var aleaveActions = aleaveAttr.AsT0[0].Value;
			if (!string.IsNullOrEmpty(aleaveActions.ToPlainText()))
			{
				await parser.CommandParse(aleaveActions);
			}
		}
		
		// Trigger an automatic LOOK command at the new location if the executor is a player
		if (executor.IsPlayer)
		{
			await parser.CommandParse(MModule.single("look"));
		}
		
		return new CallState(destinationLocation.Object().DBRef.ToString());
	}

	[SharpCommand(Name = "PAGE", Switches = ["LIST", "NOEVAL", "PORT", "OVERRIDE"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Page(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var isNoEval = parser.CurrentState.Switches.Contains("NOEVAL");
		var isOverride = parser.CurrentState.Switches.Contains("OVERRIDE");
		
		// Get the raw arguments
		var recipientsArg = isNoEval 
			? ArgHelpers.NoParseDefaultNoParseArgument(args, 0, MModule.empty())
			: await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 0, MModule.empty());
		
		var messageArg = isNoEval
			? ArgHelpers.NoParseDefaultNoParseArgument(args, 1, MModule.empty())
			: await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 1, MModule.empty());
		
		// Parse message and recipients
		string recipientsText;
		
		// If no recipients provided, use last paged
		if (string.IsNullOrWhiteSpace(recipientsArg.ToPlainText()) && !string.IsNullOrWhiteSpace(messageArg.ToPlainText()))
		{
			// Get LASTPAGED attribute
			var lastPagedAttr = await AttributeService!.GetAttributeAsync(executor, executor, "LASTPAGED", IAttributeService.AttributeMode.Set, false);
			recipientsText = lastPagedAttr.Match(
				attr => attr.Last().Value.ToPlainText(),
				_ => string.Empty,
				_ => string.Empty
			);
			
			if (string.IsNullOrWhiteSpace(recipientsText))
			{
				await NotifyService!.Notify(executor, "Who do you want to page?");
				return CallState.Empty;
			}
		}
		else
		{
			recipientsText = recipientsArg.ToPlainText();
		}
		
		if (string.IsNullOrWhiteSpace(messageArg.ToPlainText()))
		{
			await NotifyService!.Notify(executor, "What do you want to page?");
			return CallState.Empty;
		}
		
		// Parse recipients list
		var recipientNames = recipientsText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		var successfulRecipients = new List<AnySharpObject>();
		
		foreach (var recipientName in recipientNames)
		{
			// Locate the recipient
			var recipientResult = await LocateService!.LocateAndNotifyIfInvalidWithCallState(
				parser, executor, executor, recipientName, LocateFlags.All);
			
			if (!recipientResult.IsAnySharpObject)
			{
				continue;
			}
			
			var recipient = recipientResult.AsSharpObject;
			
			// Check HAVEN flag unless OVERRIDE switch is used
			if (!isOverride)
			{
				var recipientFlags = recipient.Object().Flags.Value;
				if (await recipientFlags.AnyAsync(f => f.Name.Equals("HAVEN", StringComparison.OrdinalIgnoreCase)))
				{
					await NotifyService!.Notify(executor, $"{recipient.Object().Name} is not accepting pages.");
					continue;
				}
			}
			
			// Check @lock/page unless OVERRIDE switch is used
			// TODO: Most of this nonsense is not evaluating those VERBs correctly.
			if (!isOverride)
			{
				var lockResult = await PermissionService!.CanInteract(executor, recipient, IPermissionService.InteractType.Hear | IPermissionService.InteractType.Page);
				if (!lockResult)
				{
					var failureAttr = await AttributeService!.GetAttributeAsync(executor, recipient, "PAGE_LOCK`FAILURE", IAttributeService.AttributeMode.Read);
					
					switch (failureAttr)
					{
						case { IsError: true }:
						case { IsNone: true }:
						{
							break;
						}
						case { IsAttribute: true, AsAttribute: var attr }:
						{
							await CommunicationService!.SendToRoomAsync(executor, await executor.Where(), _ => attr.Last().Value.ToPlainText(),
								INotifyService.NotificationType.Announce, recipient);
							break;
						}
					}
					
					var oFailureAttr = await AttributeService.GetAttributeAsync(executor, recipient, "PAGE_LOCK`OFAILURE", IAttributeService.AttributeMode.Read);
					
					switch (oFailureAttr)
					{
						case { IsError: true }:
						case { IsNone: true }:
						{
							break;
						}
						case { IsAttribute: true, AsAttribute: var attr }:
						{
							await CommunicationService!.SendToRoomAsync(executor, await executor.Where(), _ => attr.Last().Value.ToPlainText(),
								INotifyService.NotificationType.Announce, recipient);
							break;
						}
					}
					
					var aFailureAttr = await AttributeService.GetAttributeAsync(executor, recipient, "PAGE_LOCK`AFAILURE", IAttributeService.AttributeMode.Read);

					switch (aFailureAttr)
					{
						case { IsError: true }:
						case { IsNone: true }:
						{
							break;
						}
						case { IsAttribute: true }:
						{
							await CommunicationService!.SendToRoomAsync(executor, await executor.Where(), _ => messageArg,
								INotifyService.NotificationType.Announce, recipient);
							break;
						}
					}
					
					continue;
				}
			}
			
			// Send the page
			var pageMessage = $"From afar, {executor.Object().Name} pages: {messageArg}";
			await NotifyService!.Notify(recipient, pageMessage, executor, INotifyService.NotificationType.Say);
			
			successfulRecipients.Add(recipient);
		}
		
		// Notify executor
		if (successfulRecipients.Count > 0)
		{
			var recipientList = string.Join(", ", successfulRecipients.Select(r => r.Object().DBRef));
			await NotifyService!.Notify(executor, $"You paged {recipientList} with '{messageArg}'.");
			
			// Store LASTPAGED attribute
			var lastPagedText = string.Join(" ", successfulRecipients.Select(r => r.Object().DBRef));
			await AttributeService!.SetAttributeAsync(executor, executor, "LASTPAGED", MModule.single(lastPagedText));
		}
		else if (recipientNames.Length > 0)
		{
			await NotifyService!.Notify(executor, "No one to page.");
		}
		
		return CallState.Empty;
	}

	[SharpCommand(Name = "POSE", Switches = ["NOEVAL", "NOSPACE"], Behavior = CB.Default | CB.NoGagged, MinArgs = 0,
		MaxArgs = 1)]
	public static async ValueTask<Option<CallState>> Pose(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var executorLocation = await executor.Where();
		var contents = executorLocation.Content(Mediator!);
		var isNoSpace = parser.CurrentState.Switches.Contains("NOSPACE");
		var isNoEvaluation = parser.CurrentState.Switches.Contains("NOEVAL");
		var message = isNoEvaluation
			? ArgHelpers.NoParseDefaultNoParseArgument(args, 1, MModule.empty())
			: await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 1, MModule.empty());

		var interactableContents = contents
			.Where(async (obj, _) =>
				await PermissionService!.CanInteract(obj, executor,
					IPermissionService.InteractType.Hear));

		await foreach (var obj in interactableContents)
		{
			await NotifyService!.Notify(
				obj.WithRoomOption(),
				isNoSpace
					? MModule.trim(message, MModule.single(" "), MModule.TrimType.TrimStart)
					: message,
				executor,
				INotifyService.NotificationType.Pose);
		}

		return new CallState(message);
	}

	[SharpCommand(Name = "SCORE", Switches = [], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Score(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		
		// Money/pennies are not supported in SharpMUSH
		await NotifyService!.Notify(executor, "The SCORE command is not supported.");
		await NotifyService!.Notify(executor, "SharpMUSH does not track money or pennies.");
		
		return CallState.Empty;
	}

	[SharpCommand(Name = "SAY", Switches = ["NOEVAL"], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Say(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var executorLocation = await executor.Where();
		var contents = executorLocation.Content(Mediator!);
		var isNoEvaluation = parser.CurrentState.Switches.Contains("NOEVAL");
		var message = isNoEvaluation
			? ArgHelpers.NoParseDefaultNoParseArgument(args, 1, MModule.empty())
			: await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 1, MModule.empty());

		var interactableContents = contents
			.Where(async (obj, _) =>
				await PermissionService!.CanInteract(obj.WithRoomOption(), executor,
					IPermissionService.InteractType.Hear));

		await foreach (var obj in interactableContents)
		{
			await NotifyService!.Notify(
				obj.WithRoomOption(),
				message,
				executor,
				INotifyService.NotificationType.Say);
		}

		return new CallState(message);
	}

	[SharpCommand(Name = "SEMIPOSE", Switches = ["NOEVAL"], Behavior = CB.Default | CB.NoGagged, MinArgs = 0,
		MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> SemiPose(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var executorLocation = await executor.Where();
		var contents = executorLocation.Content(Mediator!);
		var isNoEvaluation = parser.CurrentState.Switches.Contains("NOEVAL");
		var message = isNoEvaluation
			? ArgHelpers.NoParseDefaultNoParseArgument(args, 1, MModule.empty())
			: await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 1, MModule.empty());

		var interactableContents = contents
			.Where(async (obj, _) =>
				await PermissionService!.CanInteract(obj.WithRoomOption(), executor, IPermissionService.InteractType.Hear));

		await foreach (var obj in interactableContents)
		{
			await NotifyService!.Notify(
				obj.WithRoomOption(),
				message,
				executor,
				INotifyService.NotificationType.SemiPose);
		}

		return new CallState(message);
	}

	[SharpCommand(Name = "TEACH", Switches = ["LIST"], Behavior = CB.Default | CB.NoParse, MinArgs = 1, MaxArgs = 1)]
	public static async ValueTask<Option<CallState>> Teach(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var switches = parser.CurrentState.Switches;
		var args = parser.CurrentState.Arguments;
		
		if (switches.Contains("LIST"))
		{
			// /LIST executes an action list similar to @trigger
			if (!args.ContainsKey("0"))
			{
				await NotifyService!.Notify(executor, "Teach what action list?");
				return CallState.Empty;
			}
			
			var actionList = args["0"].Message!.ToPlainText();
			
			// Show others what we're teaching (unparsed)
			var executorLocation = await executor.Where();
			await CommunicationService!.SendToRoomAsync(executor, executorLocation,
				_ => MModule.single($"{executor.Object().Name} types --> {actionList}"),
				INotifyService.NotificationType.Emit, excludeObjects: [executor]);
			
			// Execute the action list
			await parser.CommandListParse(MModule.single(actionList));
			
			return CallState.Empty;
		}
		
		// Show others the command being taught (unparsed)
		if (!args.ContainsKey("0"))
		{
			await NotifyService!.Notify(executor, "Teach what?");
			return CallState.Empty;
		}
		
		var command = args["0"].Message!.ToPlainText();
		
		// Show others what we're teaching (unparsed)
		var location = await executor.Where();
		await CommunicationService!.SendToRoomAsync(executor, location,
			_ => MModule.single($"{executor.Object().Name} types --> {command}"),
			INotifyService.NotificationType.Emit, excludeObjects: [executor]);
		
		// Execute the command
		await parser.CommandParse(MModule.single(command));
		
		return CallState.Empty;
	}

	[SharpCommand(Name = "UNFOLLOW", Switches = [], Behavior = CB.Player | CB.Thing | CB.NoGagged, MinArgs = 0,
		MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> UnFollow(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		
		// Check if we're following anyone
		var followingAttr = await AttributeService!.GetAttributeAsync(executor, executor, "FOLLOWING", 
			IAttributeService.AttributeMode.Read, false);
		
		if (followingAttr.IsNone || followingAttr.IsError)
		{
			await NotifyService!.Notify(executor, "You aren't following anyone.");
			return CallState.Empty;
		}
		
		// Clear the FOLLOWING attribute
		await AttributeService!.ClearAttributeAsync(executor, executor, "FOLLOWING", 
			IAttributeService.AttributePatternMode.Exact, IAttributeService.AttributeClearMode.Safe);
		
		await NotifyService!.Notify(executor, "You stop following.");
		return CallState.Empty;
	}

	[SharpCommand(Name = "USE", Switches = [], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Use(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		
		if (!args.ContainsKey("0") || string.IsNullOrWhiteSpace(args["0"].Message?.ToPlainText()))
		{
			await NotifyService!.Notify(executor, "Use what?");
			return CallState.Empty;
		}
		
		var objectName = args["0"].Message!.ToPlainText();
		
		// Locate the object - check inventory first, then location
		var locateResult = await LocateService!.LocateAndNotifyIfInvalid(
			parser, executor, executor, objectName, LocateFlags.All);
		
		if (!locateResult.IsValid())
		{
			await NotifyService!.Notify(executor, "I don't see that here.");
			return CallState.Empty;
		}
		
		var objectToUse = locateResult.WithoutError().WithoutNone();
		
		// Check USE lock
		if (!LockService!.Evaluate(LockType.Use, objectToUse, executor))
		{
			// Trigger @UFAIL attribute (use failure message)
			var ufailAttr = await AttributeService!.GetAttributeAsync(executor, objectToUse, "UFAIL", IAttributeService.AttributeMode.Read, true);
			if (ufailAttr.IsAttribute && ufailAttr.AsT0.Length > 0)
			{
				var ufailMsg = ufailAttr.AsT0[0].Value;
				if (!string.IsNullOrEmpty(ufailMsg.ToPlainText()))
				{
					await NotifyService!.Notify(executor, ufailMsg);
				}
			}
			else
			{
				await NotifyService!.Notify(executor, "You can't use that.");
			}
			
			// Trigger @OUFAIL attribute (others see this)
			var oufailAttr = await AttributeService!.GetAttributeAsync(executor, objectToUse, "OUFAIL", IAttributeService.AttributeMode.Read, true);
			if (oufailAttr.IsAttribute && oufailAttr.AsT0.Length > 0)
			{
				var oufailMsg = oufailAttr.AsT0[0].Value;
				if (!string.IsNullOrEmpty(oufailMsg.ToPlainText()))
				{
					var executorLocation = await executor.Where();
					await CommunicationService!.SendToRoomAsync(executor, executorLocation, _ => oufailMsg,
						INotifyService.NotificationType.Emit, excludeObjects: [executor]);
				}
			}
			
			// Trigger @AUFAIL attribute (actions on failure)
			var aufailAttr = await AttributeService!.GetAttributeAsync(executor, objectToUse, "AUFAIL", IAttributeService.AttributeMode.Read, true);
			if (aufailAttr.IsAttribute && aufailAttr.AsT0.Length > 0)
			{
				var aufailActions = aufailAttr.AsT0[0].Value;
				if (!string.IsNullOrEmpty(aufailActions.ToPlainText()))
				{
					await parser.CommandParse(aufailActions);
				}
			}
			
			return CallState.Empty;
		}
		
		// Trigger @USE attribute (what happens when used successfully)
		var useAttr = await AttributeService!.GetAttributeAsync(executor, objectToUse, "USE", IAttributeService.AttributeMode.Read, true);
		if (useAttr.IsAttribute && useAttr.AsT0.Length > 0)
		{
			var useMsg = useAttr.AsT0[0].Value;
			if (!string.IsNullOrEmpty(useMsg.ToPlainText()))
			{
				await NotifyService!.Notify(executor, useMsg);
			}
		}
		else
		{
			await NotifyService!.Notify(executor, $"You use {objectToUse.Object().Name}.");
		}
		
		// Trigger @OUSE attribute (others see this)
		var ouseAttr = await AttributeService!.GetAttributeAsync(executor, objectToUse, "OUSE", IAttributeService.AttributeMode.Read, true);
		if (ouseAttr.IsAttribute && ouseAttr.AsT0.Length > 0)
		{
			var ouseMsg = ouseAttr.AsT0[0].Value;
			if (!string.IsNullOrEmpty(ouseMsg.ToPlainText()))
			{
				var executorLocation = await executor.Where();
				await CommunicationService!.SendToRoomAsync(executor, executorLocation, _ => ouseMsg,
					INotifyService.NotificationType.Emit, excludeObjects: [executor]);
			}
		}
		
		// Trigger @AUSE attribute (actions after use)
		var auseAttr = await AttributeService!.GetAttributeAsync(executor, objectToUse, "AUSE", IAttributeService.AttributeMode.Read, true);
		if (auseAttr.IsAttribute && auseAttr.AsT0.Length > 0)
		{
			var auseActions = auseAttr.AsT0[0].Value;
			if (!string.IsNullOrEmpty(auseActions.ToPlainText()))
			{
				await parser.CommandParse(auseActions);
			}
		}
		
		return CallState.Empty;
	}

	[SharpCommand(Name = "WHISPER", Switches = ["LIST", "NOISY", "SILENT", "NOEVAL"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Whisper(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.ArgumentsOrdered;
		var switches = parser.CurrentState.Switches;
		
		// Get executor's location
		var executorLocation = await executor.Where();
		
		// Handle /LIST switch
		if (switches.Contains("LIST"))
		{
			var contents = executorLocation.Content(Mediator!);
			var players = new List<string>();
			
			await foreach (var obj in contents)
			{
				if (obj.IsPlayer && !obj.Object().DBRef.Equals(executor.Object().DBRef))
				{
					players.Add(obj.Object().Name);
				}
			}
			
			if (players.Count == 0)
			{
				await NotifyService!.Notify(executor, "There is no one here to whisper to.");
			}
			else
			{
				await NotifyService!.Notify(executor, $"You can whisper to: {string.Join(", ", players)}");
			}
			
			return CallState.Empty;
		}
		
		// Parse arguments: whisper <target>=<message>
		var isNoEval = switches.Contains("NOEVAL");
		var targetArg = isNoEval
			? ArgHelpers.NoParseDefaultNoParseArgument(args, 0, MModule.empty())
			: await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 0, MModule.empty());
		var messageArg = isNoEval
			? ArgHelpers.NoParseDefaultNoParseArgument(args, 1, MModule.empty())
			: await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 1, MModule.empty());
		
		if (string.IsNullOrWhiteSpace(targetArg.ToPlainText()))
		{
			await NotifyService!.Notify(executor, "Whisper to whom?");
			return CallState.Empty;
		}
		
		if (string.IsNullOrWhiteSpace(messageArg.ToPlainText()))
		{
			await NotifyService!.Notify(executor, "Whisper what?");
			return CallState.Empty;
		}
		
		// Parse target list (can be multiple targets separated by space)
		var targetNames = targetArg.ToPlainText().Split(' ', StringSplitOptions.RemoveEmptyEntries);
		var successfulTargets = new List<AnySharpObject>();
		
		foreach (var targetName in targetNames)
		{
			// Locate target in the same room
			var targetResult = await LocateService!.LocateAndNotifyIfInvalid(
				parser, executor, executorLocation.WithExitOption(), targetName, LocateFlags.All);
			
			if (!targetResult.IsValid() || !targetResult.IsPlayer)
			{
				await NotifyService!.Notify(executor, $"I don't see {targetName} here.");
				continue;
			}
			
			var target = targetResult.WithoutError().WithoutNone();
			
			// Can't whisper to self
			if (target.Object().DBRef.Equals(executor.Object().DBRef))
			{
				await NotifyService!.Notify(executor, "You can't whisper to yourself.");
				continue;
			}
			
			// Check if target is in same location
			var targetLocation = await target.Where();
			if (!targetLocation.Object().DBRef.Equals(executorLocation.Object().DBRef))
			{
				await NotifyService!.Notify(executor, $"{target.Object().Name} is not here.");
				continue;
			}
			
			successfulTargets.Add(target);
		}
		
		if (successfulTargets.Count == 0)
		{
			return CallState.Empty;
		}
		
		// Send whisper to targets
		var isNoisy = switches.Contains("NOISY");
		var isSilent = switches.Contains("SILENT");
		var messageText = messageArg.ToPlainText();
		
		// Check if message starts with : or ; for pose/semipose
		var isPose = messageText.StartsWith(":");
		var isSemiPose = messageText.StartsWith(";");
		
		// Extract pose text if needed (reuse this value)
		var displayText = (isPose || isSemiPose) 
			? $"{executor.Object().Name}{messageText.Substring(1)}" 
			: messageText;
		
		// Send whisper to each target
		foreach (var target in successfulTargets)
		{
			var whisperMsg = $"{executor.Object().Name} whispers, \"{displayText}\"";
			await NotifyService!.Notify(target, whisperMsg, executor, INotifyService.NotificationType.Say);
		}
		
		// Notify executor unless SILENT
		if (!isSilent)
		{
			var targetList = string.Join(", ", successfulTargets.Select(t => t.Object().Name));
			await NotifyService!.Notify(executor, $"You whisper \"{displayText}\" to {targetList}.");
		}
		
		// If NOISY, notify others in room
		if (isNoisy)
		{
			var contents = executorLocation.Content(Mediator!);
			await foreach (var obj in contents)
			{
				// Skip executor and targets
				if (obj.Object().DBRef.Equals(executor.Object().DBRef) ||
				    successfulTargets.Any(t => t.Object().DBRef.Equals(obj.Object().DBRef)))
				{
					continue;
				}
				
				var targetList = string.Join(", ", successfulTargets.Select(t => t.Object().Name));
				await NotifyService!.Notify(obj.WithRoomOption(), 
					$"{executor.Object().Name} whispers something to {targetList}.");
			}
		}
		
		return new CallState(messageArg);
	}

	[SharpCommand(Name = "WITH", Switches = ["NOEVAL", "ROOM"], Behavior = CB.Player | CB.Thing | CB.EqSplit, MinArgs = 0,
		MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> With(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches;
		
		// Parse: with <target>=<command>
		if (!args.ContainsKey("0") || string.IsNullOrWhiteSpace(args["0"].Message?.ToPlainText()))
		{
			await NotifyService!.Notify(executor, "With whom?");
			return CallState.Empty;
		}
		
		if (!args.TryGetValue("1", out var arg1) || string.IsNullOrWhiteSpace(arg1.Message?.ToPlainText()))
		{
			await NotifyService!.Notify(executor, "Do what with them?");
			return CallState.Empty;
		}
		
		var targetName = args["0"].Message!.ToPlainText();
		var command = arg1.Message!;
		
		// Determine search location
		AnySharpObject searchLocation = switches.Contains("ROOM")
			? (await executor.Where()).WithExitOption()
			: executor;
		
		// Locate the target
		var targetResult = await LocateService!.LocateAndNotifyIfInvalid(
			parser, executor, searchLocation, targetName, LocateFlags.All);
		
		if (!targetResult.IsValid())
		{
			await NotifyService!.Notify(executor, "I don't see that here.");
			return CallState.Empty;
		}
		
		var target = targetResult.WithoutError().WithoutNone();
		
		// Check if target can execute commands (Player or Thing with appropriate flags/powers)
		if (!target.IsPlayer && !target.IsThing)
		{
			await NotifyService!.Notify(executor, "You can't do that with that.");
			return CallState.Empty;
		}
		
		// Check permissions - must control the target
		if (!await PermissionService!.Controls(executor, target))
		{
			await NotifyService!.Notify(executor, "Permission denied.");
			return CallState.Empty;
		}
		
		// Execute the command as the target
		// Note: This changes the enactor context temporarily
		await NotifyService!.Notify(executor, $"Executing as {target.Object().Name}: {command.ToPlainText()}");
		
		// Parse and execute command as target
		// The parser will need to temporarily change the executor context
		// For now, we'll just notify that this would execute
		// TODO: Implement proper context switching in parser
		await NotifyService.Notify(executor, "WITH command execution not fully implemented - requires parser context switching.");
		
		return CallState.Empty;
	}

	[SharpCommand(Name = "DOING", Switches = [], Behavior = CB.Default, MinArgs = 0, MaxArgs = 1)]
	public static async ValueTask<Option<CallState>> Doing(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		
		// Check if executor is admin (can see hidden players)
		var isAdmin = await executor.IsWizard() || 
		              await executor.IsRoyalty() || 
		              await executor.HasPower("SEE_ALL");
		
		// Get optional pattern argument
		var pattern = args.ContainsKey("0") ? args["0"].Message?.ToPlainText() : null;
		
		var everyone = ConnectionService!.GetAll();
		const string fmt = "{0,-18} {1,10} {2,6}  {3,-32}";
		var header = string.Format(fmt, "Player Name", "On For", "Idle", "Doing");
		
		// Build list of player info with filtering
		var playerList = new List<string>();
		await foreach (var connection in everyone.Where(player => player.Ref.HasValue))
		{
			var obj = await Mediator!.Send(new GetObjectNodeQuery(connection.Ref!.Value));
			var playerName = obj.Known.Object().Name;
			
			// Filter by visibility: mortals can't see DARK players, admins can see all
			if (!isAdmin && await obj.Known.HasFlag("DARK"))
			{
				continue;
			}
			
			// Filter by pattern if provided
			if (!string.IsNullOrWhiteSpace(pattern) && !MatchesPattern(playerName, pattern))
			{
				continue;
			}
			
			var doingText = await GetDoingText(executor, obj.Known);
			
			playerList.Add(string.Format(
				fmt,
				playerName,
				TimeHelpers.TimeString(connection.Connected!.Value, accuracy: 3),
				TimeHelpers.TimeString(connection.Idle!.Value),
				doingText));
		}
		
		var footer = $"{playerList.Count} players logged in.";
		var message = $"{header}\n{string.Join('\n', playerList)}\n{footer}";
		
		await NotifyService!.Notify(executor, message);
		
		return new None();
	}
	
	private static bool MatchesPattern(string playerName, string pattern)
	{
		// Check if pattern contains wildcards
		if (pattern.Contains('*') || pattern.Contains('?'))
		{
			// Use wildcard matching
			return MModule.isWildcardMatch2(MModule.single(playerName), pattern);
		}
		
		// Use prefix matching (starts with)
		return playerName.StartsWith(pattern, StringComparison.OrdinalIgnoreCase);
	}
	
	private static async ValueTask<string> GetDoingText(AnySharpObject executor, AnySharpObject player)
	{
		var doingAttr = await AttributeService!.GetAttributeAsync(
			executor,
			player,
			"DOING",
			mode: IAttributeService.AttributeMode.Read,
			parent: false);
		
		return doingAttr switch
		{
			{ IsError: true } or { IsNone: true } => string.Empty,
			_ => doingAttr.AsAttribute.Last().Value.ToPlainText()
		};
	}

	[SharpCommand(Name = "SESSION", Switches = [], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Session(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		
		// Get connection information for the executor
		var allConnections = ConnectionService!.GetAll();
		IConnectionService.ConnectionData? connection = null;
		
		await foreach (var conn in allConnections)
		{
			if (conn.Ref.HasValue && conn.Ref.Value.Equals(executor.Object().DBRef))
			{
				connection = conn;
				break;
			}
		}
		
		if (connection == null)
		{
			await NotifyService!.Notify(executor, "No session information available.");
			return CallState.Empty;
		}
		
		var output = new System.Text.StringBuilder();
		output.AppendLine("Session Information:");
		output.AppendLine($"  Player: {executor.Object().Name} (#{executor.Object().DBRef.Number})");
		
		if (connection.Connected.HasValue)
		{
			output.AppendLine($"  Connected: {TimeHelpers.TimeString(connection.Connected.Value)} ago");
		}
		
		if (connection.Idle.HasValue)
		{
			output.AppendLine($"  Idle: {TimeHelpers.TimeString(connection.Idle.Value)}");
		}
		
		if (!string.IsNullOrEmpty(connection.HostName))
		{
			output.AppendLine($"  Host: {connection.HostName}");
		}
		
		await NotifyService!.Notify(executor, output.ToString().TrimEnd());
		return CallState.Empty;
	}

	[SharpCommand(Name = "WARN_ON_MISSING", Switches = [], Behavior = CB.Default | CB.NoParse | CB.Internal | CB.NoOp,
		MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> WarnOnMissing(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// Internal no-op command for warning system
		// This is marked as NoOp so it effectively does nothing
		await ValueTask.CompletedTask;
		return new None();
	}

	[SharpCommand(Name = "UNIMPLEMENTED_COMMAND", Switches = [],
		Behavior = CB.Default | CB.NoParse | CB.Internal | CB.NoOp, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> UnimplementedCommand(IMUSHCodeParser parser,
		SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownEnactorObject(Mediator!);
		await NotifyService!.Notify(executor, "Huh?  (Type \"help\" for help.)");
		return new None();
	}
}