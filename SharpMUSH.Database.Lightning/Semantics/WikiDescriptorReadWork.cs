using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning.Semantics;

// Single-request, single-thread owner. A retained NativeBudget reference never replaces Check().
internal sealed class WikiDescriptorReadWork : IDisposable
{
	internal const int TokenLimit = 262144;
	internal const int CharacterLimit = 131072;
	internal const long ByteWorkLimit = 8 * 1024 * 1024;
	private readonly TimeProvider _time;
	private readonly long _started;
	private readonly int _tokenLimit;
	private readonly int _characterLimit;
	private readonly long _byteLimit;
	private readonly NativeReadBudget _native;
	private WikiDescriptorPage? _head;
	private bool _disposed;

	internal WikiDescriptorReadWork(CancellationToken cancellationToken = default)
		: this(cancellationToken, TimeProvider.System, 4 * 1024 * 1024, TokenLimit, CharacterLimit, ByteWorkLimit) { }

	// Test-only lower caps/clock. Never permits enlarging the frozen profile.
	internal static WikiDescriptorReadWork ForTests(TimeProvider clock, CancellationToken token = default,
		long working = 4 * 1024 * 1024, int tokens = TokenLimit, int characters = CharacterLimit, long bytes = ByteWorkLimit)
		=> new(token, clock, working, tokens, characters, bytes);

	private WikiDescriptorReadWork(CancellationToken token, TimeProvider clock, long working, int tokens, int characters, long bytes)
	{
		if (working is <= 0 or > 4 * 1024 * 1024 || tokens is <= 0 or > TokenLimit
			|| characters is < 0 or > CharacterLimit || bytes is <= 0 or > ByteWorkLimit)
			throw new ArgumentOutOfRangeException(nameof(working));
		_time = clock ?? throw new ArgumentNullException(nameof(clock));
		_started = _time.GetTimestamp();
		_native = new(8192, 2 * 1024 * 1024, working, token, 1024);
		(_tokenLimit, _characterLimit, _byteLimit) = (tokens, characters, bytes);
	}

	internal NativeReadBudget NativeBudget { get { Check(); return _native; } }
	internal long WorkingBytes => _native.WorkingBytes;
	internal int Tokens { get; private set; }
	internal int MaterializedUtf16 { get; private set; }
	internal long JsonBytes { get; private set; }
	internal int OutstandingOwners { get; private set; }

	internal void Check()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		_native.CancellationToken.ThrowIfCancellationRequested();
		var elapsed = _time.GetElapsedTime(_started);
		if (elapsed < TimeSpan.Zero || elapsed >= TimeSpan.FromSeconds(1))
			throw new NativeReadLimitException("descriptor-deadline");
	}
	internal void Token()
	{
		Check();
		if (Tokens == _tokenLimit) throw new NativeReadLimitException("descriptor-json-tokens");
		Tokens++;
	}
	internal void Scan(long bytes)
	{
		Check();
		if (bytes < 0 || bytes > _byteLimit - JsonBytes) throw new NativeReadLimitException("descriptor-json-bytes");
		JsonBytes += bytes;
	}
	internal void Materialize(int characters)
	{
		Check();
		if (characters < 0 || characters > _characterLimit - MaterializedUtf16)
			throw new NativeReadLimitException("descriptor-json-utf16");
		MaterializedUtf16 += characters;
	}
	internal WikiDescriptorPage CreatePage()
	{
		Check();
		// Conservative owner fields/object overhead, before its allocation; reservation adds its own64.
		var lease = _native.ReserveWorking(512);
		try
		{
			var page = new WikiDescriptorPage(this, lease);
			page.Next = _head;
			if (_head is not null) _head.Previous = page;
			_head = page;
			OutstandingOwners++;
			return page;
		}
		catch { lease.Dispose(); throw; }
	}
	internal void Remove(WikiDescriptorPage page)
	{
		if (page.Previous is { } previous) previous.Next = page.Next;
		else _head = page.Next;
		if (page.Next is { } next) next.Previous = page.Previous;
		page.Previous = page.Next = null;
		OutstandingOwners--;
	}
	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		while (_head is { } page) page.Dispose();
	}
}

// Borrowed strings/spans may not escape this exclusive owner. Accessors fail after disposal.
internal sealed class WikiDescriptorPage : IDisposable
{
	private WikiDescriptorReadWork? _work;
	private NativeReadReservation? _lease;
	private string? _namespace = "main", _slug = "", _title = "", _author = "";
	private string[]? _categories;
	private int _categoryCount;
	private bool _published = true;
	internal WikiDescriptorPage? Previous, Next;
	internal WikiDescriptorPage(WikiDescriptorReadWork work, NativeReadReservation lease) => (_work, _lease) = (work, lease);
	private void Live() => ObjectDisposedException.ThrowIf(_work is null, this);
	internal string Namespace { get { Live(); return _namespace!; } }
	internal string Slug { get { Live(); return _slug!; } }
	internal string Title { get { Live(); return _title!; } }
	internal string AuthorDbref { get { Live(); return _author!; } }
	internal bool Published { get { Live(); return _published; } }
	internal ReadOnlySpan<string> Categories { get { Live(); return _categories is null ? [] : _categories.AsSpan(0, _categoryCount); } }
	internal void Reserve(long bytes) { Live(); _work!.Check(); _lease!.ReserveAdditional(bytes); }
	internal void SetString(int field, string value)
	{
		switch (field) { case 0: _namespace = value; break; case 1: _slug = value; break; case 2: _title = value; break; case 3: _author = value; break; default: throw new ArgumentOutOfRangeException(nameof(field)); }
	}
	internal void SetPublished(bool value) => _published = value;
	internal void BeginCategories()
	{
		Reserve(32 + 128 * 8);
		_categories = new string[128];
	}
	internal void AddCategory(string value) => _categories![_categoryCount++] = value;
	public void Dispose()
	{
		if (_work is not { } work) return;
		_work = null;
		_namespace = _slug = _title = _author = null;
		_categories = null;
		_categoryCount = 0;
		work.Remove(this);
		var lease = _lease;
		_lease = null;
		lease?.Dispose();
	}
}
