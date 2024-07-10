using Serilog;
using System.Text;
using AnsiString = MarkupString.MarkupStringModule.MarkupString;
using A = MarkupString.MarkupStringModule;
using M = MarkupString.MarkupImplementation.AnsiMarkup;
using System.Drawing;
using ANSILibrary;

namespace SharpMUSH.Tests.Markup
{
	[TestClass]
	public class AnsiStringUnitTests : BaseUnitTest
	{
		[DataTestMethod]
		[DynamicData(nameof(Data.Concat.ConcatData), typeof(Data.Concat))]
		public void Concat(AnsiString strA, AnsiString strB, AnsiString expected)
		{
			var result = A.concat(strA, strB);

			Log.Logger.Information("Result: {Result}{NewLine}Expected: {Expected}", result, Environment.NewLine, expected);

			CollectionAssert.AreEqual(Encoding.Unicode.GetBytes(expected.ToString()), Encoding.Unicode.GetBytes(result.ToString()));
		}
		
		[DataTestMethod]
		[DynamicData(nameof(Data.Substring.SubstringData), typeof(Data.Substring))]
		public void Substring(AnsiString str, int start, AnsiString expected)
		{
			var result = A.substring(start, A.getLength(str) - start, str);

			Log.Logger.Information("Result: {Result}{NewLine}Expected: {Expected}", result, Environment.NewLine, expected);

			CollectionAssert.AreEqual(Encoding.Unicode.GetBytes(expected.ToString()), Encoding.Unicode.GetBytes(result.ToString()));
		}
		
		[DataTestMethod]
		[DynamicData(nameof(Data.InsertAt.InsertAtData), typeof(Data.InsertAt))]
		public void InsertAt(AnsiString str, int index, AnsiString insert, AnsiString expected)
		{
			var result = A.insertAt(str, insert, index);

			Log.Logger.Information("Result: {Result}{NewLine}Expected: {Expected}", result, Environment.NewLine, expected);

			CollectionAssert.AreEqual(Encoding.Unicode.GetBytes(expected.ToString()), Encoding.Unicode.GetBytes(result.ToString()));
		}

		[DataTestMethod]
		[DynamicData(nameof(Data.Substring.SubstringLengthData), typeof(Data.Substring))]
		public void SubstringLength(AnsiString str, int length, AnsiString expected)
		{
			var result = A.substring(0, length, str);

			Log.Logger.Information("Result: {Result}{NewLine}Expected: {Expected}", result, Environment.NewLine, expected);

			CollectionAssert.AreEqual(Encoding.Unicode.GetBytes(expected.ToString()), Encoding.Unicode.GetBytes(result.ToString()));
		}

		[DataTestMethod]
		[DynamicData(nameof(Data.Split.SplitData), typeof(Data.Split))]
		public void Split(AnsiString str, string delimiter, AnsiString[] expected)
		{
			var result = A.split(delimiter, str);

			foreach (var (expectedItem, resultItem) in expected.Zip(result))
			{
				Log.Logger.Information("Result: {Result}{NewLine}Expected: {Expected}", resultItem, Environment.NewLine, expectedItem);
			}

			foreach (var (expectedItem, resultItem) in expected.Zip(result))
			{
				CollectionAssert.AreEqual(Encoding.Unicode.GetBytes(expectedItem.ToString()), Encoding.Unicode.GetBytes(resultItem.ToString()));
			}
		}

		[TestMethod]
		public void Simple()
		{
			var simpleString = A.single("red");
			var redString = A.markupSingle(M.Create(foreground: StringExtensions.rgb(Color.Red)), "red");
			var redAnsiString = A.markupSingle(M.Create(foreground: StringExtensions.ansiByte(31)), "red");
			// var complexAnsiString = A.markupSingle(M.Create(foreground: StringExtensions.ansiByte(32)),"green");

			Assert.AreEqual("red", simpleString.ToString());
			Assert.AreEqual("\u001b[38;2;255;0;0mred\u001b[0m", redString.ToString());
			Assert.AreEqual("\u001b[31mred\u001b[0m", redAnsiString.ToString());
			// Assert.AreEqual("\u001b[32mwoo\u001b[0m", complexAnsiString.ToString());
		}
	}
}
