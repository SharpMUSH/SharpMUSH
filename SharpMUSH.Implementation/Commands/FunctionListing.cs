using System.Collections.Immutable;
using System.Globalization;
using MarkupString;
using MarkupString.Layout;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Commands;

/// <summary>
/// What <c>@function</c> shows: the listing (PennMUSH's <c>do_function</c> with no name, <c>src/function.c</c>)
/// and one function's report (<c>build_function_report</c>), each a <see cref="ServerLayout.Panel"/>.
/// </summary>
public partial class Commands
{
	/// <summary>
	/// The listing's columns. A narrow client loses where each function lives first, then its argument counts;
	/// the name (never wrapped) and its state always stay, and the state fills what is left of the width.
	/// </summary>
	private static readonly ImmutableArray<TableColumn> FunctionColumns =
	[
		new TableColumn(MarkupText.Plain("Function")) { Wrap = false },
		new TableColumn(MarkupText.Plain("Location")) { Wrap = false, Priority = 3 },
		new TableColumn(MarkupText.Plain("Args")) { Wrap = false, Priority = 2 },
		new TableColumn(MarkupText.Plain("State")) { Min = 12, Grow = 1 },
	];

	/// <summary>
	/// Whoever may define global functions sees where each one lives, as PennMUSH shows its table only to a
	/// player with Global_Funcs; anyone else gets the names.
	/// </summary>
	private async ValueTask<bool> SeesFunctionLocations(AnySharpObject executor) =>
		await executor.HasPower("FUNCTIONS") || await executor.Can(PortalPermission.ConfigAdmin);

	/// <summary>
	/// <paramref name="functions"/> in a panel titled <paramref name="title"/>, sorted by name: a table with
	/// <paramref name="detailed"/>, a grid of names without, and <paramref name="summary"/> under a rule.
	/// </summary>
	private MarkupText FunctionListing(string title, IReadOnlyCollection<UserDefinedFunction> functions,
		Func<string, UserDefinedFunction?> lookup, bool detailed, string empty, string summary)
	{
		var sorted = functions.OrderBy(fn => fn.Name, StringComparer.OrdinalIgnoreCase).ToArray();
		Block body = sorted.Length switch
		{
			0 => new TextBlock(MarkupText.Plain(empty)),
			_ when detailed => ServerLayout.Listing(FunctionColumns, sorted.Select(fn => FunctionRow(fn, lookup))),
			_ => new Grid([.. sorted.Select(fn => MarkupText.Plain(fn.Name))]) { Gap = 2 },
		};
		var footer = new TextBlock(ToneMarkup.Build(ThemeRole.Muted, MarkupText.Plain(summary), ToneMarkup.Standard(ThemeRole.Muted)));
		return ServerLayout.Build(ServerLayout.Panel(MarkupText.Plain(title), body, new Rule(), footer), 78, HouseLook.Look);
	}

	private static ImmutableArray<Block> FunctionRow(UserDefinedFunction fn, Func<string, UserDefinedFunction?> lookup) =>
	[
		new TextBlock(MarkupText.Plain(FunctionSignature(fn, lookup))),
		new TextBlock(MarkupText.Plain(FunctionLocation(fn))),
		new TextBlock(MarkupText.Plain(fn.MinArgs == fn.MaxArgs
			? fn.MinArgs.ToString(CultureInfo.InvariantCulture)
			: $"{fn.MinArgs}-{fn.MaxArgs}")),
		new TextBlock(MarkupText.Join(MarkupText.Plain(" · "), FunctionState(fn))),
	];

	/// <summary>
	/// The name, and the names set with <c>@function/args</c> as a call would read: <c>line(title, width)</c>.
	/// An alias takes its target's.
	/// </summary>
	private static string FunctionSignature(UserDefinedFunction fn, Func<string, UserDefinedFunction?> lookup)
	{
		var names = fn.AliasOf is { } target ? lookup(target)?.ArgumentNames ?? [] : fn.ArgumentNames;
		return names.Length == 0 ? fn.Name : $"{fn.Name}({string.Join(", ", names)})";
	}

