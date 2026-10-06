using Markdig;
using Markdig.Extensions.CustomContainers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SharpMUSH.Library.Services;

/// <summary>Which way a <see cref="FlexBlock"/> lays out its items.</summary>
public enum FlexDirection { Row, Column }

/// <summary>Where items sit across the row (CSS <c>align-items</c> / <c>align-self</c>).</summary>
public enum FlexAlign { Start, Center, End, Stretch }

/// <summary>Where spare width goes along the row (CSS <c>justify-content</c>).</summary>
public enum FlexJustify { Start, Center, End, Between }

/// <summary>A fixed item width: a share of the row, or a number of character columns.</summary>
public readonly record struct FlexBasis(int Value, bool IsPercent)
{
	/// <summary>The width in columns this basis takes out of <paramref name="available"/>.</summary>
	public int Columns(int available) => Math.Clamp(IsPercent ? available * Value / 100 : Value, 1, Math.Max(available, 1));

	/// <summary>The CSS length: a percentage, or columns as <c>ch</c>.</summary>
	public string Css => IsPercent
		? $"{Value.ToString(CultureInfo.InvariantCulture)}%"
		: $"{Value.ToString(CultureInfo.InvariantCulture)}ch";
}

/// <summary>A <c>flex</c> block's settings, each one validated: a value off the list falls back to its default.</summary>
public sealed record FlexOptions(FlexDirection Direction, int Gap, FlexAlign Align, FlexJustify Justify, bool Wrap)
{
	public const int DefaultGap = 2;
	public static FlexOptions Default { get; } = new(FlexDirection.Row, DefaultGap, FlexAlign.Stretch, FlexJustify.Start, Wrap: true);
}

/// <summary>An <c>item</c>'s settings, each one validated.</summary>
public sealed record FlexItemOptions(int Grow, FlexBasis? Basis, int Min, FlexAlign? Align)
{
	public const int DefaultMin = 24;
	public static FlexItemOptions Default { get; } = new(1, null, DefaultMin, null);
}

/// <summary>
/// A <c>flex</c> container: its items side by side (or stacked), on the web as CSS flexbox and in the terminal
/// as columns. Every child is a <see cref="FlexItemBlock"/>; content not fenced as an <c>item</c> becomes an
/// item of its own (and <see cref="MarkdownFenceCheck"/> warns about it).
/// </summary>
public sealed class FlexBlock() : ContainerBlock(null)
{
	public FlexOptions Options { get; init; } = FlexOptions.Default;
}

/// <summary>One item of a <see cref="FlexBlock"/>.</summary>
public sealed class FlexItemBlock() : ContainerBlock(null)
{
	public FlexItemOptions Options { get; init; } = FlexItemOptions.Default;
}

/// <summary>
/// Reads <c>flex</c> and <c>item</c> custom containers as a layout:
/// <code>
/// :::: flex {gap=2 align=center}
/// ::: item {grow=2}
/// The wide column.
/// :::
/// ::: item {basis=30%}
/// The narrow one.
/// :::
/// ::::
/// </code>
/// A container holding others takes more colons than they do, the rule every CommonMark container syntax
/// shares (Pandoc and Quarto fenced divs, the generic-directives proposal, markdown-it-container). The settings
/// are an allowlist read from the attribute block; the HTML carries them as classes and CSS custom properties
/// holding validated numbers, never as author-written style.
/// </summary>
/// <remarks>
/// Shared by the wiki pipeline and <c>RecursiveMarkdownHelper.ConfigureHelpSyntax</c>, so the portal's wiki,
/// its help pages and the terminal renderer agree. Register it before <see cref="WikiSafetyExtension"/>, which
/// drops the attributes this reads.
/// </remarks>
public sealed partial class MarkdownFlexExtension : IMarkdownExtension
{
	public const string FlexName = "flex";
	public const string ItemName = "item";

	public const int MaxGap = 4;
	public const int MaxGrow = 12;
	public const int MinColumns = 4;
	public const int MaxColumns = 200;

	[GeneratedRegex(@"^(\d{1,3})(%?)$")]
	private static partial Regex BasisPattern();

	public void Setup(MarkdownPipelineBuilder pipeline)
	{
		pipeline.DocumentProcessed -= Recognise;
		pipeline.DocumentProcessed += Recognise;
	}

