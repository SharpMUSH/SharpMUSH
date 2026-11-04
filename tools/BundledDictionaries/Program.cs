using System.IO.Compression;
using System.Text.RegularExpressions;
using MarkupString;
using MarkupString.Ansi;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models;
using YamlDotNet.RepresentationModel;

// Trains one of the dictionaries the Lightning provider ships, from what is in this repository:
//   feed        lines shaped like the ones games store (channel recall, feed lines with a location, lines from Discord)
//   attributes  the attribute values of the bundled packages under examples/packages
//   mail        mail shaped like players' (a subject, a few paragraphs, now and then some colour)
// The words of feed lines and mail come from the help files, so a dictionary learns the records' and markup's shape
// rather than any one game's talk. Usage: <kind> <output path>.
MarkupRegistry.Default = MarkupRegistry.Empty.WithAnsi();

if (args is not [var kind, var output])
{
	Console.Error.WriteLine("Usage: BundledDictionaries feed|attributes|mail <output path>");
	return 1;
}

var root = FindRoot();
var sentences = Directory.GetFiles(Path.Join(root, "SharpMUSH.Documentation", "Helpfiles"), "*.md", SearchOption.AllDirectories)
	.Order(StringComparer.Ordinal)
	.SelectMany(file => Regex.Split(File.ReadAllText(file), @"(?<=[.!?])\s+"))
	.Select(sentence => Regex.Replace(Regex.Replace(sentence, @"[#`*\[\]\(\)|>_-]+", " "), @"\s+", " ").Trim())
	.Where(sentence => sentence.Length is > 8 and < 240 && sentence.Count(char.IsLetter) > sentence.Length / 2)
	.ToArray();

var (table, values) = kind switch
{
	"feed" => (LightningDatabase.FeedLines, FeedLines()),
	"attributes" => (LightningDatabase.AttributeValues, AttributeValues()),
	"mail" => (LightningDatabase.MailValues, MailRows()),
	_ => (null, [])
};
if (table is null)
{
	Console.Error.WriteLine($"Unknown kind {kind}: feed, attributes or mail.");
	return 1;
}

values = [.. values.Where(value => value.Length >= table.MinBytes)];
var cut = values.Count * 4 / 5;
var training = values.Take(cut).ToList();
var heldOut = values.Skip(cut).ToList();
using var dictionary = ZstandardDictionary.Train(training.SelectMany(value => value).ToArray(), training.Select(value => value.Length).ToArray(),
	table.DictionaryBytes);
var bytes = dictionary.Data.ToArray();
// A bundled dictionary is never replaced: worlds hold values written with it.
using (var file = new FileStream(output, FileMode.CreateNew, FileAccess.Write))
	file.Write(bytes);

using var check = ZstandardDictionary.Create(bytes, ZstandardCompressionOptions.DefaultQuality);
var buffer = new byte[1024 * 1024];
var compressed = heldOut.Sum(value => ZstandardEncoder.TryCompress(value, buffer, out var written, check, ZstandardCompressionOptions.DefaultWindowLog2)
	? Math.Min(written + 3, value.Length)
	: value.Length);
Console.WriteLine($"Wrote {bytes.Length} bytes to {output} from {training.Count} values. Held-out values: {heldOut.Average(value => value.Length):F1} B "
	+ $"as they are, {compressed / (double)heldOut.Count:F1} B compressed.");
return 0;

