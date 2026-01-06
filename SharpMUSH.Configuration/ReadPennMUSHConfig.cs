using System.Collections.Immutable;
using System.Text.RegularExpressions;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Configuration.Generated;
using FileOptions = SharpMUSH.Configuration.Options.FileOptions;

namespace SharpMUSH.Configuration;

public static partial class ReadPennMushConfig
{
	public static SharpMUSHOptions Create(string configFile)
	{
		string[] text;
		
		// Use generated metadata instead of reflection
		var propertyDictionary = ConfigMetadata.PropertyToAttributeName;
		var configDictionary = ConfigMetadata.AttributeToPropertyName.Keys
			.ToDictionary(key => key, _ => string.Empty);

		var splitter = KeyValueSplittingRegex();

		try
		{
			text = File.ReadAllLines(configFile);
		}
		catch (Exception ex) when (ex is FileNotFoundException or IOException)
		{
			throw;
		}

		// Parse config lines using regex pattern
		foreach (var configLine in text
							 .Where(line => configDictionary.Keys.Any(line.Trim().StartsWith))
							 .Select(line => splitter.Match(line.Trim()))
							 .Where(match => match.Success)
							 .Select(match => match.Groups))
		{
			configDictionary[configLine["Key"].Value] = configLine["Value"].Value;
		}

		// This is a lot of dupe work. This can likely be done with a proper bit of Reflection. 
		var work = new SharpMUSHOptions()
		{
			Attribute = new AttributeOptions(
				Boolean(Get(nameof(AttributeOptions.ADestroy)), false),
				Boolean(Get(nameof(AttributeOptions.AMail)), false),
				Boolean(Get(nameof(AttributeOptions.PlayerListen)), true),
				Boolean(Get(nameof(AttributeOptions.PlayerAHear)), true),
				Boolean(Get(nameof(AttributeOptions.Startups)), true),
				Boolean(Get(nameof(AttributeOptions.ReadRemoteDesc)), false),
				Boolean(Get(nameof(AttributeOptions.RoomConnects)), true),
				Boolean(Get(nameof(AttributeOptions.ReverseShs)), true),
				Boolean(Get(nameof(AttributeOptions.EmptyAttributes)), true),
				String(Get(nameof(AttributeOptions.GenderAttribute)), "SEX"),
				String(Get(nameof(AttributeOptions.PossessivePronounAttribute)), null),
				String(Get(nameof(AttributeOptions.AbsolutePossessivePronounAttribute)), null),
				String(Get(nameof(AttributeOptions.ObjectivePronounAttribute)), null),
				String(Get(nameof(AttributeOptions.SubjectivePronounAttribute)), null)
			),
			Chat = new ChatOptions(
				Get(nameof(ChatOptions.ChatTokenAlias)).FirstOrDefault('+'),
				Boolean(Get(nameof(ChatOptions.UseMuxComm)), true),
				UnsignedInteger(Get(nameof(ChatOptions.MaxChannels)), 200),
				UnsignedInteger(Get(nameof(ChatOptions.MaxPlayerChannels)), 0),
				UnsignedInteger(Get(nameof(ChatOptions.ChannelCost)), 1000),
				Boolean(Get(nameof(ChatOptions.NoisyCEmit)), false),
				UnsignedInteger(Get(nameof(ChatOptions.ChannelTitleLength)), 80)
			),
			Command = new CommandOptions(
				Boolean(Get(nameof(CommandOptions.NoisyWhisper)), false),
				Boolean(Get(nameof(CommandOptions.PossessiveGet)), true),
				Boolean(Get(nameof(CommandOptions.PossessiveGetD)), false),
				Boolean(Get(nameof(CommandOptions.LinkToObject)), true),
				Boolean(Get(nameof(CommandOptions.OwnerQueues)), false),
				Boolean(Get(nameof(CommandOptions.FullInvisibility)), false),
				Boolean(Get(nameof(CommandOptions.WizardNoAEnter)), false),
				Boolean(Get(nameof(CommandOptions.ReallySafe)), true),
				Boolean(Get(nameof(CommandOptions.DestroyPossessions)), true),
				RequiredDatabaseReference(Get(nameof(CommandOptions.ProbateJudge)), 1)
			),
			Compatibility = new CompatibilityOptions(
				Boolean(Get(nameof(CompatibilityOptions.NullEqualsZero)), true),
				Boolean(Get(nameof(CompatibilityOptions.TinyBooleans)), false),
				Boolean(Get(nameof(CompatibilityOptions.TinyTrimFun)), false),
				Boolean(Get(nameof(CompatibilityOptions.TinyMath)), false),
				Boolean(Get(nameof(CompatibilityOptions.SilentPEmit)), false)
			),
			Cosmetic = new CosmeticOptions(
				RequiredString(Get(nameof(CosmeticOptions.MoneySingular)), "Penny").Trim(),
				RequiredString(Get(nameof(CosmeticOptions.MoneyPlural)), "Pennies").Trim(),
				Boolean(Get(nameof(CosmeticOptions.PlayerNameSpaces)), true),
				Boolean(Get(nameof(CosmeticOptions.AnsiNames)), true),
				Boolean(Get(nameof(CosmeticOptions.OnlyAsciiInNames)), true),
				Boolean(Get(nameof(CosmeticOptions.Monikers)), true),
				UnsignedInteger(Get(nameof(CosmeticOptions.FloatPrecision)), 6),
				Boolean(Get(nameof(CosmeticOptions.CommaExitList)), true),
				Boolean(Get(nameof(CosmeticOptions.CountAll)), false),
				Boolean(Get(nameof(CosmeticOptions.PageAliases)), false),
				Boolean(Get(nameof(CosmeticOptions.FlagsOnExamine)), true),
				Boolean(Get(nameof(CosmeticOptions.ExaminePublicAttributes)), true),
				RequiredString(Get(nameof(CosmeticOptions.WizardWallPrefix)), "Broadcast:").Trim(),
				RequiredString(Get(nameof(CosmeticOptions.RoyaltyWallPrefix)), "Admin:").Trim(),
				RequiredString(Get(nameof(CosmeticOptions.WallPrefix)), "Announcement:").Trim(),
				Boolean(Get(nameof(CosmeticOptions.AnnounceConnects)), true),
				Boolean(Get(nameof(CosmeticOptions.ChatStripQuote)), true)
			),
			Cost = new CostOptions(
				UnsignedInteger(Get(nameof(CostOptions.ObjectCost)), 10),
				UnsignedInteger(Get(nameof(CostOptions.ExitCost)), 1),
				UnsignedInteger(Get(nameof(CostOptions.LinkCost)), 1),
				UnsignedInteger(Get(nameof(CostOptions.RoomCost)), 10),
				UnsignedInteger(Get(nameof(CostOptions.QueueCost)), 10),
				UnsignedInteger(Get(nameof(CostOptions.QuotaCost)), 1),
				UnsignedInteger(Get(nameof(CostOptions.FindCost)), 100)
			),
			Database = new DatabaseOptions(
				RequiredDatabaseReference(Get(nameof(DatabaseOptions.PlayerStart)), 0),
				RequiredDatabaseReference(Get(nameof(DatabaseOptions.MasterRoom)), 2),
				RequiredDatabaseReference(Get(nameof(DatabaseOptions.BaseRoom)), 0),
				RequiredDatabaseReference(Get(nameof(DatabaseOptions.DefaultHome)), 0),
				Boolean(Get(nameof(DatabaseOptions.ExitsConnectRooms)), false),
				Boolean(Get(nameof(DatabaseOptions.ZoneControlZmpOnly)), true),
				DatabaseReference(Get(nameof(DatabaseOptions.AncestorRoom)), null),
				DatabaseReference(Get(nameof(DatabaseOptions.AncestorExit)), null),
				DatabaseReference(Get(nameof(DatabaseOptions.AncestorThing)), null),
				DatabaseReference(Get(nameof(DatabaseOptions.AncestorPlayer)), null),
				DatabaseReference(Get(nameof(DatabaseOptions.EventHandler)), null),
				DatabaseReference(Get(nameof(DatabaseOptions.HttpHandler)), null),
				UnsignedInteger(Get(nameof(DatabaseOptions.HttpRequestsPerSecond)), 30)
			),
			Dump = new DumpOptions(
				RequiredString(Get(nameof(DumpOptions.PurgeInterval)), "10m1s")
			),
			File = new FileOptions(
				RequiredString(Get(nameof(FileOptions.AccessFile)), "access.cnf"),
				RequiredString(Get(nameof(FileOptions.NamesFile)), "names.cnf"),
				RequiredString(Get(nameof(FileOptions.SSLPrivateKeyFile)), string.Empty),
				RequiredString(Get(nameof(FileOptions.SSLCertificateFile)), string.Empty),
				RequiredString(Get(nameof(FileOptions.SSLCAFile)), string.Empty),
				RequiredString(Get(nameof(FileOptions.SSLCADirectory)), string.Empty),
				RequiredString(Get(nameof(FileOptions.DictionaryFile)), string.Empty),
				RequiredString(Get(nameof(FileOptions.ColorsFile)), "colors.json")
			),
			Flag = new FlagOptions(
				PlayerFlags: RequiredString(Get(nameof(FlagOptions.PlayerFlags)), "enter_ok ansi no_command").Split(' '),
				RoomFlags: RequiredString(Get(nameof(FlagOptions.RoomFlags)), "no_command").Split(' '),
				ThingFlags: RequiredString(Get(nameof(FlagOptions.ThingFlags)), "").Split(' '),
				ExitFlags: RequiredString(Get(nameof(FlagOptions.ExitFlags)), "no_command").Split(' '),
				ChannelFlags: RequiredString(Get(nameof(FlagOptions.ChannelFlags)), "player").Split(' ')
			),
			Function = new FunctionOptions(
				SaferUserFunctions: Boolean(Get(nameof(FunctionOptions.SaferUserFunctions)), true),
				FunctionSideEffects: Boolean(Get(nameof(FunctionOptions.FunctionSideEffects)), true)
			),
			Limit = new LimitOptions(
				UnsignedInteger(Get(nameof(LimitOptions.MaxAliases)), 3),
				DatabaseReference(Get(nameof(LimitOptions.MaxDbReference)), null),
				UnsignedInteger(Get(nameof(LimitOptions.MaxAttributesPerObj)), 2048),
				UnsignedInteger(Get(nameof(LimitOptions.MaxLogins)), 120),
				Integer(Get(nameof(LimitOptions.MaxGuests)), -1),
				UnsignedInteger(Get(nameof(LimitOptions.MaxNamedQRegisters)), 100),
				UnsignedInteger(Get(nameof(LimitOptions.ConnectFailLimit)), 10),
				UnsignedInteger(Get(nameof(LimitOptions.IdleTimeout)), 0),
				UnsignedInteger(Get(nameof(LimitOptions.UnconnectedIdleTimeout)), 300),
				UnsignedInteger(Get(nameof(LimitOptions.KeepaliveTimeout)), 300),
				UnsignedInteger(Get(nameof(LimitOptions.WhisperLoudness)), 100),
				UnsignedInteger(Get(nameof(LimitOptions.StartingQuota)), 20),
				UnsignedInteger(Get(nameof(LimitOptions.StartingMoney)), 150),
				UnsignedInteger(Get(nameof(LimitOptions.Paycheck)), 50),
				UnsignedInteger(Get(nameof(LimitOptions.GuestPaycheck)), 0),
				UnsignedInteger(Get(nameof(LimitOptions.MaxPennies)), 1000000000),
				UnsignedInteger(Get(nameof(LimitOptions.MaxGuestPennies)), 1000000000),
				UnsignedInteger(Get(nameof(LimitOptions.MaxParents)), 10),
				UnsignedInteger(Get(nameof(LimitOptions.MailLimit)), 300),
				UnsignedInteger(Get(nameof(LimitOptions.MaxDepth)), 10),
				UnsignedInteger(Get(nameof(LimitOptions.PlayerQueueLimit)), 100),
				UnsignedInteger(Get(nameof(LimitOptions.QueueLoss)), 63),
				UnsignedInteger(Get(nameof(LimitOptions.QueueChunk)), 3),
				UnsignedInteger(Get(nameof(LimitOptions.FunctionRecursionLimit)), 100),
				UnsignedInteger(Get(nameof(LimitOptions.FunctionInvocationLimit)), 100000),
				UnsignedInteger(Get(nameof(LimitOptions.CallLimit)), 1000),
				UnsignedInteger(Get(nameof(LimitOptions.PlayerNameLen)), 21),
				UnsignedInteger(Get(nameof(LimitOptions.QueueEntryCpuTime)), 1000),
				Boolean(Get(nameof(LimitOptions.UseQuota)), true),
				UnsignedInteger(Get(nameof(LimitOptions.ChunkMigrate)), 150)
			),
			Log = new LogOptions(
				Boolean(Get(nameof(LogOptions.UseSyslog)), false),
				Boolean(Get(nameof(LogOptions.LogCommands)), false),
				Boolean(Get(nameof(LogOptions.LogForces)), true),
				RequiredString(Get(nameof(LogOptions.ErrorLog)), "log/netmush.log"),
				RequiredString(Get(nameof(LogOptions.CommandLog)), "log/command.log"),
				RequiredString(Get(nameof(LogOptions.WizardLog)), "log/wizard.log"),
				RequiredString(Get(nameof(LogOptions.CheckpointLog)), "log/checkpoint.log"),
				RequiredString(Get(nameof(LogOptions.TraceLog)), "log/trace.log"),
				RequiredString(Get(nameof(LogOptions.ConnectLog)), "log/connect.log"),
				Boolean(Get(nameof(LogOptions.MemoryCheck)), false),
				Boolean(Get(nameof(LogOptions.UseConnLog)), true)
			),
			Message = new MessageOptions(
				RequiredString(Get(nameof(MessageOptions.ConnectFile)), "connect.txt"),
				RequiredString(Get(nameof(MessageOptions.MessageOfTheDayFile)), "motd.txt"),
				RequiredString(Get(nameof(MessageOptions.WizMessageOfTheDayFile)), "wizmotd.txt"),
				RequiredString(Get(nameof(MessageOptions.NewUserFile)), "newuser.txt"),
				RequiredString(Get(nameof(MessageOptions.RegisterCreateFile)), "register.txt"),
				RequiredString(Get(nameof(MessageOptions.QuitFile)), "quit.txt"),
				RequiredString(Get(nameof(MessageOptions.DownFile)), "down.txt"),
				RequiredString(Get(nameof(MessageOptions.FullFile)), "full.txt"),
				RequiredString(Get(nameof(MessageOptions.GuestFile)), "guest.txt"),
				RequiredString(Get(nameof(MessageOptions.WhoFile)), "who.txt"),
				RequiredString(Get(nameof(MessageOptions.ConnectHtmlFile)), "connect.html"),
				RequiredString(Get(nameof(MessageOptions.MessageOfTheDayHtmlFile)), "motd.html"),
				RequiredString(Get(nameof(MessageOptions.WizMessageOfTheDayHtmlFile)), "wizmotd.html"),
				RequiredString(Get(nameof(MessageOptions.NewUserHtmlFile)), "newuser.html"),
				RequiredString(Get(nameof(MessageOptions.RegisterCreateHtmlFile)), "register.html"),
				RequiredString(Get(nameof(MessageOptions.QuitHtmlFile)), "quit.html"),
				RequiredString(Get(nameof(MessageOptions.DownHtmlFile)), "down.html"),
				RequiredString(Get(nameof(MessageOptions.FullHtmlFile)), "full.html"),
				RequiredString(Get(nameof(MessageOptions.GuestHtmlFile)), "guest.html"),
				RequiredString(Get(nameof(MessageOptions.WhoHtmlFile)), "who.html"),
				RequiredString(Get(nameof(MessageOptions.IndexHtmlFile)), "index.html")
			),
			Net = new NetOptions(
				RequiredString(Get(nameof(NetOptions.MudName)), "SharpMUSH"),
				String(Get(nameof(NetOptions.MudUrl)), null),
				String(Get(nameof(NetOptions.IpAddr)), null),
				String(Get(nameof(NetOptions.SslIpAddr)), null),
				UnsignedInteger(Get(nameof(NetOptions.Port)), 4203),
				UnsignedInteger(Get(nameof(NetOptions.SslPort)), 4202),
				UnsignedInteger(Get(nameof(NetOptions.PortalPort)), 5117),
				UnsignedInteger(Get(nameof(NetOptions.SslPortalPort)), 7296),
				RequiredString(Get(nameof(NetOptions.SocketFile)), "netmush.sock"),
				Boolean(Get(nameof(NetOptions.UseWebsockets)), true),
				RequiredString(Get(nameof(NetOptions.WebsocketUrl)), "/wsclient"),
				Boolean(Get(nameof(NetOptions.UseDns)), true),
				Boolean(Get(nameof(NetOptions.Logins)), true),
				Boolean(Get(nameof(NetOptions.PlayerCreation)), true),
				Boolean(Get(nameof(NetOptions.Guests)), true),
				Boolean(Get(nameof(NetOptions.Pueblo)), true),
				String(Get(nameof(NetOptions.SqlPlatform)), null),
				String(Get(nameof(NetOptions.SqlHost)), "localhost"),
				String(Get(nameof(NetOptions.SqlDatabase)), null),
				String(Get(nameof(NetOptions.SqlUsername)), null),
				String(Get(nameof(NetOptions.SqlPassword)), null),
				Boolean(Get(nameof(NetOptions.JsonUnsafeUnescape)), false),
				Boolean(Get(nameof(NetOptions.SslRequireClientCert)), false)
			),
			Debug = new DebugOptions(
				Boolean(Get(nameof(DebugOptions.DebugSharpParser)), false)
			),
			Alias = new AliasOptions(
				FunctionAliases: new Dictionary<string, string[]>
				{
					{ "atrlock", ["attrlock"] },
					{ "iter", ["parse"] },
					{ "lsearch", ["search"] },
					{ "lstats", ["stats"] },
					{ "lthings", ["lobjects"] },
					{ "lvthings", ["lvobjects"] },
					{ "modulo", ["mod", "modulus"] },
					{ "nattr", ["attrcnt"] },
					{ "nattrp", ["attrpcnt"] },
					{ "nthings", ["nobjects"] },
					{ "nvthings", ["nvobjects"] },
					{ "randword", ["pickrand"] },
					{ "soundslike", ["soundlike"] },
					{ "textfile", ["dynhelp"] },
					{ "trunc", ["val"] },
					{ "ufun", ["u"] },
					{ "xthings", ["xobjects"] },
					{ "xvthings", ["xvobjects"] }
				},
				CommandAliases: new Dictionary<string, string[]>
				{
					{ "@ATRLOCK", ["@attrlock"] },
					{ "@ATRCHOWN", ["@attrchown"] },
					{ "@EDIT", ["@gedit"] },
					{ "@IFELSE", ["@if"] },
					{ "@SWITCH", ["@sw"] },
					{ "GET", ["take"] },
					{ "GOTO", ["move"] },
					{ "INVENTORY", ["i"] },
					{ "LOOK", ["l"] },
					{ "PAGE", ["p"] },
					{ "WHISPER", ["w"] }
				}
			),
			Restriction = new RestrictionOptions(
				CommandRestrictions: new Dictionary<string, string[]>(),
				FunctionRestrictions: new Dictionary<string, string[]>()
			),
			BannedNames = new BannedNamesOptions(
				BannedNames: []
			),
			SitelockRules = new SitelockRulesOptions(
				Rules: new Dictionary<string, string[]>()
			),
			Warning = new WarningOptions(
				WarnInterval: RequiredString(Get(nameof(WarningOptions.WarnInterval)), "1h")
			),
			TextFile = new TextFileOptions(
				TextFilesDirectory: RequiredString(Get(nameof(TextFileOptions.TextFilesDirectory)), "text_files"),
				EnableMarkdownRendering: Boolean(Get(nameof(TextFileOptions.EnableMarkdownRendering)), true),
				CacheOnStartup: Boolean(Get(nameof(TextFileOptions.CacheOnStartup)), true)
			)
		};

		return work;

		string Get(string key) => configDictionary[propertyDictionary[key]];
	}