	public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
	{
		if (renderer is not HtmlRenderer html) return;
		if (!html.ObjectRenderers.Contains<HtmlFlexRenderer>())
		{
			html.ObjectRenderers.Insert(0, new HtmlFlexRenderer());
		}

		if (!html.ObjectRenderers.Contains<HtmlFlexItemRenderer>())
		{
			html.ObjectRenderers.Insert(0, new HtmlFlexItemRenderer());
		}
	}

	/// <summary>The container's name, the first word of its fence line (Markdig splits it across Info and Arguments).</summary>
	public static string ContainerName(CustomContainer container) =>
		$"{container.Info} {container.Arguments}".Trim().Split(' ', 2)[0];

	private static bool IsNamed(Block block, string name) =>
		block is CustomContainer container && ContainerName(container).Equals(name, StringComparison.OrdinalIgnoreCase);

	private static void Recognise(MarkdownDocument document)
	{
		// Innermost first: a flex nested in an item is already a FlexBlock when the outer one is read.
		foreach (var container in document.Descendants<CustomContainer>().Where(c => IsNamed(c, FlexName)).Reverse().ToList())
		{
			if (container.Parent is not { } parent) continue;

			var flex = new FlexBlock
			{
				Options = ReadFlex(Properties(container)),
				Line = container.Line,
				Span = container.Span,
				LinesBefore = container.LinesBefore,
				LinesAfter = container.LinesAfter
			};

			var children = container.ToList();
			container.Clear();
			foreach (var child in children)
			{
				flex.Add(AsItem(child));
			}

			var index = parent.IndexOf(container);
			parent.RemoveAt(index);
			parent.Insert(index, flex);
		}
	}

	private static FlexItemBlock AsItem(Block child)
	{
		var item = new FlexItemBlock
		{
			Options = IsNamed(child, ItemName) ? ReadItem(Properties(child)) : FlexItemOptions.Default,
			Line = child.Line,
			Span = child.Span
		};

		if (IsNamed(child, ItemName) && child is CustomContainer container)
		{
			var contents = container.ToList();
			container.Clear();
			foreach (var block in contents)
			{
				item.Add(block);
			}
		}
		else
		{
			item.Add(child);
		}

		return item;
	}

	private static Dictionary<string, string> Properties(Block block) =>
		(block.TryGetAttributes()?.Properties ?? [])
			.Where(p => p.Value is not null)
			.GroupBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
			.ToDictionary(g => g.Key, g => g.First().Value!.Trim(), StringComparer.OrdinalIgnoreCase);

	/// <summary>A <c>flex</c> block's settings from its attributes.</summary>
	public static FlexOptions ReadFlex(IReadOnlyDictionary<string, string> properties)
	{
		var options = FlexOptions.Default;
		return options with
		{
			Direction = properties.GetValueOrDefault("direction")?.ToLowerInvariant() switch
			{
				"column" => FlexDirection.Column,
				"row" => FlexDirection.Row,
				_ => options.Direction
			},
			Gap = Number(properties.GetValueOrDefault("gap"), 0, MaxGap) ?? options.Gap,
			Align = Align(properties.GetValueOrDefault("align")) ?? options.Align,
			Justify = properties.GetValueOrDefault("justify")?.ToLowerInvariant() switch
			{
				"start" => FlexJustify.Start,
				"center" => FlexJustify.Center,
				"end" => FlexJustify.End,
				"between" => FlexJustify.Between,
				_ => options.Justify
			},
			Wrap = properties.GetValueOrDefault("wrap")?.ToLowerInvariant() switch
			{
				"no" => false,
				"yes" => true,
				_ => options.Wrap
			}
		};
	}

	/// <summary>An <c>item</c>'s settings from its attributes.</summary>
	public static FlexItemOptions ReadItem(IReadOnlyDictionary<string, string> properties)
	{
		var options = FlexItemOptions.Default;
		return options with
		{
			Grow = Number(properties.GetValueOrDefault("grow"), 1, MaxGrow) ?? options.Grow,
			Basis = Basis(properties.GetValueOrDefault("basis")),
			Min = Number(properties.GetValueOrDefault("min"), MinColumns, MaxColumns) ?? options.Min,
			Align = Align(properties.GetValueOrDefault("align"))
		};
	}

	private static int? Number(string? value, int min, int max) =>
		int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number >= min && number <= max
			? number
			: null;

