using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Server.Authentication;
namespace SharpMUSH.Tests.Authentication.External;

public class AccountSecurityRegistrationTests
{
	[Test]
	public async Task ActualMvcAntiforgeryAttributeResolvesItsAuthorizationFilter()
	{
		var registrations = new ServiceCollection(); registrations.AddLogging(); registrations.AddDataProtection().UseEphemeralDataProtectionProvider(); registrations.AddAccountSecurity();
		using var services = registrations.BuildServiceProvider(); using var scope = services.CreateScope();
		var filter = ((IFilterFactory)new ValidateAntiForgeryTokenAttribute()).CreateInstance(scope.ServiceProvider);
		await Assert.That(filter.GetType().Name).IsEqualTo("ValidateAntiforgeryTokenAuthorizationFilter");
	}
}
