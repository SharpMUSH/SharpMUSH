using Microsoft.Extensions.Options;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

public class OptionsWrapper<T>(IOptionsMonitor<T> options) : IOptionsWrapper<T>
{
	public T CurrentValue => options.CurrentValue;
}
