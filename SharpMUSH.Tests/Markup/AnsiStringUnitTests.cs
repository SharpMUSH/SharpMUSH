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

			foreach (var (First, Second) in expected.Zip(result))
			{
				Log.Logger.Information("Result: {Result}{NewLine}Expected: {Expected}", Second, Environment.NewLine, First);
			}

			foreach (var (First, Second) in expected.Zip(result))
			{
				CollectionAssert.AreEqual(Encoding.Unicode.GetBytes(First.ToString()), Encoding.Unicode.GetBytes(Second.ToString()));
			}
		}

		[TestMethod]
		public void Simple()
		{
			var simpleString = A.single("red");
			var redString = A.markupSingle(M.Create(foreground: StringExtensions.rgb(Color.Red)), "red");

			Assert.AreEqual("red", simpleString.ToString());
			Assert.AreEqual("\u001b[38;2;255;0;0mred\u001b[0m", redString.ToString());
		}
	}
}