	private static FlexBasis? Basis(string? value)
	{
		if (value is null || BasisPattern().Match(value) is not { Success: true } match) return null;
		var number = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
		var percent = match.Groups[2].Length > 0;
		return percent ? number is >= 1 and <= 100 ? new FlexBasis(number, true) : null
			: number is >= MinColumns and <= MaxColumns ? new FlexBasis(number, false) : null;
	}

	private static FlexAlign? Align(string? value) => value?.ToLowerInvariant() switch
	{
		"start" => FlexAlign.Start,
		"center" => FlexAlign.Center,
		"end" => FlexAlign.End,
		"stretch" => FlexAlign.Stretch,
		_ => null
	};
}

/// <summary>Writes a <see cref="FlexBlock"/> as a flexbox <c>div</c> whose settings are classes.</summary>
public sealed class HtmlFlexRenderer : HtmlObjectRenderer<FlexBlock>
{
	protected override void Write(HtmlRenderer renderer, FlexBlock block)
	{
		var options = block.Options;
		renderer.EnsureLine();
		renderer.Write("<div class=\"md-flex");
		renderer.Write(options.Direction == FlexDirection.Column ? " md-column" : " md-row");
		renderer.Write($" md-gap-{options.Gap.ToString(CultureInfo.InvariantCulture)}");
		renderer.Write($" md-align-{options.Align.ToString().ToLowerInvariant()}");
		renderer.Write($" md-justify-{options.Justify.ToString().ToLowerInvariant()}");
		if (!options.Wrap) renderer.Write(" md-nowrap");
		renderer.WriteLine("\">");
		renderer.WriteChildren(block);
		renderer.WriteLine("</div>");
	}
}

/// <summary>
/// Writes a <see cref="FlexItemBlock"/>; its grow, basis and minimum go in CSS custom properties, each a
/// validated number.
/// </summary>
public sealed class HtmlFlexItemRenderer : HtmlObjectRenderer<FlexItemBlock>
{
	protected override void Write(HtmlRenderer renderer, FlexItemBlock block)
	{
		var options = block.Options;
		renderer.EnsureLine();
		renderer.Write("<div class=\"md-item");
		if (options.Basis is not null) renderer.Write(" md-fixed");
		if (options.Align is { } align) renderer.Write($" md-self-{align.ToString().ToLowerInvariant()}");
		renderer.Write($"\" style=\"--md-grow:{options.Grow.ToString(CultureInfo.InvariantCulture)};--md-min:{options.Min.ToString(CultureInfo.InvariantCulture)}");
		if (options.Basis is { } basis) renderer.Write($";--md-basis:{BasisCss(basis, block.Parent as FlexBlock)}");
		renderer.WriteLine("\">");
		renderer.WriteChildren(block);
		renderer.WriteLine("</div>");
	}

	/// <summary>
	/// A basis as CSS. A percentage in a row is of the width left once the gaps are taken out, as the terminal
	/// counts it; a browser places items on a line by their basis before shrinking any, so two 50% items with a
	/// gap between them would otherwise never share one.
	/// </summary>
	private static string BasisCss(FlexBasis basis, FlexBlock? flex)
	{
		var gaps = flex is null ? 0 : flex.Count - 1;
		if (!basis.IsPercent || flex is null || flex.Options.Direction == FlexDirection.Column || gaps <= 0
			|| flex.Options.Gap == 0)
		{
			return basis.Css;
		}

		// md-gap-N is N × 0.5rem.
		var gapRem = (flex.Options.Gap * 0.5m * gaps).ToString("0.##", CultureInfo.InvariantCulture);
		return $"calc((100% - {gapRem}rem) * {basis.Value.ToString(CultureInfo.InvariantCulture)} / 100)";
	}
}

/// <summary><see cref="MarkdownPipelineBuilder"/> extension method for <see cref="MarkdownFlexExtension"/>.</summary>
public static class MarkdownFlexExtensions
{
	/// <summary>Reads <c>flex</c> / <c>item</c> containers as a <see cref="FlexBlock"/> layout.</summary>
	public static MarkdownPipelineBuilder UseFlexLayout(this MarkdownPipelineBuilder pipeline)
	{
		pipeline.Extensions.AddIfNotAlready<MarkdownFlexExtension>();
		return pipeline;
	}
}
