using System;
using System.Collections.ObjectModel;
using SmartHunter.Core.Data;

namespace SmartHunter.Game.Data
{
    public class Player : Bindable, IComparable<Player>
    {
        int m_Index;

        public int Index
        {
            get { return m_Index; }
            set { SetProperty(ref m_Index, value); }
        }

        string m_Name;
        public string Name
        {
            get { return m_Name; }
            set { SetProperty(ref m_Name, value); }
        }

        int m_Damage;
        public int Damage
        {
            get { return m_Damage; }
            set
            {
                if (m_Damage != value)
                {
                    UpdateDamagePoint(value);
                }

                SetProperty(ref m_Damage, value);
            }
        }

        float m_DamageFraction;
        public float DamageFraction
        {
            get { return m_DamageFraction; }
            set { SetProperty(ref m_DamageFraction, value); }
        }

        float m_BarFraction;
        public float BarFraction
        {
            get { return m_BarFraction; }
            set { SetProperty(ref m_BarFraction, value); }
        }

        private void UpdateDamagePoint(int damage)
        {
            var timestamp = DateTime.Now.ToFileTime();
            DamagePoints.Add(new DamagePoint(timestamp, damage));
        }

        public ObservableCollection<DamagePoint> DamagePoints { get; } = new ObservableCollection<DamagePoint>();

        public int CompareTo(Player other)
        {
            return other == null ? 1 : other.Damage.CompareTo(Damage);
        }

        public override string ToString()
        {
            return String.Format("{0} {1} {2}%", this.Name, this.Damage, (this.DamageFraction * 100).ToString("0.##"));
        }
    }
}
