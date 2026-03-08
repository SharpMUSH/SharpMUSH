using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.Core;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

[NotInParallel]
public class TelDiagnosticTests
{
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public required ServerWebAppFactory WebAppFactoryArg { get; init; }

private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

private static string? ExtractMessage(ICall call)
{
if (call.GetMethodInfo().Name != nameof(INotifyService.Notify)) return null;
var args = call.GetArguments();
if (args.Length < 2) return null;
if (args[1] is OneOf<MString, string> oneOf)
return oneOf.Match(mstr => mstr.ToString(), str => str);
if (args[1] is string str2) return str2;
if (args[1] is MString mstr2) return mstr2.ToString();
return null;
}

/// <summary>
/// Verify @tel thing-into-thing by name works without errors.
/// Reproduces the BBS scenario: @tel bbpocket=mbboard
/// </summary>
[Test]
public async ValueTask TelThingIntoThingByName()
{
await Parser.CommandParse(1, ConnectionService, MModule.single("@create telfixtest_obj"));
await Parser.CommandParse(1, ConnectionService, MModule.single("@create telfixtest_dest"));

var preCount = NotifyService.ReceivedCalls().Count();
await Parser.CommandParse(1, ConnectionService, MModule.single("@tel telfixtest_obj=telfixtest_dest"));
var errorMessages = NotifyService.ReceivedCalls().Skip(preCount)
.Select(ExtractMessage)
.Where(m => m != null && (m.Contains("#-1") || m.Contains("can't see") || m.Contains("can't go")))
.ToList();

foreach (var e in errorMessages) Console.WriteLine($"ERROR: {e}");
await Assert.That(errorMessages).IsEmpty()
.Because("@tel of one thing into another thing by name should work without errors");
}

/// <summary>
/// Verify @tel thing-into-thing by dbref works without errors.
/// </summary>
[Test]
public async ValueTask TelThingIntoThingByDbref()
{
var r1 = await Parser.CommandParse(1, ConnectionService, MModule.single("@create telfixtest_obj2"));
var obj = r1.Message!.ToPlainText()!.Trim();
var r2 = await Parser.CommandParse(1, ConnectionService, MModule.single("@create telfixtest_dest2"));
var dest = r2.Message!.ToPlainText()!.Trim();

var preCount = NotifyService.ReceivedCalls().Count();
await Parser.CommandParse(1, ConnectionService, MModule.single($"@tel {obj}={dest}"));
var errorMessages = NotifyService.ReceivedCalls().Skip(preCount)
.Select(ExtractMessage)
.Where(m => m != null && (m.Contains("#-1") || m.Contains("can't see") || m.Contains("can't go")))
.ToList();

foreach (var e in errorMessages) Console.WriteLine($"ERROR: {e}");
await Assert.That(errorMessages).IsEmpty()
.Because("@tel of one thing into another thing by dbref should work without errors");
}

/// <summary>
/// Verify @tel with single arg (self-teleport) works when destination is a thing.
/// </summary>
[Test]
public async ValueTask TelSelfIntoThing()
{
await Parser.CommandParse(1, ConnectionService, MModule.single("@create telfixtest_container"));
await Parser.CommandParse(1, ConnectionService, MModule.single("@set telfixtest_container=ENTER_OK"));

var preCount = NotifyService.ReceivedCalls().Count();
await Parser.CommandParse(1, ConnectionService, MModule.single("@tel telfixtest_container"));
var errorMessages = NotifyService.ReceivedCalls().Skip(preCount)
.Select(ExtractMessage)
.Where(m => m != null && (m.Contains("#-1") || m.Contains("can't see") || m.Contains("can't go")))
.ToList();

foreach (var e in errorMessages) Console.WriteLine($"ERROR: {e}");
await Assert.That(errorMessages).IsEmpty()
.Because("@tel self into a container should work without errors");
}
}
