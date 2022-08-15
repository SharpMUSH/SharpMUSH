namespace SharpMUSH
{
    public class Program
    {



        private static void Main(string[] args)
        {


            MUSHSingleton.Instance.Start();

            // TCP server port          

            //// Perform text input
            for (; ; )
            {
                string line = Console.ReadLine();
                //if (string.IsNullOrEmpty(line))
                //    break;

                // Restart the server
                if (line == "!")
                {
                    Console.Write("Server restarting...");
                    MUSHSingleton.Instance.Server.Restart();
                    Console.WriteLine("Done!");
                    continue;
                }

                // Multicast admin message to all sessions
                line = "(admin) " + line;
                MUSHSingleton.Instance.Server.Multicast(line);
            }

            // Stop the server


        }
    }
}