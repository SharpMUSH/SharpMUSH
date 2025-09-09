using AutoBogus;
using SharpMUSH.Configuration.Options;

namespace SharpMUSH.Client.Services;

public class AdminConfigService
{
	public PennMUSHOptions GetOptions()
	{
		var fakeoptions = new AutoFaker<PennMUSHOptions>().Generate();
		return fakeoptions;
	}
}


public static class PennMUSHOptionsExtension
{
	public static IEnumerable<object> ToDatagrid(this PennMUSHOptions options)
	{
		return [];
	}
}