
using System.Collections.ObjectModel;
using System.ComponentModel;
using FAForever.Replay.Viewer.Features;
using MudBlazor;

namespace FAForever.Replay.Viewer.Services
{

    public enum ReplayType { FAForever, SCFA }

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

        private ReadOnlyCollection<ReplayChatMessage>? _chatMessages = null;

        public ReadOnlyCollection<ReplayChatMessage>? ChatMessages
        {
            get => this._chatMessages; set
            {
                if (value == this._chatMessages) return;

                this._chatMessages = value;

                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.ChatMessages)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public void LoadSCFAReplay(MemoryStream stream)
        {
            this.Replay = ReplayLoader.LoadSCFAReplayFromStream(stream);
            this.ChatMessages = ReplaySemantics.GetChatMessages(this.Replay).AsReadOnly();
        }


        public void LoadFAForeverReplay(MemoryStream stream)
        {
            this.Replay = ReplayLoader.LoadFAFReplayFromMemory(stream);
            this.ChatMessages = ReplaySemantics.GetChatMessages(this.Replay).AsReadOnly();
        }
    }
}
