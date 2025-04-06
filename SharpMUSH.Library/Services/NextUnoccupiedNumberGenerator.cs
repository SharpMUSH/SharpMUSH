namespace SharpMUSH.Library.Services;

public class NextUnoccupiedNumberGenerator(long initial)
{
	private readonly Stack<long> _unoccupiedNumbers = [];
	private long _initial = initial;

	public void Release(long number)
	{
		_unoccupiedNumbers.Push(number);
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
				yield return _unoccupiedNumbers.Pop();
			}
			else
			{
				var oldValue = _initial;
				var newValue = generator(_initial);
				_initial = newValue;
				yield return oldValue;
			}
		}
	}
}