using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using CB = SharpMUSH.Implementation.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

public static partial class Commands
{

	[SharpCommand(Name = "@ALLHALT", Switches = [], Behavior = CB.Default, CommandLock = "FLAG^WIZARD|POWER^HALT", MinArgs = 0)]
	public static async ValueTask<Option<CallState>> ALLHALT(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@@", Switches = [], Behavior = CB.Default | CB.NoParse, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> At(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@FLAG", Switches = ["ADD", "TYPE", "LETTER", "LIST", "RESTRICT", "DELETE", "ALIAS", "DISABLE", "ENABLE", "DEBUG", "DECOMPILE"], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> FLAG(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@LOG", Switches = ["CHECK", "CMD", "CONN", "ERR", "TRACE", "WIZ", "RECALL"], Behavior = CB.Default | CB.NoGagged, CommandLock = "FLAG^WIZARD", MinArgs = 0)]
	public static async ValueTask<Option<CallState>> LOG(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@POOR", Switches = [], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> POOR(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@RESPOND", Switches = ["HEADER", "TYPE"], Behavior = CB.Default | CB.NoGagged | CB.EqSplit, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> RESPOND(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@SQUOTA", Switches = [], Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> SQUOTA(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@WIZWALL", Switches = ["NOEVAL", "EMIT"], Behavior = CB.Default, CommandLock = "FLAG^WIZARD", MinArgs = 0)]
	public static async ValueTask<Option<CallState>> WIZWALL(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@ALLQUOTA", Switches = ["QUIET"], Behavior = CB.Default, CommandLock = "FLAG^WIZARD|POWER^QUOTA", MinArgs = 0)]
	public static async ValueTask<Option<CallState>> ALLQUOTA(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@DBCK", Switches = [], Behavior = CB.Default, CommandLock = "FLAG^WIZARD", MinArgs = 0)]
	public static async ValueTask<Option<CallState>> DBCK(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@HIDE", Switches = ["NO", "OFF", "YES", "ON"], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> HIDE(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@MOTD", Switches = ["CONNECT", "LIST", "WIZARD", "DOWN", "FULL", "CLEAR"], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> MOTD(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@POWER", Switches = ["ADD", "TYPE", "LETTER", "LIST", "RESTRICT", "DELETE", "ALIAS", "DISABLE", "ENABLE", "DECOMPILE"], Behavior = CB.Default | CB.EqSplit | CB.RSArgs, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> POWER(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@REJECTMOTD", Switches = ["CLEAR"], Behavior = CB.Default, CommandLock = "FLAG^WIZARD", MinArgs = 0)]
	public static async ValueTask<Option<CallState>> REJECTMOTD(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@SUGGEST", Switches = ["ADD", "DELETE", "LIST"], Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> SUGGEST(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@BOOT", Switches = ["PORT", "ME", "SILENT"], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> BOOT(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@DISABLE", Switches = [], Behavior = CB.Default, CommandLock = "FLAG^WIZARD", MinArgs = 0)]
	public static async ValueTask<Option<CallState>> DISABLE(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@HOOK", Switches = ["LIST", "AFTER", "BEFORE", "EXTEND", "IGSWITCH", "IGNORE", "OVERRIDE", "INPLACE", "INLINE", "LOCALIZE", "CLEARREGS", "NOBREAK"], Behavior = CB.Default | CB.EqSplit | CB.RSArgs, CommandLock = "FLAG^WIZARD|POWER^HOOK", MinArgs = 0)]
	public static async ValueTask<Option<CallState>> HOOK(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@NEWPASSWORD", Switches = ["GENERATE"], Behavior = CB.Default | CB.EqSplit | CB.RSNoParse, CommandLock = "FLAG^WIZARD", MinArgs = 0)]
	public static async ValueTask<Option<CallState>> NEWPASSWORD(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@PURGE", Switches = [], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> PURGE(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@SHUTDOWN", Switches = ["PANIC", "REBOOT", "PARANOID"], Behavior = CB.Default, CommandLock = "FLAG^WIZARD", MinArgs = 0)]
	public static async ValueTask<Option<CallState>> SHUTDOWN(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@UPTIME", Switches = ["MORTAL"], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> UPTIME(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@CHOWNALL", Switches = ["PRESERVE", "THINGS", "ROOMS", "EXITS"], Behavior = CB.Default | CB.EqSplit, CommandLock = "FLAG^WIZARD", MinArgs = 0)]
	public static async ValueTask<Option<CallState>> CHOWNALL(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@DUMP", Switches = ["PARANOID", "DEBUG", "NOFORK"], Behavior = CB.Default, CommandLock = "FLAG^WIZARD", MinArgs = 0)]
	public static async ValueTask<Option<CallState>> DUMP(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@HTTP", Switches = ["DELETE", "POST", "PUT"], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged | CB.NoGuest, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> HTTP(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	/// <remarks>
	/// Creating on the DBRef is not implemented.
	/// </remarks>
	[SharpCommand(Name = "@PCREATE", Behavior = CB.Default, MinArgs = 2, MaxArgs = 3)]
	public static async ValueTask<Option<CallState>> PCreate(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// TODO: Validate Name and Passwords
		var args = parser.CurrentState.Arguments;
		var name = MModule.plainText(args["0"].Message!);
		var password = MModule.plainText(args["1"].Message!);

		var player = await parser.Mediator.Send(new CreatePlayerCommand(name, password, parser.CurrentState.Executor!.Value));

		return new CallState(player.ToString());
	}

	[SharpCommand(Name = "@QUOTA", Switches = ["ALL", "SET"], Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> QUOTA(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@SITELOCK", Switches = ["BAN", "CHECK", "REGISTER", "REMOVE", "NAME", "PLAYER"], Behavior = CB.Default | CB.EqSplit | CB.RSArgs, CommandLock = "FLAG^WIZARD", MinArgs = 0)]
	public static async ValueTask<Option<CallState>> SITELOCK(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@WALL", Switches = ["NOEVAL", "EMIT"], Behavior = CB.Default, CommandLock = "FLAG^WIZARD ROYALTY|POWER^ANNOUNCE", MinArgs = 0)]
	public static async ValueTask<Option<CallState>> WALL(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@CHZONEALL", Switches = ["PRESERVE"], Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> CHZONEALL(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@ENABLE", Switches = [], Behavior = CB.Default | CB.NoGagged, CommandLock = "FLAG^WIZARD", MinArgs = 0)]
	public static async ValueTask<Option<CallState>> ENABLE(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@KICK", Switches = [], Behavior = CB.Default, CommandLock = "FLAG^WIZARD", MinArgs = 0)]
	public static async ValueTask<Option<CallState>> KICK(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@POLL", Switches = ["CLEAR"], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> POLL(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@READCACHE", Switches = [], Behavior = CB.Default, CommandLock = "FLAG^WIZARD", MinArgs = 0)]
	public static async ValueTask<Option<CallState>> READCACHE(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@SQL", Switches = [], Behavior = CB.Default, CommandLock = "FLAG^WIZARD|POWER^SQL_OK", MinArgs = 0)]
	public static async ValueTask<Option<CallState>> SQL(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@MAPSQL", Switches = ["NOTIFY", "COLNAMES", "SPOOF"], Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> MAPSQL(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}

	[SharpCommand(Name = "@WIZMOTD", Switches = ["CLEAR"], Behavior = CB.Default, CommandLock = "FLAG^WIZARD", MinArgs = 0)]
	public static async ValueTask<Option<CallState>> WIZMOTD(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}
}
