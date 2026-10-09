using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MudBlazor;
using SharpMUSH.Client.Models.Applications;

namespace SharpMUSH.Client.Components.Schema;

/// <summary>
/// The pieces of schema rendering that hold no UI state: safe markdown, the MudBlazor color and variant a
/// schema names, a row's link, and a timeline entry read out of a data row. Shared by both renderers
/// through <see cref="SchemaDisplay"/>.
/// </summary>
public static partial class SchemaRendering
{
	/// <summary>
	/// CommonMark plus tables, emphasis extras, task lists and bare-URL links, with raw HTML disabled. Not
	/// <c>UseAdvancedExtensions</c>: its generic-attributes extension lets <c>{onclick=...}</c> through.
	/// </summary>
	private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
		.UsePipeTables()
		.UseEmphasisExtras()
		.UseTaskLists()
		.UseAutoLinks()
		.DisableHtml()
		.Build();

	/// <summary>
	/// A section's elements in the order given, with each run of consecutive buttons gathered into one
	/// group, so the renderers draw a run as a single row that wraps rather than a button per row or per
	/// grid column. Every other element is a group of one.
	/// </summary>
	public static IEnumerable<IReadOnlyList<SchemaElement>> GroupButtons(IEnumerable<SchemaElement> elements)
	{
		List<SchemaElement>? run = null;
		foreach (var element in elements)
		{
			if (IsButton(element))
			{
				(run ??= []).Add(element);
				continue;
			}

			if (run is not null)
			{
				yield return run;
				run = null;
			}

			yield return [element];
		}

		if (run is not null)
		{
			yield return run;
		}
	}

	public static bool IsButton(SchemaElement element) =>
		string.Equals(element.Kind, "button", StringComparison.OrdinalIgnoreCase);

	/// <summary>True when a section holds nothing but hidden fields, which draw nothing and so need no card.</summary>
	public static bool IsHiddenOnly(SchemaSection section) =>
		(section.Elements ?? []).All(e => string.Equals(e.Kind ?? "field", "field", StringComparison.OrdinalIgnoreCase)
			&& string.Equals(e.Type, "hidden", StringComparison.OrdinalIgnoreCase));

	/// <summary>
	/// Renders markdown to HTML that is safe to inject: raw HTML is escaped, and a link or image whose URL
	/// carries a scheme other than http, https or mailto (<c>javascript:</c>, <c>data:</c>) points nowhere.
	/// Timeline bodies are often player-written, so this is the only markdown path the renderers use.
	/// </summary>
	public static string MarkdownToHtml(string? markdown)
	{
		if (string.IsNullOrEmpty(markdown))
		{
			return string.Empty;
		}

		var document = Markdown.Parse(markdown, Pipeline);
		foreach (var link in document.Descendants<LinkInline>().Where(l => !IsSafeUrl(l.Url)))
		{
			link.Url = "#";
		}

		foreach (var link in document.Descendants<AutolinkInline>().Where(l => !IsSafeUrl(l.Url)))
		{
			link.Url = "#";
		}

		return document.ToHtml(Pipeline);
	}

	/// <summary>
	/// True for a relative URL, or an absolute one whose scheme is http, https or mailto. Browsers ignore
	/// tabs, newlines and other control characters inside a scheme, so they are dropped before looking.
	/// </summary>
	public static bool IsSafeUrl(string? url)
	{
		if (string.IsNullOrWhiteSpace(url))
		{
			return true;
		}

		var cleaned = new string(url.Where(c => !char.IsControl(c) && !char.IsWhiteSpace(c)).ToArray());
		var colon = cleaned.IndexOf(':');
		var boundary = cleaned.IndexOfAny(['/', '?', '#']);
		if (colon < 0 || (boundary >= 0 && boundary < colon))
		{
			return true;
		}

		var scheme = cleaned[..colon];
		return scheme.Equals("http", StringComparison.OrdinalIgnoreCase)
			|| scheme.Equals("https", StringComparison.OrdinalIgnoreCase)
			|| scheme.Equals("mailto", StringComparison.OrdinalIgnoreCase);
	}

	private static readonly IReadOnlyDictionary<string, Color> Colors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
	{
		["default"] = Color.Default,
		["primary"] = Color.Primary,
		["secondary"] = Color.Secondary,
		["tertiary"] = Color.Tertiary,
		["info"] = Color.Info,
		["success"] = Color.Success,
		["warning"] = Color.Warning,
		["error"] = Color.Error,
		["dark"] = Color.Dark,
	};

