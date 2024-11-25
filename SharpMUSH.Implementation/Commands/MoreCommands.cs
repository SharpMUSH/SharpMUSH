using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using System;
using CB = SharpMUSH.Implementation.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands
{
	internal class MoreCommands
	{
		[SharpCommand(Name = "@COMMAND", Switches = ["ADD", "ALIAS", "CLONE", "DELETE", "EqSplit", "LSARGS", "RSARGS", "NOEVAL", "ON", "OFF", "QUIET", "ENABLE", "DISABLE", "RESTRICT", "NOPARSE", "RSNoParse"], Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> COMMAND(IMUSHCodeParser parser, SharpCommandAttribute _2)
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

		[SharpCommand(Name = "@ALLHALT", Switches = [], Behavior = CB.Default, CommandLock = "FLAG^WIZARD|POWER^HALT", MinArgs = 0)]
		public static async ValueTask<Option<CallState>> ALLHALT(IMUSHCodeParser parser, SharpCommandAttribute _2)
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

		[SharpCommand(Name = "@ASSERT", Switches = ["INLINE", "QUEUED"], Behavior = CB.Default | CB.EqSplit | CB.RSNoParse | CB.Rs_Brace, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> ASSERT(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@ATRLOCK", Switches = [], Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> ATRLOCK(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@ATRCHOWN", Switches = [], Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> ATRCHOWN(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@ATTRIBUTE", Switches = ["ACCESS", "DELETE", "RENAME", "RETROACTIVE", "LIMIT", "ENUM", "DECOMPILE"], Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> ATTRIBUTE(IMUSHCodeParser parser, SharpCommandAttribute _2)
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

		[SharpCommand(Name = "@BREAK", Switches = ["INLINE", "QUEUED"], Behavior = CB.Default | CB.EqSplit | CB.RSNoParse | CB.Rs_Brace, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> BREAK(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@SKIP", Switches = ["IFELSE"], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.RSNoParse, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> SKIP(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@IFELSE", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.RSNoParse, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> IFELSE(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@CEMIT", Switches = ["NOEVAL", "NOISY", "SILENT", "SPOOF"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> CEMIT(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@CHANNEL", Switches = ["LIST", "ADD", "DELETE", "RENAME", "MOGRIFIER", "NAME", "PRIVS", "QUIET", "DECOMPILE", "DESCRIBE", "CHOWN", "WIPE", "MUTE", "UNMUTE", "GAG", "UNGAG", "HIDE", "UNHIDE", "WHAT", "TITLE", "BRIEF", "RECALL", "BUFFER", "COMBINE", "UNCOMBINE", "ON", "JOIN", "OFF", "LEAVE", "WHO"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged | CB.RSArgs, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> CHANNEL(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@CHAT", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> CHAT(IMUSHCodeParser parser, SharpCommandAttribute _2)
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

		[SharpCommand(Name = "@CHOWN", Switches = ["PRESERVE"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> CHOWN(IMUSHCodeParser parser, SharpCommandAttribute _2)
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

		[SharpCommand(Name = "@CHZONE", Switches = ["PRESERVE"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> CHZONE(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@CONFIG", Switches = ["SET", "SAVE", "LOWERCASE", "LIST"], Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> CONFIG(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@CPATTR", Switches = ["CONVERT", "NOFLAGCOPY"], Behavior = CB.Default | CB.EqSplit | CB.RSArgs, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> CPATTR(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@CREATE", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> CREATE(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@CLONE", Switches = ["PRESERVE"], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> CLONE(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@CLOCK", Switches = ["JOIN", "SPEAK", "MOD", "SEE", "HIDE"], Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> CLOCK(IMUSHCodeParser parser, SharpCommandAttribute _2)
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

		[SharpCommand(Name = "@DECOMPILE", Switches = ["DB", "NAME", "PREFIX", "TF", "FLAGS", "ATTRIBS", "SKIPDEFAULTS"], Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> DECOMPILE(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@DESTROY", Switches = ["OVERRIDE"], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> DESTROY(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@DIG", Switches = ["TELEPORT"], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> DIG(IMUSHCodeParser parser, SharpCommandAttribute _2)
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

		[SharpCommand(Name = "@DOLIST", Switches = ["NOTIFY", "DELIMIT", "INPLACE", "INLINE", "LOCALIZE", "CLEARREGS", "NOBREAK"], Behavior = CB.Default | CB.EqSplit | CB.RSNoParse | CB.Rs_Brace, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> DOLIST(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@DRAIN", Switches = ["ALL", "ANY"], Behavior = CB.Default | CB.EqSplit | CB.RSArgs, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> DRAIN(IMUSHCodeParser parser, SharpCommandAttribute _2)
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

		[SharpCommand(Name = "@EDIT", Switches = ["FIRST", "CHECK", "QUIET", "REGEXP", "NOCASE", "ALL"], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.RSNoParse | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> EDIT(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@EMIT", Switches = ["NOEVAL", "SPOOF"], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> EMIT(IMUSHCodeParser parser, SharpCommandAttribute _2)
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

		[SharpCommand(Name = "@ENTRANCES", Switches = ["EXITS", "THINGS", "PLAYERS", "ROOMS"], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> ENTRANCES(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@FIND", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> FIND(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@FIRSTEXIT", Switches = [], Behavior = CB.Default | CB.Args, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> FIRSTEXIT(IMUSHCodeParser parser, SharpCommandAttribute _2)
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

		[SharpCommand(Name = "@FORCE", Switches = ["NOEVAL", "INPLACE", "INLINE", "LOCALIZE", "CLEARREGS", "NOBREAK"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged | CB.Rs_Brace, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> FORCE(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@FUNCTION", Switches = ["ALIAS", "BUILTIN", "CLONE", "DELETE", "ENABLE", "DISABLE", "PRESERVE", "RESTORE", "RESTRICT"], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> FUNCTION(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@GREP", Switches = ["LIST", "PRINT", "ILIST", "IPRINT", "REGEXP", "WILD", "NOCASE", "PARENT"], Behavior = CB.Default | CB.EqSplit | CB.RSNoParse | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> GREP(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@HALT", Switches = ["ALL", "NOEVAL", "PID"], Behavior = CB.Default | CB.EqSplit | CB.Rs_Brace, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> HALT(IMUSHCodeParser parser, SharpCommandAttribute _2)
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

		[SharpCommand(Name = "@HOOK", Switches = ["LIST", "AFTER", "BEFORE", "EXTEND", "IGSWITCH", "IGNORE", "OVERRIDE", "INPLACE", "INLINE", "LOCALIZE", "CLEARREGS", "NOBREAK"], Behavior = CB.Default | CB.EqSplit | CB.RSArgs, CommandLock = "FLAG^WIZARD|POWER^HOOK", MinArgs = 0)]
		public static async ValueTask<Option<CallState>> HOOK(IMUSHCodeParser parser, SharpCommandAttribute _2)
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

		[SharpCommand(Name = "@INCLUDE", Switches = ["LOCALIZE", "CLEARREGS", "NOBREAK"], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> INCLUDE(IMUSHCodeParser parser, SharpCommandAttribute _2)
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

		[SharpCommand(Name = "@LEMIT", Switches = ["NOEVAL", "NOISY", "SILENT", "SPOOF"], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> LEMIT(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@LINK", Switches = ["PRESERVE"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> LINK(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@LISTMOTD", Switches = [], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> LISTMOTD(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@LIST", Switches = ["LOWERCASE", "MOTD", "LOCKS", "FLAGS", "FUNCTIONS", "POWERS", "COMMANDS", "ATTRIBS", "ALLOCATIONS", "ALL", "BUILTIN", "LOCAL"], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> LIST(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@LOCK", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.Switches | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> LOCK(IMUSHCodeParser parser, SharpCommandAttribute _2)
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

		[SharpCommand(Name = "@LOGWIPE", Switches = ["CHECK", "CMD", "CONN", "ERR", "TRACE", "WIZ", "ROTATE", "TRIM", "WIPE"], Behavior = CB.Default | CB.NoGagged | CB.God, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> LOGWIPE(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@LSET", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> LSET(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@MAIL", Switches = ["NOEVAL", "NOSIG", "STATS", "CSTATS", "DSTATS", "FSTATS", "DEBUG", "NUKE", "FOLDERS", "UNFOLDER", "LIST", "READ", "UNREAD", "CLEAR", "UNCLEAR", "STATUS", "PURGE", "FILE", "TAG", "UNTAG", "FWD", "FORWARD", "SEND", "SILENT", "URGENT", "REVIEW", "RETRACT"], Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> MAIL(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@MALIAS", Switches = ["SET", "CREATE", "DESTROY", "DESCRIBE", "RENAME", "STATS", "CHOWN", "NUKE", "ADD", "REMOVE", "LIST", "ALL", "WHO", "MEMBERS", "USEFLAG", "SEEFLAG"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> MALIAS(IMUSHCodeParser parser, SharpCommandAttribute _2)
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

		[SharpCommand(Name = "@MESSAGE", Switches = ["NOEVAL", "SPOOF", "NOSPOOF", "REMIT", "OEMIT", "SILENT", "NOISY"], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> MESSAGE(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@MONIKER", Switches = [], Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> MONIKER(IMUSHCodeParser parser, SharpCommandAttribute _2)
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

		[SharpCommand(Name = "@MVATTR", Switches = ["CONVERT", "NOFLAGCOPY"], Behavior = CB.Default | CB.EqSplit | CB.RSArgs, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> MVATTR(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@NAME", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.NoGagged | CB.NoGuest, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> NAME(IMUSHCodeParser parser, SharpCommandAttribute _2)
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

		[SharpCommand(Name = "@NOTIFY", Switches = ["ALL", "ANY", "SETQ"], Behavior = CB.Default | CB.EqSplit | CB.RSArgs, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> NOTIFY(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@NSCEMIT", Switches = ["NOEVAL", "NOISY", "SILENT"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> NSCEMIT(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@NSEMIT", Switches = ["ROOM", "NOEVAL", "SILENT"], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> NSEMIT(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@NSLEMIT", Switches = ["NOEVAL", "NOISY", "SILENT"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> NSLEMIT(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@NSOEMIT", Switches = ["NOEVAL"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> NSOEMIT(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@NSPEMIT", Switches = ["LIST", "SILENT", "NOISY", "NOEVAL"], Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> NSPEMIT(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@NSPROMPT", Switches = ["SILENT", "NOISY", "NOEVAL"], Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> NSPROMPT(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@NSREMIT", Switches = ["LIST", "NOEVAL", "NOISY", "SILENT"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> NSREMIT(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@NSZEMIT", Switches = ["NOISY", "SILENT"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> NSZEMIT(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@NUKE", Switches = [], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> NUKE(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@OEMIT", Switches = ["NOEVAL", "SPOOF"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> OEMIT(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@OPEN", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> OPEN(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@PARENT", Switches = [], Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> PARENT(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@PASSWORD", Switches = [], Behavior = CB.Player | CB.EqSplit | CB.NoParse | CB.RSNoParse | CB.NoGuest, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> PASSWORD(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@PCREATE", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.RSArgs, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> PCREATE(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@PEMIT", Switches = ["LIST", "CONTENTS", "SILENT", "NOISY", "NOEVAL", "PORT", "SPOOF"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> PEMIT(IMUSHCodeParser parser, SharpCommandAttribute _2)
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

		[SharpCommand(Name = "@POOR", Switches = [], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> POOR(IMUSHCodeParser parser, SharpCommandAttribute _2)
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

		[SharpCommand(Name = "@PROMPT", Switches = ["SILENT", "NOISY", "NOEVAL", "SPOOF"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> PROMPT(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@PS", Switches = ["ALL", "SUMMARY", "COUNT", "QUICK", "DEBUG"], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> PS(IMUSHCodeParser parser, SharpCommandAttribute _2)
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

		[SharpCommand(Name = "@QUOTA", Switches = ["ALL", "SET"], Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> QUOTA(IMUSHCodeParser parser, SharpCommandAttribute _2)
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

		[SharpCommand(Name = "@RECYCLE", Switches = ["OVERRIDE"], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> RECYCLE(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@REMIT", Switches = ["LIST", "NOEVAL", "NOISY", "SILENT", "SPOOF"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> REMIT(IMUSHCodeParser parser, SharpCommandAttribute _2)
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

		[SharpCommand(Name = "@RESPOND", Switches = ["HEADER", "TYPE"], Behavior = CB.Default | CB.NoGagged | CB.EqSplit, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> RESPOND(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@RESTART", Switches = ["ALL"], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> RESTART(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@RETRY", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.RSNoParse | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> RETRY(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@RWALL", Switches = ["NOEVAL", "EMIT"], Behavior = CB.Default, CommandLock = "FLAG^WIZARD|FLAG^ROYALTY", MinArgs = 0)]
		public static async ValueTask<Option<CallState>> RWALL(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@SCAN", Switches = ["ROOM", "SELF", "ZONE", "GLOBALS"], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> SCAN(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@SEARCH", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.RSNoParse, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> SEARCH(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@SELECT", Switches = ["NOTIFY", "REGEXP", "INPLACE", "INLINE", "LOCALIZE", "CLEARREGS", "NOBREAK"], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.RSNoParse, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> SELECT(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@SET", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> SET(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@SOCKSET", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.NoGagged | CB.RSArgs, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> SOCKSET(IMUSHCodeParser parser, SharpCommandAttribute _2)
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

		[SharpCommand(Name = "@SLAVE", Switches = ["RESTART"], Behavior = CB.Default, CommandLock = "FLAG^WIZARD", MinArgs = 0)]
		public static async ValueTask<Option<CallState>> SLAVE(IMUSHCodeParser parser, SharpCommandAttribute _2)
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

		[SharpCommand(Name = "@SITELOCK", Switches = ["BAN", "CHECK", "REGISTER", "REMOVE", "NAME", "PLAYER"], Behavior = CB.Default | CB.EqSplit | CB.RSArgs, CommandLock = "FLAG^WIZARD", MinArgs = 0)]
		public static async ValueTask<Option<CallState>> SITELOCK(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@STATS", Switches = ["CHUNKS", "FREESPACE", "PAGING", "REGIONS", "TABLES", "FLAGS"], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> STATS(IMUSHCodeParser parser, SharpCommandAttribute _2)
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

		[SharpCommand(Name = "@SWEEP", Switches = ["CONNECTED", "HERE", "INVENTORY", "EXITS"], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> SWEEP(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@SWITCH", Switches = ["NOTIFY", "FIRST", "ALL", "REGEXP", "INPLACE", "INLINE", "LOCALIZE", "CLEARREGS", "NOBREAK"], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.RSNoParse | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> SWITCH(IMUSHCodeParser parser, SharpCommandAttribute _2)
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

		[SharpCommand(Name = "@TELEPORT", Switches = ["SILENT", "INSIDE", "LIST"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> TELEPORT(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@TRIGGER", Switches = ["CLEARREGS", "SPOOF", "INLINE", "NOBREAK", "LOCALIZE", "INPLACE", "MATCH"], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> TRIGGER(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@UNDESTROY", Switches = [], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> UNDESTROY(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@UNLINK", Switches = [], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> UNLINK(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@UNLOCK", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.Switches | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> UNLOCK(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@UNRECYCLE", Switches = [], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> UNRECYCLE(IMUSHCodeParser parser, SharpCommandAttribute _2)
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

		[SharpCommand(Name = "@VERB", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.RSArgs, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> VERB(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@VERSION", Switches = [], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> VERSION(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@WAIT", Switches = ["PID", "UNTIL"], Behavior = CB.Default | CB.EqSplit | CB.RSNoParse | CB.Rs_Brace, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> WAIT(IMUSHCodeParser parser, SharpCommandAttribute _2)
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

		[SharpCommand(Name = "@WARNINGS", Switches = [], Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> WARNINGS(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@WCHECK", Switches = ["ALL", "ME"], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> WCHECK(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@WHEREIS", Switches = [], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> WHEREIS(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@WIPE", Switches = [], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> WIPE(IMUSHCodeParser parser, SharpCommandAttribute _2)
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

		[SharpCommand(Name = "@WIZMOTD", Switches = ["CLEAR"], Behavior = CB.Default, CommandLock = "FLAG^WIZARD", MinArgs = 0)]
		public static async ValueTask<Option<CallState>> WIZMOTD(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@ZEMIT", Switches = ["NOISY", "SILENT"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> ZEMIT(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "BUY", Switches = [], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> BUY(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "BRIEF", Switches = ["OPAQUE"], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> BRIEF(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "DESERT", Switches = [], Behavior = CB.Player | CB.Thing, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> DESERT(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "DISMISS", Switches = [], Behavior = CB.Player | CB.Thing, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> DISMISS(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "DROP", Switches = [], Behavior = CB.Player | CB.Thing, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> DROP(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "EXAMINE", Switches = ["ALL", "BRIEF", "DEBUG", "MORTAL", "OPAQUE", "PARENT"], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> EXAMINE(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "EMPTY", Switches = [], Behavior = CB.Player | CB.Thing | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> EMPTY(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "ENTER", Switches = [], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> ENTER(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "FOLLOW", Switches = [], Behavior = CB.Player | CB.Thing | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> FOLLOW(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "GET", Switches = [], Behavior = CB.Player | CB.Thing | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> GET(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "GIVE", Switches = ["SILENT"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> GIVE(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "GOTO", Switches = [], Behavior = CB.Player | CB.Thing, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> GOTO(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "HOME", Switches = [], Behavior = CB.Player | CB.Thing, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> HOME(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "INVENTORY", Switches = [], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> INVENTORY(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "LOOK", Switches = ["OUTSIDE", "OPAQUE"], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> LOOK(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "LEAVE", Switches = [], Behavior = CB.Player | CB.Thing, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> LEAVE(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "PAGE", Switches = ["LIST", "NOEVAL", "PORT", "OVERRIDE"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> PAGE(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "POSE", Switches = ["NOEVAL", "NOSPACE"], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> POSE(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "SCORE", Switches = [], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> SCORE(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "SAY", Switches = ["NOEVAL"], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> SAY(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "SEMIPOSE", Switches = ["NOEVAL"], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> SEMIPOSE(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "TEACH", Switches = ["LIST"], Behavior = CB.Default | CB.NoParse, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> TEACH(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "THINK", Switches = ["NOEVAL"], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> THINK(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "UNFOLLOW", Switches = [], Behavior = CB.Player | CB.Thing | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> UNFOLLOW(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "USE", Switches = [], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> USE(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "WHISPER", Switches = ["LIST", "NOISY", "SILENT", "NOEVAL"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> WHISPER(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "WITH", Switches = ["NOEVAL", "ROOM"], Behavior = CB.Player | CB.Thing | CB.EqSplit, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> WITH(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "WHO", Switches = [], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> WHO(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "DOING", Switches = [], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> DOING(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "SESSION", Switches = [], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> SESSION(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "ATTRIB_SET", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.NoGagged | CB.Internal, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> ATTRIB_SET(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "WARN_ON_MISSING", Switches = [], Behavior = CB.Default | CB.NoParse | CB.Internal | CB.Nop, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> WARN_ON_MISSING(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "HUH_COMMAND", Switches = [], Behavior = CB.Default | CB.NoParse | CB.Internal | CB.Nop, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> HUH_COMMAND(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "UNIMPLEMENTED_COMMAND", Switches = [], Behavior = CB.Default | CB.NoParse | CB.Internal | CB.Nop, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> UNIMPLEMENTED_COMMAND(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "ADDCOM", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> ADDCOM(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "DELCOM", Switches = [], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> DELCOM(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "@CLIST", Switches = ["FULL"], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> CLIST(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "COMTITLE", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> COMTITLE(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}

		[SharpCommand(Name = "COMLIST", Switches = [], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
		public static async ValueTask<Option<CallState>> COMLIST(IMUSHCodeParser parser, SharpCommandAttribute _2)
		{
			await ValueTask.CompletedTask;
			throw new NotImplementedException();
		}
	}
}
