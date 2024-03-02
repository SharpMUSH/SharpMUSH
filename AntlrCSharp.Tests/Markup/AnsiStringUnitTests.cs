using Serilog;
using System.Text;
using AnsiString = MarkupString.MarkupStringModule.MarkupString;
using A = MarkupString.MarkupStringModule;

namespace AntlrCSharp.Tests.Markup
{
    [TestClass]
	public class AnsiStringUnitTests : BaseUnitTest
	{
		public AnsiStringUnitTests()
		{
			A.initialize();
		}

		

		[TestMethod]
		[DynamicData(nameof(Data.Concat.ConcatData))]
		public void Concat(AnsiString strA, AnsiString strB, AnsiString expected)
		{
			var result = A.concat(strA, strB);

			Log.Logger.Information("Result: {Result}{NewLine}Expected: {Expected}", result, Environment.NewLine, expected);

			CollectionAssert.AreEqual(Encoding.Unicode.GetBytes(expected.ToString()), Encoding.Unicode.GetBytes(result.ToString()));
		}

		[TestMethod]
		[DynamicData(nameof(Markup.Substring.SubstringData))]
		public void Substring(AnsiString str, int start, AnsiString expected)
		{
			var result = A.substring(start, A.getLength(str) - start, str);

			Log.Logger.Information("Result: {Result}{NewLine}Expected: {Expected}", result, Environment.NewLine, expected);

			CollectionAssert.AreEqual(Encoding.Unicode.GetBytes(expected.ToString()), Encoding.Unicode.GetBytes(result.ToString()));
		}

		[TestMethod]
		[DynamicData(nameof(Markup.Substring.SubstringLengthData))]
		public void SubstringLength(AnsiString str, int length, AnsiString expected)
		{
			var result = A.substring(0, length, str);

			Log.Logger.Information("Result: {Result}{NewLine}Expected: {Expected}", result, Environment.NewLine, expected);

			CollectionAssert.AreEqual(Encoding.Unicode.GetBytes(expected.ToString()), Encoding.Unicode.GetBytes(result.ToString()));
		}

		[TestMethod]
		[DynamicData(nameof(Data.Split.SplitData))]
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
	}
}
