using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Library.Services;

/// <summary>
/// How an <c>@function/restrict</c> restriction applies to a function. PennMUSH's <c>apply_restrictions</c>
/// (<c>src/function.c</c>) sets each word's bit on the function's own flags, and a leading <c>!</c> clears it,
/// so a restriction can loosen a built-in as well as tighten it. Here the words apply in order onto what the
/// function declares: its <see cref="FunctionFlags"/> and its <see cref="SharpFunctionAttribute.Restrict"/>
/// grants, the roles, powers, flags and permissions any one of which lets a caller use it. A <c>!</c> on a grant
/// is one the caller must not hold, as a <c>!</c> lock key is.
/// </summary>
public static class FunctionRestrictionFlags
{
	/// <summary>Penn's FN_NOSIDEFX: a side-effect function answers <c>#-1 FUNCTION DISABLED</c>.</summary>
	public const string NoSideEffects = "nosidefx";

	/// <summary>Empties the function's grants, so being able to call it needs none.</summary>
	public const string Everyone = "everyone";

	/// <summary>The words that change how a function runs rather than who may call it.</summary>
	private static readonly Dictionary<string, FunctionFlags> Words = new(StringComparer.OrdinalIgnoreCase)
	{
		["logname"] = FunctionFlags.LogName,
		["logargs"] = FunctionFlags.LogArgs,
		["deprecated"] = FunctionFlags.Deprecated,
		["localize"] = FunctionFlags.Localize,
		["noparse"] = FunctionFlags.NoParse
	};

	/// <summary>The words <c>check_func</c> tests: each a bit of the function's own flags.</summary>
	private static readonly Dictionary<string, FunctionFlags> GateWords = new(StringComparer.OrdinalIgnoreCase)
	{
		["nobody"] = FunctionFlags.Disabled,
		["noguest"] = FunctionFlags.NoGuest,
		["nogagged"] = FunctionFlags.NoGagged,
		["nofixed"] = FunctionFlags.NoFixed,
		["admin"] = FunctionFlags.AdminOnly,
		["wizard"] = FunctionFlags.WizardOnly,
		["god"] = FunctionFlags.GodOnly
	};

	private static readonly string[] GrantPrefixes = ["role^", "power^", "flag^", "perm^"];

	/// <summary>Whether <paramref name="word"/>, without a leading <c>!</c>, is one of the words that change how a function runs.</summary>
	public static bool IsFlagWord(string word)
		=> Words.ContainsKey(word) || word.Equals(NoSideEffects, StringComparison.OrdinalIgnoreCase);

	/// <summary>Whether <paramref name="word"/>, without a leading <c>!</c>, is one of the words <c>check_func</c> tests.</summary>
	public static bool IsGateWord(string word) => GateWords.ContainsKey(word);

	/// <summary>
	/// Whether <paramref name="word"/>, without a leading <c>!</c>, names a grant: <c>role^&lt;role&gt;</c>,
	/// <c>power^&lt;power&gt;</c>, <c>flag^&lt;flag&gt;</c>, <c>perm^&lt;permission&gt;</c> or a bare dotted permission,
	/// <c>royalty</c>, or <see cref="Everyone"/>.
	/// </summary>
	public static bool IsGrantWord(string word)
		=> word.Equals(Everyone, StringComparison.OrdinalIgnoreCase) || GrantOf(word) is not null;

	/// <summary>
	/// <paramref name="flags"/> with the words of <paramref name="restriction"/> applied in order. PennMUSH
	/// logs only a built-in's calls (<c>src/parse.c:3025-3041</c>), and <c>noparse</c> is for @functions
	/// alone here, so a built-in keeps its own way of reading its arguments.
	/// </summary>
	public static FunctionFlags Apply(FunctionFlags flags, string? restriction, bool userFunction)
	{
		// Asked on every function call, and nearly every function has no restriction.
		if (string.IsNullOrEmpty(restriction)) return flags;

		foreach (var (word, clear) in Terms(restriction))
		{
			if (GateWords.TryGetValue(word, out var gate))
			{
				flags = clear ? flags & ~gate : flags | gate;
				continue;
			}

			if (!Words.TryGetValue(word, out var flag)
				|| (userFunction ? flag is FunctionFlags.LogName or FunctionFlags.LogArgs : flag is FunctionFlags.NoParse))
			{
				continue;
			}

			flags = clear ? flags & ~flag : flags | flag;
		}

		return flags;
	}

