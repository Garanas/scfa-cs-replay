
using System.Text.Json.Serialization;

namespace FAForever.Replay
{
    public record ReplayMetadata() {

        [JsonPropertyName("complete")]
        public bool Complete { get; init; }
        
        [JsonPropertyName("featured_mod")]
        public string FeaturedMod { get; init; }

        [JsonPropertyName("game_end")]
        public double game_end { get; init; }

        [JsonPropertyName("game_type")]
        public string GameType { get; init; }

        [JsonPropertyName("host")]
        public string host { get; init; }

        [JsonPropertyName("launched_at")]
        public double launched_at { get; init; }

        [JsonPropertyName("mapname")]
        public string mapname { get; init; }

        [JsonPropertyName("num_players")]
        public int num_players { get; init; }

        [JsonPropertyName("recorder")]
        public string recorder { get; init; }

        [JsonPropertyName("state")]
        public string state { get; init; }

        [JsonPropertyName("title")]
        public string title { get; init; }

        [JsonPropertyName("uid")]
        public int uid { get; init; }

        [JsonPropertyName("compression")]
        public string compression { get; init; }

        [JsonPropertyName("version")]
        public int version { get; init; }
    }
}