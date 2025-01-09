using System.Collections.Immutable;
using System.Reflection;
using System.Text;
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
			new FileOptions(),
			new FlagOptions(),
			new FunctionOptions(),
			new LimitOptions(),
			new LogOptions(),
			new MessageOptions(),
			new NetConfig()
		);

		return work;

		string Get(string key) => configDictionary[propertyDictionary[key]];
	}

	[GeneratedRegex(@"^(?<Key>.+)\s+(?<Value>.+)\s*$")]
	private static partial Regex KeyValueSplittingRegex();
}