using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using Range = SharpMUSH.Library.Models.Range;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Where the softcode in an attribute value actually starts.
/// <para>
/// An attribute whose value begins <c>$pattern:</c> or <c>^pattern:</c> is <b>not</b> evaluated in
/// its entirety. <c>CommandAttributeScanner</c> slices the <c>$</c> prefix off with
/// <see cref="CommandDiscoveryService.CommandPatternRegex"/>, compiles the span before the <c>:</c>
/// as a wildcard or a regex, and hands only the remainder to <c>CommandListParse</c> (via
/// <c>SharpAttribute.CommandListIndex</c>); <c>GetListenAttributesQueryHandler</c> does the same with
/// <see cref="CommandDiscoveryService.ListenPatternRegex"/>. So the pattern half is match data —
/// structural to a lexer, inert to the evaluator, exactly like a <c>lit()</c> body — and neither the
/// layout engine nor the validator may treat it as code.
/// </para>
/// <para>
/// <b>Why this is re-derived from the text rather than read from <c>SharpAttribute.CommandListIndex</c>.</b>
/// That field is populated only on the copies <c>CommandAttributeScanner</c> puts in the
/// command-attribute cache (<c>attr with { CommandListIndex = … }</c>). Every attribute that comes
/// straight off the database carries <c>null</c> there — all three providers pass <c>null</c> when
/// they materialise a <see cref="SharpAttribute"/> — and <c>@examine</c>, <c>@grep/PRINT</c> and
/// <c>AttributeService</c>'s set-time validation all read attributes that way. Plumbing the field
/// through would therefore have to populate it first, in the database layer, for a value only the
/// display path wants. Re-matching with the very regexes the scanner and the listen handler use keeps
/// one definition of the split without that.
/// </para>
/// </summary>
public static class SoftcodeSource
{
	/// <summary>
	/// The number of leading characters of <paramref name="source"/> that are match data rather than
	/// code, or <c>0</c> when the value has no <c>$</c>/<c>^</c> pattern prefix.
	/// <para>
	/// Gated on <see cref="ParseType.CommandList"/> — the dialect a <c>cmdsyntax</c> attribute declares
	/// and the only one in which a leading <c>$</c>/<c>^</c> means "the engine will split this". A
	/// <c>funsyntax</c> attribute is evaluated whole by <c>u()</c> and friends, so nothing there is
	/// inert.
	/// </para>
	/// <para>
	/// The returned offset runs past any spaces after the <c>:</c>, mirroring
	/// <c>CommandAttributeScanner</c>'s own <c>commandBodyStart</c> — which skips them so that
	/// <c>$cmd: @pemit</c> and <c>$cmd:@pemit</c> behave alike. Protecting that run costs nothing and
	/// keeps a break out of whitespace the command dispatcher discards.
	/// </para>
	/// </summary>
	public static int MatchPatternPrefixLength(string source, ParseType parseType)
	{
		if (parseType != ParseType.CommandList || source.Length == 0)
		{
			return 0;
		}

		// Both regexes are anchored at the start of the value and keyed to its first character, so at
		// most one can apply and neither costs anything on an ordinary attribute.
		var match = source[0] switch
		{
			'$' => CommandDiscoveryService.CommandPatternRegex().Match(source),
			'^' => CommandDiscoveryService.ListenPatternRegex().Match(source),
			_ => null
		};

		if (match is not { Success: true })
		{
			return 0;
		}

		var offset = match.Length;
		while (offset < source.Length && source[offset] == ' ')
		{
			offset++;
		}

		return offset;
	}

	/// <summary>
	/// Validates only the part of <paramref name="source"/> the evaluator will parse, then reports the
	/// errors in the whole value's coordinates.
	/// <para>
	/// Validating the pattern half as if it were softcode produces a <c>#-1 PARSER FAILURE</c> for an
	/// attribute that works perfectly — in <c>$give [a,b} to *:@pemit %#=ok</c> the <c>[</c> opens a
	/// bracket group the <c>}</c> never closes, but to the command scanner both are just characters the
	/// pattern matches. Slicing first would move every
	/// reported position, so each error is shifted back by the prefix and re-pointed at the full text:
	/// a caller painting error spans over the original value (<c>SoftcodeFormatter</c>) and a caller
	/// printing <see cref="ParseError.ToMushFailureString"/> (<c>AttributeService</c>) both need
	/// offsets in the value the user actually typed.
	/// </para>
	/// <para>
	/// When there is no prefix this is <see cref="IMUSHCodeParser.ValidateAndGetErrors"/> verbatim,
	/// list and all — the regression contract for every attribute that is not a <c>$</c>-command.
	/// </para>
	/// </summary>
	public static IReadOnlyList<ParseError> Validate(IMUSHCodeParser parser, MString source, ParseType parseType)
	{
		var plain = MModule.plainText(source);
		var offset = MatchPatternPrefixLength(plain, parseType);

		if (offset == 0)
		{
			return parser.ValidateAndGetErrors(source, parseType);
		}

		var errors = parser.ValidateAndGetErrors(
			MModule.substring(offset, plain.Length - offset, source), parseType);

		if (errors.Count == 0)
		{
			return errors;
		}

		var prefix = plain.AsSpan(0, offset);
		var lastNewline = prefix.LastIndexOf('\n');
		var lineShift = prefix.Count('\n');
		// Only the code's first line shares a line with the prefix, and only by however much of the
		// prefix follows its last newline.
		var columnShift = offset - (lastNewline + 1);

		return [.. errors.Select(error => Shift(error, lineShift, columnShift, plain))];
	}

	/// <summary>
	/// Moves one error from the sliced code's coordinates back into the full value's.
	/// <see cref="ParseError.Line"/> is 1-based and <see cref="Range"/>'s lines are 0-based, so "the
	/// first line of the code" is <c>Line == 1</c> in the former and <c>Line == 0</c> in the latter;
	/// only that line takes the column shift.
	/// </summary>
	private static ParseError Shift(ParseError error, int lineShift, int columnShift, string fullInput) =>
		error with
		{
			Line = error.Line + lineShift,
			Column = error.Line == 1 ? error.Column + columnShift : error.Column,
			Range = error.Range is null
				? null
				: new Range(
					ShiftPosition(error.Range.Start, lineShift, columnShift),
					ShiftPosition(error.Range.End, lineShift, columnShift)),
			InputText = fullInput
		};

	private static Position ShiftPosition(Position position, int lineShift, int columnShift) =>
		new(position.Line + lineShift, position.Line == 0 ? position.Character + columnShift : position.Character);
}
