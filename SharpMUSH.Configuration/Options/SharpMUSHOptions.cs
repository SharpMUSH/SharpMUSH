namespace SharpMUSH.Configuration.Options;

public record SharpMUSHOptions
{
	public required AttributeOptions Attribute { get; init; }
	public required ChatOptions Chat { get; init; }
	public required CommandOptions Command { get; init; }
	public required CompatibilityOptions Compatibility { get; init; }
	public required CosmeticOptions Cosmetic { get; init; }
	public required CostOptions Cost { get; init; }
	public required DatabaseOptions Database { get; init; }
	public required DumpOptions Dump { get; init; }
	public required FileOptions File { get; init; }
	public required FlagOptions Flag { get; init; }
	public required FunctionOptions Function { get; init; }
	public required LimitOptions Limit { get; init; }
	public required LogOptions Log { get; init; }
	public required MessageOptions Message { get; init; }
	public required NetOptions Net { get; init; }
	public required DebugOptions Debug { get; init; }
	public required AliasOptions Alias { get; init; }
	public required RestrictionOptions Restriction { get; init; }
	public required BannedNamesOptions BannedNames { get; init; }
	public required SitelockRulesOptions SitelockRules { get; init; }
	public required WarningOptions Warning { get; init; }
	public required TextFileOptions TextFile { get; init; }
	public required WikiOptions Wiki { get; init; }

