using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;

namespace SharpMUSH.Configuration;

public class ReadColorsOptionsFactory(
	ILogger<ReadColorsOptionsFactory> logger,
	[FromKeyedServices("colorFile")] string filePath)
	: IOptionsFactory<ColorsOptions>
{
	public ColorsOptions Create(string _)
	{
		string text;
		try
		{
			text = File.ReadAllText(filePath);
		}
		catch (Exception ex) when (ex is FileNotFoundException or IOException)
		{
			logger.LogCritical(ex, nameof(Create));
			throw;
		}

		try
		{
			var colorIdentities = JsonSerializer.Deserialize<ColorIdentity[]>(text);

			return new ColorsOptions(colorIdentities!);
		}
		catch (Exception ex)
		{
			logger.LogCritical(ex, nameof(Create));
			throw;
		}
	}
}