using SharpMUSH.Configuration.Generated;
using SharpMUSH.Configuration.Mssp;
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
	public static SharpMUSHOptions Create(string configFile) => Import(configFile).Options;

	/// <summary>
	/// Reads a PennMUSH <c>mush.cnf</c> as <see cref="Create"/> does, and says which of its lines could
	/// not be carried over. PennMUSH's <c>config_file_startup</c> (<c>src/conf.c</c>) follows
	/// <c>include</c> lines, and the shipped <c>mush.cnf</c> keeps every <c>restrict_command</c> in the
	/// <c>restrict.cnf</c> it includes; reading neither lost all of a game's command restrictions.
	/// </summary>
	/// <param name="configFile">The <c>mush.cnf</c> to read.</param>
	/// <param name="followIncludes">
	/// False for a file that did not come from the server's own disk, such as one uploaded through the
	/// portal: its <c>include</c> lines are reported rather than followed, so it cannot name a file on
	/// the server and have that file's settings read back into the configuration.
	/// </param>
	public static PennMushConfigImport Import(string configFile, bool followIncludes = true)
	{
		List<string> skipped = [];
		var text = ReadWithIncludes(configFile, skipped, followIncludes);

		var propertyDictionary = ConfigMetadata.PropertyToAttributeName;
		var configDictionary = ConfigMetadata.AttributeToPropertyName.Keys
			.ToDictionary(key => key, _ => string.Empty);

		var splitter = KeyValueSplittingRegex();

		// PennMUSH's cf_flag (src/conf.c) appends each line of a flag option to what earlier lines set,
		// so the shipped mushcnf.dst can give player_flags as three lines (enter_ok, ansi, no_command).
		// Every other option takes its last line.
		HashSet<string> accumulating =
		[
			propertyDictionary[nameof(FlagOptions.PlayerFlags)],
			propertyDictionary[nameof(FlagOptions.RoomFlags)],
			propertyDictionary[nameof(FlagOptions.ExitFlags)],
			propertyDictionary[nameof(FlagOptions.ThingFlags)],
			propertyDictionary[nameof(FlagOptions.ChannelFlags)]
		];

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
			var key = groups["Key"].Value;
			var value = groups["Value"].Value;
			configDictionary[key] = accumulating.Contains(key) && configDictionary[key].Length > 0
				? $"{configDictionary[key]} {value}"
				: value;
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
				UnsignedInteger(Get(nameof(ChatOptions.ChannelTitleLength)), d.Chat.ChannelTitleLength),
				Boolean(Get(nameof(ChatOptions.PageLog)), d.Chat.PageLog),
				Integer(Get(nameof(ChatOptions.PageLogRetentionDays)), d.Chat.PageLogRetentionDays)
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
				Boolean(Get(nameof(CosmeticOptions.ChatStripQuote)), d.Cosmetic.ChatStripQuote),
				RequiredString(Get(nameof(CosmeticOptions.LayoutBorder)), d.Cosmetic.LayoutBorder).Trim().ToLowerInvariant(),
				String(Get(nameof(CosmeticOptions.LayoutTheme)), d.Cosmetic.LayoutTheme)?.Trim() ?? string.Empty,
				RequiredString(Get(nameof(CosmeticOptions.ImageHosts)), d.Cosmetic.ImageHosts).Trim().ToLowerInvariant(),
				String(Get(nameof(CosmeticOptions.ImageHostList)), d.Cosmetic.ImageHostList)?.Trim() ?? string.Empty,
				String(Get(nameof(CosmeticOptions.PortalLogo)), d.Cosmetic.PortalLogo)?.Trim() ?? string.Empty,
				String(Get(nameof(CosmeticOptions.PortalFavicon)), d.Cosmetic.PortalFavicon)?.Trim() ?? string.Empty
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
				Boolean(Get(nameof(DatabaseOptions.AllowBrowserCode)), d.Database.AllowBrowserCode),
				DatabaseReference(Get(nameof(DatabaseOptions.MessagesObject)), d.Database.MessagesObject)
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
				GuestOutputLimit = UnsignedInteger(Get(nameof(LimitOptions.GuestOutputLimit)), d.Limit.GuestOutputLimit),
				CommandBurstSize = UnsignedInteger(Get(nameof(LimitOptions.CommandBurstSize)), d.Limit.CommandBurstSize)
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
			Restriction = new RestrictionOptions(
				CommandRestrictions: Restrictions("restrict_command", text, skipped),
				FunctionRestrictions: Restrictions("restrict_function", text, skipped)
			),
			// The two the default seeds with examples start empty for an import: the game being
			// imported brings its own names.cnf and access.cnf, and seeding a PennMUSH game with
			// SharpMUSH's example rules would lock a site nobody asked to lock.
			BannedNames = new BannedNamesOptions(
				BannedNames: []
			),
			SitelockRules = new SitelockRulesOptions(
				Rules: new Dictionary<string, string[]>()
			),
			// PennMUSH has no such table.
			AsciiTranslations = d.AsciiTranslations,
			Mssp = new MsspOptions(
				Variables: Mssp(text, skipped, d.Mssp.Variables)
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

		// A value that parses but sits outside its option's declared range is held to the bound, as
		// PennMUSH's cf_int clamps and logs it, and reported with the lines that were not carried over
		// as written (#1335).
		work = ConfigBounds.ClampAll(work, correction => skipped.Add(correction.ToString()));

		// Only who may call a function is applied (ConfiguredFunctionRestrictions.PermissionWords, which
		// this project cannot reference); a word that changes how it runs is kept but does nothing.
		foreach (var (name, restriction) in work.Restriction.FunctionRestrictions)
		{
			foreach (var word in restriction[0].Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)
						 .Where(word => !FunctionPermissionWords.Contains(word, StringComparer.OrdinalIgnoreCase)))
			{
				skipped.Add($"restrict_function {name} {word}: kept in function_restrictions, but SharpMUSH applies only {string.Join(", ", FunctionPermissionWords)}, so it has no effect.");
			}
		}

		foreach (var line in text.Where(line => DirectiveName(line) is { } name
							 && name.StartsWith("restrict_", StringComparison.OrdinalIgnoreCase)
							 && !name.Equals("restrict_command", StringComparison.OrdinalIgnoreCase)
							 && !name.Equals("restrict_function", StringComparison.OrdinalIgnoreCase)))
		{
			skipped.Add($"{line.Trim()}: SharpMUSH has no equivalent, so it was not carried over.");
		}

		var named = configDictionary.Where(entry => entry.Value.Length > 0).Select(entry => entry.Key)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		return new PennMushConfigImport(work, skipped, named);

		string Get(string key) => configDictionary[propertyDictionary[key]];
	}

	/// <summary>
	/// The file's lines with each <c>include</c> line replaced by the lines of the file it names, as
	/// <c>config_file_startup</c> reads them: an included file may include others, ten deep at most. A
	/// relative name is found beside the file that names it, which is PennMUSH's game directory for
	/// the shipped <c>mush.cnf</c>. An include that cannot be read is reported and passed over, as
	/// PennMUSH logs it and reads on; only the top-level file has to exist.
	/// </summary>
	private static List<string> ReadWithIncludes(string configFile, List<string> skipped, bool followIncludes, int depth = 0)
	{
		List<string> lines = [];
		foreach (var line in File.ReadAllLines(configFile))
		{
			if (DirectiveName(line) is not { } name || !name.Equals("include", StringComparison.OrdinalIgnoreCase))
			{
				lines.Add(line);
				continue;
			}

			var included = DirectiveValue(line);
			if (included.Length == 0)
			{
				skipped.Add($"include in {configFile}: names no file.");
				continue;
			}

			if (!followIncludes)
			{
				skipped.Add($"include {included}: not followed, because an uploaded configuration cannot read files on the server; nothing it sets was carried over.");
				continue;
			}

			if (depth >= 10)
			{
				skipped.Add($"include {included} in {configFile}: include depth too deep, not read.");
				continue;
			}

			var path = Path.IsPathRooted(included)
				? included
				: Path.Join(Path.GetDirectoryName(Path.GetFullPath(configFile)), included);
			try
			{
				lines.AddRange(ReadWithIncludes(path, skipped, followIncludes, depth + 1));
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				skipped.Add($"include {included} in {configFile}: cannot read {path} ({ex.Message}); nothing it sets was carried over.");
			}
		}

		return lines;
	}

	/// <summary>The <c>restrict_function</c> words SharpMUSH applies: the ones <c>check_func</c> tests (<c>src/function.c</c>).</summary>
	private static readonly string[] FunctionPermissionWords = ["nobody", "noguest", "nogagged", "nofixed", "admin", "wizard", "god"];

	/// <summary>
	/// Every <paramref name="directive"/> line — <c>restrict_command</c> or <c>restrict_function</c> —
	/// as the name it restricts and the restriction, which is the shape <c>@config/set restrict_command</c>
	/// stores. PennMUSH applies the lines in order. A <c>restrict_command</c> line replaces the
	/// restriction's lock and message, so a later line for the same name takes the place of an earlier
	/// one here, and says so. A <c>restrict_function</c> line sets or clears the function's bits one word
	/// at a time (<c>apply_restrictions</c>, <c>src/function.c</c>), so its words are merged into the
	/// earlier lines': each word takes the place of an earlier mention of the same word, with or without
	/// its <c>!</c>.
	/// </summary>
	private static Dictionary<string, string[]> Restrictions(string directive, IEnumerable<string> lines, List<string> skipped)
	{
		var restrictions = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
		foreach (var line in lines.Where(line => directive.Equals(DirectiveName(line), StringComparison.OrdinalIgnoreCase)))
		{
			var value = DirectiveValue(line);
			var space = value.IndexOfAny([' ', '\t']);
			var name = space < 0 ? value : value[..space];
			var restriction = space < 0 ? "" : value[(space + 1)..].Trim();
			if (restriction.Length == 0)
			{
				// PennMUSH's own words for the line (config_set, src/conf.c).
				skipped.Add($"{line.Trim()}: {directive} {name} requires a restriction value.");
				continue;
			}

			if (directive == "restrict_function" && restrictions.TryGetValue(name, out var bits))
			{
				restriction = MergeFunctionRestriction(bits[0], restriction);
			}
			else if (restrictions.TryGetValue(name, out var earlier) && earlier[0] != restriction)
			{
				var earlierName = restrictions.Keys.First(key => key.Equals(name, StringComparison.OrdinalIgnoreCase));
				skipped.Add($"{directive} {earlierName} {earlier[0]}: replaced by the later line {directive} {name} {restriction}.");
			}

			// Removed first so the key is the later line's spelling of the name.
			restrictions.Remove(name);
			restrictions[name] = [restriction];
		}

		return restrictions;
	}

	/// <summary>
	/// Every <c>mssp name/value</c> line, as the variables <see cref="MsspOptions"/> holds. The name ends
	/// at the first <c>/</c>, so a value may hold more (a URL does). A name given on several lines
	/// carries each line's value, in order, which is MSSP's own array form with the last value the
	/// default. A variable the server reports itself is not carried over: PennMUSH would report it twice.
	/// One the server already reports as the line says, like the shipped <c>mssp ansi/1</c>, goes
	/// without a note, since nothing is lost. A default variable the file does not name is kept, as every
	/// other option the file leaves out is.
	/// </summary>
	private static Dictionary<string, string[]> Mssp(IEnumerable<string> lines, List<string> skipped,
		IReadOnlyDictionary<string, string[]> defaults)
	{
		var variables = new Dictionary<string, List<string>>(StringComparer.Ordinal);
		foreach (var line in lines.Where(line => "mssp".Equals(DirectiveName(line), StringComparison.OrdinalIgnoreCase)))
		{
			var value = DirectiveValue(line);
			var slash = value.IndexOf('/');
			if (slash <= 0)
			{
				skipped.Add($"{line.Trim()}: an mssp line is name/value, so it was not carried over.");
				continue;
			}

			var name = MsspCatalog.Canonicalize(value[..slash]);
			var setting = value[(slash + 1)..].Trim();
			if (MsspCatalog.Find(name) is { ReportedByServer: true } reported)
			{
				if (!(reported is { Source: MsspSource.Server, Kind: MsspValueKind.Flag } && setting == "1"))
				{
					skipped.Add(reported.Option is { } option
						? $"{line.Trim()}: SharpMUSH reports {name} from {option}, so it was not carried over."
						: $"{line.Trim()}: SharpMUSH reports {name} itself, so it was not carried over.");
				}

				continue;
			}

			var values = variables.TryGetValue(name, out var earlier) ? earlier : variables[name] = [];
			values.Add(setting);
			if (MsspCatalog.Validate(name, values) is { } problem)
			{
				values.RemoveAt(values.Count - 1);
				if (values.Count == 0)
				{
					variables.Remove(name);
				}

				skipped.Add($"{line.Trim()}: {problem}");
			}
		}

		var result = defaults
			.Where(entry => !variables.ContainsKey(entry.Key))
			.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
		foreach (var (name, values) in variables)
		{
			result[name] = [.. values];
		}

		return result;
	}

	/// <summary>
	/// <paramref name="later"/>'s words applied over <paramref name="earlier"/>'s, as
	/// <c>apply_restrictions</c> ORs a word's bit in and clears it for <c>!word</c>.
	/// </summary>
	private static string MergeFunctionRestriction(string earlier, string later)
	{
		var words = earlier.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).ToList();
		foreach (var word in later.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
		{
			words.RemoveAll(kept => kept.TrimStart('!').Equals(word.TrimStart('!'), StringComparison.OrdinalIgnoreCase));
			words.Add(word);
		}

		return string.Join(' ', words);
	}

	/// <summary>The first word of a configuration line, or null for a blank line or a comment.</summary>
	private static string? DirectiveName(string line)
	{
		var trimmed = line.Trim();
		if (trimmed.Length == 0 || trimmed[0] == '#')
		{
			return null;
		}

		var space = trimmed.IndexOfAny([' ', '\t']);
		return space < 0 ? trimmed : trimmed[..space];
	}

	/// <summary>
	/// What follows a directive line's first word, as <c>config_file_startup</c> cuts it: a <c>#</c> not
	/// followed by a digit starts a comment (a <c>#</c> followed by one is a dbref), and trailing space
	/// goes.
	/// </summary>
	private static string DirectiveValue(string line)
	{
		var trimmed = line.Trim();
		var space = trimmed.IndexOfAny([' ', '\t']);
		if (space < 0)
		{
			return "";
		}

		var value = trimmed[(space + 1)..].TrimStart();
		for (var i = 0; i < value.Length; i++)
		{
			if (value[i] == '#' && (i + 1 >= value.Length || !char.IsDigit(value[i + 1])))
			{
				value = value[..i];
				break;
			}
		}

		return value.TrimEnd();
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