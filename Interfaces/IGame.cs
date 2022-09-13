namespace SharpMUSH.Interfaces
{
    internal interface IGame
    {
        internal MUSHServer Server { get; }
        internal MUSHDatabase DB { get; }
        internal InputHandler InputHandle { get; }
        public void Start();
    }

}