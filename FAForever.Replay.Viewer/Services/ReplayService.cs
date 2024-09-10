
using System.ComponentModel;

namespace FAForever.Replay.Viewer.Services
{
    public class ReplayService : INotifyPropertyChanged
    {
        private Replay? _replay = null;

        public Replay? Replay
        {
            get => this._replay;
            set
            {
                if (value == this._replay) return;

                this._replay = value;

                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.Replay)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public void LoadReplay(MemoryStream stream)
        {
            this.Replay = ReplayLoader.LoadFAFReplayFromMemory(stream);
        }
    }
}
