
namespace SmartHunter.Game.Config
{
    public class MonsterSoftenPartsConfig
    {
        public string StringId;
        public uint[] PartIds;

        public MonsterSoftenPartsConfig(string stringId, uint[] partIds)
        {
            StringId = stringId;
            PartIds = partIds;
        }
    }
}
