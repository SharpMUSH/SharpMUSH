using System.IO.Compression;
using System.Text.RegularExpressions;
using MarkupString;
using MarkupString.Ansi;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Library.Models;

// Lines shaped like the ones games store: channel recall (say, pose, semipose, emit, with the channel's colour and now
// and then a link on the speaker), feed lines with a location, and lines from Discord. Their words come from the
// help files, so the dictionary learns the records' and markup's shape rather than any one game's talk.
MarkupRegistry.Default = MarkupRegistry.Empty.WithAnsi();

var output = args.Length > 0 ? args[0] : "feed-lines-0.zdict";
var root = FindRoot();
var sentences = Directory.GetFiles(Path.Join(root, "SharpMUSH.Documentation", "Helpfiles"), "*.md", SearchOption.AllDirectories)
	.Order(StringComparer.Ordinal)
	.SelectMany(file => Regex.Split(File.ReadAllText(file), @"(?<=[.!?])\s+"))
	.Select(sentence => Regex.Replace(Regex.Replace(sentence, @"[#`*\[\]\(\)|>_-]+", " "), @"\s+", " ").Trim())
	.Where(sentence => sentence.Length is > 8 and < 240 && sentence.Count(char.IsLetter) > sentence.Length / 2)
	.ToArray();

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

var lines = Enumerable.Range(0, 20000).Select(index => LightningDatabase.FeedLineJson(Line(index))).ToList();
var training = lines.Take(16000).ToList();
var heldOut = lines.Skip(16000).ToList();
using var dictionary = ZstandardDictionary.Train(training.SelectMany(line => line).ToArray(), training.Select(line => line.Length).ToArray(),
	LightningDatabase.DictionaryBytes);
var bytes = dictionary.Data.ToArray();
File.WriteAllBytes(output, bytes);

using var check = ZstandardDictionary.Create(bytes, ZstandardCompressionOptions.DefaultQuality);
var buffer = new byte[64 * 1024];
var compressed = heldOut.Sum(line => ZstandardEncoder.TryCompress(line, buffer, out var written, check, ZstandardCompressionOptions.DefaultWindowLog2) ? written + 3 : line.Length);
Console.WriteLine($"Wrote {bytes.Length} bytes to {output}. Held-out lines: {heldOut.Average(line => line.Length):F1} B as JSON, {compressed / (double)heldOut.Count:F1} B compressed.");

static string FindRoot()
{
	for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
		if (File.Exists(Path.Join(directory.FullName, "SharpMUSH.sln"))) return directory.FullName;
	throw new InvalidOperationException("Run from inside the SharpMUSH repository.");
}
