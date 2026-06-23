using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Services.Interfaces;
using FileOptions = SharpMUSH.Configuration.Options.FileOptions;

namespace SharpMUSH.Tests.Server;

/// <summary>
/// Builds a fully-populated <see cref="SharpMUSHOptions"/> for controller tests, varying only the
/// <c>Database.AllowBrowserCode</c> gate. Mirrors the OptionsService defaults so the positional records bind.
/// </summary>
internal static class TestSharpMushOptions
{
	public static SharpMUSHOptions Create(bool allowBrowserCode = false) => new()
	{
		Attribute = new AttributeOptions(
			ADestroy: false, AMail: false, EmptyAttributes: false,
			GenderAttribute: "SEX", PlayerAHear: true, PlayerListen: true,
			ReadRemoteDesc: false, ReverseShs: true, RoomConnects: true,
			Startups: true, ObjectivePronounAttribute: null,
			PossessivePronounAttribute: null, SubjectivePronounAttribute: null,
			AbsolutePossessivePronounAttribute: null),
		Chat = new ChatOptions(
			ChannelCost: 1000, ChannelTitleLength: 80, ChatTokenAlias: '+',
			MaxChannels: 200, MaxPlayerChannels: 0, NoisyCEmit: false, UseMuxComm: true),
		Command = new CommandOptions(
			DestroyPossessions: true, FullInvisibility: false, LinkToObject: true,
			NoisyWhisper: false, OwnerQueues: false, PossessiveGet: true,
			PossessiveGetD: false, ProbateJudge: 1, ReallySafe: true, WizardNoAEnter: false),
		Compatibility = new CompatibilityOptions(
			NullEqualsZero: true, SilentPEmit: false, TinyBooleans: false,
			TinyMath: false, TinyTrimFun: false),
		Cosmetic = new CosmeticOptions(
			AnnounceConnects: true, AnsiNames: true, ChatStripQuote: true,
			CommaExitList: true, CountAll: false, ExaminePublicAttributes: true,
			FlagsOnExamine: true, FloatPrecision: 15, MoneyPlural: "Pennies",
			MoneySingular: "Penny", Monikers: true, OnlyAsciiInNames: true,
			PageAliases: false, PlayerNameSpaces: true,
			RoyaltyWallPrefix: "Admin:", WallPrefix: "Announcement:",
			WizardWallPrefix: "Broadcast:"),
		Cost = new CostOptions(
			ExitCost: 1, FindCost: 100, LinkCost: 1, ObjectCost: 10,
			QueueCost: 10, QuotaCost: 1, RoomCost: 10),
		Database = new DatabaseOptions(
			AncestorExit: null, AncestorPlayer: null, AncestorRoom: null,
			AncestorThing: null, BaseRoom: 0, DefaultHome: 0, EventHandler: null,
			ExitsConnectRooms: true, HttpHandler: null, PackageManager: null,
			HttpRequestsPerSecond: 10,
			MasterRoom: 2, PlayerStart: 0, ZoneControlZmpOnly: true,
			AllowBrowserCode: allowBrowserCode),
		Debug = new DebugOptions(DebugSharpParser: false),
		Dump = new DumpOptions(PurgeInterval: "10m1s"),
		File = new FileOptions(
			AccessFile: "access.cnf", ColorsFile: "colors.cnf",
			DictionaryFile: null, NamesFile: "names.cnf",
			SSLCADirectory: null, SSLCAFile: null,
			SSLCertificateFile: null, SSLPrivateKeyFile: null),
		Flag = new FlagOptions(
			ChannelFlags: ["player"], ExitFlags: ["no_command"],
			PlayerFlags: ["enter_ok", "ansi", "no_command"],
			RoomFlags: [""], ThingFlags: [""]),
		Function = new FunctionOptions(
			FunctionSideEffects: true, SaferUserFunctions: true),
		Limit = new LimitOptions(
			CallLimit: 1000, ChunkMigrate: 150, ConnectFailLimit: 10,
			FunctionInvocationLimit: 100000, FunctionRecursionLimit: 100,
			GuestPaycheck: 0, IdleTimeout: 0, KeepaliveTimeout: 300,
			MaxAttributeValueLength: 8192, MailLimit: 300, MaxAliases: 3,
			MaxAttributesPerObj: 2048, MaxDbReference: null, MaxDepth: 50,
			MaxGuestPennies: 1000000000, MaxGuests: -1, MaxLogins: 120,
			MaxNamedQRegisters: 100, MaxParents: 10, MaxPennies: 1000000000,
			Paycheck: 50, PlayerNameLen: 21, PlayerQueueLimit: 100,
			QueueChunk: 3, QueueEntryCpuTime: 1000, QueueLoss: 63,
			StartingMoney: 150, StartingQuota: 20,
			UnconnectedIdleTimeout: 300, UseQuota: true, WhisperLoudness: 100),
		Log = new LogOptions(
			CheckpointLog: "log/checkpoint.log", CommandLog: "log/command.log",
			ConnectLog: "log/connect.log", ErrorLog: "log/error.log",
			LogCommands: false, LogForces: true, MemoryCheck: false,
			TraceLog: "log/trace.log", UseConnLog: true, UseSyslog: false,
			WizardLog: "log/wizard.log"),
		Message = new MessageOptions(
			ConnectFile: "connect.txt", ConnectHtmlFile: "connect.html",
			DownFile: "down.txt", DownHtmlFile: "down.html",
			FullFile: "full.txt", FullHtmlFile: "full.html",
			GuestFile: "guest.txt", GuestHtmlFile: "guest.html",
			IndexHtmlFile: "index.html", MessageOfTheDayFile: "motd.txt",
			MessageOfTheDayHtmlFile: "motd.html", NewUserFile: "newuser.txt",
			NewUserHtmlFile: "newuser.html", QuitFile: "quit.txt",
			QuitHtmlFile: "quit.html", RegisterCreateFile: "register.txt",
			RegisterCreateHtmlFile: "register.html", WhoFile: "who.txt",
			WhoHtmlFile: "who.html", WizMessageOfTheDayFile: "wizmotd.txt",
			WizMessageOfTheDayHtmlFile: "wizmotd.html"),
		Net = new NetOptions(
			Guests: true, IpAddr: null, JsonUnsafeUnescape: false,
			Logins: true, MudName: "SharpMUSH", MudUrl: null,
			PlayerCreation: true, Port: 4203, PortalPort: 5117,
			Pueblo: true, SslPortalPort: 7296, SocketFile: "netmush.sock",
			SqlHost: null, SqlPlatform: null, SqlPassword: null,
			SqlDatabase: null, SqlUsername: null, SslIpAddr: null,
			SslPort: 4202, SslRequireClientCert: false, UseDns: true,
			UseWebsockets: true, WebsocketUrl: "/wsclient"),
		Alias = new AliasOptions(
			FunctionAliases: new Dictionary<string, string[]>(),
			CommandAliases: new Dictionary<string, string[]>()),
		Restriction = new RestrictionOptions(
			CommandRestrictions: new Dictionary<string, string[]>(),
			FunctionRestrictions: new Dictionary<string, string[]>()),
		BannedNames = new BannedNamesOptions(BannedNames: ["Guest"]),
		SitelockRules = new SitelockRulesOptions(
			Rules: new Dictionary<string, string[]>()),
		Warning = new WarningOptions(WarnInterval: "1h"),
		TextFile = new TextFileOptions(
			TextFilesDirectory: "TextFiles", EnableMarkdownRendering: true,
			CacheOnStartup: true)
	};

	/// <summary>A minimal <see cref="IOptionsWrapper{T}"/> returning a fixed options snapshot.</summary>
	public sealed class FixedWrapper(SharpMUSHOptions value) : IOptionsWrapper<SharpMUSHOptions>
	{
		public SharpMUSHOptions CurrentValue { get; } = value;
	}
}