	/// <summary>The MudBlazor color a schema names; anything not on the list is <paramref name="fallback"/>.</summary>
	public static Color ParseColor(string? name, Color fallback = Color.Default)
		=> name is not null && Colors.TryGetValue(name.Trim(), out var color) ? color : fallback;

	/// <summary>A button's variant: <c>filled</c>, <c>outlined</c> or <c>text</c>; anything else is <paramref name="fallback"/>.</summary>
	public static Variant ParseVariant(string? name, Variant fallback = Variant.Outlined)
		=> name?.Trim().ToLowerInvariant() switch
		{
			"filled" => Variant.Filled,
			"outlined" => Variant.Outlined,
			"text" => Variant.Text,
			_ => fallback
		};

	[GeneratedRegex(@"\{([A-Za-z0-9_.\-]+)\}")]
	private static partial Regex LinkToken();

	/// <summary>
	/// Fills a column's link template from a row: each <c>{field}</c> becomes that field's value,
	/// URL-escaped (a missing field is empty). Null when the result is not a URL the portal will follow.
	/// </summary>
	public static string? FillLink(string template, JsonElement row)
	{
		var href = LinkToken().Replace(template, match =>
			Uri.EscapeDataString(row.ValueKind == JsonValueKind.Object && row.TryGetProperty(match.Groups[1].Value, out var value)
				? SchemaViewRenderer.ValueToString(value)
				: string.Empty));
		return IsSafeUrl(href) ? href : null;
	}

	/// <summary>One action button on a timeline entry.</summary>
	public sealed record TimelineAction(string Label, string Action, IReadOnlyDictionary<string, JsonElement>? Values, string? Confirm);

	/// <summary>One link on a timeline entry, beside its actions: a page the entry leads to, such as a reply form.</summary>
	public sealed record TimelineLink(string Label, string Href);

	/// <summary>
	/// One timeline entry, read from a data row. <c>Group</c> joins it to the rows around it holding the same
	/// group, drawn as one conversation; <c>ReplyTo</c> is the small tag naming what it answers; <c>Anchor</c>
	/// is its element id, for a link ending <c>#anchor</c>; <c>Unread</c> marks it new; <c>More</c> links to
	/// the <c>ChildrenHidden</c> replies left out under it; <c>Links</c> are followed rather than posted.
	/// </summary>
	public sealed record TimelineEntry(
		string Author,
		string Time,
		string Body,
		bool IsCode,
		string? Tag,
		Color TagColor,
		IReadOnlyList<TimelineAction> Actions,
		bool IsMarkup = false,
		string? Group = null,
		string? ReplyTo = null,
		string? Anchor = null,
		bool Unread = false,
		int ChildrenHidden = 0,
		string? More = null,
		IReadOnlyList<TimelineLink>? Links = null);

	/// <summary>Reads a timeline entry from a data row; a row that is not an object reads as empty.</summary>
	public static TimelineEntry ReadTimelineEntry(JsonElement row)
	{
		string Text(string name) =>
			row.ValueKind == JsonValueKind.Object && row.TryGetProperty(name, out var value)
				? SchemaViewRenderer.ValueToString(value)
				: string.Empty;

		var actions = new List<TimelineAction>();
		if (row.ValueKind == JsonValueKind.Object
			&& row.TryGetProperty("actions", out var list) && list.ValueKind == JsonValueKind.Array)
		{
			foreach (var item in list.EnumerateArray().Where(a => a.ValueKind == JsonValueKind.Object))
			{
				var action = item.TryGetProperty("action", out var name) ? SchemaViewRenderer.ValueToString(name) : string.Empty;
				if (string.IsNullOrWhiteSpace(action))
				{
					continue;
				}

				var label = item.TryGetProperty("label", out var labelValue) ? SchemaViewRenderer.ValueToString(labelValue) : action;
				var confirm = item.TryGetProperty("confirm", out var confirmValue) ? SchemaViewRenderer.ValueToString(confirmValue) : null;
				IReadOnlyDictionary<string, JsonElement>? values = item.TryGetProperty("values", out var valuesValue)
					&& valuesValue.ValueKind == JsonValueKind.Object
					? valuesValue.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.Ordinal)
					: null;
				actions.Add(new TimelineAction(label, action, values, string.IsNullOrWhiteSpace(confirm) ? null : confirm));
			}
		}

