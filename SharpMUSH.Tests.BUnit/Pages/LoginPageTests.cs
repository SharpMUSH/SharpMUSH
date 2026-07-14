using Microsoft.AspNetCore.Components;
using SharpMUSH.Client.Layout;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>
/// Regression coverage for /login getting the dedicated full-screen treatment: it must render
/// inside <see cref="OnboardingLayout"/> (no portal chrome — nav/topbar/terminal), not the
/// default <c>MainLayout</c> it fell back to before <c>@layout OnboardingLayout</c> was added.
/// Mirrors <c>SetupPageTests.Setup_UsesOnboardingLayout</c>.
/// </summary>
public class LoginPageTests
{
	[TUnit.Core.Test]
	public async Task Login_UsesOnboardingLayout()
	{
		var layoutAttribute = typeof(SharpMUSH.Client.Pages.Login)
			.GetCustomAttributes(typeof(LayoutAttribute), inherit: true)
			.Cast<LayoutAttribute>()
			.SingleOrDefault();

		await Assert.That(layoutAttribute).IsNotNull();
		await Assert.That(layoutAttribute!.LayoutType).IsEqualTo(typeof(OnboardingLayout));
	}
}