List<byte[]> FeedLines()
{
	var random = new Random(20261103);
	string[] channels = ["Public", "Newbie", "OOC", "Staff", "Spoilers", "Trivia", "Guest", "Help", "RP-Requests", "Builders"];
	string[] names = ["Grave", "Mercutio", "Ann", "Bob_the_Builder", "Kestrel", "Wanderer", "Lin", "Morgana", "Tybalt", "Rook",
		"Aiko", "Seraphine", "Dax", "Holloway", "Juniper", "Vex", "Old_Tom", "Quill", "Marisol", "Thorne"];
	var start = 1793700000000L;

	AnsiColor Colour() => random.Next(3) switch
	{
		0 => new AnsiColor.Standard((byte)random.Next(1, 8), random.Next(2) == 0),
		1 => new AnsiColor.Xterm((byte)random.Next(16, 256)),
		_ => new AnsiColor.Rgb((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256))
	};
	var colours = channels.ToDictionary(channel => channel, _ => AnsiMarkup.Create(foreground: Colour()));

	MarkupText Message()
	{
		var sentence = sentences[random.Next(sentences.Length)];
		if (random.Next(8) != 0) return MarkupText.Plain(sentence);
		var cut = random.Next(1, sentence.Length);
		var style = random.Next(3) switch
		{
			0 => AnsiMarkup.Create(bold: true),
			1 => AnsiMarkup.Create(underlined: true),
			_ => AnsiMarkup.Create(foreground: Colour())
		};
		return MarkupText.Concat([MarkupText.Plain(sentence[..cut]), MarkupText.Wrap(style, sentence[cut..])]);
	}

	SharpFeedMessage Line(int index)
	{
		var speaker = random.Next(names.Length);
		var name = names[speaker];
		var dbref = new DBRef(10 + speaker * 7, start - 86_400_000L * (speaker + 1));
		var id = start * 1000 + index * 7919L;
		var at = DateTimeOffset.FromUnixTimeMilliseconds(start + index * 41_000L);
		var text = Message();
		switch (random.Next(10))
		{
			case < 6:
				{
					var channel = channels[random.Next(channels.Length)];
					var style = random.Next(4) switch { 0 => FeedStyles.Pose, 1 => "semipose", 2 => "emit", _ => FeedStyles.Say };
					var who = random.Next(6) == 0
						? MarkupText.Wrap(AnsiMarkup.Create(linkUrl: $"look {name}", linkKind: LinkKind.Command), name)
						: MarkupText.Plain(name);
					var line = style switch
					{
						"say" => MarkupText.Concat([MarkupText.Wrap(colours[channel], $"<{channel}>"), MarkupText.Plain(" "), who, MarkupText.Plain(" says, \""), text, MarkupText.Plain("\"")]),
						"emit" => MarkupText.Concat([MarkupText.Wrap(colours[channel], $"<{channel}>"), MarkupText.Plain(" "), text]),
						"semipose" => MarkupText.Concat([MarkupText.Wrap(colours[channel], $"<{channel}>"), MarkupText.Plain(" "), who, text]),
						_ => MarkupText.Concat([MarkupText.Wrap(colours[channel], $"<{channel}>"), MarkupText.Plain(" "), who, MarkupText.Plain(" "), text])
					};
					return new SharpFeedMessage(id, "channel", $"Channel/{channel}", at, dbref, name, dbref, name, null, "", style, text,
						Line: line, Audience: random.Next(40) == 0 ? "seeall" : "", Picture: random.Next(20) == 0 ? $"#{dbref.Number}:{dbref.CreationMilliseconds}" : "");
				}
			case < 8:
				{
					var frequency = $"{random.Next(88, 108)}.{random.Next(10)}";
					var room = new DBRef(200 + random.Next(300), start - 3_600_000L * random.Next(1, 500));
					return new SharpFeedMessage(id, "radio", frequency, at, dbref, name, dbref, name, room, sentences[random.Next(sentences.Length)].Split(' ')[0],
						random.Next(3) == 0 ? FeedStyles.Pose : FeedStyles.Say, text);
				}
			default:
				{
					var display = names[random.Next(names.Length)].Replace('_', ' ');
					var identity = new ExternalFeedIdentity("discord", (100000000000000000L + random.NextInt64(900000000000000000L)).ToString(),
						display, random.Next(2) == 0 ? $"https://cdn.discordapp.com/avatars/{random.NextInt64()}/{random.NextInt64():x}.png" : "");
					var channel = channels[random.Next(channels.Length)];
					var line = MarkupText.Concat([MarkupText.Wrap(colours[channel], $"<{channel}>"), MarkupText.Plain($" {identity.DisplayName} says, \""), text, MarkupText.Plain("\"")]);
					return new SharpFeedMessage(id, "channel", $"Channel/{channel}", at, new DBRef(-1), identity.DisplayName, dbref, name, null, "",
						FeedStyles.Say, text, identity.DisplayName, line, ExternalIdentity: identity);
				}
		}
	}

	return [.. Enumerable.Range(0, 20000).Select(index => LightningDatabase.FeedLineJson(Line(index)))];
}

