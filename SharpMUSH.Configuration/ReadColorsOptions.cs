using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;
using System.Text.Json;

namespace SharpMUSH.Configuration;

public class ReadColorsOptionsFactory(string Name) : IOptionsFactory<ColorsOptions>
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
			throw;
		}

		try
		{
			var colorIdentities = JsonSerializer.Deserialize<ColorIdentity[]>(text);

			return new ColorsOptions(colorIdentities!);

		}
		catch
		{
			// Should add logging: ex
			throw;
		}
	}
}
