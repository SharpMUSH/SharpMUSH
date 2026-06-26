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
        () => new(A.single("  test  "), A.single(" "), TrimType.TrimStart, A.single("test  ")),
        () => new(
            A.MarkupSingle(M.Create(foreground: StringExtensions.Rgb(Color.Red)), "  test  "),
            A.single(" "),
            TrimType.TrimStart,
            A.MarkupSingle(M.Create(foreground: StringExtensions.Rgb(Color.Red)), "test  ")),
        () => new(A.single("test"), A.single(" "), TrimType.TrimStart, A.single("test")),

        () => new(A.single("  test  "), A.single(" "), TrimType.TrimEnd, A.single("  test")),
        () => new(
            A.MarkupSingle(M.Create(foreground: StringExtensions.Rgb(Color.Red)), "  test  "),
            A.single(" "),
            TrimType.TrimEnd,
            A.MarkupSingle(M.Create(foreground: StringExtensions.Rgb(Color.Red)), "  test")),
        () => new(A.single("test"), A.single(" "), TrimType.TrimEnd, A.single("test")),

        () => new(A.single("  test  "), A.single(" "), TrimType.TrimBoth, A.single("test")),
        () => new(
            A.MarkupSingle(M.Create(foreground: StringExtensions.Rgb(Color.Red)), "  test  "),
            A.single(" "),
            TrimType.TrimBoth,
            A.MarkupSingle(M.Create(foreground: StringExtensions.Rgb(Color.Red)), "test")),
        () => new(A.single("test"), A.single(" "), TrimType.TrimBoth, A.single("test")),

        () => new(A.single("--test--"), A.single("-"), TrimType.TrimBoth, A.single("test")),

        () => new(A.single("=~=~= Trim =~=~="), A.single("=~"), TrimType.TrimBoth, A.single(" Trim ")),

        () => new(A.single("test"), A.single("-"), TrimType.TrimBoth, A.single("test")),

        () => new(A.single("-test-"), A.single("-"), TrimType.TrimBoth, A.single("test")),

        () => new(
            A.concat(
                A.MarkupSingle(M.Create(foreground: StringExtensions.Rgb(Color.Red)), "-"),
                A.concat(A.single("test"), A.MarkupSingle(M.Create(foreground: StringExtensions.Rgb(Color.Red)), "-"))),
            A.single("-"),
            TrimType.TrimBoth,
            A.single("test")),
    ];
}