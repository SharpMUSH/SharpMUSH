using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using MarkupString;
using MarkupString.Layout;
using SharpMUSH.Documentation.MarkdownToAsciiRenderer;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.Models.Portal.Applications;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Functions;

/// <summary>
/// <c>schemarender()</c> and <c>schemacheck()</c>: a Portal Schema Document drawn in the terminal, and the
/// problems with one. The portal draws the same document from the same data, so a package writes its sheet once.
/// </summary>
public partial class Functions
{
	/// <summary>
	/// <c>schemacheck(&lt;schema&gt;[, &lt;data&gt;])</c> — nothing when the document is fine, else one
	/// <c>&lt;path&gt;: &lt;problem&gt;</c> per line. Given the data its route answers, it also names what an
	/// element reads that the data does not hold.
	/// </summary>
	[SharpFunction(Name = "schemacheck", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["schema", "data"])]
	public ValueTask<CallState> SchemaCheck(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var data = Arg(args, 1).ToPlainText();
		var problems = PortalSchema.Check(ParseJson(Arg(args, 0).ToPlainText()), data.Trim().Length == 0 ? null : ParseJson(data) ?? new JsonArray());
		return ValueTask.FromResult(new CallState(string.Join('\n', problems)));
	}

	/// <summary>
	/// <c>schemarender(&lt;schema&gt;, &lt;data&gt;[, &lt;options&gt;])</c> — the document drawn with the data a
	/// route answers for it, in the game's look at the reader's width. A view's tabs show one page:
	/// <c>{"page":"&lt;key&gt;"}</c>, the first when none is named. <c>width</c> sets a fixed width.
	/// </summary>
	[SharpFunction(Name = "schemarender", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["schema", "data", "options"])]
	public ValueTask<CallState> SchemaRender(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		if (ParseJson(Arg(args, 0).ToPlainText()) is not JsonObject document)
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.InvalidSchema));

		var dataText = Arg(args, 1).ToPlainText();
		var data = dataText.Trim().Length == 0 ? null : ParseJson(dataText) as JsonObject;
		if (dataText.Trim().Length > 0 && data is null) return ValueTask.FromResult(new CallState(ErrorMessages.Returns.InvalidSchemaData));

		var optionsText = Arg(args, 2).ToPlainText();
		var options = optionsText.Trim().Length == 0 ? new JsonObject() : ParseJson(optionsText) as JsonObject;
		if (options is null) return ValueTask.FromResult(new CallState(ErrorMessages.Returns.InvalidArgument));
		var widthArg = options["width"] is JsonValue width ? MarkupText.Plain(width.ToString()) : MarkupText.Empty;
		if (LayoutWidth(parser, widthArg) is not (int cells, bool fluid)) return ValueTask.FromResult(new CallState(ErrorMessages.Returns.ArgRange));

		var drawing = new SchemaDrawing(data?["fields"] as JsonObject, cells, parser);
		var block = drawing.Document(document, PortalSchema.Text(options, "page"));
		return ValueTask.FromResult(new CallState(block is null ? MarkupText.Empty : Build(block, cells, fluid)));
	}

	private static JsonNode? ParseJson(string text)
	{
		try
		{
			return JsonNode.Parse(text);
		}
		catch (JsonException)
		{
			return null;
		}
	}

	/// <summary>A document laid out as blocks, from the data's <c>fields</c>, the way the portal's view renderer reads them.</summary>
	private sealed class SchemaDrawing(JsonObject? fields, int width, IMUSHCodeParser parser)
	{
		public Block? Document(JsonObject document, string pageKey)
		{
			var pages = Ordered(document["pages"] as JsonArray).ToList();
			var tabbed = string.Equals(PortalSchema.Text(document, "layout"), "tabs", StringComparison.OrdinalIgnoreCase)
				&& !string.Equals(PortalSchema.Text(document, "kind"), "form", StringComparison.OrdinalIgnoreCase)
				&& pages.Count > 1;

			var parts = new List<Block>();
			if (tabbed)
			{
				var shown = pages.FirstOrDefault(page => PortalSchema.Text(page, "key") == pageKey) ?? pages[0];
				parts.AddRange(Page(shown));
				var others = pages.Where(page => page != shown).Select(PageName).ToList();
				if (others.Count > 0) parts.Add(new Fields([new Field(MarkupText.Plain("Other pages"), new TextBlock(MarkupText.Plain(string.Join(" · ", others))))]));
				var title = string.Join(" · ", ((string[])[PortalSchema.Text(document, "title"), PageName(shown)]).Where(t => t.Length > 0));
				return parts.Count == 0 ? null : ServerLayout.Panel(MarkupText.Plain(title), [.. parts]);
			}

			foreach (var page in pages)
			{
				var drawn = Page(page).ToList();
				if (drawn.Count == 0) continue;
				if (pages.Count > 1 && PortalSchema.Text(page, "title") is { Length: > 0 } title)
					parts.Add(new Rule(MarkupText.Plain(title)));
				parts.AddRange(drawn);
			}
			return parts.Count == 0 ? null : ServerLayout.Panel(MarkupText.Plain(PortalSchema.Text(document, "title")), [.. parts]);
		}

		private static string PageName(JsonObject page) =>
			PortalSchema.Text(page, "title") is { Length: > 0 } title ? title : PortalSchema.Text(page, "key");

		private IEnumerable<Block> Page(JsonObject page)
		{
			foreach (var section in Ordered(page["sections"] as JsonArray))
			{
				var blocks = Section(section).ToList();
				if (blocks.Count == 0) continue;
				if (PortalSchema.Text(section, "name") is { Length: > 0 } name) yield return new Rule(MarkupText.Plain(name));
				var columns = section["columns"] is JsonValue c && c.TryGetValue<int>(out var n) ? n : 1;
				if (columns < 2 || blocks.Count < 2)
				{
					foreach (var block in blocks) yield return block;
					continue;
				}
				foreach (var row in blocks.Chunk(columns)) yield return row.Length == 1 ? row[0] : new Flex([.. row]) { Gap = 2 };
			}
		}

		/// <summary>A section's elements: neighbouring fields share one set of lined-up labels.</summary>
		private IEnumerable<Block> Section(JsonObject section)
		{
			var pending = new List<Field>();
			foreach (var element in (section["elements"] as JsonArray ?? []).OfType<JsonObject>())
			{
				var kind = PortalSchema.KindOf(element);
				if (kind is "field" or "keyvalue" && !IsGraphField(element))
				{
					pending.AddRange(kind == "field" ? FieldRow(element) : KeyValueRows(element));
					continue;
				}
				if (pending.Count > 0)
				{
					yield return new Fields([.. pending]);
					pending.Clear();
				}
				if (Element(kind, element) is { } block) yield return block;
			}
			if (pending.Count > 0) yield return new Fields([.. pending]);
		}

		private static bool IsGraphField(JsonObject element) =>
			string.Equals(PortalSchema.Text(element, "type"), "graph", StringComparison.OrdinalIgnoreCase);

		private Block? Element(string kind, JsonObject element) => kind switch
		{
			"divider" => new Rule(),
			"markdown" => Markdown(element),
			"image" => Image(element),
			"table" => Table(element),
			"stats" => Stats(element),
			"chips" => Chips(element),
			"timeline" => Timeline(element),
			"graph" => Graph(element, PortalSchema.Text(element, "src_field")),
			"field" => Graph(element, PortalSchema.Text(element, "key")),
			_ => null,
		};

		/// <summary>A field shows when the data holds it and lets the viewer see it; an empty one shows its default unless <c>show_when_empty</c> is false.</summary>
		private IEnumerable<Field> FieldRow(JsonObject element)
		{
			if (Visible(PortalSchema.Text(element, "key")) is not { } value) yield break;
			var text = string.Equals(PortalSchema.Text(element, "type"), "mstring", StringComparison.OrdinalIgnoreCase)
				? Markup(ValueText(value))
				: MarkupText.Plain(ValueText(value));
			if (text.Length == 0)
			{
				if (element["show_when_empty"] is JsonValue shown && shown.GetValueKind() == JsonValueKind.False) yield break;
				text = MarkupText.Plain(ValueText(element["default"]));
			}
			var label = PortalSchema.Text(element, "label") is { Length: > 0 } named ? named : PortalSchema.Text(element, "key");
			yield return new Field(MarkupText.Plain(label), ServerLayout.Body(text));
		}

		private IEnumerable<Field> KeyValueRows(JsonObject element)
		{
			return Strings(element["fields"] as JsonArray)
				.Select(key => (Key: key, Value: Visible(key)))
				.Where(row => row.Value is not null)
				.Select(row => (row.Key, Text: row.Value is JsonObject item ? ValueText(item["text"]) : ValueText(row.Value)))
				.Where(row => row.Text.Length > 0)
				.Select(row => new Field(MarkupText.Plain(row.Key), new TextBlock(MarkupText.Plain(row.Text))));
		}

		private Block? Markdown(JsonObject element)
		{
			var value = PortalSchema.Text(element, "value");
			if (value.Length == 0) return null;
			return string.Equals(PortalSchema.Text(element, "format"), "mstring", StringComparison.OrdinalIgnoreCase)
				? ServerLayout.Body(Markup(value))
				: ServerLayout.Body(RecursiveMarkdownHelper.RenderMarkdown(value, width, parser));
		}

		/// <summary>A picture is its address: a client that cannot draw it can still open it.</summary>
		private Block? Image(JsonObject element)
		{
			var source = PortalSchema.Text(element, "src_field") is { Length: > 0 } key ? ValueText(Visible(key)) : PortalSchema.Text(element, "value");
			return source.Length == 0 ? null : Text(MarkupText.Plain(source));
		}

		private Block? Table(JsonObject element)
		{
			var columns = (element["columns"] as JsonArray ?? []).OfType<JsonObject>().ToList();
			if (columns.Count == 0) return null;
			var rows = Rows(element);
			if (rows.Count == 0) return Empty(element);

			var grouped = rows.Any(row => RowText(row, "group").Length > 0);
			var headings = columns.Select(column => new TableColumn(MarkupText.Plain(PortalSchema.Text(column, "label")))).ToList();
			if (grouped) headings.Insert(0, new TableColumn(MarkupText.Empty));

			string? group = null;
			var cells = rows.Select(row =>
			{
				var line = columns.Select(column => (Block)new TextBlock(MarkupText.Plain(RowText(row, PortalSchema.Text(column, "key"))))).ToList();
				if (grouped)
				{
					var rowGroup = RowText(row, "group");
					line.Insert(0, new TextBlock(MarkupText.Plain(rowGroup == group ? string.Empty : rowGroup)));
					group = rowGroup;
				}
				return line.ToImmutableArray();
			});
			return ServerLayout.Listing([.. headings], cells);
		}

		/// <summary>Each tile's label and value, with a bar (or pips) for a tile that has a maximum, and its caption after.</summary>
		private Block? Stats(JsonObject element)
		{
			var tiles = Rows(element).Where(row => RowText(row, "value").Length > 0).ToList();
			if (tiles.Count == 0) return Empty(element);
			return new Fields([.. tiles.Select(tile =>
			{
				var caption = RowText(tile, "caption");
				var value = RowText(tile, "value") + (caption.Length > 0 ? $" ({caption})" : string.Empty);
				Block shown = new TextBlock(MarkupText.Plain(value));
				if (RowNumber(tile, "max") is > 0 and var max)
				{
					var current = Math.Clamp(RowNumber(tile, "current") ?? 0, 0, max);
					shown = RowText(tile, "pips") is "true" or "1"
						? new TextBlock(MarkupText.Plain($"{new string('●', current)}{new string('○', max - current)} {value}"))
						: new Flex([new Gauge(current, max), new TextBlock(MarkupText.Plain(value))]) { Gap = 1 };
				}
				return new Field(MarkupText.Plain(RowText(tile, "label")), shown);
			})]);
		}

		private Block? Chips(JsonObject element)
		{
			var key = PortalSchema.Text(element, "src_field");
			var chips = (Visible(key) as JsonArray ?? [])
				.Select(chip => chip is JsonObject item ? ValueText(item["text"]) : ValueText(chip))
				.Where(text => text.Trim().Length > 0)
				.ToList();
			if (chips.Count == 0) return Empty(element);
			var line = MarkupText.Plain(string.Join(" · ", chips));
			return PortalSchema.Text(element, "label") is { Length: > 0 } label
				? new Fields([new Field(MarkupText.Plain(label), new TextBlock(line))])
				: Text(line);
		}

		/// <summary>One entry per line pair: who and when, then what they wrote.</summary>
		private Block? Timeline(JsonObject element)
		{
			var entries = Rows(element);
			if (entries.Count == 0) return Empty(element);
			return new Fields([.. entries.Select(entry =>
			{
				var head = string.Join(" ", ((string[])[RowText(entry, "author"), TimeText(entry is JsonObject row ? row["time"] : null), RowText(entry, "tag") is { Length: > 0 } tag ? $"[{tag}]" : ""])
					.Where(part => part.Length > 0));
				var body = RowText(entry, "body");
				var text = RowText(entry, "format") switch
				{
					"mstring" => ServerLayout.Body(Markup(body)),
					"code" => new TextBlock(MarkupText.Plain(body)),
					_ => ServerLayout.Body(RecursiveMarkdownHelper.RenderMarkdown(body, Math.Max(20, width - head.Length - 4), parser)),
				};
				return new Field(MarkupText.Plain(head), text);
			})]);
		}

		/// <summary>A graph as each step and where its outcomes lead.</summary>
		private Block? Graph(JsonObject element, string key)
		{
			if (Visible(key) is not JsonObject graph) return null;
			var nodes = (graph["nodes"] as JsonArray ?? []).OfType<JsonObject>().ToList();
			if (nodes.Count == 0) return Empty(element);
			var names = nodes.ToDictionary(node => PortalSchema.Text(node, "key"), node => PortalSchema.Text(node, "label") is { Length: > 0 } label ? label : PortalSchema.Text(node, "key"));
			var edges = (graph["edges"] as JsonArray ?? []).OfType<JsonObject>().ToList();
			var rows = nodes.Select(node =>
			{
				var from = PortalSchema.Text(node, "key");
				var leads = edges.Where(edge => PortalSchema.Text(edge, "from") == from)
					.Select(edge => (PortalSchema.Text(edge, "port") is { Length: > 0 } port ? port + " → " : "→ ") + names.GetValueOrDefault(PortalSchema.Text(edge, "to"), PortalSchema.Text(edge, "to")));
				return new Field(MarkupText.Plain(names[from]), new TextBlock(MarkupText.Plain(string.Join(", ", leads))));
			});
			var fieldsBlock = new Fields([.. rows]);
			return PortalSchema.Text(element, "label") is { Length: > 0 } title
				? new Stack([new Rule(MarkupText.Plain(title)), fieldsBlock])
				: fieldsBlock;
		}

		private Block? Empty(JsonObject element) =>
			PortalSchema.Text(element, "empty") is { Length: > 0 } empty ? Text(MarkupText.Plain(empty)) : null;

		private List<JsonNode?> Rows(JsonObject element) =>
			Visible(PortalSchema.Text(element, "rows_field")) is JsonArray rows ? [.. rows] : [];

		/// <summary>A datum the viewer may see, or null when the data leaves it out or hides it.</summary>
		private JsonNode? Visible(string key)
		{
			if (key.Length == 0 || fields?[key] is not JsonObject datum) return null;
			if (datum["visible"] is JsonValue visible && visible.GetValueKind() == JsonValueKind.False) return null;
			return datum["value"] ?? JsonValue.Create(string.Empty);
		}

		private static IEnumerable<JsonObject> Ordered(JsonArray? list) =>
			(list ?? []).OfType<JsonObject>().Select((node, index) => (node, index))
				.OrderBy(pair => pair.node["order"] is JsonValue order && order.TryGetValue<int>(out var n) ? n : pair.index)
				.ThenBy(pair => pair.index)
				.Select(pair => pair.node);

		private static IEnumerable<string> Strings(JsonArray? list) =>
			(list ?? []).OfType<JsonValue>().Where(v => v.GetValueKind() == JsonValueKind.String).Select(v => v.GetValue<string>());

		/// <summary>A value as the portal shows it: a string unquoted, a list of words joined, anything else as JSON.</summary>
		private static string ValueText(JsonNode? value) => value switch
		{
			null => string.Empty,
			JsonValue v when v.GetValueKind() == JsonValueKind.String => v.GetValue<string>(),
			JsonValue v when v.GetValueKind() == JsonValueKind.Null => string.Empty,
			JsonValue v => v.ToJsonString(),
			JsonArray list when list.All(item => item is JsonValue) => string.Join(", ", list.Select(ValueText)),
			_ => value.ToJsonString(),
		};

		private static string RowText(JsonNode? row, string name) => row is JsonObject item ? ValueText(item[name]) : string.Empty;

		private static int? RowNumber(JsonNode? row, string name) =>
			int.TryParse(RowText(row, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;

		/// <summary>A timeline time: unix seconds as a UTC date and time, anything else as written.</summary>
		private static string TimeText(JsonNode? time)
		{
			var text = ValueText(time);
			return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
				? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
				: text;
		}

		/// <summary>A serialized MString, or the text as written when it is not one.</summary>
		private static MString Markup(string value)
		{
			if (value.Length == 0) return MarkupText.Empty;
			try
			{
				return MarkupTextSerializer.Deserialize(value);
			}
			catch (Exception ex) when (ex is JsonException or InvalidOperationException)
			{
				return MarkupText.Plain(value);
			}
		}

		private static Block Text(MString text) => new TextBlock(text);
	}
}
