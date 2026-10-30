namespace SmartHunter.Game.Config
{
    public enum RelayRole { Auto, Host, Guest }
    public sealed class RelayConfig
    {
        public const string DefaultServerUrl = "http://140.238.55.121/";
        public const string DefaultWildsServerUrl = DefaultServerUrl;
        public const string DefaultWatchBaseUrl = "https://smarthunter.dragonyue.net/";
        public bool Enabled = false;
        public string ServerUrl = DefaultServerUrl, ClientId = "";
        // Accept old files, but never copy a transport identity into new files.
        // The relay now owns one fresh identity per running client instance.
        public bool ShouldSerializeClientId() => false;
        public RelayRole Role = RelayRole.Auto;
        public bool SyncAilmentBuildup = false;
        public bool ShareWebView = false;
        [System.Runtime.Serialization.OnDeserialized]
        void AfterLoad(System.Runtime.Serialization.StreamingContext context)
        {
            if (System.String.IsNullOrWhiteSpace(ServerUrl)) ServerUrl = DefaultServerUrl;
        }
        public RelayConfig Copy() => (RelayConfig)MemberwiseClone();
        public void ApplyGameDefaults(SmartHunter.World.GameChoice game)
        {
            bool wilds = game == SmartHunter.World.GameChoice.Wilds;
            if (System.String.IsNullOrWhiteSpace(ServerUrl)) ServerUrl = wilds ? DefaultWildsServerUrl : DefaultServerUrl;
            if (wilds && System.Uri.TryCreate(ServerUrl.Trim(), System.UriKind.Absolute, out var address) &&
                IsBuiltInEndpoint(address, new System.Uri(DefaultWatchBaseUrl).Host))
                ServerUrl = DefaultServerUrl;
        }
        internal static bool IsBuiltInEndpoint(System.Uri address, string host) =>
            (address.Scheme == "http" || address.Scheme == "https") && address.Host == host && address.IsDefaultPort &&
            System.String.IsNullOrEmpty(address.Query) && System.String.IsNullOrEmpty(address.Fragment) &&
            System.String.IsNullOrEmpty(address.UserInfo) &&
            System.Array.IndexOf(new[] { "", "/index.php", "/wilds/v1/sync" }, address.AbsolutePath.TrimEnd('/')) >= 0;
        public void CopyFrom(RelayConfig source)
        {
            Enabled=source.Enabled; ServerUrl=source.ServerUrl; ClientId=source.ClientId;
            Role=source.Role; SyncAilmentBuildup=source.SyncAilmentBuildup; ShareWebView=source.ShareWebView;
        }
    }
    public sealed class GameServerProfiles
    {
        public RelayConfig World = new RelayConfig();
        public RelayConfig Wilds = new RelayConfig { ServerUrl = RelayConfig.DefaultWildsServerUrl };
        [Newtonsoft.Json.JsonIgnore] internal bool WasLoaded;
        [System.Runtime.Serialization.OnDeserializing]
        void BeforeLoad(System.Runtime.Serialization.StreamingContext context) { WasLoaded=true; }
        [System.Runtime.Serialization.OnDeserialized]
        void AfterLoad(System.Runtime.Serialization.StreamingContext context)
        { World=World??new RelayConfig(); Wilds=Wilds??new RelayConfig(); Wilds.ApplyGameDefaults(SmartHunter.World.GameChoice.Wilds); }
        public RelayConfig Get(SmartHunter.World.GameChoice game) => game == SmartHunter.World.GameChoice.World ? World : Wilds;
    }
}
