using System.Drawing;
using System.Text;
using System.Text.Json;
using ANSILibrary;
using MarkupString;
using Serilog;
using SharpMUSH.Tests.Markup.Data;
using A = MarkupString.MarkupStringModule;
using M = MarkupString.MarkupImplementation.AnsiMarkup;
using StringExtensions = ANSILibrary.StringExtensions;

namespace SharpMUSH.Tests.Markup;

public class AnsiStringUnitTests
{
	[Test]
	[MethodDataSource(typeof(Concat), nameof(Data.Concat.ConcatData))]
	public async Task Concat(ConcatTestData data)
	{
		var (strA, strB, expected) = data;
		var result = A.concat(strA, strB);

		Log.Logger.Information("Result: {Result}{NewLine}Expected: {Expected}", result, Environment.NewLine, expected);
		var resultBytes = Encoding.Unicode.GetBytes(result.ToString());
		var expectedBytes = Encoding.Unicode.GetBytes(expected.ToString());

		foreach (var (first, second) in resultBytes.Zip(expectedBytes))
		{
			await Assert
				.That(first)
				.IsEqualTo(second);
		}
	}
	
	[Test]
	[MethodDataSource(typeof(ConcatAttach), nameof(Data.ConcatAttach.ConcatAttachData))]
	public async Task ConcatAttach(ConcatTestData data)
	{
		var (strA, strB, expected) = data;
		var result = A.concatAttach(strA, strB);

		Log.Logger.Information("Result: {Result}{NewLine}Expected: {Expected}", result, Environment.NewLine, expected);
		var resultBytes = Encoding.Unicode.GetBytes(result.ToString());
		var expectedBytes = Encoding.Unicode.GetBytes(expected.ToString());

		foreach (var (first, second) in resultBytes.Zip(expectedBytes))
		{
			await Assert
				.That(first)
				.IsEqualTo(second);
		}
	}

	[Test]
	[MethodDataSource(typeof(Substring), nameof(Data.Substring.SubstringData))]
	public async Task Substring(SubstringTestData2 data)
	{
		var (str, start, expected) = data;
		var result = A.substring(start, A.getLength(str) - start, str);

		Log.Logger.Information("Result: {Result}{NewLine}Expected: {Expected}", result, Environment.NewLine, expected);

		var resultBytes = Encoding.Unicode.GetBytes(result.ToString());
		var expectedBytes = Encoding.Unicode.GetBytes(expected.ToString());

		foreach (var (resultByte, expectedByte) in resultBytes.Zip(expectedBytes))
		{
			await Assert
				.That(resultByte)
				.IsEqualTo(expectedByte);
		}
	}

	[Test]
	[MethodDataSource(typeof(Pad), nameof(Data.Pad.PadData))]
	public async Task Pad(
		PadTestData data)
	{
		var (input, padStr, width, padType, truncType, expected) = data;

		// Call the Pad method
		var result = MModule.pad(input, padStr, width, padType, truncType);

		Log.Logger.Information("Result: {Result}{NewLine}Expected: {Expected}", result.ToString(), Environment.NewLine,
			expected.ToString());

		// Convert result and expected to bytes for detailed verification
		var resultBytes = Encoding.Unicode.GetBytes(result.ToString());
		var expectedBytes = Encoding.Unicode.GetBytes(expected.ToString());

		foreach (var (resultByte, expectedByte) in resultBytes.Zip(expectedBytes))
		{
			await Assert.That(resultByte).IsEqualTo(expectedByte);
		}
	}

	[Test]
	[MethodDataSource(typeof(InsertAt), nameof(Data.InsertAt.InsertAtData))]
	public async Task InsertAt(InsertAtTestData data)
	{
		var (str, index, insert, expected) = data;
		var result = A.insertAt(str, insert, index);

		Log.Logger.Information("Result: {Result}{NewLine}Expected: {Expected}", result, Environment.NewLine, expected);

		var resultBytes = Encoding.Unicode.GetBytes(result.ToString());
		var expectedBytes = Encoding.Unicode.GetBytes(expected.ToString());
		

		foreach (var (first, second) in resultBytes.Zip(expectedBytes))
		{
			await Assert
				.That(first)
				.IsEqualTo(second);
		}
	}

	[Test]
	[MethodDataSource(typeof(Substring), nameof(Data.Substring.SubstringLengthData))]
	public async Task SubstringLength(SubstringTestData data)
	{
		var (str, length, expected) = data;
		var result = A.substring(0, length, str);

		Log.Logger.Information("Result: {Result}{NewLine}Expected: {Expected}", result, Environment.NewLine, expected);

		var resultBytes = Encoding.Unicode.GetBytes(result.ToString());
		var expectedBytes = Encoding.Unicode.GetBytes(expected.ToString());

		foreach (var (first, second) in resultBytes.Zip(expectedBytes))
		{
			await Assert
				.That(first)
				.IsEqualTo(second);
		}
	}

