
using System.ComponentModel;


namespace FAForever.Replay.Viewer.Services
{

    public class ReplayService : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private ReplayState? _ReplayState = null;

        public ReplayState? ReplayState
        {
            get => this._ReplayState; set
            {
                if (value == this._ReplayState) return;

                this._ReplayState = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ReplayState)));
            }
        }
    }
}
