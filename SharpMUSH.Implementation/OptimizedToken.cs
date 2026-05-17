using Antlr4.Runtime;

namespace SharpMUSH.Implementation;

internal sealed class OptimizedToken(
	Tuple<ITokenSource, ICharStream> source,
	int type,
	int channel,
	int start,
	int stop,
	string input) : CommonToken(source, type, channel, start, stop)
{
	private readonly string _input = input ?? string.Empty;
	private string? _cachedText;

	public override string Text
	{
		get
		{
			if (_cachedText is not null)
			{
				return _cachedText;
			}

			var start = StartIndex;
			var stop = StopIndex;
			if ((uint)start < (uint)_input.Length && stop >= start && stop < _input.Length)
			{
				_cachedText = _input.Substring(start, stop - start + 1);
				return _cachedText;
			}

			_cachedText = base.Text;
			return _cachedText;
		}
		set
		{
			_cachedText = value;
			base.Text = value;
		}
	}
}
