using System.Collections.Immutable;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;
using FileOptions = SharpMUSH.Configuration.Options.FileOptions;

namespace SharpMUSH.Configuration;

public partial class ReadPennMUSHConfig : IOptionsFactory<PennMUSHOptions>
{
	private static bool Boolean(string value, bool fallback) =>
		string.IsNullOrWhiteSpace(value)
			? fallback
			: value is "-1" or "0" or "false";

	private static string String(string value, string fallback) =>
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

	public PennMUSHOptions Create(string filename)
	{
		var keys = typeof(PennMUSHOptions)
			.GetProperties()
			.Select(property => property.GetType())
			.SelectMany(configType => configType
				.GetProperties()
				.Where(property => property.CustomAttributes.Any())
				.SelectMany(configProperty => configProperty
					.GetCustomAttributes<PennConfigAttribute>()
					.Select(attribute => (configProperty, attribute)))).ToImmutableHashSet();

		var propertyDictionary = keys.ToDictionary(
			key => key.configProperty.Name,
			key => key.attribute.Name);
		var configDictionary = keys.ToDictionary(
			key => key.attribute.Name,
			_ => string.Empty);

		var splitter = KeyValueSplittingRegex();

		var text = File.ReadAllLines(filename);

		// TODO: Use a Regex to split the values.
		foreach (var configLine in text
			         .Where(line => configDictionary.Keys.Any(line.Trim().StartsWith))
			         .Select(line => splitter.Match(line.Trim()))
			         .Where(match => match.Success)
			         .Select(match => match.Groups))
		{
			configDictionary[configLine["Key"].Value] = configLine["Value"].Value;
		}

		// This is a lot of dupe work. This can likely be done with a proper bit of Reflection. 
		var work = new PennMUSHOptions(
			new AttributeOptions(
				Boolean(Get(nameof(AttributeOptions.ADestroy)), false),
				Boolean(Get(nameof(AttributeOptions.AMail)), false),
				Boolean(Get(nameof(AttributeOptions.PlayerListen)), true),
				Boolean(Get(nameof(AttributeOptions.PlayerAHear)), true),
				Boolean(Get(nameof(AttributeOptions.Startups)), true),
				Boolean(Get(nameof(AttributeOptions.ReadRemoteDesc)), false),
				Boolean(Get(nameof(AttributeOptions.RoomConnects)), true),
				Boolean(Get(nameof(AttributeOptions.ReverseShs)), true),
				Boolean(Get(nameof(AttributeOptions.EmptyAttributes)), true)
			),
			new ChatOptions(
				Get(nameof(ChatOptions.ChatTokenAlias)).FirstOrDefault('+'),
				Boolean(Get(nameof(ChatOptions.UseMuxComm)), true),
				UnsignedInteger(Get(nameof(ChatOptions.MaxChannels)), 200),
				UnsignedInteger(Get(nameof(ChatOptions.MaxPlayerChannels)), 0),
				UnsignedInteger(Get(nameof(ChatOptions.ChannelCost)), 1000),
				Boolean(Get(nameof(ChatOptions.NoisyCEmit)), false),
				UnsignedInteger(Get(nameof(ChatOptions.ChannelTitleLength)), 80)
			),
			new CommandOptions(
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
			new CompatibilityOptions(
				Boolean(Get(nameof(CompatibilityOptions.NullEqualsZero)), true),
				Boolean(Get(nameof(CompatibilityOptions.TinyBooleans)), false),
				Boolean(Get(nameof(CompatibilityOptions.TinyTrimFun)), false),
				Boolean(Get(nameof(CompatibilityOptions.TinyMath)), false),
				Boolean(Get(nameof(CompatibilityOptions.SilentPEmit)), false)
			),
			new CosmeticOptions(
				String(Get(nameof(CosmeticOptions.MoneySingular)), "Penny").Trim(),
				String(Get(nameof(CosmeticOptions.MoneyPlural)), "Pennies").Trim(),
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
				String(Get(nameof(CosmeticOptions.WizardWallPrefix)), "Broadcast:").Trim(),
				String(Get(nameof(CosmeticOptions.RoyaltyWallPrefix)), "Admin:").Trim(),
				String(Get(nameof(CosmeticOptions.WallPrefix)), "Announcement:").Trim(),
				Boolean(Get(nameof(CosmeticOptions.AnnounceConnects)), true),
				Boolean(Get(nameof(CosmeticOptions.ChatStripQuote)), true)
			),
			new CostOptions(
				UnsignedInteger(Get(nameof(CostOptions.ObjectCost)), 10),
				UnsignedInteger(Get(nameof(CostOptions.ExitCost)), 1),
				UnsignedInteger(Get(nameof(CostOptions.LinkCost)), 1),
				UnsignedInteger(Get(nameof(CostOptions.RoomCost)), 10),
				UnsignedInteger(Get(nameof(CostOptions.QueueCost)), 10),
				UnsignedInteger(Get(nameof(CostOptions.QuotaCost)), 1),
				UnsignedInteger(Get(nameof(CostOptions.FindCost)), 100)
			),
			new DatabaseOptions(
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
			new DumpOptions(
				Boolean(Get(nameof(DumpOptions.ForkingDump)), true),
				String(Get(nameof(DumpOptions.DumpMessage)), "Saving Database. Game may freeze for a moment."),
				String(Get(nameof(DumpOptions.DumpComplete)), "Save complete."),
				String(Get(nameof(DumpOptions.DumpWarning1Min)), "Database Save in 1 minute."),
				String(Get(nameof(DumpOptions.DumpWarning5Min)), "Database Save in 5 minutes."),
				String(Get(nameof(DumpOptions.DumpInterval)), "4h"),
				String(Get(nameof(DumpOptions.WarningInterval)), "1h"),
				String(Get(nameof(DumpOptions.PurgeInterval)), "10m1s"),
				String(Get(nameof(DumpOptions.DatabaseCheckInterval)), "9m59s")
			),
			new FileOptions(
				InputDatabase: "ignored",
				OutputDatabase: "ignored",
				CrashDatabase: "ignored",
				MailDatabase: "ignored",
				ChatDatabase: "ignored",
				CompressSuffix: "ignored",
				CompressProgram: "ignored",
				UnCompressProgram: "ignored",
				String(Get(nameof(FileOptions.AccessFile)), "access.cnf"),
				String(Get(nameof(FileOptions.NamesFile)), "names.cnf"),
				ChunkSwapFile: "ignored",
				ChunkSwapInitialSize: "ignored",
				ChunkCacheMemory: "ignored",
				String(Get(nameof(FileOptions.SSLPrivateKeyFile)), string.Empty),
				String(Get(nameof(FileOptions.SSLCertificateFile)), string.Empty),
				String(Get(nameof(FileOptions.SSLCAFile)), string.Empty),
				String(Get(nameof(FileOptions.SSLCADirectory)), string.Empty),
				String(Get(nameof(FileOptions.DictionaryFile)), string.Empty),
				String(Get(nameof(FileOptions.ColorsFile)), "colors.json")
			),
			new FlagOptions(
				PlayerFlags: String(Get(nameof(FlagOptions.PlayerFlags)), "enter_ok ansi no_command").Split(' '),
				RoomFlags: String(Get(nameof(FlagOptions.RoomFlags)), "no_command").Split(' '),
				ThingFlags: String(Get(nameof(FlagOptions.ThingFlags)), "").Split(' '),
				ExitFlags: String(Get(nameof(FlagOptions.ExitFlags)), "no_command").Split(' '),
				ChannelFlags: String(Get(nameof(FlagOptions.ChannelFlags)), "player").Split(' ')
			),
			new FunctionOptions(
				SaferUserFunctions: Boolean(Get(nameof(FunctionOptions.FunctionSideEffects)), true),
				FunctionSideEffects: Boolean(Get(nameof(FunctionOptions.FunctionSideEffects)), true)
			),
			new LimitOptions(
				UnsignedInteger(Get(nameof(LimitOptions.MaxAliases)), 3),
				DatabaseReference(Get(nameof(LimitOptions.MaxDbReference)), null),
				UnsignedInteger(Get(nameof(LimitOptions.MaxAttrsPerObj)), 2048),
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
				UnsignedInteger(Get(nameof(LimitOptions.CallLimit)), 100),
				UnsignedInteger(Get(nameof(LimitOptions.PlayerNameLen)), 21),
				UnsignedInteger(Get(nameof(LimitOptions.QueueEntryCpuTime)), 1000),
				Boolean(Get(nameof(LimitOptions.UseQuota)), true),
				UnsignedInteger(Get(nameof(LimitOptions.ChunkMigrate)), 150)
			),
			new LogOptions(
				UseSyslog: Boolean(Get(nameof(LogOptions.UseSyslog)), false),
				LogCommands: Boolean(Get(nameof(LogOptions.LogCommands)), false),
				LogForces: Boolean(Get(nameof(LogOptions.LogForces)), true),
				ErrorLog: String(Get(nameof(LogOptions.ErrorLog)), "log/netmush.log"),
				CommandLog: String(Get(nameof(LogOptions.CommandLog)), "log/command.log"),
				WizardLog: String(Get(nameof(LogOptions.WizardLog)), "log/wizard.log"),
				CheckpointLog: String(Get(nameof(LogOptions.CheckpointLog)), "log/checkpoint.log"),
				TraceLog: String(Get(nameof(LogOptions.TraceLog)), "log/trace.log"),
				ConnectLog: String(Get(nameof(LogOptions.ConnectLog)), "log/connect.log"),
				MemoryCheck: Boolean(Get(nameof(LogOptions.MemoryCheck)), false),
				UseConnLog: Boolean(Get(nameof(LogOptions.UseConnLog)), true)
			),
			new MessageOptions(
				ConnectFile: String(Get(nameof(MessageOptions.ConnectFile)), "connect.txt"),
				MessageOfTheDayFile: String(Get(nameof(MessageOptions.MessageOfTheDayFile)), "motd.txt"),
				WizMessageOfTheDayFile: String(Get(nameof(MessageOptions.WizMessageOfTheDayFile)), "wizmotd.txt"),
				NewUserFile: String(Get(nameof(MessageOptions.NewUserFile)), "newuser.txt"),
				RegisterCreateFile: String(Get(nameof(MessageOptions.RegisterCreateFile)), "register.txt"),
				QuitFile: String(Get(nameof(MessageOptions.QuitFile)), "quit.txt"),
				DownFile: String(Get(nameof(MessageOptions.DownFile)), "down.txt"),
				FullFile: String(Get(nameof(MessageOptions.FullFile)), "full.txt"),
				GuestFile: String(Get(nameof(MessageOptions.GuestFile)), "guest.txt"),
				WhoFile: String(Get(nameof(MessageOptions.WhoFile)), "who.txt"),
				ConnectHtmlFile: String(Get(nameof(MessageOptions.ConnectHtmlFile)), "connect.html"),
				MessageOfTheDayHtmlFile: String(Get(nameof(MessageOptions.MessageOfTheDayHtmlFile)), "motd.html"),
				WizMessageOfTheDayHtmlFile: String(Get(nameof(MessageOptions.WizMessageOfTheDayHtmlFile)), "wizmotd.html"),
				NewUserHtmlFile: String(Get(nameof(MessageOptions.NewUserHtmlFile)), "newuser.html"),
				RegisterCreateHtmlFile: String(Get(nameof(MessageOptions.RegisterCreateHtmlFile)), "register.html"),
				QuitHtmlFile: String(Get(nameof(MessageOptions.QuitHtmlFile)), "quit.html"),
				DownHtmlFile: String(Get(nameof(MessageOptions.DownHtmlFile)), "down.html"),
				FullHtmlFile: String(Get(nameof(MessageOptions.FullHtmlFile)), "full.html"),
				GuestHtmlFile: String(Get(nameof(MessageOptions.GuestHtmlFile)), "guest.html"),
				WhoHtmlFile: String(Get(nameof(MessageOptions.WhoHtmlFile)), "who.html"),
				IndexHtmlFile: String(Get(nameof(MessageOptions.IndexHtmlFile)), "index.html")
			),
			new NetConfig(
				MudName: String(Get(nameof(NetConfig.MudName)), "SharpMUSH"),
				MudUrl: String(Get(nameof(NetConfig.MudUrl)), null),
				IpAddr: String(Get(nameof(NetConfig.IpAddr)), null),
				SslIpAddr: String(Get(nameof(NetConfig.SslIpAddr)), null),
				Port: UnsignedInteger(Get(nameof(NetConfig.Port)), 4201),
				SslPort: UnsignedInteger(Get(nameof(NetConfig.SslPort)), 4202),
				SocketFile: String(Get(nameof(NetConfig.SocketFile)), "netmush.sock"),
				UseWs: Boolean(Get(nameof(NetConfig.UseWs)), true),
				WsUrl: String(Get(nameof(NetConfig.WsUrl)), "/wsclient"),
				UseDns: Boolean(Get(nameof(NetConfig.UseDns)), true),
				Logins: Boolean(Get(nameof(NetConfig.Logins)), true),
				PlayerCreation: Boolean(Get(nameof(NetConfig.PlayerCreation)), true),
				Guests: Boolean(Get(nameof(NetConfig.Guests)), true),
				Pueblo: Boolean(Get(nameof(NetConfig.Pueblo)), true),
				SqlPlatform: String(Get(nameof(NetConfig.SqlPlatform)), null),
				SqlHost: String(Get(nameof(NetConfig.SqlHost)), "localhost"),
				JsonUnsafeUnescape: Boolean(Get(nameof(NetConfig.JsonUnsafeUnescape)), false),
				SslRequireClientCert: Boolean(Get(nameof(NetConfig.SslRequireClientCert)), false)
			)
		);

		return work;

		string Get(string key) => configDictionary[propertyDictionary[key]];
	}

	[GeneratedRegex(@"^(?<Key>.+)\s+(?<Value>.+)\s*$")]
	private static partial Regex KeyValueSplittingRegex();
}