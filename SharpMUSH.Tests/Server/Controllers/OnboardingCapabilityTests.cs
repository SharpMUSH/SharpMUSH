using NSubstitute;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Server.Controllers;

public class OnboardingCapabilityTests
{
	[Test]
	[Arguments("unchanged", 1, "1.8.0")]
	[Arguments("unchanged", 1, "1.8.1")]
	[Arguments("bundled", 1, "1.8.0")]
	[Arguments("bundled", 1, "1.8.1")]
	[Arguments("bundled-wrong-path", 0, "1.8.0")]
	[Arguments("bundled-wrong-path", 0, "1.8.1")]
	[Arguments("bundled-wrong-commit", 0, "1.8.0")]
	[Arguments("bundled-wrong-commit", 0, "1.8.1")]
	[Arguments("changed", 0, "1.8.0")]
	[Arguments("changed", 0, "1.8.1")]
	[Arguments("unreachable", 0, "1.8.0")]
	[Arguments("unreachable", 0, "1.8.1")]
	[Arguments("shadowed", 0, "1.8.0")]
	[Arguments("shadowed", 0, "1.8.1")]
	[Arguments("unsupported", 0, "1.8.0")]
	[Arguments("unsupported", 0, "1.8.1")]
	[Arguments("foreign-source", 0, "1.8.0")]
	[Arguments("foreign-source", 0, "1.8.1")]
	[Arguments("unsafe-path", 0, "1.8.0")]
	[Arguments("unsafe-path", 0, "1.8.1")]
	[Arguments("missing-commit", 0, "1.8.0")]
	[Arguments("missing-commit", 0, "1.8.1")]
	[Arguments("mixed-handlers", 0, "1.8.0")]
	[Arguments("mixed-handlers", 0, "1.8.1")]
	[Arguments("old-baseline", 0, "1.8.0")]
	[Arguments("old-baseline", 0, "1.8.1")]
	public async Task OnlyReviewedReachableUnchangedPackageCommandsAreOffered(string condition, int expected, string version)
	{
		var registry = Substitute.For<IPackageRegistryService>();
		var probe = Substitute.For<IOnboardingCommandProbe>();
		var package = new InstalledPackageRecord("plus-help", condition == "unsupported" ? "99.0.0" : version,
			"https://github.com/SharpMUSH/SharpMUSH-Packages", "plus-help", new string('a', 40), null,
			DateTimeOffset.UtcNow, 1);
		package = condition switch
		{
			"bundled" => package with { SourceRepo = "bundled:sharpmush", InstalledCommit = "bundled" },
			"bundled-wrong-path" => package with { SourceRepo = "bundled:sharpmush", InstalledCommit = "bundled", SourcePath = "other" },
			"bundled-wrong-commit" => package with { SourceRepo = "bundled:sharpmush", InstalledCommit = "unverified" },
			"foreign-source" => package with { SourceRepo = "https://example.invalid/unreviewed" },
			"unsafe-path" => package with { SourcePath = "../private" },
			"missing-commit" => package with { InstalledCommit = "main" },
			_ => package
		};
		registry.GetInstalledPackagesAsync().Returns(new[] { package });
		registry.GetManagedAttributesAsync("plus-help").Returns(new[]
		{
			new ManagedAttributeRecord("plus-help", "#42:100", "CMD`HELP", "$+help:@pemit %#=Help", "", condition == "old-baseline" ? "1.7.0" : version)
		});
		probe.MatchAsync(Arg.Any<DBRef>(), "+help", Arg.Any<CancellationToken>()).Returns(condition switch
		{
			"unreachable" => Array.Empty<OnboardingCommandMatch>(),
			"mixed-handlers" => new[] { new OnboardingCommandMatch("#42:100", "CMD`HELP", "$+help:@pemit %#=Help"), new OnboardingCommandMatch("#43:100", "CMD`OTHER", "$+help:@emit extra") },
			"shadowed" => new[] { new OnboardingCommandMatch("#43:100", "CMD`HELP", "$+help:@pemit %#=Help") },
			_ => new[] { new OnboardingCommandMatch("#42:100", "CMD`HELP", condition == "changed" ? "$+help:@emit Surprise" : "$+help:@pemit %#=Help") }
		});
		var result = await new OnboardingCapabilityService(registry, probe).ResolveAsync(new DBRef(5), default);
		await Assert.That(result.Count).IsEqualTo(expected);
		if (expected == 1)
		{
			await Assert.That(result[0].InstalledVersion).IsEqualTo(version);
			await Assert.That(result[0].Command).IsEqualTo("+help");
			if (condition == "bundled") await Assert.That(result[0].HelpUrl).IsNull();
			else await Assert.That(result[0].HelpUrl).IsEqualTo("https://github.com/SharpMUSH/SharpMUSH-Packages/blob/" + new string('a', 40) + "/plus-help/package.yaml");
		}
	}

	/// <summary>The bundled +welcome checklist is offered once its handler is the reviewed one.</summary>
	[Test]
	[Arguments("1.0.1", 1)]
	[Arguments("9.0.0", 0)]
	public async Task TheBundledWelcomeChecklistIsOffered(string version, int expected)
	{
		var registry = Substitute.For<IPackageRegistryService>();
		var probe = Substitute.For<IOnboardingCommandProbe>();
		registry.GetInstalledPackagesAsync().Returns(new[]
		{
			new InstalledPackageRecord("onboarding", version, "bundled:sharpmush", "onboarding", "bundled", null, DateTimeOffset.UnixEpoch, 1)
		});
		registry.GetManagedAttributesAsync("onboarding").Returns(new[]
		{
			new ManagedAttributeRecord("onboarding", "#50:100", "CMD`WELCOME", "$+welcome*:@include me/INC", "", version)
		});
		probe.MatchAsync(Arg.Any<DBRef>(), "+welcome", Arg.Any<CancellationToken>())
			.Returns(new[] { new OnboardingCommandMatch("#50:100", "CMD`WELCOME", "$+welcome*:@include me/INC") });
		var result = await new OnboardingCapabilityService(registry, probe).ResolveAsync(new DBRef(5), default);
		await Assert.That(result.Select(capability => capability.Command)).IsEquivalentTo(expected == 1 ? new[] { "+welcome" } : []);
	}
}
