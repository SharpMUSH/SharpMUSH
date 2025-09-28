namespace SharpMUSH.Configuration.Options;

public record ColorsOptions(ColorIdentity[] Colors)
{
	public Dictionary<string, ColorIdentity> ColorsByName = Colors.ToDictionary(c => c.name, StringComparer.OrdinalIgnoreCase);
	public Dictionary<string, ColorIdentity[]> ColorsByRgb = Colors.GroupBy(c => c.rgb).ToDictionary(c => c.Key, c => c.ToArray(), StringComparer.OrdinalIgnoreCase);
	public Dictionary<string, ColorIdentity[]> ColorsByXterm = Colors.GroupBy(c => c.xterm).ToDictionary(c => c.Key.ToString(), c => c.ToArray(), StringComparer.OrdinalIgnoreCase);
	public Dictionary<string, ColorIdentity[]> ColorsByAnsi = Colors.GroupBy(c => c.ansi).ToDictionary(c => c.Key.ToString(), c => c.ToArray(), StringComparer.OrdinalIgnoreCase);
}