	/// <summary>
	/// The one place a shipped default is written down.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Everything else that needs a default reads it from here: <c>OptionsService.Default()</c> (the
	/// configuration a fresh world is created with), <c>ReadPennMushConfig.Create</c>'s fallbacks (what
	/// an option means when a PennMUSH <c>mush.cnf</c> does not mention it) and the test harness'
	/// <c>TestSharpMushOptions.Create</c>. They used to state their own values and had diverged on
	/// fourteen options, so the same game behaved differently depending on how it was first created —
	/// <c>max_depth</c> was 50 in a new world and 10 in an imported one.
	/// </para>
	/// <para>
	/// A default therefore belongs in exactly one of two places: here, or as a constructor-parameter
	/// default on the option record when a compile-time constant is wanted elsewhere too
	/// (<see cref="WikiOptions.DefaultLocaleFallback"/>, <see cref="LimitOptions.GlobalQueueLimit"/>).
	/// <c>ConfigurationDefaultsTests</c> holds the two kinds to each other.
	/// </para>
	/// <para>
	/// A method rather than a property because the configuration source generators enumerate this
	/// record's properties to find the option categories, and a static one would be read as a
	/// twenty-fourth category.
	/// </para>
	/// </remarks>
	public static SharpMUSHOptions Default() => new()
	{
		Attribute = new AttributeOptions(
			ADestroy: false,
			AMail: false,
			EmptyAttributes: true,
			GenderAttribute: "SEX",
			PlayerAHear: true,
			PlayerListen: true,
			ReadRemoteDesc: false,
			ReverseShs: true,
			RoomConnects: true,
			Startups: true,
			ObjectivePronounAttribute: null,
			PossessivePronounAttribute: null,
			SubjectivePronounAttribute: null,
			AbsolutePossessivePronounAttribute: null
		),
		Chat = new ChatOptions(
			ChannelCost: 1000,
			ChannelTitleLength: 80,
			ChatTokenAlias: '+',
			MaxChannels: 200,
			MaxPlayerChannels: 0,
			NoisyCEmit: false,
			UseMuxComm: true
		),
		Command = new CommandOptions(
			DestroyPossessions: true,
			FullInvisibility: false,
			LinkToObject: true,
			NoisyWhisper: false,
			OwnerQueues: false,
			PossessiveGet: true,
			PossessiveGetD: false,
			ProbateJudge: 1,
			ReallySafe: true,
			WizardNoAEnter: false
		),
		Compatibility = new CompatibilityOptions(
			NullEqualsZero: true,
			SilentPEmit: false,
			TinyBooleans: false,
			TinyMath: false,
			TinyTrimFun: false,
			ParenGroups: false
		),
		Cosmetic = new CosmeticOptions(
			AnnounceConnects: true,
			AnsiNames: true,
			ChatStripQuote: true,
			CommaExitList: true,
			CountAll: false,
			ExaminePublicAttributes: true,
			FlagsOnExamine: true,
			FloatPrecision: 6,
			MoneyPlural: "Pennies",
			MoneySingular: "Penny",
			Monikers: true,
			OnlyAsciiInNames: true,
			PageAliases: false,
			PlayerNameSpaces: true,
			RoyaltyWallPrefix: "Admin:",
			WallPrefix: "Announcement:",
			WizardWallPrefix: "Broadcast:"
		),
		Cost = new CostOptions(
			ExitCost: 1,
			FindCost: 100,
			LinkCost: 1,
			ObjectCost: 10,
			QueueCost: 10,
			QuotaCost: 1,
			RoomCost: 10
		),
		// The ancestor and handler objects are the ones Migration_CreateDatabase seeds: #3 Ancestor
		// Room, #4 Ancestor Player, #5 Ancestor Exit, #6 Ancestor Thing, #7 Package Manager,
		// #8 HTTP Handler, #9 Event Handler. A -1 in a config file disables that ancestor.
		Database = new DatabaseOptions(
			AncestorRoom: 3,
			AncestorPlayer: 4,
			AncestorExit: 5,
			AncestorThing: 6,
			BaseRoom: 0,
			DefaultHome: 0,
			EventHandler: 9,
			// PennMUSH's exits_connect_rooms defaults to 0 (conf.c:1285).
			ExitsConnectRooms: false,
			HttpHandler: 8,
			PackageManager: 7,
			HttpRequestsPerSecond: 30,
			MasterRoom: 2,
			PlayerStart: 0,
			ZoneControlZmpOnly: true,
			AllowBrowserCode: false
		),
		Debug = new DebugOptions(
			DebugSharpParser: false
		),
		Dump = new DumpOptions(
			PurgeInterval: "10m1s"
		),
		File = new FileOptions(
			AccessFile: "access.cnf",
			// The colours file is JSON — SharpMUSH.Configuration ships colors.json — not a cnf.
			ColorsFile: "colors.json",
			DictionaryFile: null,
			NamesFile: "names.cnf",
			SSLCADirectory: null,
			SSLCAFile: null,
			SSLCertificateFile: null,
			SSLPrivateKeyFile: null
		),
		Flag = new FlagOptions(
			ChannelFlags: FlagOptions.Defaults.Split(FlagOptions.Defaults.Channel),
			ExitFlags: FlagOptions.Defaults.Split(FlagOptions.Defaults.Exit),
			PlayerFlags: FlagOptions.Defaults.Split(FlagOptions.Defaults.Player),
			RoomFlags: FlagOptions.Defaults.Split(FlagOptions.Defaults.Room),
			ThingFlags: FlagOptions.Defaults.Split(FlagOptions.Defaults.Thing)
		),
		Function = new FunctionOptions(
			FunctionSideEffects: true,
			SaferUserFunctions: true
		),
		Limit = new LimitOptions(
			CallLimit: 1000,
			ChunkMigrate: 150,
			ConnectFailLimit: 10,
			FunctionInvocationLimit: 100000,
			FunctionRecursionLimit: 100,
			GuestPaycheck: 0,
			IdleTimeout: 0,
			KeepaliveTimeout: 300,
			MaxAttributeValueLength: 8192,
			MailLimit: 300,
			MaxAliases: 3,
			MaxAttributesPerObj: 2048,
			MaxDbReference: null,
			// PennMUSH's max_depth defaults to 10 (conf.c:1321).
			MaxDepth: 10,
			MaxGuestPennies: 1000000000,
			MaxGuests: -1,
			MaxLogins: 120,
			MaxNamedQRegisters: 100,
			MaxParents: 10,
			MaxPennies: 1000000000,
			Paycheck: 50,
			// PennMUSH's player_name_len defaults to 15 (conf.c:1327).
			PlayerNameLen: 15,
			PlayerQueueLimit: 100,
			QueueChunk: 3,
			QueueEntryCpuTime: LimitOptions.DefaultQueueEntryCpuTime,
			QueueLoss: 63,
			StartingMoney: 150,
			StartingQuota: 20,
			UnconnectedIdleTimeout: 300,
			UseQuota: true,
			WhisperLoudness: 100
		),
		Log = new LogOptions(
			CheckpointLog: "log/checkpoint.log",
			CommandLog: "log/command.log",
			ConnectLog: "log/connect.log",
			ErrorLog: "log/netmush.log",
			LogCommands: false,
			LogForces: true,
			MemoryCheck: false,
			TraceLog: "log/trace.log",
			UseConnLog: true,
			UseSyslog: false,
			WizardLog: "log/wizard.log"
		),
		Message = new MessageOptions(
			ConnectFile: "connect.txt",
			ConnectHtmlFile: "connect.html",
			DownFile: "down.txt",
			DownHtmlFile: "down.html",
			FullFile: "full.txt",
			FullHtmlFile: "full.html",
			GuestFile: "guest.txt",
			GuestHtmlFile: "guest.html",
			IndexHtmlFile: "index.html",
			MessageOfTheDayFile: "motd.txt",
			MessageOfTheDayHtmlFile: "motd.html",
			NewUserFile: "newuser.txt",
			NewUserHtmlFile: "newuser.html",
			QuitFile: "quit.txt",
			QuitHtmlFile: "quit.html",
			RegisterCreateFile: "register.txt",
			RegisterCreateHtmlFile: "register.html",
			WhoFile: "who.txt",
			WhoHtmlFile: "who.html",
			WizMessageOfTheDayFile: "wizmotd.txt",
			WizMessageOfTheDayHtmlFile: "wizmotd.html"
		),
		Net = new NetOptions(
			Guests: true,
			IpAddr: null,
			JsonUnsafeUnescape: false,
			Logins: true,
			MudName: "SharpMUSH",
			MudUrl: null,
			// SharpMUSH-only; MXP is negotiated per client, so this only says whether to offer it.
			Mxp: true,
			PlayerCreation: true,
			Port: 4201,
			PortalPort: 5117,
			// PennMUSH's support_pueblo defaults to 0 (conf.c:1247), as does the shipped mushcnf.dst.
			Pueblo: false,
			SslPortalPort: 7296,
			SocketFile: "netmush.sock",
			SqlHost: null,
			SqlPlatform: null,
			SqlPassword: null,
			SqlDatabase: null,
			SqlUsername: null,
			SslIpAddr: null,
			SslPort: 4203,
			SslRequireClientCert: false,
			UseDns: true,
			UseWebsockets: true,
			WebsocketUrl: "/wsclient"
		),
		Alias = AliasOptions.Default,
		Restriction = new RestrictionOptions(
			CommandRestrictions: new Dictionary<string, string[]>(),
			FunctionRestrictions: new Dictionary<string, string[]>()
		),
		// Seeded examples, so a new game shows what these two look like rather than an empty page.
		// An imported game keeps its own names.cnf and access.cnf, so ReadPennMushConfig starts both
		// empty instead; ConfigurationDefaultsTests records that as the one deliberate difference.
		BannedNames = new BannedNamesOptions(
			BannedNames: ["Guest", "Admin", "Wizard"]
		),
		SitelockRules = new SitelockRulesOptions(
			Rules: new Dictionary<string, string[]>
			{
				{ "*.example.com", ["!connect", "!create", "!guest"] },
				{ "192.168.1.*", ["register"] },
				{ "trusted.domain.org", ["connect", "create", "guest"] }
			}
		),
		Warning = new WarningOptions(
			WarnInterval: "1h"
		),
		TextFile = new TextFileOptions(
			TextFilesDirectory: "TextFiles",
			EnableMarkdownRendering: true,
			CacheOnStartup: true
		),
		Wiki = new WikiOptions()
	};
};
