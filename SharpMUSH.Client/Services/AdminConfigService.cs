using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using FileOptions = SharpMUSH.Configuration.Options.FileOptions;
using System.Reflection;
using System.Net.Http.Json;

namespace SharpMUSH.Client.Services;

public class ConfigurationResponse
{
	public SharpMUSHOptions Configuration { get; set; } = null!;
	public Dictionary<string, SharpConfigAttribute> Metadata { get; set; } = [];
}

public class AdminConfigService
{
	private readonly ILogger<AdminConfigService> _logger;
	private readonly HttpClient _httpClient;
	private SharpMUSHOptions? _currentOptions;

	public AdminConfigService(ILogger<AdminConfigService> logger, HttpClient httpClient)
	{
		_logger = logger;
		_httpClient = httpClient;
	}

	public async Task<SharpMUSHOptions> GetOptionsAsync()
	{
		try
		{
			if (_currentOptions == null)
			{
				var configResponse = await FetchConfigurationFromServer();
				_currentOptions = configResponse.Configuration;
			}
			return _currentOptions;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error fetching options from server, using defaults");
			return _currentOptions ?? CreateMinimalOptions();
		}
	}

	public SharpMUSHOptions GetOptions()
	{
		// Synchronous wrapper for backwards compatibility
		return _currentOptions ?? CreateMinimalOptions();
	}

	public async Task<SharpMUSHOptions> ImportFromConfigFileAsync(string configFileContent)
	{
		try
		{
			var response = await _httpClient.PostAsJsonAsync("/api/configuration/import", configFileContent);
			response.EnsureSuccessStatusCode();
			
			var configResponse = await response.Content.ReadFromJsonAsync<ConfigurationResponse>();
			if (configResponse?.Configuration != null)
			{
				_currentOptions = configResponse.Configuration;
			}
			return configResponse?.Configuration ?? CreateMinimalOptions();
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error importing configuration file");
			throw;
		}
	}

	public void ResetToDefault()
	{
		_currentOptions = null;
	}

	private async Task<ConfigurationResponse> FetchConfigurationFromServer()
	{
		try
		{
			var response = await _httpClient.GetAsync("/api/configuration");
			response.EnsureSuccessStatusCode();
			
			var configResponse = await response.Content.ReadFromJsonAsync<ConfigurationResponse>();
			return configResponse!;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error fetching configuration from server");
			return new ConfigurationResponse 
			{ 
				Configuration = CreateMinimalOptions()
			};
		}
	}

	private SharpMUSHOptions CreateMinimalOptions()
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
				PlayerFlags: ["player"],
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

	public class ConfigItem
	{
		public string Section { get; set; } = string.Empty;
		public string Key { get; set; } = string.Empty;
		public string Value { get; set; } = string.Empty;
		public string Type { get; set; } = string.Empty;
		public object? RawValue { get; set; }
		public string Description { get; set; } = string.Empty;
		public string Category { get; set; } = string.Empty;
		public bool IsAdvanced { get; set; }
		public string? DefaultValue { get; set; }
		
		public bool IsBoolean => Type == "Boolean";
		public bool IsNumber => Type is "Int32" or "UInt32" or "Double" or "Single" or "Decimal";
		public bool IsArray => Type.EndsWith("[]");
		public bool IsNullable => Type.StartsWith("Nullable");
	}
}


public static class PennMUSHOptionsExtension
{
	public static IEnumerable<object> ToDatagrid(this SharpMUSHOptions options)
	{
		return [];
	}

	public static IEnumerable<AdminConfigService.ConfigItem> ToConfigItems(this SharpMUSHOptions options)
	{
		var configItems = new List<AdminConfigService.ConfigItem>();

		try
		{
			// Use reflection to get all properties and their values
			var optionsType = typeof(SharpMUSHOptions);
			var properties = optionsType.GetProperties();

			foreach (var prop in properties)
			{
				try
				{
					var sectionName = prop.Name;
					var sectionValue = prop.GetValue(options);
					
					if (sectionValue != null)
					{
						var sectionType = prop.PropertyType;
						var sectionProperties = sectionType.GetProperties();

						foreach (var sectionProp in sectionProperties)
						{
							try
							{
								var value = sectionProp.GetValue(sectionValue);
								var valueString = value switch
								{
									null => "null",
									bool b => b.ToString(),
									string s => s,
									System.Collections.IEnumerable enumerable when enumerable is not string => 
										string.Join(", ", enumerable.Cast<object>().Select(x => x?.ToString() ?? "null")),
									_ => value.ToString() ?? "null"
								};

								// Get metadata from centralized source

								configItems.Add(new AdminConfigService.ConfigItem
								{
									Section = sectionName,
									Key = sectionProp.Name,
									Value = valueString,
									Type = sectionProp.PropertyType.Name,
									RawValue = value
										/*,
									Description = metadata?.Description ?? GetFallbackDescription(sectionName, sectionProp.Name),
									Category = metadata?.Category ?? sectionName,
									IsAdvanced = metadata?.Nullable ?? false,
									DefaultValue = metadata?.DefaultValue?.ToString()*/
								});
							}
							catch (Exception ex)
							{
								// If we can't get a specific property, add an error entry
								configItems.Add(new AdminConfigService.ConfigItem
								{
									Section = sectionName,
									Key = sectionProp.Name,
									Value = $"Error: {ex.Message}",
									Type = sectionProp.PropertyType.Name,
									Description = "Error loading property",
									Category = sectionName
								});
							}
						}
					}
				}
				catch (Exception ex)
				{
					// If we can't process a section, add an error entry
					configItems.Add(new AdminConfigService.ConfigItem
					{
						Section = prop.Name,
						Key = "Error",
						Value = $"Failed to load section: {ex.Message}",
						Type = "Error",
						Description = "Error loading section",
						Category = "Error"
					});
				}
			}
		}
		catch (Exception ex)
		{
			// If everything fails, return a single error item
			return [new AdminConfigService.ConfigItem
			{
				Section = "Error",
				Key = "ConfigurationError",
				Value = $"Failed to load configuration: {ex.Message}",
				Type = "Error",
				Description = "Critical configuration error",
				Category = "Error"
			}];
		}

		return configItems.OrderBy(x => x.Section).ThenBy(x => x.Key);
	}

	private static string GetFallbackDescription(string section, string propertyName)
	{
		// Fallback for properties without metadata
		return propertyName switch
		{
			var name when name.Contains("Port") => $"Network port for {section.ToLower()} connections",
			var name when name.Contains("Addr") => $"IP address for {section.ToLower()} binding",
			var name when name.Contains("File") => $"File path for {section.ToLower()} data",
			var name when name.Contains("Database") => $"Database file for {section.ToLower()} storage",
			var name when name.Contains("Limit") => $"Maximum limit for {section.ToLower()} operations",
			var name when name.Contains("Cost") => $"Cost setting for {section.ToLower()} operations",
			var name when name.Contains("Enable") || name.Contains("Use") => $"Enable/disable {section.ToLower()} functionality",
			_ => $"{section} {propertyName} setting"
		};
	}
}