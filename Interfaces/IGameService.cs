namespace SharpMUSH.Interfaces
{
    internal interface IGameService
    {
        public MUSHServer Server { get; }
        public MUSHDatabase DB { get; }
        public InputHandler InputHandle { get; }
    }
}
