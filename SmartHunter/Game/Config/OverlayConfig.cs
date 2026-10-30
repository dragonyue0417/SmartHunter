namespace SmartHunter.Game.Config
{
    public class OverlayConfig
    {
        public float ScaleMin = 0.5f;
        public float ScaleMax = 2f;
        public float ScaleStep = 0.05f;
        public int ScaleStepVersion = 1;
        [System.Runtime.Serialization.OnDeserializing]
        void BeforeLoad(System.Runtime.Serialization.StreamingContext context) {ScaleStepVersion=0;}
        [System.Runtime.Serialization.OnDeserialized]
        void AfterLoad(System.Runtime.Serialization.StreamingContext context)
        {
            if(ScaleStepVersion<1 && System.Math.Abs(ScaleStep-0.1f)<0.00001f) ScaleStep=0.05f;
            ScaleStepVersion=1;
        }
        public bool HideWhenGameWindowIsInactive = false;
        public int UpdatesPerSecond = 10;

        public TeamWidgetConfig TeamWidget = new TeamWidgetConfig(220, 220);
        public MonsterWidgetConfig MonsterWidget = new MonsterWidgetConfig(320, 120);
        public PlayerWidgetConfig PlayerWidget = new PlayerWidgetConfig(120, 320);
        public DebugWidgetConfig DebugWidget = new DebugWidgetConfig(24, 360);
    }
}
