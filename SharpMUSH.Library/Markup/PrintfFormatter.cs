using System.Globalization;
using System.Runtime.InteropServices;
using MarkupString;
using MarkupString.Layout;
using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Library.Markup;

/// <summary>The bounded MUSH printf grammar; text measurement and slicing belong to MarkupString.</summary>
public static class PrintfFormatter
{
	public const int MaxFields = 128;
	public const int MaxFieldMeasure = 65_536;
	public const int MaxFormatCodeUnits = 65_536;
	public const string InvalidFormat = "#-1 INVALID PRINTF FORMAT";
	public const string FieldLimitExceeded = "#-1 PRINTF FIELD LIMIT EXCEEDED";
	public const string ArgumentCountMismatch = "#-1 PRINTF ARGUMENT COUNT MISMATCH";

	private readonly record struct Field(int Start, int End, char Type, int Width, int? Precision,
		bool Left, bool Plus, bool Zero);

	public static bool TryFormat(MarkupText format, IReadOnlyList<MarkupText> values,
		out MarkupText result, out string? error)
	{
		result = MarkupText.Empty;
		if (!TryParse(format.Text, out var fields, out error)) return false;
		if (fields.Count(field => field.Type != '%') != values.Count)
		{
			error = ArgumentCountMismatch;
			return false;
		}

		var parts = new List<MarkupText>();
		var length = 0L;
		var position = 0;
		var argument = 0;
		foreach (var field in fields)
		{
			if (!Append(format.Substring(position, field.Start - position))) return TooLarge(out error);
			MarkupText value;
			if (field.Type == '%') value = format.Substring(field.Start, 1);
			else
			{
				var source = values[argument++];
				if (field.Type == 's')
					value = field.Precision is { } cells ? source.TruncateToWidth(cells, CutFrom.End) : source;
				else
				{
					// .NET numeric parsing accepts trailing NULs; the printf grammar does not.
					if (source.Text.Contains('\0')) { error = ErrorMessages.Returns.Numbers; return false; }
					string number;
					if (field.Type == 'd')
					{
						if (!long.TryParse(source.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer))
						{ error = ErrorMessages.Returns.Numbers; return false; }
						number = integer.ToString("D" + (field.Precision ?? 1).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
					}
					else
					{
						const NumberStyles numeric = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
						if (!decimal.TryParse(source.Text, numeric, CultureInfo.InvariantCulture, out var fraction))
						{ error = ErrorMessages.Returns.Numbers; return false; }
						var precision = field.Precision ?? 6;
						number = decimal.Round(fraction, precision, MidpointRounding.ToEven)
							.ToString("F" + precision.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
					}
					if (field.Plus && !number.StartsWith('-')) number = "+" + number;
					if (field.Zero && !field.Left && field.Width > number.Length)
					{
						var sign = number[0] is '+' or '-' ? 1 : 0;
						number = number.Insert(sign, new string('0', field.Width - number.Length));
					}
					value = ApplyMarkupAt(source, 0, MarkupText.Plain(number));
				}
				var padding = Math.Max(0, field.Width - value.DisplayWidth);
				if (length + value.Length + padding > FunctionLimits.MaxOutputCodeUnits) return TooLarge(out error);
				if (padding > 0)
				{
					var fill = MarkupText.Plain(new string(' ', padding));
					value = field.Left ? MarkupText.Concat(value, fill) : MarkupText.Concat(fill, value);
				}
				value = ApplyMarkupAt(format, field.Start, value);
			}
			if (!Append(value)) return TooLarge(out error);
			position = field.End;
		}
		if (!Append(format.Substring(position))) return TooLarge(out error);
		result = MarkupText.Concat(CollectionsMarshal.AsSpan(parts));
		error = null;
		return true;

		bool Append(MarkupText part)
		{
			length += part.Length;
			if (length > FunctionLimits.MaxOutputCodeUnits) return false;
			parts.Add(part);
			return true;
		}
	}

	private static bool TooLarge(out string? error)
	{
		error = ErrorMessages.Returns.OutputTooLarge;
		return false;
	}

	private static MarkupText ApplyMarkupAt(MarkupText source, int index, MarkupText value)
		=> source.Substring(index, 1).Runs
			.SelectMany(run => run.Markups)
			.Aggregate(value, (wrapped, markup) => MarkupText.Wrap(markup, wrapped));

	private static bool TryParse(string format, out List<Field> fields, out string? error)
	{
		fields = [];
		error = InvalidFormat;
		if (format.Length > MaxFormatCodeUnits) { error = FieldLimitExceeded; return false; }
		var position = 0;
		var conversions = 0;
		while (position < format.Length)
		{
			var start = format.IndexOf('%', position);
			if (start < 0) break;
			position = start + 1;
			if (position == format.Length || !Graphemes.IsBoundary(format, start)) return false;
			if (fields.Count == 1024) { error = FieldLimitExceeded; return false; }
			if (format[position] == '%')
			{
				position++;
				if (!Graphemes.IsBoundary(format, position)) return false;
				fields.Add(new Field(start, position, '%', 0, null, false, false, false));
				continue;
			}
			if (++conversions > MaxFields) { error = FieldLimitExceeded; return false; }
			var left = false;
			var plus = false;
			var zero = false;
			while (position < format.Length && format[position] is '-' or '+' or '0')
			{
				switch (format[position++])
				{
					case '-': if (left) return false; left = true; break;
					case '+': if (plus) return false; plus = true; break;
					case '0': if (zero) return false; zero = true; break;
				}
			}
			if (!ReadMeasure(format, ref position, out var width)) { error = FieldLimitExceeded; return false; }
			int? precision = null;
			if (position < format.Length && format[position] == '.')
			{
				position++;
				if (position == format.Length || !char.IsAsciiDigit(format[position])) return false;
				if (!ReadMeasure(format, ref position, out var digits)) { error = FieldLimitExceeded; return false; }
				precision = digits;
			}
			if (position == format.Length) return false;
			var type = format[position++];
			if (type is not ('s' or 'd' or 'f') || !Graphemes.IsBoundary(format, position)) return false;
			if (type == 's' && (plus || zero)) return false;
			if (type == 'f' && precision > 28) { error = FieldLimitExceeded; return false; }
			fields.Add(new Field(start, position, type, width, precision, left, plus, zero));
		}
		error = null;
		return true;
	}

	private static bool ReadMeasure(string text, ref int position, out int value)
	{
		value = 0;
		while (position < text.Length && char.IsAsciiDigit(text[position]))
		{
			var digit = text[position++] - '0';
			if (value > (MaxFieldMeasure - digit) / 10) return false;
			value = value * 10 + digit;
		}
		return true;
	}
}
