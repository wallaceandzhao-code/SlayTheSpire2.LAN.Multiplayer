using System.Text.Json.Serialization;

namespace SlayTheSpire2.LAN.Multiplayer.Models
{
    internal static class LanDiscoveryProtocol
    {
        public const string Magic = "STS2_LAN_DISCOVERY";
        public const int Version = 1;
        public const int Port = 33772;
        public const int TimeoutMs = 1200;
    }

    internal class LanDiscoveryRequestModel
    {
        [JsonPropertyName("magic")] public string Magic { get; set; } = LanDiscoveryProtocol.Magic;

        [JsonPropertyName("version")] public int Version { get; set; } = LanDiscoveryProtocol.Version;
    }

    internal class LanDiscoveryResponseModel
    {
        [JsonPropertyName("magic")] public string Magic { get; set; } = LanDiscoveryProtocol.Magic;

        [JsonPropertyName("version")] public int Version { get; set; } = LanDiscoveryProtocol.Version;

        [JsonPropertyName("host_name")] public string HostName { get; set; } = string.Empty;

        [JsonPropertyName("host_port")] public ushort HostPort { get; set; }

        [JsonPropertyName("game_mode")] public string GameMode { get; set; } = string.Empty;

        [JsonPropertyName("max_players")] public int MaxPlayers { get; set; }
    }

    internal class LanDiscoveredRoomModel
    {
        public string HostAddress { get; set; } = string.Empty;

        public string HostName { get; set; } = string.Empty;

        public ushort HostPort { get; set; }

        public string GameMode { get; set; } = string.Empty;

        public int MaxPlayers { get; set; }
    }
}