// Every attribute value of every bundled package, as the store holds it: the value's markup as JSON. An object
// reference ({{ref}}) is what an installed package holds in its place, a dbref. Shuffled, so the values held out to
// check the dictionary come from every package.
List<byte[]> AttributeValues()
{
	var random = new Random(20261104);
	var found = new List<string>();
	foreach (var file in Directory.GetFiles(Path.Join(root, "examples", "packages"), "package.yaml", SearchOption.AllDirectories)
		.Order(StringComparer.Ordinal))
	{
		var yaml = new YamlStream();
		yaml.Load(new StringReader(File.ReadAllText(file)));
		foreach (var document in yaml.Documents) Collect(document.RootNode);
	}

	return [.. found
		.Select(value => Regex.Replace(value, @"\{\{[^}]+\}\}", _ => $"#{random.Next(10, 5000)}"))
		.Select(value => LightningDatabase.AttributeValueJson(MarkupText.Plain(value)))
		.OrderBy(_ => random.Next())];

	void Collect(YamlNode node)
	{
		switch (node)
		{
			case YamlMappingNode mapping:
				foreach (var (key, value) in mapping.Children)
				{
					if (key is YamlScalarNode { Value: "attributes" } && value is YamlMappingNode attributes)
						found.AddRange(attributes.Children.Values.Select(Value).OfType<string>());
					else Collect(value);
				}
				break;
			case YamlSequenceNode sequence:
				foreach (var child in sequence.Children) Collect(child);
				break;
		}
	}

	static string? Value(YamlNode node) => node switch
	{
		YamlScalarNode scalar => scalar.Value,
		YamlMappingNode mapping when mapping.Children.TryGetValue(new YamlScalarNode("value"), out var value) => (value as YamlScalarNode)?.Value,
		_ => null
	};
}

// Mail as players write it: a subject, a few paragraphs, now and then a word in colour, between players over a year.
List<byte[]> MailRows()
{
	var random = new Random(20261105);
	string[] folders = ["INBOX", "INBOX", "INBOX", "Plots", "Staff", "Archive"];
	var start = 1793700000000L;
	return [.. Enumerable.Range(0, 8000).Select(_ =>
	{
		var parts = new List<MarkupText>();
		var paragraphs = random.Next(1, 5);
		for (var paragraph = 0; paragraph < paragraphs; paragraph++)
		{
			if (paragraph > 0) parts.Add(MarkupText.Plain("\n\n"));
			var count = random.Next(1, 6);
			for (var index = 0; index < count; index++)
			{
				var sentence = sentences[random.Next(sentences.Length)] + (index < count - 1 ? " " : "");
				parts.Add(random.Next(12) == 0
					? MarkupText.Wrap(AnsiMarkup.Create(foreground: new AnsiColor.Standard((byte)random.Next(1, 8), random.Next(2) == 0)), sentence)
					: MarkupText.Plain(sentence));
			}
		}

		var subject = sentences[random.Next(sentences.Length)];
		return Codec.Serialize(new MailRecord
		{
			Sender = random.Next(10, 2000),
			SenderCreationTime = start - 86_400_000L * random.Next(1, 900),
			Recipient = random.Next(10, 2000),
			DateSent = start + random.NextInt64(365L * 86_400_000L),
			Fresh = random.Next(3) == 0,
			Read = random.Next(2) == 0,
			Tagged = random.Next(10) == 0,
			Urgent = random.Next(20) == 0,
			Folder = folders[random.Next(folders.Length)],
			Content = MarkupTextSerializer.Serialize(MarkupText.Concat(parts)),
			Subject = MarkupTextSerializer.Serialize(MarkupText.Plain(subject.Length > 60 ? subject[..60] : subject))
		});
	})];
}

static string FindRoot()
{
	for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
		if (File.Exists(Path.Join(directory.FullName, "SharpMUSH.sln"))) return directory.FullName;
	throw new InvalidOperationException("Run from inside the SharpMUSH repository.");
}