	[Test]
	[MethodDataSource(typeof(Split), nameof(Data.Split.SplitData))]
	public async Task Split(SplitTestData data)
	{
		var (str, delimiter, expected) = data;
		var result = A.split(delimiter, str);

		foreach (var (expectedItem, resultItem) in expected.Zip(result))
		{
			Log.Logger.Information("Result: {Result}{NewLine}Expected: {Expected}", resultItem, Environment.NewLine,
				expectedItem);
		}

		foreach (var (expectedItem, resultItem) in expected.Zip(result))
		{
			var resultBytes = Encoding.Unicode.GetBytes(resultItem.ToString());
			var expectedBytes = Encoding.Unicode.GetBytes(expectedItem.ToString());

			foreach (var (first, second) in resultBytes.Zip(expectedBytes))
			{
				await Assert
					.That(first)
					.IsEqualTo(second);
			}
		}
	}

	[Test]
	public async Task Simple()
	{
		var simpleString = A.single("red");
		var redString = A.markupSingle(M.Create(foreground: StringExtensions.rgb(Color.Red)), "red");
		var redAnsiString = A.markupSingle(M.Create(foreground: StringExtensions.ansiByte(31)), "red");
		// var complexAnsiString = A.markupSingle(M.Create(foreground: StringExtensions.ansiByte(32)),"green");
		
		await Assert.That(simpleString.ToString()).IsEqualTo("red");
		await Assert.That(redString.ToString()).IsEqualTo("\e[38;2;255;0;0mred\e[0m");
		await Assert.That(redAnsiString.ToString()).IsEqualTo("\e[31mred\e[0m");
		// Assert.AreEqual("\e[32mwoo\e[0m", complexAnsiString.ToString());
	}
	
	[Test]
	public async Task SimpleFullJustification()
	{
		var simpleString = A.single("ab de ef de");

		var overflow = A.pad(simpleString, A.single(" "), 15, MModule.PadType.Full, MModule.TruncationType.Overflow);
		var truncated = A.pad(simpleString, A.single(" "), 15, MModule.PadType.Full, MModule.TruncationType.Truncate);

		await Assert.That(overflow.ToString()).IsEqualTo("ab   de  ef  de");
		await Assert.That(truncated.ToString()).IsEqualTo("ab   de  ef  de");
	}

	[Test]
	public async Task EvaluateWith()
	{
		var redString = A.markupSingle2(
			M.Create(foreground: StringExtensions.rgb(Color.Red)), 
			A.markupSingle(
				M.Create(background: StringExtensions.rgb(Color.Yellow)), "red"));
		
		var result = redString.EvaluateWith((x, y) => x switch
		{
			MModule.MarkupTypes.MarkedupText { Item: M { Details: var structure} } =>
				$"ansi({ItemName(structure)},{y})",
			_ => y
		});
		
		await Assert.That(result).IsEqualTo("ansi(Red,ansi(/Yellow,red))");
		return;

		string ItemName(MarkupImplementation.AnsiStructure structure)
		{
			var sb = new StringBuilder();
			
			if(structure.Foreground is ANSI.AnsiColor.RGB rgb)
				sb.Append(rgb.Item.Name);
			if(structure.Background is ANSI.AnsiColor.RGB rgb2)
				sb.Append($"/{rgb2.Item.Name}");

			return sb.ToString();
		}
	}
	
	[Test]
	public async Task AnsiBleed()
	{
		var normalString1 = A.single("n1");
		var normalString2 = A.single("n2");
		var redString = A.markupSingle(M.Create(foreground: StringExtensions.ansiByte(31)), "red");

		var concat = A.concat(normalString1, A.concat(redString, normalString2));
		var result = concat.ToString();
		
		await Assert.That(result).IsEqualTo("n1\e[31mred\e[0mn2");
	}
	
	[Test]
	public async Task AnsiBleedRgb()
	{
		var normalString1 = A.single("n1");
		var normalString2 = A.single("n2");
		var redString = A.markupSingle(M.Create(foreground: StringExtensions.rgb(Color.Red)), "red");

		var concat = A.concat(normalString1, A.concat(redString, normalString2));
		var result = concat.ToString();
		
		await Assert.That(result).IsEqualTo("n1\e[38;2;255;0;0mred\e[0mn2");
	}

	[Test]
	public async Task SimpleSerialization()
	{
		var original = A.single("red");

		var serialized = MModule.serialize(original);
		var deserialized = MModule.deserialize(serialized);

		await Assert.That(deserialized.ToString()).IsEquatableOrEqualTo(original.ToString());
	}

	[Test]
	public async Task SerializationRgb()
	{
		var original = A.markupSingle(M.Create(foreground: StringExtensions.rgb(Color.Red)), "red");

		var serialized = MModule.serialize(original);
		var deserialized = MModule.deserialize(serialized);

		await Assert.That(deserialized.ToString()).IsEquatableOrEqualTo(original.ToString());
	}

	[Test]
	public async Task SerializationNull()
	{
		var original = A.markupSingle(M.Create(foreground: null), "red");

		var serialized = MModule.serialize(original);
		var deserialized = MModule.deserialize(serialized);

		await Assert.That(deserialized.ToString()).IsEquatableOrEqualTo(original.ToString());
	}
	
	[Test]
	public async Task GetLength()
	{
		var original = A.markupSingle(M.Create(foreground: StringExtensions.rgb(Color.Red)), "red");
		var original2 = A.markupSingle(M.Create(foreground: StringExtensions.rgb(Color.Red)), "");

		await Assert.That(MModule.getLength(original)).IsEqualTo("red".Length);
		await Assert.That(MModule.getLength(original2)).IsEqualTo("".Length);
	}
}