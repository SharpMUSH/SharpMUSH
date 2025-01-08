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
	public PennMUSHOptions Create(string filename)
	{
		var keys = typeof(PennMUSHOptions)
			.GetProperties()
			.Select(property => property.GetType())
			.SelectMany(configType => configType
				.GetProperties()
				.SelectMany(configProperty => configProperty
					.GetCustomAttributes<PennConfigAttribute>()
					.Select(x => x.Name))).ToImmutableHashSet();

		var configDictionary = keys.ToDictionary(key => key, key => string.Empty);
		var splitter = KeyValueSplittingRegex();
		
		var text = File.ReadAllLines(filename);
		
		// TODO: Use a Regex to split the values.
		foreach (var configLine in text
			         .Where(line => splitter.Match(line).Success)
			         .Select(line => splitter.Match(line).Groups))
		{
			configDictionary[configLine["Key"].Value] = configLine["Value"].Value;
		}

		// This is a lot of dupe work. This can likely be done with a proper bit of Reflection. 
		var work = new PennMUSHOptions(
			new AttributeOptions(
				ADestroy: configDictionary["adestroy"] is "-1" or "0" or "false"
				          || !string.IsNullOrEmpty(configDictionary["adestroy"]),
				AMail: configDictionary["amail"] is "-1" or "0" or "false"
				       || !string.IsNullOrEmpty(configDictionary["amail"]),
				PlayerListen: configDictionary["player_listen"] is "-1" or "0" or "false"
				              || string.IsNullOrEmpty(configDictionary["player_listen"]),
				PlayerAHear: configDictionary["player_ahear"] is "-1" or "0" or "false"
				             || string.IsNullOrEmpty(configDictionary["player_ahear"]),
				Startups: configDictionary["startups"] is "-1" or "0" or "false"
				          || string.IsNullOrEmpty(configDictionary["startups"]),
				ReadRemoteDesc: configDictionary["read_remote_desc"] is "-1" or "0" or "false"
				                || !string.IsNullOrEmpty(configDictionary["read_remote_desc"]),
				RoomConnects: configDictionary["room_connects"] is "-1" or "0" or "false"
				              || string.IsNullOrEmpty(configDictionary["room_connects"]),
				ReverseShs: configDictionary["reverse_shs"] is "-1" or "0" or "false"
				            || string.IsNullOrEmpty(configDictionary["reverse_shs"]),
				EmptyAttributes: configDictionary["empty_attributes"] is "-1" or "0" or "false"
				                 || string.IsNullOrEmpty(configDictionary["empty_attributes"])
			),
			new ChatOptions(),
			new CommandOptions(),
			new CompatibilityOptions(),
			new CosmeticOptions(),
			new CostOptions(),
			new DatabaseOptions(),
			new DumpOptions(),
			new FileOptions(),
			new FlagOptions(),
			new FunctionOptions(),
			new LimitOptions(),
			new LogOptions(),
			new MessageOptions(),
			new NetConfig()
		);

		return work;
	}

    [GeneratedRegex(@"^(?<Key>.+)\s+(?<Value>.+)\s*$")]
    private static partial Regex KeyValueSplittingRegex();
}