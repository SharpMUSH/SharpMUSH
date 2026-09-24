using SharpMUSH.Configuration.Generated;
using SharpMUSH.Configuration.Options;
using System.Text.RegularExpressions;
using FileOptions = SharpMUSH.Configuration.Options.FileOptions;

namespace SharpMUSH.Configuration;

public static partial class ReadPennMushConfig
{
	/// <summary>
	/// Reads a PennMUSH <c>mush.cnf</c> into the engine's configuration. Every option the file does not
	/// mention keeps the value <see cref="Options.SharpMUSHOptions.Default()"/> gives it, which is the one
	/// place a shipped default is written down — this used to restate all of them and had drifted from
	/// the default on fourteen options, so a world imported from a <c>mush.cnf</c> was not the world a
	/// fresh <c>@config</c> described.
	/// </summary>
	public static SharpMUSHOptions Create(string configFile)
	{
		string[] text;

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

		// Keys the configuration file names but SharpMUSH has no option for are ignored: a PennMUSH
		// mush.cnf carries plenty of them (chunk_swap_file, forking_dump, compress_program...). The
		// lookup has to be a miss rather than an indexer, because the previous prefix filter admitted
		// any line merely starting with a known key and then indexed the unknown one.
		foreach (var groups in text
							 .Select(line => splitter.Match(line.Trim()))
							 .Where(match => match.Success)
							 .Select(match => match.Groups)
							 .Where(groups => configDictionary.ContainsKey(groups["Key"].Value)))
		{
			configDictionary[groups["Key"].Value] = groups["Value"].Value;
		}

		var d = SharpMUSHOptions.Default();

		var work = new SharpMUSHOptions()
		{
			Attribute = new AttributeOptions(
				Boolean(Get(nameof(AttributeOptions.ADestroy)), d.Attribute.ADestroy),
				Boolean(Get(nameof(AttributeOptions.AMail)), d.Attribute.AMail),
				Boolean(Get(nameof(AttributeOptions.PlayerListen)), d.Attribute.PlayerListen),
				Boolean(Get(nameof(AttributeOptions.PlayerAHear)), d.Attribute.PlayerAHear),
				Boolean(Get(nameof(AttributeOptions.Startups)), d.Attribute.Startups),
				Boolean(Get(nameof(AttributeOptions.ReadRemoteDesc)), d.Attribute.ReadRemoteDesc),
				Boolean(Get(nameof(AttributeOptions.RoomConnects)), d.Attribute.RoomConnects),
				Boolean(Get(nameof(AttributeOptions.ReverseShs)), d.Attribute.ReverseShs),
				Boolean(Get(nameof(AttributeOptions.EmptyAttributes)), d.Attribute.EmptyAttributes),
				String(Get(nameof(AttributeOptions.GenderAttribute)), d.Attribute.GenderAttribute),
				String(Get(nameof(AttributeOptions.PossessivePronounAttribute)), d.Attribute.PossessivePronounAttribute),
				String(Get(nameof(AttributeOptions.AbsolutePossessivePronounAttribute)), d.Attribute.AbsolutePossessivePronounAttribute),
				String(Get(nameof(AttributeOptions.ObjectivePronounAttribute)), d.Attribute.ObjectivePronounAttribute),
				String(Get(nameof(AttributeOptions.SubjectivePronounAttribute)), d.Attribute.SubjectivePronounAttribute)
			),
			Chat = new ChatOptions(
				Get(nameof(ChatOptions.ChatTokenAlias)).FirstOrDefault(d.Chat.ChatTokenAlias),
				Boolean(Get(nameof(ChatOptions.UseMuxComm)), d.Chat.UseMuxComm),
				UnsignedInteger(Get(nameof(ChatOptions.MaxChannels)), d.Chat.MaxChannels),
				UnsignedInteger(Get(nameof(ChatOptions.MaxPlayerChannels)), d.Chat.MaxPlayerChannels),
				UnsignedInteger(Get(nameof(ChatOptions.ChannelCost)), d.Chat.ChannelCost),
				Boolean(Get(nameof(ChatOptions.NoisyCEmit)), d.Chat.NoisyCEmit),
				UnsignedInteger(Get(nameof(ChatOptions.ChannelTitleLength)), d.Chat.ChannelTitleLength)
			),
			Command = new CommandOptions(
				Boolean(Get(nameof(CommandOptions.NoisyWhisper)), d.Command.NoisyWhisper),
				Boolean(Get(nameof(CommandOptions.PossessiveGet)), d.Command.PossessiveGet),
				Boolean(Get(nameof(CommandOptions.PossessiveGetD)), d.Command.PossessiveGetD),
				Boolean(Get(nameof(CommandOptions.LinkToObject)), d.Command.LinkToObject),
				Boolean(Get(nameof(CommandOptions.OwnerQueues)), d.Command.OwnerQueues),
				Boolean(Get(nameof(CommandOptions.FullInvisibility)), d.Command.FullInvisibility),
				Boolean(Get(nameof(CommandOptions.WizardNoAEnter)), d.Command.WizardNoAEnter),
				Boolean(Get(nameof(CommandOptions.ReallySafe)), d.Command.ReallySafe),
				Boolean(Get(nameof(CommandOptions.DestroyPossessions)), d.Command.DestroyPossessions),
				RequiredDatabaseReference(Get(nameof(CommandOptions.ProbateJudge)), d.Command.ProbateJudge)
			),
			Compatibility = new CompatibilityOptions(
				Boolean(Get(nameof(CompatibilityOptions.NullEqualsZero)), d.Compatibility.NullEqualsZero),
				Boolean(Get(nameof(CompatibilityOptions.TinyBooleans)), d.Compatibility.TinyBooleans),
				Boolean(Get(nameof(CompatibilityOptions.TinyTrimFun)), d.Compatibility.TinyTrimFun),
				Boolean(Get(nameof(CompatibilityOptions.TinyMath)), d.Compatibility.TinyMath),
				Boolean(Get(nameof(CompatibilityOptions.SilentPEmit)), d.Compatibility.SilentPEmit),
				Boolean(Get(nameof(CompatibilityOptions.ParenGroups)), d.Compatibility.ParenGroups)
			),
			Cosmetic = new CosmeticOptions(
				RequiredString(Get(nameof(CosmeticOptions.MoneySingular)), d.Cosmetic.MoneySingular).Trim(),
				RequiredString(Get(nameof(CosmeticOptions.MoneyPlural)), d.Cosmetic.MoneyPlural).Trim(),
				Boolean(Get(nameof(CosmeticOptions.PlayerNameSpaces)), d.Cosmetic.PlayerNameSpaces),
				Boolean(Get(nameof(CosmeticOptions.AnsiNames)), d.Cosmetic.AnsiNames),
				Boolean(Get(nameof(CosmeticOptions.OnlyAsciiInNames)), d.Cosmetic.OnlyAsciiInNames),
				Boolean(Get(nameof(CosmeticOptions.Monikers)), d.Cosmetic.Monikers),
				UnsignedInteger(Get(nameof(CosmeticOptions.FloatPrecision)), d.Cosmetic.FloatPrecision),
				Boolean(Get(nameof(CosmeticOptions.CommaExitList)), d.Cosmetic.CommaExitList),
				Boolean(Get(nameof(CosmeticOptions.CountAll)), d.Cosmetic.CountAll),
				Boolean(Get(nameof(CosmeticOptions.PageAliases)), d.Cosmetic.PageAliases),
				Boolean(Get(nameof(CosmeticOptions.FlagsOnExamine)), d.Cosmetic.FlagsOnExamine),
				Boolean(Get(nameof(CosmeticOptions.ExaminePublicAttributes)), d.Cosmetic.ExaminePublicAttributes),
				RequiredString(Get(nameof(CosmeticOptions.WizardWallPrefix)), d.Cosmetic.WizardWallPrefix).Trim(),
				RequiredString(Get(nameof(CosmeticOptions.RoyaltyWallPrefix)), d.Cosmetic.RoyaltyWallPrefix).Trim(),
				RequiredString(Get(nameof(CosmeticOptions.WallPrefix)), d.Cosmetic.WallPrefix).Trim(),
				Boolean(Get(nameof(CosmeticOptions.AnnounceConnects)), d.Cosmetic.AnnounceConnects),
				Boolean(Get(nameof(CosmeticOptions.ChatStripQuote)), d.Cosmetic.ChatStripQuote)
			),
			Cost = new CostOptions(
				UnsignedInteger(Get(nameof(CostOptions.ObjectCost)), d.Cost.ObjectCost),
				UnsignedInteger(Get(nameof(CostOptions.ExitCost)), d.Cost.ExitCost),
				UnsignedInteger(Get(nameof(CostOptions.LinkCost)), d.Cost.LinkCost),
				UnsignedInteger(Get(nameof(CostOptions.RoomCost)), d.Cost.RoomCost),
				UnsignedInteger(Get(nameof(CostOptions.QueueCost)), d.Cost.QueueCost),
				UnsignedInteger(Get(nameof(CostOptions.QuotaCost)), d.Cost.QuotaCost),
				UnsignedInteger(Get(nameof(CostOptions.FindCost)), d.Cost.FindCost)
			),
			Database = new DatabaseOptions(
				RequiredDatabaseReference(Get(nameof(DatabaseOptions.PlayerStart)), d.Database.PlayerStart),
				RequiredDatabaseReference(Get(nameof(DatabaseOptions.MasterRoom)), d.Database.MasterRoom),
				RequiredDatabaseReference(Get(nameof(DatabaseOptions.BaseRoom)), d.Database.BaseRoom),
				RequiredDatabaseReference(Get(nameof(DatabaseOptions.DefaultHome)), d.Database.DefaultHome),
				Boolean(Get(nameof(DatabaseOptions.ExitsConnectRooms)), d.Database.ExitsConnectRooms),
				Boolean(Get(nameof(DatabaseOptions.ZoneControlZmpOnly)), d.Database.ZoneControlZmpOnly),
				// A value of -1 in the config disables that ancestor.
				DatabaseReference(Get(nameof(DatabaseOptions.AncestorRoom)), d.Database.AncestorRoom),
				DatabaseReference(Get(nameof(DatabaseOptions.AncestorExit)), d.Database.AncestorExit),
				DatabaseReference(Get(nameof(DatabaseOptions.AncestorThing)), d.Database.AncestorThing),
				DatabaseReference(Get(nameof(DatabaseOptions.AncestorPlayer)), d.Database.AncestorPlayer),
				DatabaseReference(Get(nameof(DatabaseOptions.EventHandler)), d.Database.EventHandler),
				DatabaseReference(Get(nameof(DatabaseOptions.HttpHandler)), d.Database.HttpHandler),
				DatabaseReference(Get(nameof(DatabaseOptions.PackageManager)), d.Database.PackageManager),
				UnsignedInteger(Get(nameof(DatabaseOptions.HttpRequestsPerSecond)), d.Database.HttpRequestsPerSecond),
				Boolean(Get(nameof(DatabaseOptions.AllowBrowserCode)), d.Database.AllowBrowserCode)
			),
			Dump = new DumpOptions(
				RequiredString(Get(nameof(DumpOptions.PurgeInterval)), d.Dump.PurgeInterval)
			),
			File = new FileOptions(
				RequiredString(Get(nameof(FileOptions.AccessFile)), d.File.AccessFile),
				RequiredString(Get(nameof(FileOptions.NamesFile)), d.File.NamesFile),
				String(Get(nameof(FileOptions.SSLPrivateKeyFile)), d.File.SSLPrivateKeyFile),
				String(Get(nameof(FileOptions.SSLCertificateFile)), d.File.SSLCertificateFile),
				String(Get(nameof(FileOptions.SSLCAFile)), d.File.SSLCAFile),
				String(Get(nameof(FileOptions.SSLCADirectory)), d.File.SSLCADirectory),
				String(Get(nameof(FileOptions.DictionaryFile)), d.File.DictionaryFile),
				String(Get(nameof(FileOptions.ColorsFile)), d.File.ColorsFile)
			),
			Flag = new FlagOptions(
				PlayerFlags: FlagOptions.Defaults.Split(RequiredString(Get(nameof(FlagOptions.PlayerFlags)), FlagOptions.Defaults.Player)),
				RoomFlags: FlagOptions.Defaults.Split(RequiredString(Get(nameof(FlagOptions.RoomFlags)), FlagOptions.Defaults.Room)),
				ThingFlags: FlagOptions.Defaults.Split(RequiredString(Get(nameof(FlagOptions.ThingFlags)), FlagOptions.Defaults.Thing)),
				ExitFlags: FlagOptions.Defaults.Split(RequiredString(Get(nameof(FlagOptions.ExitFlags)), FlagOptions.Defaults.Exit)),
				ChannelFlags: FlagOptions.Defaults.Split(RequiredString(Get(nameof(FlagOptions.ChannelFlags)), FlagOptions.Defaults.Channel))
			),
			Function = new FunctionOptions(
				SaferUserFunctions: Boolean(Get(nameof(FunctionOptions.SaferUserFunctions)), d.Function.SaferUserFunctions),
				FunctionSideEffects: Boolean(Get(nameof(FunctionOptions.FunctionSideEffects)), d.Function.FunctionSideEffects)
			),
			Limit = new LimitOptions(
				UnsignedInteger(Get(nameof(LimitOptions.MaxAliases)), d.Limit.MaxAliases),
				DatabaseReference(Get(nameof(LimitOptions.MaxDbReference)), d.Limit.MaxDbReference),
				UnsignedInteger(Get(nameof(LimitOptions.MaxAttributesPerObj)), d.Limit.MaxAttributesPerObj),
				UnsignedInteger(Get(nameof(LimitOptions.MaxLogins)), d.Limit.MaxLogins),
				Integer(Get(nameof(LimitOptions.MaxGuests)), d.Limit.MaxGuests),
				UnsignedInteger(Get(nameof(LimitOptions.MaxNamedQRegisters)), d.Limit.MaxNamedQRegisters),
				UnsignedInteger(Get(nameof(LimitOptions.ConnectFailLimit)), d.Limit.ConnectFailLimit),
				UnsignedInteger(Get(nameof(LimitOptions.IdleTimeout)), d.Limit.IdleTimeout),
				UnsignedInteger(Get(nameof(LimitOptions.UnconnectedIdleTimeout)), d.Limit.UnconnectedIdleTimeout),
				UnsignedInteger(Get(nameof(LimitOptions.KeepaliveTimeout)), d.Limit.KeepaliveTimeout),
				UnsignedInteger(Get(nameof(LimitOptions.WhisperLoudness)), d.Limit.WhisperLoudness),
				UnsignedInteger(Get(nameof(LimitOptions.StartingQuota)), d.Limit.StartingQuota),
				UnsignedInteger(Get(nameof(LimitOptions.StartingMoney)), d.Limit.StartingMoney),
				UnsignedInteger(Get(nameof(LimitOptions.Paycheck)), d.Limit.Paycheck),
				UnsignedInteger(Get(nameof(LimitOptions.GuestPaycheck)), d.Limit.GuestPaycheck),
				UnsignedInteger(Get(nameof(LimitOptions.MaxPennies)), d.Limit.MaxPennies),
				UnsignedInteger(Get(nameof(LimitOptions.MaxGuestPennies)), d.Limit.MaxGuestPennies),
				UnsignedInteger(Get(nameof(LimitOptions.MaxParents)), d.Limit.MaxParents),
				UnsignedInteger(Get(nameof(LimitOptions.MailLimit)), d.Limit.MailLimit),
				UnsignedInteger(Get(nameof(LimitOptions.MaxDepth)), d.Limit.MaxDepth),
				UnsignedInteger(Get(nameof(LimitOptions.PlayerQueueLimit)), d.Limit.PlayerQueueLimit),
				UnsignedInteger(Get(nameof(LimitOptions.QueueLoss)), d.Limit.QueueLoss),
				UnsignedInteger(Get(nameof(LimitOptions.QueueChunk)), d.Limit.QueueChunk),
				UnsignedInteger(Get(nameof(LimitOptions.FunctionRecursionLimit)), d.Limit.FunctionRecursionLimit),
				UnsignedInteger(Get(nameof(LimitOptions.FunctionInvocationLimit)), d.Limit.FunctionInvocationLimit),
				UnsignedInteger(Get(nameof(LimitOptions.CallLimit)), d.Limit.CallLimit),
				UnsignedInteger(Get(nameof(LimitOptions.PlayerNameLen)), d.Limit.PlayerNameLen),
				UnsignedInteger(Get(nameof(LimitOptions.QueueEntryCpuTime)), d.Limit.QueueEntryCpuTime),
				Boolean(Get(nameof(LimitOptions.UseQuota)), d.Limit.UseQuota),
				UnsignedInteger(Get(nameof(LimitOptions.ChunkMigrate)), d.Limit.ChunkMigrate),
				UnsignedInteger(Get(nameof(LimitOptions.MaxAttributeValueLength)), d.Limit.MaxAttributeValueLength))
			{
				GlobalQueueLimit = UnsignedInteger(Get(nameof(LimitOptions.GlobalQueueLimit)), d.Limit.GlobalQueueLimit),
				GuestOutputLimit = UnsignedInteger(Get(nameof(LimitOptions.GuestOutputLimit)), d.Limit.GuestOutputLimit)
			},
			Log = new LogOptions(
				Boolean(Get(nameof(LogOptions.UseSyslog)), d.Log.UseSyslog),
				Boolean(Get(nameof(LogOptions.LogCommands)), d.Log.LogCommands),
				Boolean(Get(nameof(LogOptions.LogForces)), d.Log.LogForces),
				RequiredString(Get(nameof(LogOptions.ErrorLog)), d.Log.ErrorLog),
				RequiredString(Get(nameof(LogOptions.CommandLog)), d.Log.CommandLog),
				RequiredString(Get(nameof(LogOptions.WizardLog)), d.Log.WizardLog),
				RequiredString(Get(nameof(LogOptions.CheckpointLog)), d.Log.CheckpointLog),
				RequiredString(Get(nameof(LogOptions.TraceLog)), d.Log.TraceLog),
				RequiredString(Get(nameof(LogOptions.ConnectLog)), d.Log.ConnectLog),
				Boolean(Get(nameof(LogOptions.MemoryCheck)), d.Log.MemoryCheck),
				Boolean(Get(nameof(LogOptions.UseConnLog)), d.Log.UseConnLog)
			),
			Message = new MessageOptions(
				RequiredString(Get(nameof(MessageOptions.ConnectFile)), d.Message.ConnectFile),
				RequiredString(Get(nameof(MessageOptions.MessageOfTheDayFile)), d.Message.MessageOfTheDayFile),
				RequiredString(Get(nameof(MessageOptions.WizMessageOfTheDayFile)), d.Message.WizMessageOfTheDayFile),
				RequiredString(Get(nameof(MessageOptions.NewUserFile)), d.Message.NewUserFile),
				RequiredString(Get(nameof(MessageOptions.RegisterCreateFile)), d.Message.RegisterCreateFile),
				RequiredString(Get(nameof(MessageOptions.QuitFile)), d.Message.QuitFile),
				RequiredString(Get(nameof(MessageOptions.DownFile)), d.Message.DownFile),
				RequiredString(Get(nameof(MessageOptions.FullFile)), d.Message.FullFile),
				RequiredString(Get(nameof(MessageOptions.GuestFile)), d.Message.GuestFile),
				RequiredString(Get(nameof(MessageOptions.WhoFile)), d.Message.WhoFile),
				RequiredString(Get(nameof(MessageOptions.ConnectHtmlFile)), d.Message.ConnectHtmlFile),
				RequiredString(Get(nameof(MessageOptions.MessageOfTheDayHtmlFile)), d.Message.MessageOfTheDayHtmlFile),
				RequiredString(Get(nameof(MessageOptions.WizMessageOfTheDayHtmlFile)), d.Message.WizMessageOfTheDayHtmlFile),
				RequiredString(Get(nameof(MessageOptions.NewUserHtmlFile)), d.Message.NewUserHtmlFile),
				RequiredString(Get(nameof(MessageOptions.RegisterCreateHtmlFile)), d.Message.RegisterCreateHtmlFile),
				RequiredString(Get(nameof(MessageOptions.QuitHtmlFile)), d.Message.QuitHtmlFile),
				RequiredString(Get(nameof(MessageOptions.DownHtmlFile)), d.Message.DownHtmlFile),
				RequiredString(Get(nameof(MessageOptions.FullHtmlFile)), d.Message.FullHtmlFile),
				RequiredString(Get(nameof(MessageOptions.GuestHtmlFile)), d.Message.GuestHtmlFile),
				RequiredString(Get(nameof(MessageOptions.WhoHtmlFile)), d.Message.WhoHtmlFile),
				RequiredString(Get(nameof(MessageOptions.IndexHtmlFile)), d.Message.IndexHtmlFile)
			),
			Net = new NetOptions(
				RequiredString(Get(nameof(NetOptions.MudName)), d.Net.MudName),
				String(Get(nameof(NetOptions.MudUrl)), d.Net.MudUrl),
				String(Get(nameof(NetOptions.IpAddr)), d.Net.IpAddr),
				String(Get(nameof(NetOptions.SslIpAddr)), d.Net.SslIpAddr),
				UnsignedInteger(Get(nameof(NetOptions.Port)), d.Net.Port),
				UnsignedInteger(Get(nameof(NetOptions.SslPort)), d.Net.SslPort),
				UnsignedInteger(Get(nameof(NetOptions.PortalPort)), d.Net.PortalPort),
				UnsignedInteger(Get(nameof(NetOptions.SslPortalPort)), d.Net.SslPortalPort),
				RequiredString(Get(nameof(NetOptions.SocketFile)), d.Net.SocketFile),
				Boolean(Get(nameof(NetOptions.UseWebsockets)), d.Net.UseWebsockets),
				String(Get(nameof(NetOptions.WebsocketUrl)), d.Net.WebsocketUrl),
				Boolean(Get(nameof(NetOptions.UseDns)), d.Net.UseDns),
				Boolean(Get(nameof(NetOptions.Logins)), d.Net.Logins),
				Boolean(Get(nameof(NetOptions.PlayerCreation)), d.Net.PlayerCreation),
				Boolean(Get(nameof(NetOptions.Guests)), d.Net.Guests),
				Boolean(Get(nameof(NetOptions.Pueblo)), d.Net.Pueblo),
				Boolean(Get(nameof(NetOptions.Mxp)), d.Net.Mxp),
				String(Get(nameof(NetOptions.SqlPlatform)), d.Net.SqlPlatform),
				String(Get(nameof(NetOptions.SqlHost)), d.Net.SqlHost),
				String(Get(nameof(NetOptions.SqlDatabase)), d.Net.SqlDatabase),
				String(Get(nameof(NetOptions.SqlUsername)), d.Net.SqlUsername),
				String(Get(nameof(NetOptions.SqlPassword)), d.Net.SqlPassword),
				Boolean(Get(nameof(NetOptions.JsonUnsafeUnescape)), d.Net.JsonUnsafeUnescape),
				Boolean(Get(nameof(NetOptions.SslRequireClientCert)), d.Net.SslRequireClientCert)
			),
			Debug = new DebugOptions(
				Boolean(Get(nameof(DebugOptions.DebugSharpParser)), d.Debug.DebugSharpParser),
				Enumeration(Get(nameof(DebugOptions.ParserPredictionMode)), d.Debug.ParserPredictionMode)
			),
			Alias = d.Alias,
			Restriction = d.Restriction,
			// The two the default seeds with examples start empty for an import: the game being
			// imported brings its own names.cnf and access.cnf, and seeding a PennMUSH game with
			// SharpMUSH's example rules would lock a site nobody asked to lock.
			BannedNames = new BannedNamesOptions(
				BannedNames: []
			),
			SitelockRules = new SitelockRulesOptions(
				Rules: new Dictionary<string, string[]>()
			),
			Warning = new WarningOptions(
				WarnInterval: RequiredString(Get(nameof(WarningOptions.WarnInterval)), d.Warning.WarnInterval)
			),
			TextFile = new TextFileOptions(
				TextFilesDirectory: RequiredString(Get(nameof(TextFileOptions.TextFilesDirectory)), d.TextFile.TextFilesDirectory),
				EnableMarkdownRendering: Boolean(Get(nameof(TextFileOptions.EnableMarkdownRendering)), d.TextFile.EnableMarkdownRendering),
				CacheOnStartup: Boolean(Get(nameof(TextFileOptions.CacheOnStartup)), d.TextFile.CacheOnStartup)
			),
			Wiki = new WikiOptions(
				DefaultLocale: RequiredString(Get(nameof(WikiOptions.DefaultLocale)), d.Wiki.DefaultLocale)
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


	/// <summary>A named enumeration member, case-insensitively; anything else keeps the default.</summary>
	private static TEnum Enumeration<TEnum>(string value, TEnum fallback) where TEnum : struct, Enum =>
		Enum.TryParse<TEnum>(value, ignoreCase: true, out var result) && Enum.IsDefined(result)
			? result
			: fallback;

	private static int Integer(string value, int fallback) =>
		string.IsNullOrWhiteSpace(value)
			? fallback
			: int.TryParse(value, out var result)
				? result
				: fallback;

	private static uint? DatabaseReference(string value, uint? fallback)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return fallback;
		}

		// PennMUSH dbref-disable semantics: a negative value (canonically -1) means "no object"
		// (e.g. ANCESTOR_* disabled). Negatives never parse as uint, so handle them explicitly
		// rather than silently falling back to the default.
		if (int.TryParse(value, out var signed) && signed < 0)
		{
			return null;
		}

		return uint.TryParse(value, out var result)
			? result
			: fallback;
	}

	private static uint RequiredDatabaseReference(string value, uint fallback) =>
		UnsignedInteger(value, fallback);

	[GeneratedRegex(@"^(?<Key>[^\s]+)\s+(?<Value>.+)\s*$")]
	private static partial Regex KeyValueSplittingRegex();
}