using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Packages;

public class PackageLifecycleRuntimeTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	[Test]
	[Arguments(0)]
	[Arguments(1)]
	[Arguments(2)]
	public async Task StoredHookReportsRealParserVerdictWithoutClaimingBusinessSuccess(int mode)
	{
		var mediator = Factory.Services.GetRequiredService<IMediator>();
		var god = (await mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<SharpPlayer>();
		AnySharpContainer home = god;
		var target = await mediator.Send(new CreateThingCommand("lifecycle-verdict-" + Guid.NewGuid().ToString("N"), home, god, home));
		try
		{
			var script = mode switch
			{
				0 => "&HOOK_DONE me=yes",
				1 => "nonexistent_lifecycle_command_57219; &HOOK_DONE me=yes",
				_ => "&HOOK_DONE me=before; think [strcat(foo,bar"
			};
			await mediator.Send(new SetAttributeCommand(target, ["AINSTALL"], MarkupText.Plain(script), god));
			var legacy = Factory.Services.GetRequiredService<IPackageLifecycleRunner>();
			if (legacy is not IPackageLifecycleObserver observer) throw new InvalidOperationException("Actual lifecycle runner lacks verdict capability");
			var verdict = await observer.RunObservedLifecycleAsync(target.ToString(), "AINSTALL");
			var marker = await mediator.CreateStream(new GetAttributeQuery(target, ["HOOK_DONE"])).LastOrDefaultAsync();
			if (mode == 2)
			{
				await Assert.That(verdict is PackageLifecycleOutcome.ParseFailure or PackageLifecycleOutcome.CommandFailure).IsTrue();
				if (verdict == PackageLifecycleOutcome.ParseFailure)
					await Assert.That(marker is null).IsTrue().Because("a root parse failure ran none of the list");
				else
					await Assert.That(marker?.Value.ToPlainText()).IsEqualTo("before")
						.Because("an evaluated command failed after a preceding command already changed the object");
			}
			else
			{
				// HUH_COMMAND does not mark an unknown command as HadErrors. Parsed describes the
				// local parser verdict, and must never be promoted to a business-success certificate.
				await Assert.That(verdict).IsEqualTo(PackageLifecycleOutcome.Parsed);
				await Assert.That(marker?.Value.ToPlainText()).IsEqualTo("yes");
			}
		}
		finally { await mediator.Send(new DeleteObjectCommand(target)); }
	}
}
