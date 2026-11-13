using System.Text.Json;

namespace Windsock
{
    public class Config
    {
        public NetworkConfig Network { get; set; } = new();
        public string PlatformType { get; set; } = "wxscan_or_domestic";
        public ApiConfig API { get; set; } = new();
        public Timing Timing { get; set; } = new();

        public static Config Load(string configPath)
        {
            JsonSerializerOptions options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            using JsonDocument json = JsonDocument.Parse(File.ReadAllText(configPath));

            Config config = new Config
            {
                Network = json.RootElement.GetProperty("network").Deserialize<NetworkConfig>(options) ?? throw new InvalidOperationException("Network configuration is empty."),
                PlatformType = json.RootElement.TryGetProperty("platformType", out JsonElement platformTypeEl) && platformTypeEl.ValueKind == JsonValueKind.String 
                    ? platformTypeEl.GetString() ?? "wxscan_or_domestic" 
                    : "wxscan_or_domestic",
                API = json.RootElement.GetProperty("api").Deserialize<ApiConfig>(options) ?? throw new InvalidOperationException("API configuration is empty."),
                Timing = json.RootElement.TryGetProperty("timing", out JsonElement timing) ? timing.Deserialize<Timing>(options) ?? throw new InvalidOperationException("Timing configuration is empty.") : new Timing()
            };

            if (config.Timing.Radar <= 0 || config.Timing.SatRad <= 0 || config.Timing.CurrentConditions <= 0 || config.Timing.DailyForecast <= 0 || config.Timing.HourlyForecast <= 0 || config.Timing.Headlines <= 0 || config.Timing.Bulletins <= 0)
            {
                throw new InvalidOperationException("All timing intervals must be positive numbers of minutes.");
            }

            return config;
        }
    }

    public class NetworkConfig
    {
        public string InterfaceAddress { get; set; } = "10.100.102.1";
        public string MulticastAddress { get; set; } = "224.1.1.77";
        public int RoutinePort { get; set; } = 7777;
        public int PriorityPort { get; set; } = 7778;
        public int Ttl { get; set; } = 2;
        public int PacketDelayMs { get; set; } = 10;
    }

    public class ApiConfig
    {
        public string TwcForecastsKey { get; set; } = "your_api_key_here";
        public string TwcRadarKey { get; set; } = "your_api_key_here";
    }

    public class Timing
    {
        public int Radar { get; set; } = 5;
        public int SatRad { get; set; } = 5;
        public int CurrentConditions { get; set; } = 30;
        public int DailyForecast { get; set; } = 30;
        public int HourlyForecast { get; set; } = 30;
        public int Headlines { get; set; } = 5;
        public int Bulletins { get; set; } = 5;
    }
}
