using Serilog;
using System.Text;
using AnsiString = MarkupString.MarkupStringModule.MarkupString;
using A = MarkupString.MarkupStringModule;
using M = MarkupString.MarkupImplementation.AnsiMarkup;

namespace AntlrCSharp.Tests.Markup
{
	[TestClass]
	public class AnsiStringUnitTests : BaseUnitTest
	{
		public AnsiStringUnitTests()
		{
			A.initialize();
		}

		public static IEnumerable<object[]> ConcatData
		{
			get
			{
				return new object[][] {
					[A.single("con"), A.single("cat"), A.single("concat")],
					[A.markupSingle2(M.Create(foreground: "#FF0000"), A.single("red")), A.single("cat"),
					 A.multiple([A.markupSingle2(M.Create(foreground: "#FF0000"), A.single("red")),
											 A.single("cat")])],
					[A.markupSingle2(M.Create(foreground: "#FF0000"), A.single("red")), A.markupSingle(M.Create(foreground: "#0000FF"),"cat"),
					 A.multiple([A.markupSingle2(M.Create(foreground: "#FF0000"), A.single("red")),
											 A.markupSingle2(M.Create(foreground: "#0000FF"), A.single("cat"))])]
				};
			}
		}

		public static IEnumerable<object[]> SplitData
		{
			get
			{
				return new object[][] {
					[A.concat(A.single("con"), A.single(";cat")), ";",
						new AnsiString[]
						{
							A.single("con"),
							A.single("cat")
						}],
					[A.concat(A.single("ca"), A.single(";t")), "",
						new AnsiString[]
						{
							A.single(""),
							A.single("c"),
							A.single("a"),
							A.single(";"),
							A.single("t")
						}],
					[A.concat(A.single(";con"), A.single(";cat;")), ";",
						new AnsiString[]
						{
							A.single(""),
							A.single("con"),
							A.single("cat"),
							A.single("")
						}],
					[A.concat(A.markupSingle( M.Create(foreground: "#FF0000"),"red"), A.single(";cat")), ";",
					 new AnsiString[] { 
						 A.markupSingle(M.Create(foreground: "#FF0000"), "red"), 
						 A.single("cat")
					 }],
					[A.concat(A.markupSingle( M.Create(foreground: "#FF0000"),"r;e;d"), A.single("c;at")), ";",
					 new AnsiString[] 
					 {
						 A.markupSingle(M.Create(foreground: "#FF0000"), "r"),
						 A.markupSingle(M.Create(foreground: "#FF0000"), "e"),
						 A.multiple([ A.markupSingle(M.Create(foreground: "#FF0000"), "d"), 
							A.single("c")]), 
						 A.single("at") 
					 }]
				};
			}
		}

		public static IEnumerable<object[]> SubstringData
		{
			get
			{
				return new object[][] {
					[A.single("redCat"), 3, A.single("Cat")],
					[A.single("redCat"), 0, A.single("redCat")],
					[A.single("redCat"), 6, A.single(string.Empty)],
					[A.concat(A.markupSingle( M.Create(foreground: "#FF0000"),"red"),A.markupSingle(M.Create(foreground: "#0000FF"), "cat")),
					 3, A.markupSingle(M.Create(foreground: "#0000FF" ), "cat")],
					[A.concat(A.markupSingle( M.Create(foreground: "#FF0000"),"red"),A.markupSingle(M.Create(foreground: "#0000FF"), "cat")),
					 2, A.concat(A.markupSingle(M.Create(foreground: "#FF0000"),"d"),A.markupSingle(M.Create(foreground: "#0000FF"), "cat"))],
					[A.multiple([A.markupSingle2(M.Create(foreground: "#FF0000"), A.single("red")),
											 A.markupSingle2(M.Create(foreground: "#0000FF"), A.single("cat")),
											 A.markupSingle2(M.Create(foreground: "#00FF00"), A.single("green")),
											 A.markupSingle2(M.Create(foreground: "#0000FF"), A.single("cat"))]),
					 8, A.multiple([A.markupSingle2(M.Create(foreground: "#00FF00"), A.single("een")),
											    A.markupSingle2(M.Create(foreground: "#0000FF"), A.single("cat"))])],
					[A.concat(A.markupSingle( M.Create(foreground: "#FF0000"),"red"),A.markupSingle(M.Create(foreground: "#0000FF"), "cat")),
					 6, A.single(string.Empty)]
				};
			}
		}