	/// <summary>
	/// <paramref name="declared"/> grants with the grant words of <paramref name="restriction"/> applied in order. The
	/// first grant named replaces the declared ones and later ones add to it; a <c>!</c> grant is one the caller must
	/// not hold, kept as <c>!&lt;grant&gt;</c>; <see cref="Everyone"/> takes them all away. A caller may use the function
	/// when it holds none of the <c>!</c> grants and, when any others are left, one of them.
	/// </summary>
	public static string[] Grants(IEnumerable<string> declared, string? restriction)
	{
		var grants = declared.SelectMany(entry => entry.Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries))
			.Select(term => term.StartsWith('!') ? "!" + (GrantOf(term[1..]) ?? term[1..]) : GrantOf(term) ?? term)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
		if (string.IsNullOrEmpty(restriction)) return [.. grants];

		var replaced = false;
		foreach (var (word, clear) in Terms(restriction))
		{
			if (word.Equals(Everyone, StringComparison.OrdinalIgnoreCase))
			{
				if (!clear)
				{
					grants.Clear();
					replaced = true;
				}

				continue;
			}

			if (GrantOf(word) is not { } grant) continue;

			var (added, opposite) = clear ? ("!" + grant, grant) : (grant, "!" + grant);
			grants.RemoveAll(held => held.Equals(opposite, StringComparison.OrdinalIgnoreCase));
			if (!clear && !replaced)
			{
				grants.RemoveAll(held => !held.StartsWith('!'));
				replaced = true;
			}

			if (!grants.Contains(added, StringComparer.OrdinalIgnoreCase)) grants.Add(added);
		}

		return [.. grants];
	}

	/// <summary>
	/// <paramref name="attribute"/> as <paramref name="restriction"/> leaves it: its flags (<see cref="Apply"/>) and its
	/// grants (<see cref="Grants"/>). The same instance when there is no restriction.
	/// </summary>
	public static SharpFunctionAttribute Restricted(SharpFunctionAttribute attribute, string? restriction, bool userFunction)
	{
		if (string.IsNullOrWhiteSpace(restriction)) return attribute;

		// Asked on every call of a restricted built-in: the last answer for this declaration is kept beside it.
		var cached = RestrictedCache.GetOrCreateValue(attribute);
		if (cached.Value is { } hit && hit.UserFunction == userFunction && ReferenceEquals(hit.Restriction, restriction)) return hit.Result;

		var result = Compute(attribute, restriction, userFunction);
		cached.Value = new RestrictedEntry(restriction, userFunction, result);
		return result;
	}

	private sealed record RestrictedEntry(string Restriction, bool UserFunction, SharpFunctionAttribute Result);

	private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<SharpFunctionAttribute,
		System.Runtime.CompilerServices.StrongBox<RestrictedEntry?>> RestrictedCache = new();

	private static SharpFunctionAttribute Compute(SharpFunctionAttribute attribute, string restriction, bool userFunction)
	{
		var flags = Apply(attribute.Flags, restriction, userFunction);
		var grants = Grants(attribute.Restrict, restriction);
		return flags == attribute.Flags && grants.SequenceEqual(attribute.Restrict, StringComparer.OrdinalIgnoreCase)
			? attribute
			: attribute.WithRestriction(flags, grants);
	}

	/// <summary>Whether <paramref name="restriction"/> leaves <c>nosidefx</c> set.</summary>
	public static bool ForbidsSideEffects(string? restriction)
		=> !string.IsNullOrEmpty(restriction) && Terms(restriction)
			.Where(term => term.Word.Equals(NoSideEffects, StringComparison.OrdinalIgnoreCase))
			.Select(term => !term.Clear)
			.LastOrDefault();

	/// <summary>
	/// A grant word as it is held in <see cref="SharpFunctionAttribute.Restrict"/>: a permission in lower case
	/// (<c>perm^</c> dropped), <c>royalty</c> as <see cref="PortalPermission.GameRoyalty"/>, and the other prefixes
	/// in lower case with the name after them as written. Null for any other word.
	/// </summary>
	private static string? GrantOf(string word)
	{
		if (word.Equals("royalty", StringComparison.OrdinalIgnoreCase)) return PortalPermission.GameRoyalty;

		if (Array.Find(GrantPrefixes, prefix => word.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) is { } named)
		{
			var name = word[named.Length..];
			return name.Length == 0 ? null : named == "perm^" ? name.ToLowerInvariant() : named + name;
		}

		return word.Contains('.') && word.IndexOf('^') < 0 ? word.ToLowerInvariant() : null;
	}

	private static IEnumerable<(string Word, bool Clear)> Terms(string? restriction)
		=> (restriction ?? string.Empty)
			.Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries)
			.Select(term => term.StartsWith('!') ? (term[1..], true) : (term, false));
}