	private static bool Boolean(string value, bool fallback) =>
		string.IsNullOrWhiteSpace(value)
			? fallback
			: value is not ("-1" or "0" or "false" or "no");

	private static string RequiredString(string value, string fallback) =>
		string.IsNullOrWhiteSpace(value)
			? fallback
			: value;

	private static string? String(string value, string? fallback) =>
		string.IsNullOrWhiteSpace(value)
			? fallback
			: value;

	private static uint UnsignedInteger(string value, uint fallback) =>
		string.IsNullOrWhiteSpace(value)
			? fallback
			: uint.TryParse(value, out var result)
				? result
				: fallback;


	private static int Integer(string value, int fallback) =>
		string.IsNullOrWhiteSpace(value)
			? fallback
			: int.TryParse(value, out var result)
				? result
				: fallback;

	private static uint? DatabaseReference(string value, uint? fallback) =>
		string.IsNullOrWhiteSpace(value)
			? fallback
			: uint.TryParse(value, out var result)
				? result
				: fallback;

	private static uint RequiredDatabaseReference(string value, uint fallback) =>
		UnsignedInteger(value, fallback);

	[GeneratedRegex(@"^(?<Key>[^\s]+)\s+(?<Value>.+)\s*$")]
	private static partial Regex KeyValueSplittingRegex();
}