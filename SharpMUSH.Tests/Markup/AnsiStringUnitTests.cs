using ANSILibrary;
using Serilog;
using System.Drawing;
using System.Text;
using A = MarkupString.MarkupStringModule;
using AnsiString = MarkupString.MarkupStringModule.MarkupString;
using M = MarkupString.MarkupImplementation.AnsiMarkup;

namespace SharpMUSH.Tests.Markup;

public class AnsiStringUnitTests : BaseUnitTest
{
	[Test]
	[MethodDataSource(typeof(Data.Concat), nameof(Data.Concat.ConcatData))]
	public async Task Concat((AnsiString strA, AnsiString strB, AnsiString expected) data)
	{
		var (strA, strB, expected) = data;
		var result = A.concat(strA, strB);

		Log.Logger.Information("Result: {Result}{NewLine}Expected: {Expected}", result, Environment.NewLine, expected);
		var resultBytes = Encoding.Unicode.GetBytes(result.ToString());
		var expectedBytes = Encoding.Unicode.GetBytes(expected.ToString());

		foreach (var (First, Second) in resultBytes.Zip(expectedBytes))
		{
			await Assert
				.That(First)
				.IsEqualTo(Second);
		}
	}

	[Test]
	[MethodDataSource(typeof(Data.Substring), nameof(Data.Substring.SubstringData))]
	public async Task Substring((AnsiString str, int start, AnsiString expected) data)
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
	[MethodDataSource(typeof(Data.Pad), nameof(Data.Pad.PadData))]
	public async Task Pad(
		(AnsiString input, AnsiString padStr, int width, MModule.PadType padType, MModule.TruncationType truncType,
			AnsiString expected) data)
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
	[MethodDataSource(typeof(Data.InsertAt), nameof(Data.InsertAt.InsertAtData))]
	public async Task InsertAt((AnsiString str, int index, AnsiString insert, AnsiString expected) data)
	{
		var (str, index, insert, expected) = data;
		var result = A.insertAt(str, insert, index);

		Log.Logger.Information("Result: {Result}{NewLine}Expected: {Expected}", result, Environment.NewLine, expected);

		var resultBytes = Encoding.Unicode.GetBytes(result.ToString());
		var expectedBytes = Encoding.Unicode.GetBytes(expected.ToString());

		foreach (var (First, Second) in resultBytes.Zip(expectedBytes))
		{
			await Assert
				.That(First)
				.IsEqualTo(Second);
		}
	}

	[Test]
	[MethodDataSource(typeof(Data.Substring), nameof(Data.Substring.SubstringLengthData))]
	public async Task SubstringLength((AnsiString str, int length, AnsiString expected) data)
	{
		var (str, length, expected) = data;
		var result = A.substring(0, length, str);

		Log.Logger.Information("Result: {Result}{NewLine}Expected: {Expected}", result, Environment.NewLine, expected);

		var resultBytes = Encoding.Unicode.GetBytes(result.ToString());
		var expectedBytes = Encoding.Unicode.GetBytes(expected.ToString());

		foreach (var (First, Second) in resultBytes.Zip(expectedBytes))
		{
			await Assert
				.That(First)
				.IsEqualTo(Second);
		}
	}

	[Test]
	[MethodDataSource(typeof(Data.Split), nameof(Data.Split.SplitData))]
	public async Task Split((AnsiString str, string delimiter, AnsiString[] expected) data)
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

			foreach (var (First, Second) in resultBytes.Zip(expectedBytes))
			{
				await Assert
					.That(First)
					.IsEqualTo(Second);
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
	public async Task AnsiBleed()
	{
		var normalString1 = A.single("n1");
		var normalString2 = A.single("n2");
		var redString = A.markupSingle(M.Create(foreground: StringExtensions.ansiByte(31)), "red");

		var concat = A.concat(normalString1, A.concat(redString, normalString2));
		var result = concat.ToString();
		// var test = A.markupSingle2(M.Create(foreground: StringExtensions.rgb(Color.White)), concat);
		
		await Assert.That(result).IsEqualTo("n1\e[31mred\e[0mn2");
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
}