// Relocated from SharpMUSH.Library: the connection server is its only consumer, and this keeps
// the process decoupled from the full Library. DotNext's LastOrNone() was replaced with the
// equivalent SortedSet check to avoid carrying the package.
namespace SharpMUSH.ConnectionServer.Services;

public class NextUnoccupiedNumberGenerator(long initial)
{
	private readonly SortedSet<long> _unoccupiedNumbers = [];
	private long _next = initial;

	public void Release(long number)
	{
		if (number == _next - 1)
		{
			var latest = _next - 1;
			while (_unoccupiedNumbers.Count > 0 && _unoccupiedNumbers.Max == latest)
			{
				_unoccupiedNumbers.Remove(latest);
				_next = latest;
				latest--;
			}
		}
		else
		{
			_unoccupiedNumbers.Add(number);
		}
	}

	public IEnumerable<long> Get()
	{
		return ApplyGenerator(x => x + 1);
	}

	private IEnumerable<long> ApplyGenerator(Func<long, long> generator)
	{
		while (true)
		{
			if (_unoccupiedNumbers.Count != 0)
			{
				var last = _unoccupiedNumbers.Last();
				_unoccupiedNumbers.Remove(last);
				yield return last;
			}
			else
			{
				var oldValue = _next;
				var newValue = generator(_next);
				_next = newValue;
				yield return oldValue;
			}
		}
	}
}