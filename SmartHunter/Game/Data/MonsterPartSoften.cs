using System.ComponentModel;
using System.Linq;
using SmartHunter.Core.Data;
using SmartHunter.Game.Helpers;

namespace SmartHunter.Game.Data
{
    public class MonsterPartSoften : TimedVisibility
    {
        Monster m_Owner;
        public ulong Address { get; private set; }

        public Progress Time { get; private set; }

        uint m_PartID;
        public uint PartID
        {
            get { return m_PartID; }
            set { SetProperty(ref m_PartID, value); }
        }

        uint m_TimesCount;
        public uint TimesCount
        {
            get { return m_TimesCount; }
            set { SetProperty(ref m_TimesCount, value); }
        }

        public string Name
        {
            get
            {
                return LocalizationHelper.GetMonsterSoftenPartName(m_Owner.Id, PartID);
            }
        }

        public string GroupId
        {
            get
            {
                if (ConfigHelper.MonsterData.Values.Monsters.TryGetValue(m_Owner.Id, out var monsterConfig))
                {
                    if (monsterConfig.SoftenParts != null)
                    {
                        var softenParts = monsterConfig.SoftenParts.Where(softenPart => softenPart.PartIds.Contains(PartID));
                        if (softenParts.Count() == 1)
                            return softenParts.ElementAt(0).StringId;
                    }
                }
                return "";
            }
        }

        public bool IsVisible
        {
            get
            {
                return IsIncluded(GroupId) && IsTimeVisible(false, ConfigHelper.Main.Values.Overlay.MonsterWidget.HideSoftenPartsAfterSeconds) && ConfigHelper.Main.Values.Overlay.MonsterWidget.ShowSoftenParts;
            }
        }

        public MonsterPartSoften(Monster owner, ulong address, float maxTime, float currentTime, uint timesCount, uint partID)
        {
            m_Owner = owner;
            Address = address;
            Time = new Progress(maxTime, currentTime);
            PartID = partID;
            m_TimesCount = timesCount;

            PropertyChanged += MonsterPartSoften_PropertyChanged;
            Time.PropertyChanged += Time_PropertyChanged;
        }

        private void MonsterPartSoften_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PartID))
            {
                UpdateLastChangedTime();
            }
        }

        private void Time_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            UpdateLastChangedTime();
        }

        public static bool IsIncluded(string groupId)
        {
            return ConfigHelper.Main.Values.Overlay.MonsterWidget.MatchIncludePartSoftenGroupIdRegex(groupId);
        }
    }
}
