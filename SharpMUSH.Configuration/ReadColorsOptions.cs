using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;

namespace SharpMUSH.Configuration;

public class ReadColorsOptionsFactory(ILogger<ReadColorsOptionsFactory> Logger, string Name) : IOptionsFactory<ColorsOptions>
{
	public ColorsOptions Create(string _)
	{
		string text;
		try
		{
			text = File.ReadAllText(Name);
		}
		catch (Exception ex) when (ex is FileNotFoundException or IOException)
		{
			Logger.LogCritical(ex, nameof(Create));
			throw;
		}

		try
		{
			var colorIdentities = JsonSerializer.Deserialize<ColorIdentity[]>(text);

			return new ColorsOptions(colorIdentities!);

		}
		catch(Exception ex)
		{
			Logger.LogCritical(ex, nameof(Create));
			throw;
		}
	}
}
