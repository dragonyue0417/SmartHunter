namespace SmartHunter.Game.Data
{
    public class DamagePoint
    {
        public long TimeStamp { get; set; }
        public int Damage { get; set; }

        public DamagePoint(long timeStamp, int damage)
        {
            TimeStamp = timeStamp;
            Damage = damage;
        }

        public DamagePoint()
        {
        }
    }
}
