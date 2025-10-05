using System.Text.Json;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;
using FileOptions = SharpMUSH.Configuration.Options.FileOptions;

namespace SharpMUSH.Library.Services;

public class OptionsService(ISharpDatabase database) : IOptionsFactory<SharpMUSHOptions>
{
	public SharpMUSHOptions Create(string _)
	{
		var data = database.GetExpandedServerData(nameof(SharpMUSHOptions))
			.AsTask().ConfigureAwait(false).GetAwaiter().GetResult();

		if (data is not null)
		{
			return JsonSerializer.Deserialize<SharpMUSHOptions>(data) ?? throw new Exception("Invalid options");
		}

		var defaultSettings = Default();
		var defaultSettingsJson = JsonSerializer.Serialize(defaultSettings);
			
		database.SetExpandedServerData(nameof(SharpMUSHOptions), defaultSettingsJson)
			.AsTask().ConfigureAwait(false).GetAwaiter().GetResult();

		return defaultSettings;
	}

	private SharpMUSHOptions Default()
	{
		return new SharpMUSHOptions
		{
			Attribute = new AttributeOptions(
				AbsolutePossessivePronounAttribute: "theirs",
				ADestroy: false,
				AMail: true,
				EmptyAttributes: false,
				GenderAttribute: "Gender",
				ObjectivePronounAttribute: "them",
				PlayerAHear: true,
				PlayerListen: false,
				PossessivePronounAttribute: "their",
				ReadRemoteDesc: false,
				ReverseShs: false,
				RoomConnects: true,
				Startups: true,
				SubjectivePronounAttribute: "they"
			),
			Chat = new ChatOptions(
				ChannelCost: 1000,
				ChannelTitleLength: 256,
				ChatTokenAlias: '+',
				MaxChannels: 100,
				MaxPlayerChannels: 20,
				NoisyCEmit: true,
				UseMuxComm: true
			),
			Command = new CommandOptions(
				DestroyPossessions: true,
				FullInvisibility: true,
				LinkToObject: false,
				NoisyWhisper: false,
				OwnerQueues: true,
				PossessiveGet: true,
				PossessiveGetD: false,
				ProbateJudge: 1,
				ReallySafe: true,
				WizardNoAEnter: true
			),
			Compatibility = new CompatibilityOptions(
				NullEqualsZero: true,
				SilentPEmit: false,
				TinyBooleans: true,
				TinyMath: true,
				TinyTrimFun: false
			),
			Cosmetic = new CosmeticOptions(
				AnnounceConnects: false,
				AnsiNames: true,
				ChatStripQuote: true,
				CommaExitList: false,
				CountAll: false,
				ExaminePublicAttributes: true,
				FlagsOnExamine: true,
				FloatPrecision: 6,
				MoneyPlural: "pennies",
				MoneySingular: "penny",
				Monikers: true,
				OnlyAsciiInNames: false,
				PageAliases: false,
				PlayerNameSpaces: true,
				RoyaltyWallPrefix: "Royalty",
				WallPrefix: "Wall",
				WizardWallPrefix: "Wizard"
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
			Database = new DatabaseOptions(
				AncestorExit: null,
				AncestorPlayer: null,
				AncestorRoom: null,
				AncestorThing: null,
				BaseRoom: 2,
				DefaultHome: 0,
				EventHandler: null,
				ExitsConnectRooms: true,
				HttpHandler: null,
				HttpRequestsPerSecond: 10,
				MasterRoom: 2,
				PlayerStart: 0,
				ZoneControlZmpOnly: false
			),
			Debug = new DebugOptions(
				DebugSharpParser: false
			),
			Dump = new DumpOptions(
				PurgeInterval: "604800"
			),
			File = new FileOptions(
				AccessFile: "access.cnf",
				ColorsFile: "colors.cnf",
				DictionaryFile: "dict.db",
				NamesFile: "names.cnf",
				SSLCADirectory: null,
				SSLCAFile: null,
				SSLCertificateFile: null,
				SSLPrivateKeyFile: null
			),
			Flag = new FlagOptions(
				ChannelFlags: ["player"],
				ExitFlags: ["no_command"],
				PlayerFlags: ["player", "no_command"],
				RoomFlags: ["room"],
				ThingFlags: ["thing"]
			),
			Function = new FunctionOptions(
				FunctionSideEffects: true,
				SaferUserFunctions: true
			),
			Limit = new LimitOptions(
				CallLimit: 10000,
				ChunkMigrate: 5,
				ConnectFailLimit: 100,
				FunctionInvocationLimit: 2500,
				FunctionRecursionLimit: 50,
				GuestPaycheck: 50,
				IdleTimeout: 3600,
				KeepaliveTimeout: 600,
				MailLimit: 1000,
				MaxAliases: 3,
				MaxAttributesPerObj: 2048,
				MaxDbReference: null,
				MaxDepth: 100,
				MaxGuestPennies: 10000,
				MaxGuests: -1,
				MaxLogins: 120,
				MaxNamedQRegisters: 100,
				MaxParents: 10,
				MaxPennies: 10000,
				Paycheck: 50,
				PlayerNameLen: 16,
				PlayerQueueLimit: 100,
				QueueChunk: 3,
				QueueEntryCpuTime: 1000,
				QueueLoss: 10,
				StartingMoney: 100,
				StartingQuota: 20,
				UnconnectedIdleTimeout: 300,
				UseQuota: false,
				WhisperLoudness: 100
			),
			Log = new LogOptions(
				CheckpointLog: "checkpoint.log",
				CommandLog: "command.log",
				ConnectLog: "connect.log",
				ErrorLog: "error.log",
				LogCommands: false,
				LogForces: true,
				MemoryCheck: true,
				TraceLog: "trace.log",
				UseConnLog: true,
				UseSyslog: true,
				WizardLog: "wizard.log"
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
				JsonUnsafeUnescape: true,
				Logins: true,
				MudName: "SharpMUSH",
				MudUrl: null,
				PlayerCreation: true,
				Port: 4203,
				PortalPort: 5117,
				Pueblo: true,
				SllPortalPort: 5443,
				SocketFile: "socket",
				SqlHost: null,
				SqlPlatform: null,
				SslIpAddr: null,
				SslPort: 4202,
				SslRequireClientCert: false,
				UseDns: true,
				UseWebsockets: true,
				WebsocketUrl: null
			)
		};
	}
}