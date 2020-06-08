using System.IO;

namespace NoLifeKing_TwitchBot
{
    public class ApexPlayerStats
    {
        public string LocalPlayerName { get; set; }

        public int Deaths { get; set; } = 0;
        public int KnockedOut { get; set; } = 0;

        public int Assists { get; set; } = 0;
        public int Knockdowns { get; set; } = 0;
        public int Kills { get; set; } = 0;
        public int SquadKills { get; set; } = 0;

        public double TotalDamageDealt { get; set; } = 0;
        public int Headshots { get; set; } = 0;

        public int Wins { get; set; } = 0;
        public int Losses { get; set; } = 0;

        public bool CurrentlyAlive { get; set; } = true;

        private string LastStats { get; set; }

        public void SaveToStreamFile()
        {
            var apexStats =
$@"Times died:        {Deaths}
Times knocked out: {KnockedOut}

Kills:             {Kills}
Knockdowns:        {Knockdowns}
Assists:           {Assists}

Squad kills:       {SquadKills}

Damage dealt:      {TotalDamageDealt}
Headshots:         {Headshots}

Wins:              {Wins}
Losses:            {Losses}";

            if (LastStats != apexStats)
            {
                LastStats = apexStats;
                File.WriteAllText(@"C:\stream\apexStats.txt", apexStats);
            }
        }
    }
}
