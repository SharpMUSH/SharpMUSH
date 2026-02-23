using A = MarkupString.MarkupStringModule;
using M = MarkupString.MarkupImplementation.AnsiMarkup;
using static MarkupString.MarkupStringModule;

namespace SharpMUSH.Tests.Markup.Data;

public record TrimTestData(MarkupString Str, MarkupString TrimStr, TrimType TrimType, MarkupString Expected);

public static class Trim
{
    public static IEnumerable<TrimTestData> TrimData
    {
        get
        {
            var simple = A.single("  test  ");
            var simpleTrim = A.single(" ");
            var markup = A.markupSingle(M.Create(foreground: "red"), "  test  ");
            var markupTrim = A.single(" ");
            var noTrim = A.single("test");

            // TrimStart
            yield return new TrimTestData(simple, simpleTrim, TrimType.TrimStart, A.single("test  "));
            yield return new TrimTestData(markup, markupTrim, TrimType.TrimStart,
                A.markupSingle(M.Create(foreground: "red"), "test  "));
            yield return new TrimTestData(noTrim, simpleTrim, TrimType.TrimStart, noTrim);

            // TrimEnd
            yield return new TrimTestData(simple, simpleTrim, TrimType.TrimEnd, A.single("  test"));
            yield return new TrimTestData(markup, markupTrim, TrimType.TrimEnd,
                A.markupSingle(M.Create(foreground: "red"), "  test"));
            yield return new TrimTestData(noTrim, simpleTrim, TrimType.TrimEnd, noTrim);

            // TrimBoth
            yield return new TrimTestData(simple, simpleTrim, TrimType.TrimBoth, A.single("test"));
            yield return new TrimTestData(markup, markupTrim, TrimType.TrimBoth,
                A.markupSingle(M.Create(foreground: "red"), "test"));
            yield return new TrimTestData(noTrim, simpleTrim, TrimType.TrimBoth, noTrim);
            
            // Multiple characters trim
            var multiChar = A.single("--test--");
            var multiCharTrim = A.single("-");
            yield return new TrimTestData(multiChar, multiCharTrim, TrimType.TrimBoth, A.single("test"));
            
            // No match
            var noMatch = A.single("test");
            var noMatchTrim = A.single("-");
            yield return new TrimTestData(noMatch, noMatchTrim, TrimType.TrimBoth, noMatch);
            
            // Trim string at beginning and end
            var edgeCase = A.single("-test-");
            yield return new TrimTestData(edgeCase, multiCharTrim, TrimType.TrimBoth, A.single("test"));
            
            // Markup with trim
            var markupComplex = A.concat(
                A.markupSingle(M.Create(foreground: "red"), "-"),
                A.single("test"),
                A.markupSingle(M.Create(foreground: "red"), "-")
            );
            yield return new TrimTestData(markupComplex, multiCharTrim, TrimType.TrimBoth, A.single("test"));
        }
    }
}