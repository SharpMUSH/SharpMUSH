using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;

namespace SharpMUSH.Configuration;

public class ReadPennMUSHConfig : IOptionsFactory<PennMUSHOptions>
{
	public PennMUSHOptions Create(string name)
	{
		throw new NotImplementedException();
	}
}