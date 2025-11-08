using OneOf.Types;
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
	private const string AttrEFail = "EFAIL";
	private const string AttrOEFail = "OEFAIL";
	private const string AttrAEFail = "AEFAIL";
	private const string AttrLinkType = "_LINKTYPE";
	private const string LinkTypeVariable = "variable";
	private const string LinkTypeHome = "home";


	[SharpCommand(Name = "@CLOCK", Switches = ["JOIN", "SPEAK", "MOD", "SEE", "HIDE"], Behavior = CB.Default | CB.EqSplit,
		MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> ChannelLock(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
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
			// Check if executor is wizard/royalty to see wizard MOTD
			var isWizard = await executor.IsWizard();
			
			// Get MOTD file paths from configuration
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
		
		// Handle /FLAGS switch - alias for @flag/list
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
		
		// Handle /POWERS switch - alias for @power/list
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
		
		// Handle /LOCKS switch - list lock types
		if (switches.Contains("LOCKS"))
		{
			var output = new System.Text.StringBuilder();
			var header = useLowercase ? "Lock Types:" : "LOCK TYPES:";
			output.AppendLine(header);
			
			// Get all lock types from the LockType enum
			var lockTypes = Enum.GetNames(typeof(LockType));
			foreach (var lockType in lockTypes.OrderBy(x => x))
			{
				var displayName = useLowercase ? lockType.ToLower() : lockType.ToUpper();
				output.AppendLine($"  {displayName}");
			}
			
			await NotifyService!.Notify(executor, output.ToString().TrimEnd());
			return CallState.Empty;
		}
		
		// Handle /ATTRIBS switch - list standard attributes
		if (switches.Contains("ATTRIBS"))
		{
			var output = new System.Text.StringBuilder();
			var header = useLowercase ? "Standard Attributes:" : "STANDARD ATTRIBUTES:";
			output.AppendLine(header);
			
			var attributes = Database!.GetAllAttributeEntriesAsync();
			await foreach (var attr in attributes.OrderBy(x => x.Name))
			{
				var attrName = useLowercase ? attr.Name.ToLower() : attr.Name;
				output.AppendLine($"  {attrName}");
			}
			
			await NotifyService!.Notify(executor, output.ToString().TrimEnd());
			return CallState.Empty;
		}
		
		// Handle /COMMANDS switch - list all commands
		if (switches.Contains("COMMANDS"))
		{
			var output = new System.Text.StringBuilder();
			var header = useLowercase ? "Commands:" : "COMMANDS:";
			output.AppendLine(header);
			
			var filterBuiltin = switches.Contains("BUILTIN");
			var filterLocal = switches.Contains("LOCAL");
			
			var commands = CommandLibrary!
				.Where(kvp => !filterBuiltin || kvp.Value.IsSystem)
				.Where(kvp => !filterLocal || !kvp.Value.IsSystem)
				.Select(kvp => kvp.Value.LibraryInformation.Attribute.Name)
				.Distinct()
				.OrderBy(x => x);
			
			foreach (var cmdName in commands)
			{
				var displayName = useLowercase ? cmdName.ToLower() : cmdName;
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
			
			var functions = FunctionLibrary!
				.Where(kvp => !filterBuiltin || kvp.Value.IsSystem)
				.Where(kvp => !filterLocal || !kvp.Value.IsSystem)
				.Select(kvp => kvp.Value.LibraryInformation.Attribute.Name)
				.Distinct()
				.OrderBy(x => x);
			
			foreach (var funcName in functions)
			{
				var displayName = useLowercase ? funcName.ToLower() : funcName;
				output.AppendLine($"  {displayName}");
			}
			
			await NotifyService!.Notify(executor, output.ToString().TrimEnd());
			return CallState.Empty;
		}
		
		// Handle /ALLOCATIONS switch - memory allocation info (admin-only)
		if (switches.Contains("ALLOCATIONS"))
		{
			// Check if user is admin/wizard
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
		
		// If no specific switch is provided, show a help message
		await NotifyService!.Notify(executor, "You must specify what to list. Use one of: /MOTD /FUNCTIONS /COMMANDS /ATTRIBS /LOCKS /FLAGS /POWERS /ALLOCATIONS");
		return CallState.Empty;
	}

	[SharpCommand(Name = "@LOGWIPE", Switches = ["CHECK", "CMD", "CONN", "ERR", "TRACE", "WIZ", "ROTATE", "TRIM", "WIPE"],
		Behavior = CB.Default | CB.NoGagged | CB.God, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> LogWipe(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@LSET", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0,
		MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> LockSet(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@MALIAS",
		Switches =
		[
			"SET", "CREATE", "DESTROY", "DESCRIBE", "RENAME", "STATS", "CHOWN", "NUKE", "ADD", "REMOVE", "LIST", "ALL", "WHO",
			"MEMBERS", "USEFLAG", "SEEFLAG"
		], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> MailAlias(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@SOCKSET", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.NoGagged | CB.RSArgs,
		MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> SocketSet(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@SLAVE", Switches = ["RESTART"], Behavior = CB.Default, CommandLock = "FLAG^WIZARD",
		MinArgs = 0)]
	public static async ValueTask<Option<CallState>> Slave(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@UNRECYCLE", Switches = [], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> UnRecycle(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@WARNINGS", Switches = [], Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Warnings(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@WCHECK", Switches = ["ALL", "ME"], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> WizardCheck(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "BUY", Switches = [], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Buy(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "BRIEF", Switches = ["OPAQUE"], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Brief(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "DESERT", Switches = [], Behavior = CB.Player | CB.Thing, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Desert(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "DISMISS", Switches = [], Behavior = CB.Player | CB.Thing, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Dismiss(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
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
		
		// Move object to current location
		var contentToDrop = objectToDrop.AsContent;
		await Mediator!.Send(new MoveObjectCommand(contentToDrop, currentRoom));
		
		// Trigger @drop attribute on the object
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
		Behavior = CB.Player | CB.Thing | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Empty(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
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

		// Move executor into object
		var executorAsContent = executor.AsContent;
		var containerToEnter = objectToEnter.AsContainer;
		await Mediator!.Send(new MoveObjectCommand(executorAsContent, containerToEnter));

		// Trigger @enter attribute (shown to entering player)
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
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "GET", Switches = [], Behavior = CB.Player | CB.Thing | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Get(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "GIVE", Switches = ["SILENT"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0,
		MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Give(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "HOME", Switches = [], Behavior = CB.Player | CB.Thing, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Home(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "INVENTORY", Switches = [], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Inventory(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "LEAVE", Switches = [], Behavior = CB.Player | CB.Thing, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Leave(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
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
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
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

	[SharpCommand(Name = "TEACH", Switches = ["LIST"], Behavior = CB.Default | CB.NoParse, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Teach(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "UNFOLLOW", Switches = [], Behavior = CB.Player | CB.Thing | CB.NoGagged, MinArgs = 0,
		MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> UnFollow(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "USE", Switches = [], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Use(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "WHISPER", Switches = ["LIST", "NOISY", "SILENT", "NOEVAL"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Whisper(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "WITH", Switches = ["NOEVAL", "ROOM"], Behavior = CB.Player | CB.Thing | CB.EqSplit, MinArgs = 0,
		MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> With(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "DOING", Switches = [], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Doing(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "SESSION", Switches = [], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Session(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "WARN_ON_MISSING", Switches = [], Behavior = CB.Default | CB.NoParse | CB.Internal | CB.NoOp,
		MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> WarnOnMissing(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
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