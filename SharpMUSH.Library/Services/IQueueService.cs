namespace SharpMUSH.Library.Services
{
	public interface IQueueService
	{
		void Queue(dynamic queueDetails, dynamic command);
	}
}
