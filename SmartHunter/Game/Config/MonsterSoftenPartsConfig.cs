using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
