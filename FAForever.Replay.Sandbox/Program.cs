using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FAForever.Replay.Sandbox
{
    public class Program
    {

        public async static void Main()
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

                // As an example: https://replay.faforever.com/23031279
                if (path.Contains("https"))
                {
                    HttpClient httpClient = new HttpClient();
                    var response = await httpClient.GetAsync(path);
                    Replay replay = ReplayLoader.LoadFAFReplayFromMemory(response.Content.ReadAsStream());
                    Console.WriteLine($"Replay from URL has {replay.Events.Count} events.");
                } else
                {
                    FileStream fileStream = new FileStream(path, FileMode.Open);
                    Replay replay = ReplayLoader.LoadFAFReplayFromMemory(fileStream);
                    Console.WriteLine($"Replay from disk has {replay.Events.Count} events.");
                }


            }
        }

    }
}
