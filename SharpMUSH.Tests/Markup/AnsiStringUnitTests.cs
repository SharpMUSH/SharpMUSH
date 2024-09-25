using Serilog;
using System.Text;
using AnsiString = MarkupString.MarkupStringModule.MarkupString;
using A = MarkupString.MarkupStringModule;
using M = MarkupString.MarkupImplementation.AnsiMarkup;
using System.Drawing;
using ANSILibrary;

namespace SharpMUSH.Tests.Markup;

public class AnsiStringUnitTests : BaseUnitTest
{
	[Test]
	[MethodDataSource(typeof(Data.Concat), nameof(Data.Concat.ConcatData))]
	public async Task Concat((AnsiString strA, AnsiString strB, AnsiString expected) data)
	{
		(AnsiString strA, AnsiString strB, AnsiString expected) = data;
		var result = A.concat(strA, strB);

		Log.Logger.Information("Result: {Result}{NewLine}Expected: {Expected}", result, Environment.NewLine, expected);
		var resultBytes = Encoding.Unicode.GetBytes(result.ToString());
		var expectedBytes = Encoding.Unicode.GetBytes(expected.ToString());
		
		foreach(var bt in resultBytes.Zip(expectedBytes))
		{		
			await Assert
				.That(bt.First)
				.IsEqualTo(bt.Second); 
		}
	}
		
	[Test]
	[MethodDataSource(typeof(Data.Substring), nameof(Data.Substring.SubstringData))]
	public async Task Substring((AnsiString str, int start, AnsiString expected) data)
	{
		(AnsiString str, int start, AnsiString expected) = data;
		var result = A.substring(start, A.getLength(str) - start, str);

		Log.Logger.Information("Result: {Result}{NewLine}Expected: {Expected}", result, Environment.NewLine, expected);

		var resultBytes = Encoding.Unicode.GetBytes(result.ToString());
		var expectedBytes = Encoding.Unicode.GetBytes(expected.ToString());
		
		foreach(var bt in resultBytes.Zip(expectedBytes))
		{		
			await Assert
				.That(bt.First)
				.IsEqualTo(bt.Second); 
		}
	}
		
	[Test]
	[MethodDataSource(typeof(Data.InsertAt), nameof(Data.InsertAt.InsertAtData))]
	public async Task InsertAt((AnsiString str, int index, AnsiString insert, AnsiString expected) data)
	{
		(AnsiString str, int index, AnsiString insert, AnsiString expected) = data;
		var result = A.insertAt(str, insert, index);

		Log.Logger.Information("Result: {Result}{NewLine}Expected: {Expected}", result, Environment.NewLine, expected);

		var resultBytes = Encoding.Unicode.GetBytes(result.ToString());
		var expectedBytes = Encoding.Unicode.GetBytes(expected.ToString());
		
		foreach(var bt in resultBytes.Zip(expectedBytes))
		{		
			await Assert
				.That(bt.First)
				.IsEqualTo(bt.Second); 
		}
	}

	[Test]
	[MethodDataSource(typeof(Data.Substring), nameof(Data.Substring.SubstringLengthData))]
	public async Task SubstringLength((AnsiString str, int length, AnsiString expected) data)
	{
		(AnsiString str, int length, AnsiString expected) = data;
		var result = A.substring(0, length, str);

		Log.Logger.Information("Result: {Result}{NewLine}Expected: {Expected}", result, Environment.NewLine, expected);

		var resultBytes = Encoding.Unicode.GetBytes(result.ToString());
		var expectedBytes = Encoding.Unicode.GetBytes(expected.ToString());
		
		foreach(var bt in resultBytes.Zip(expectedBytes))
		{		
			await Assert
				.That(bt.First)
				.IsEqualTo(bt.Second); 
		}
	}

	[Test]
	[MethodDataSource(typeof(Data.Split), nameof(Data.Split.SplitData))]
	public async Task Split((AnsiString str, string delimiter, AnsiString[] expected) data)
	{
		(AnsiString str, string delimiter, AnsiString[] expected) = data;
		var result = A.split(delimiter, str);

		foreach (var (expectedItem, resultItem) in expected.Zip(result))
		{
			Log.Logger.Information("Result: {Result}{NewLine}Expected: {Expected}", resultItem, Environment.NewLine, expectedItem);
		}

		foreach (var (expectedItem, resultItem) in expected.Zip(result))
		{
			var resultBytes = Encoding.Unicode.GetBytes(resultItem.ToString());
			var expectedBytes = Encoding.Unicode.GetBytes(expectedItem.ToString());
		
			foreach(var bt in resultBytes.Zip(expectedBytes))
			{
				await Assert
					.That(bt.First)
					.IsEqualTo(bt.Second); 
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
		await Assert.That(redString.ToString()).IsEqualTo("\u001b[38;2;255;0;0mred\u001b[0m");
		await Assert.That(redAnsiString.ToString()).IsEqualTo("\u001b[31mred\u001b[0m");
		// Assert.AreEqual("\u001b[32mwoo\u001b[0m", complexAnsiString.ToString());
	}
	
	[Test, Skip("Discriminated Unions cannot be deserialized without an extra library. WIP.")]
	public async Task Serialization()
	{
		var original = A.single("red");

		var serialized = original.Serialize();
		var deserialized = original.Deserialize(serialized);
		
		await Assert.That(deserialized).IsEqualTo(original);
	}
}