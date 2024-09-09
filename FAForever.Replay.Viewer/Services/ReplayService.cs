
using System.Reactive.Subjects;

namespace FAForever.Replay.Viewer.Services
{
    public class ReplayService
    {
        
        public Replay? Replay { get; set; }

        public void LoadReplay(MemoryStream stream)
        {
            this.Replay = ReplayLoader.LoadFAFReplayFromMemory(stream);
        }
    }
}