	/// <summary>PennMUSH's <c>#&lt;dbref&gt;/&lt;attribute&gt;</c>.</summary>
	private static string FunctionLocation(UserDefinedFunction fn) => $"#{fn.Object.Number}/{fn.Attribute}";

	/// <summary>Enabled or disabled in the reader's theme colour for it, then what else sets it apart.</summary>
	private static IEnumerable<MarkupText> FunctionState(UserDefinedFunction fn)
	{
		yield return FunctionEnabledLabel(fn.Enabled);
		if (fn.AliasOf is { } target) yield return MarkupText.Plain($"alias of {target}");
		if (fn.Preserved) yield return MarkupText.Plain("preserved");
		if (!string.IsNullOrWhiteSpace(fn.Restriction)) yield return MarkupText.Plain($"restricted: {fn.Restriction}");
	}

	private static MarkupText FunctionEnabledLabel(bool enabled)
	{
		var role = enabled ? ThemeRole.Success : ThemeRole.Warning;
		return ToneMarkup.Build(role, MarkupText.Plain(enabled ? "enabled" : "disabled"), ToneMarkup.Standard(role));
	}

	/// <summary>PennMUSH's argument count: <c>2</c>, <c>0 to 2</c>, or <c>at least 1</c>.</summary>
	private static string FunctionArity(int min, int max) =>
		min == max ? min.ToString(CultureInfo.InvariantCulture)
		: max == int.MaxValue ? $"at least {min}"
		: $"{min} to {max}";

	/// <summary><c>@function &lt;name&gt;</c> for a user-defined function.</summary>
	private MarkupText UserFunctionReport(UserDefinedFunction fn, Func<string, UserDefinedFunction?> lookup, bool showLocation)
	{
		var names = fn.AliasOf is { } target ? lookup(target)?.ArgumentNames ?? [] : fn.ArgumentNames;
		(string, MarkupText)[] fields =
		[
			("Type", MarkupText.Plain(fn.Owner is null ? "@function" : "@function/local")),
			("State", MarkupText.Join(MarkupText.Plain(" · "), FunctionState(fn))),
			.. showLocation ? new[] { ("Location", MarkupText.Plain(FunctionLocation(fn))) } : [],
			("Arguments", MarkupText.Plain(FunctionArity(fn.MinArgs, fn.MaxArgs))),
			.. names.Length == 0 ? [] : new[] { ("Named", MarkupText.Plain(string.Join(", ", names))) },
		];
		return ServerLayout.Build(ServerLayout.Panel(MarkupText.Plain($"{fn.Name}()"), ServerLayout.KeyValues(fields)), 78, HouseLook.Look);
	}

	/// <summary><c>@function &lt;name&gt;</c> for a built-in.</summary>
	private MarkupText BuiltinFunctionReport(SharpFunctionAttribute attr, bool isSystem, string? restriction)
	{
		var flags = Enum.GetValues<FunctionFlags>()
			.Where(flag => flag != FunctionFlags.Regular && flag != FunctionFlags.Arg_Mask && attr.Flags.HasFlag(flag))
			.Select(flag => flag.ToString()).DefaultIfEmpty(nameof(FunctionFlags.Regular));
		var restrictions = attr.Restrict.Append(restriction ?? string.Empty).Where(word => !string.IsNullOrWhiteSpace(word)).ToArray();
		(string, MarkupText)[] fields =
		[
			("Type", MarkupText.Plain(isSystem ? "built-in" : "@function")),
			("Arguments", MarkupText.Plain(FunctionArity(attr.MinArgs, attr.MaxArgs))),
			("Flags", MarkupText.Plain(string.Join(" · ", flags))),
			.. restrictions.Length == 0 ? [] : new[] { ("Restricted", MarkupText.Plain(string.Join(" · ", restrictions))) },
		];
		return ServerLayout.Build(ServerLayout.Panel(MarkupText.Plain($"{attr.Name.ToLowerInvariant()}()"), ServerLayout.KeyValues(fields)), 78, HouseLook.Look);
	}
}