		var links = new List<TimelineLink>();
		if (row.ValueKind == JsonValueKind.Object
			&& row.TryGetProperty("links", out var linkList) && linkList.ValueKind == JsonValueKind.Array)
		{
			foreach (var item in linkList.EnumerateArray().Where(l => l.ValueKind == JsonValueKind.Object))
			{
				var href = item.TryGetProperty("href", out var hrefValue) ? SchemaViewRenderer.ValueToString(hrefValue) : string.Empty;
				var label = item.TryGetProperty("label", out var labelValue) ? SchemaViewRenderer.ValueToString(labelValue) : string.Empty;
				if (!string.IsNullOrWhiteSpace(href) && !string.IsNullOrWhiteSpace(label) && IsSafeUrl(href))
				{
					links.Add(new TimelineLink(label, href));
				}
			}
		}

		string? Optional(string name) => Text(name) is { Length: > 0 } text && !string.IsNullOrWhiteSpace(text) ? text : null;

		var format = Text("format");
		var more = Optional("more");
		return new TimelineEntry(
			Text("author"),
			FormatTime(row.ValueKind == JsonValueKind.Object && row.TryGetProperty("time", out var time) ? time : null),
			Text("body"),
			string.Equals(format, "code", StringComparison.OrdinalIgnoreCase),
			Optional("tag"),
			ParseColor(Text("tag_color")),
			actions,
			IsMarkup: string.Equals(format, "mstring", StringComparison.OrdinalIgnoreCase),
			Group: Optional("group"),
			ReplyTo: Optional("reply_to"),
			Anchor: Optional("anchor"),
			Unread: IsTrue(row, "unread"),
			ChildrenHidden: int.TryParse(Text("children_hidden"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var hidden)
				? Math.Max(hidden, 0)
				: 0,
			More: more is not null && IsSafeUrl(more) ? more : null,
			Links: links);
	}

	/// <summary>True when row field <paramref name="name"/> is JSON true, or a string or number reading 1 or true.</summary>
	private static bool IsTrue(JsonElement row, string name)
	{
		if (row.ValueKind != JsonValueKind.Object || !row.TryGetProperty(name, out var value))
		{
			return false;
		}

		return value.ValueKind switch
		{
			JsonValueKind.True => true,
			JsonValueKind.Number => value.TryGetInt64(out var number) && number != 0,
			JsonValueKind.String => value.GetString()?.Trim().ToLowerInvariant() is "1" or "true" or "yes",
			_ => false,
		};
	}

	/// <summary>
	/// A serialized MString as safe HTML, rendered the way the terminal renders it
	/// (<c>MarkupText.Render(MarkupFormat.Html)</c>); a value that is not a serialized MString is HTML-encoded text.
	/// </summary>
	public static string MarkupToHtml(string? value)
	{
		if (string.IsNullOrEmpty(value))
		{
			return string.Empty;
		}

		try
		{
			return MarkupTextSerializer.Deserialize(value).Render(MarkupFormat.Html);
		}
		catch (Exception)
		{
			return System.Net.WebUtility.HtmlEncode(value);
		}
	}

	/// <summary>
	/// A timeline time as local date and time: unix seconds as a number or a numeric string, else any
	/// date string that parses; anything else is shown as written.
	/// </summary>
	public static string FormatTime(JsonElement? time)
	{
		if (time is not { } value)
		{
			return string.Empty;
		}

		if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var seconds))
		{
			return FromUnix(seconds);
		}

		if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var fractional))
		{
			return FromUnix((long)fractional);
		}

		var text = SchemaViewRenderer.ValueToString(value);
		if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out seconds))
		{
			return FromUnix(seconds);
		}

		return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
			? parsed.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
			: text;
	}

	private static string FromUnix(long seconds)
	{
		try
		{
			return DateTimeOffset.FromUnixTimeSeconds(seconds).ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
		}
		catch (ArgumentOutOfRangeException)
		{
			return seconds.ToString(CultureInfo.InvariantCulture);
		}
	}

	/// <summary>
	/// Fills <c>{path}</c> in a route with the sub-path segments (each URL-escaped, joined by <c>/</c>; empty
	/// when there are none), then appends <paramref name="query"/> (without its <c>?</c>), joined with
	/// <c>&amp;</c> when the route already has a query.
	/// </summary>
	public static string ApplyPath(string route, IReadOnlyList<string> segments, string? query)
	{
		var result = route.Replace("{path}", string.Join('/', segments.Select(Uri.EscapeDataString)), StringComparison.Ordinal);
		if (string.IsNullOrEmpty(query))
		{
			return result;
		}

		var builder = new StringBuilder(result);
		builder.Append(result.Contains('?') ? '&' : '?').Append(query);
		return builder.ToString();
	}
}
