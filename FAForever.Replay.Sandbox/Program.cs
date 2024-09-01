using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FAForever.Replay.Sandbox
{
    public class Program
    {

        public static void Main()
        {
            Console.WriteLine("Hello World!");

            while (true)
            {
                Console.Write("Provide a replay file: ");
                string? path = Console.ReadLine();
                if (path == null)
                {
                    continue;
                }

                if (path == string.Empty)
                {
                    continue;
                }

                FileStream fileStream = new FileStream(path, FileMode.Open);
                Replay replay = ReplayLoader.LoadFAFReplayFromMemory(fileStream);
                Console.WriteLine($"Replay has {replay.Events.Count} events."   );
            }
        }

    }
}
