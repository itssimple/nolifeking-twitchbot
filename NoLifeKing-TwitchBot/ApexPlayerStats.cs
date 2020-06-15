using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO;
using System.Linq;

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

        public ApexPlayerStats()
        {
            SaveToStreamFile();
        }

        public void OverwolfApexData(JObject item)
        {
            var ignoredFeatures = new string[] { "location" };

            if (item["feature"] != null && !ignoredFeatures.Contains(item["feature"].ToString()))
            {
                switch (item["feature"].ToString())
                {
                    case "rank":
                        if (!string.IsNullOrWhiteSpace(item["info"]?["match_info"]?["victory"]?.ToString()))
                        {
                            if ((bool)item["info"]["match_info"]["victory"])
                            {
                                Wins++;
                            }
                            else
                            {
                                Losses++;
                            }
                        }
                        break;
                    case "match_summary":
                        if (!string.IsNullOrWhiteSpace(item["info"]?["match_info"]?["match_summary"]?.ToString()))
                        {
                            var jsObj = (JObject)JsonConvert.DeserializeObject(item["info"]?["match_info"]?["match_summary"]?.ToString());
                            SquadKills += (int)jsObj["squadKills"];
                        }
                        break;
                    default:
                        //LogToConsole(item);
                        break;
                }

                SaveToStreamFile();
            }
            else
            {
                if (item["events"] != null)
                {
                    foreach (var eventItem in (JArray)item["events"])
                    {
                        var eventItemData = (JObject)JsonConvert.DeserializeObject(eventItem["data"].ToString());

                        switch (eventItem["name"].ToString())
                        {
                            case "match_start":
                                CurrentlyAlive = true;
                                break;
                            case "match_end":
                                CurrentlyAlive = false;
                                break;
                            case "death":
                                Deaths++;
                                CurrentlyAlive = false;
                                break;
                            case "respawn":
                                CurrentlyAlive = true;
                                break;
                            case "healed_from_ko":
                                CurrentlyAlive = true;
                                break;
                            case "knocked_out":
                                KnockedOut++;
                                break;
                            case "kill":
                                Kills++;
                                break;
                            case "knockdown":
                                Knockdowns++;
                                break;
                            case "assist":
                                Assists++;
                                break;
                            case "damage":
                                if (CurrentlyAlive)
                                {
                                    TotalDamageDealt += (double)eventItemData["damageAmount"];
                                    if ((bool)eventItemData["headshot"])
                                    {
                                        Headshots++;
                                    }
                                }
                                break;
                            case "kill_feed":
                                if (!string.IsNullOrWhiteSpace(eventItemData["local_player_name"]?.ToString()))
                                {
                                    LocalPlayerName = eventItemData["local_player_name"].ToString();
                                }

                                if (eventItemData["attackerName"].ToString() == LocalPlayerName)
                                {
                                    var killActions = new string[] { "kill", "Bleed Out" };
                                    if (killActions.Contains(eventItemData["action"].ToString()))
                                    {
                                        Kills++;
                                    }
                                }
                                break;
                            default:
                                //LogToConsole(eventItemData);
                                break;
                        }
                    }

                    SaveToStreamFile();
                }
            }
        }

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
