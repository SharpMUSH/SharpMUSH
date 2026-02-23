using MarkupString;
using Serilog;
using SharpMUSH.Tests.Markup.Data;

namespace SharpMUSH.Tests.Markup;

public class TrimUnitTests
{
    [Test]
    [MethodDataSource(typeof(Trim), nameof(Data.Trim.TrimData))]
    public void Trim(TrimTestData data)
    {
        var (str, trimStr, trimType, expected) = data;
        var result = MarkupStringModule.trim(str, trimStr, trimType);

        Log.Logger.Information("Result: {Result}{NewLine}Expected: {Expected}", result, Environment.NewLine, expected);
        
        Assert.That(result.ToString(), Is.EqualTo(expected.ToString()));
    }
}