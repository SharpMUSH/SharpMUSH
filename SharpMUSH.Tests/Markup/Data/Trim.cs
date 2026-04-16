using A = MarkupString.MarkupStringModule;
using M = MarkupString.MarkupImplementation.AnsiMarkup;
using System.Drawing;
using MarkupString;
using StringExtensions = ANSILibrary.StringExtensions;
using static MarkupString.MarkupStringModule;

namespace SharpMUSH.Tests.Markup.Data;

public record TrimTestData(MString Str, MString TrimStr, TrimType TrimType, MString Expected);

public static class Trim
{
    public static IEnumerable<Func<TrimTestData>> TrimData() =>
    [
        // TrimStart — plain text
        () => new(A.single("  test  "), A.single(" "), TrimType.TrimStart, A.single("test  ")),
        // TrimStart — markup preserved
        () => new(
            A.MarkupSingle(M.Create(foreground: StringExtensions.Rgb(Color.Red)), "  test  "),
            A.single(" "),
            TrimType.TrimStart,
            A.MarkupSingle(M.Create(foreground: StringExtensions.Rgb(Color.Red)), "test  ")),
        // TrimStart — no leading chars to trim
        () => new(A.single("test"), A.single(" "), TrimType.TrimStart, A.single("test")),

        // TrimEnd — plain text
        () => new(A.single("  test  "), A.single(" "), TrimType.TrimEnd, A.single("  test")),
        // TrimEnd — markup preserved
        () => new(
            A.MarkupSingle(M.Create(foreground: StringExtensions.Rgb(Color.Red)), "  test  "),
            A.single(" "),
            TrimType.TrimEnd,
            A.MarkupSingle(M.Create(foreground: StringExtensions.Rgb(Color.Red)), "  test")),
        // TrimEnd — no trailing chars to trim
        () => new(A.single("test"), A.single(" "), TrimType.TrimEnd, A.single("test")),

        // TrimBoth — plain text
        () => new(A.single("  test  "), A.single(" "), TrimType.TrimBoth, A.single("test")),
        // TrimBoth — markup preserved
        () => new(
            A.MarkupSingle(M.Create(foreground: StringExtensions.Rgb(Color.Red)), "  test  "),
            A.single(" "),
            TrimType.TrimBoth,
            A.MarkupSingle(M.Create(foreground: StringExtensions.Rgb(Color.Red)), "test")),
        // TrimBoth — nothing to trim
        () => new(A.single("test"), A.single(" "), TrimType.TrimBoth, A.single("test")),

        // TrimBoth — multiple repetitions of trim char
        () => new(A.single("--test--"), A.single("-"), TrimType.TrimBoth, A.single("test")),

        // TrimBoth — trim char set with multiple characters (= and ~)
        () => new(A.single("=~=~= Trim =~=~="), A.single("=~"), TrimType.TrimBoth, A.single(" Trim ")),

        // TrimBoth — no match returns original
        () => new(A.single("test"), A.single("-"), TrimType.TrimBoth, A.single("test")),

        // TrimBoth — single occurrence of trim char at each edge
        () => new(A.single("-test-"), A.single("-"), TrimType.TrimBoth, A.single("test")),

        // TrimBoth — markup across trim boundary (plain-text result)
        () => new(
            A.concat(
                A.MarkupSingle(M.Create(foreground: StringExtensions.Rgb(Color.Red)), "-"),
                A.concat(A.single("test"), A.MarkupSingle(M.Create(foreground: StringExtensions.Rgb(Color.Red)), "-"))),
            A.single("-"),
            TrimType.TrimBoth,
            A.single("test")),
    ];
}