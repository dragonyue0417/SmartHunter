namespace SmartHunter.Game.Config
{
    public sealed class UpdateConfig
    {
        public bool Enabled = true;
        public string Url = SmartHunter.Core.Updates.UpdateSource.DefaultUrl;
        public string PendingDirectory = "";
        public string PendingSource = "";
    }
}