		public static IEnumerable<object[]> SubstringLengthData
		{
			get
			{
				return new object[][] {
					[A.single("redCat"), 3, A.single("red")],
					[A.single("redCat"), 0, A.single(string.Empty)],
					[A.single("redCat"), 6, A.single("redCat")],
					[A.concat(A.markupSingle( M.Create(foreground: "#FF0000"),"red"),A.markupSingle(M.Create(foreground: "#0000FF"), "cat")),
					 3, A.markupSingle(M.Create(foreground: "#FF0000" ), "red")],
					[A.concat(A.markupSingle( M.Create(foreground: "#FF0000"),"red"),A.markupSingle(M.Create(foreground: "#0000FF"), "cat")),
					 4, A.concat(A.markupSingle(M.Create(foreground: "#FF0000"),"red"),A.markupSingle(M.Create(foreground: "#0000FF"), "c"))],
					[A.multiple([A.markupSingle2(M.Create(foreground: "#FF0000"), A.single("red")),
											 A.markupSingle2(M.Create(foreground: "#0000FF"), A.single("cat")),
											 A.markupSingle2(M.Create(foreground: "#00FF00"), A.single("green")),
											 A.markupSingle2(M.Create(foreground: "#0000FF"), A.single("cat"))]),
					 7, A.multiple([A.markupSingle2(M.Create(foreground: "#FF0000"), A.single("red")),
											    A.markupSingle2(M.Create(foreground: "#0000FF"), A.single("cat")),
											    A.markupSingle2(M.Create(foreground: "#00FF00"), A.single("g"))])],
					[A.concat(A.markupSingle( M.Create(foreground: "#FF0000"),"red"),A.markupSingle(M.Create(foreground: "#0000FF"), "cat")),
					 0, A.single(string.Empty)]
				};
			}
		}

		[TestMethod]
		[DynamicData(nameof(ConcatData))]
		public void Concat(AnsiString strA, AnsiString strB, AnsiString expected)
		{
			var result = A.concat(strA, strB);

			Log.Logger.Information("Result: {Result}{NewLine}Expected: {Expected}", result, Environment.NewLine, expected);

			CollectionAssert.AreEqual(Encoding.Unicode.GetBytes(expected.ToString()), Encoding.Unicode.GetBytes(result.ToString()));
		}

		[TestMethod]
		[DynamicData(nameof(SubstringData))]
		public void Substring(AnsiString str, int start, AnsiString expected)
		{
			var result = A.substring(start, A.getLength(str) - start, str);

			Log.Logger.Information("Result: {Result}{NewLine}Expected: {Expected}", result, Environment.NewLine, expected);

			CollectionAssert.AreEqual(Encoding.Unicode.GetBytes(expected.ToString()), Encoding.Unicode.GetBytes(result.ToString()));
		}

		[TestMethod]
		[DynamicData(nameof(SubstringLengthData))]
		public void SubstringLength(AnsiString str, int length, AnsiString expected)
		{
			var result = A.substring(0, length, str);

			Log.Logger.Information("Result: {Result}{NewLine}Expected: {Expected}", result, Environment.NewLine, expected);

			CollectionAssert.AreEqual(Encoding.Unicode.GetBytes(expected.ToString()), Encoding.Unicode.GetBytes(result.ToString()));
		}

		[TestMethod]
		[DynamicData(nameof(SplitData))]
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
