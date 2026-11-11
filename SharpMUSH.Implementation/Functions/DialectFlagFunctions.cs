using System.Text;
using SharpMUSH.Library;
using SharpMUSH.Library.Common;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Softcode;

namespace SharpMUSH.Implementation.Functions;

/// <summary>
/// Flag and power names and flag letters as the dialect in force spells them (<see cref="SoftcodeDialect.FlagNames"/>,
/// <see cref="SoftcodeDialect.FlagLetters"/>, <see cref="SoftcodeDialect.PowerScopes"/>).
/// </summary>
public partial class Functions
{
	/// <summary>
	/// Whether <paramref name="obj"/> has what one of a dialect's flag names or letters asks, as <paramref name="viewer"/>
	/// is told. Only God learns about an attribute that way, as TinyMUX keeps its HAS_* flags to God.
	/// </summary>
	private async ValueTask<bool> DialectHasAsync(AnySharpObject viewer, AnySharpObject obj, DialectFlag flag,
		ObjectFlagSet? read = null)
		=> flag switch
		{
			{ Flag: "CONNECTED" } => await ConnectionService.IsOnline(obj),
			{ Flag: { } name } => (read ?? await obj.ReadFlagsAsync()).Has(name) != flag.Inverted,
			{ Scope: { } scope } => (read ?? await obj.ReadFlagsAsync()).Grants.Shows(scope),
			{ Attribute: { } attribute } => viewer.IsGod() && await Mediator
				.CreateStream(new GetAttributeQuery(obj.Object().DBRef, [attribute])).LastOrDefaultAsync() is not null,
			_ => false
		};

	/// <summary>Whether <paramref name="obj"/> holds what one of the dialect's power names means, or null when it is not one.</summary>
	private static async ValueTask<bool?> DialectHasPowerAsync(AnySharpObject obj, string name)
		=> SoftcodeDialectScope.Current?.PowerScopeFor(name) is { } scope
			? (await obj.ReadFlagsAsync()).Grants.Shows(scope)
			: null;

	/// <summary>
	/// <c>flags()</c> in a dialect with letters of its own: its type letter, then its letter for each flag the viewer
	/// sees, in its order. A flag the dialect has no letter for is left out.
	/// </summary>
	private async ValueTask<string> DialectFlagLettersAsync(SoftcodeDialect dialect, AnySharpObject viewer, AnySharpObject obj,
		FlagView view)
	{
		var read = await obj.ReadFlagsAsync();
		var visible = (await MessageFormatting.VisibleFlagsAsync(obj.Object(), view, read))
			.Select(flag => flag.Name)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		var letters = new StringBuilder();
		if (dialect.TypeLetters.TryGetValue(obj.Object().Type, out var typeLetter))
		{
			letters.Append(typeLetter);
		}

		foreach (var (letter, flag) in dialect.FlagLetters)
		{
			var shown = flag switch
			{
				{ Flag: { } name } => visible.Contains(name),
				_ => await DialectHasAsync(viewer, obj, flag, read)
			};
			if (shown)
			{
				// TinyMUX's decode_flags: with no type letter, a colon keeps a leading digit from reading as part of a dbref.
				if (letters.Length == 0 && char.IsAsciiDigit(letter))
				{
					letters.Append(':');
				}

				letters.Append(letter);
			}
		}

		return letters.ToString();
	}

	/// <summary>
	/// <c>andflags()</c> and <c>orflags()</c> in a dialect with letters of its own: its type letters, then its flag
	/// letters. A letter naming nothing fails an AND and is passed over by an OR, negated or not, and a <c>!</c> with no
	/// letter after it is false, as TinyMUX's handle_flaglists has them.
	/// </summary>
	private async ValueTask<bool?> DialectFlagLetterCheck(SoftcodeDialect dialect, AnySharpObject viewer, AnySharpObject obj,
		string letters, bool orMode)
	{
		ObjectFlagSet? read = null;
		// An OR reads on after a match, so a stray ! later still fails it.
		var matched = false;
		for (var i = 0; i < letters.Length; i++)
		{
			var negate = letters[i] == '!';
			if (negate && ++i >= letters.Length)
			{
				return false;
			}

			var letter = letters[i];
			bool has;
			if (dialect.TypeLetters.FirstOrDefault(type => type.Value == letter) is { Key: { } type })
			{
				has = obj.Object().Type.Equals(type, StringComparison.OrdinalIgnoreCase);
			}
			else if (dialect.FlagLetters.FirstOrDefault(entry => entry.Key == letter) is { Value: { } flag })
			{
				read ??= await obj.ReadFlagsAsync();
				has = await DialectHasAsync(viewer, obj, flag, read);
			}
			else if (orMode)
			{
				continue;
			}
			else
			{
				return false;
			}

			if (has == negate && !orMode)
			{
				return false;
			}

			matched |= has != negate;
		}

		return !orMode || matched;
	}
}
