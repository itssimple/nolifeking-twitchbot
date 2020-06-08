using KeyVault.Client;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using TwitchLib.Api;
using TwitchLib.Client;
using TwitchLib.Client.Models;
using TwitchLib.PubSub;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace NoLifeKing_TwitchBot
{
    class Program
    {
        static TwitchAPI TwitchAPIClient;
        static TwitchClient TwitchIRCClient;
        static TwitchPubSub TwitchPubSubClient;
        static WebSocketServer WebsocketServer;
        static JoinedChannel channel = null;

        static KeyVaultClient KeyVaultClient;

        static ApexPlayerStats PlayerStats = new ApexPlayerStats();

        internal static string TwitchIRCName;

        async static Task Main(string[] args)
        {
            if (!await SetupKeyVaultClient(args))
            {
                return;
            }

            var channelId = await SetTwitchAPIClient();

            SetupIRCClient();
            SetupPubSubClient(channelId);

            SetupWebsocketServer();

            Console.ReadLine();

            TwitchIRCClient.Disconnect();
            TwitchPubSubClient.Disconnect();

            WebsocketServer.Stop();
        }

        private async static Task<bool> SetupKeyVaultClient(string[] args)
        {
            if (args.Length < 3)
            {
                ShowUsage();
                return false;
            }

            TwitchIRCName = args[0];
            string clientId = args[1];
            string certPath = args[2];

            X509Certificate2 cert;

            if (!File.Exists(certPath))
            {
                Console.WriteLine("The file doesn't exist, please make sure that you point the path to your certificate.");
                return false;
            }
            else
            {
                cert = new X509Certificate2(File.ReadAllBytes(certPath));

                if (!cert.HasPrivateKey)
                {
                    Console.WriteLine("This certificate does not contain the private key (needed to decrypt the stored data)");
                    return false;
                }

                KeyVaultClient = new KeyVaultClient(clientId, cert);

                var checkCert = await KeyVaultClient.WhoAmI();
                if (string.IsNullOrWhiteSpace(checkCert))
                {
                    Console.WriteLine("Could not authenticate with KeyVault-API");
                    return false;
                }

                Console.WriteLine("Authenticated with KeyVault-API");
            }

            return true;
        }

        private static void ShowUsage()
        {
            Console.WriteLine("Usage: NoLifeKing-TwitchBot <twitch-name> <keyvault clientid> <keyvault certificate>");
        }

        private async static Task<string> SetTwitchAPIClient()
        {
            TwitchAPIClient = new TwitchAPI();

            TwitchAPIClient.Settings.ClientId = await KeyVaultClient.GetSecretAsync("TwitchClientId");
            TwitchAPIClient.Settings.Secret = await KeyVaultClient.GetSecretAsync("TwitchSecret");
            TwitchAPIClient.Settings.AccessToken = await KeyVaultClient.GetSecretAsync("TwitchAccessToken");

            var user = await TwitchAPIClient.V5.Users.GetUserAsync();
            var authedChannel = await TwitchAPIClient.V5.Channels.GetChannelAsync();
            var channelId = authedChannel.Id;

            LogToConsole($"Logged in and authenticated as {user.DisplayName}");

            return channelId;
        }

        private static void SetupIRCClient()
        {
            TwitchIRCClient = new TwitchClient();
            var creds = new ConnectionCredentials(TwitchIRCName, TwitchAPIClient.Settings.AccessToken);

            TwitchIRCClient.OnJoinedChannel += (sender, args) => channel = TwitchIRCClient.GetJoinedChannel(args.Channel);
            TwitchIRCClient.OnLog += (sender, args) => HandleIRCLog(args);

            TwitchIRCClient.Initialize(creds, TwitchIRCName);
            TwitchIRCClient.Connect();
        }

        private static void SetupPubSubClient(string channelId)
        {
            TwitchPubSubClient = new TwitchPubSub();

            TwitchPubSubClient.OnPubSubServiceConnected += (sender, args) =>
            {
                TwitchPubSubClient.ListenToBitsEvents(channelId);
                TwitchPubSubClient.ListenToRewards(channelId);
                TwitchPubSubClient.ListenToFollows(channelId);
                TwitchPubSubClient.ListenToSubscriptions(channelId);
                TwitchPubSubClient.ListenToRaid(channelId);

                TwitchPubSubClient.SendTopics(TwitchAPIClient.Settings.AccessToken);
            };

            TwitchPubSubClient.OnRewardRedeemed += (sender, args) => HandleReward(args);
            TwitchPubSubClient.OnBitsReceived += (sender, args) => HandleBits(args);
            TwitchPubSubClient.OnChannelSubscription += TwitchPubSubClient_OnChannelSubscription;
            TwitchPubSubClient.OnRaidGo += (sender, args) => HandleRaid(args);
            TwitchPubSubClient.OnHost += (sender, args) => HandleHost(args);
            TwitchPubSubClient.OnLog += (sender, args) => HandlePubSubLog(args);

            TwitchPubSubClient.Connect();
        }

        private static void SetupWebsocketServer()
        {
            WebsocketServer = new WebSocketServer("ws://localhost:61337");
            WebsocketServer.AddWebSocketService<OverWolfBehavior>("/overwolf");
            WebsocketServer.Start();
        }

        private static void HandleIRCLog(TwitchLib.Client.Events.OnLogArgs args)
        {
            if (args.Data.Contains("PING :tmi.twitch.tv") || args.Data.Contains("PONG")) return;
            LogToConsole(args);
        }

        private static void HandlePubSubLog(TwitchLib.PubSub.Events.OnLogArgs args)
        {
            if (args.Data.Contains("PONG")) return;

            LogToConsole(JsonConvert.DeserializeObject(args.Data));
        }

        private static void HandleHost(TwitchLib.PubSub.Events.OnHostArgs args)
        {
            LogToConsole(args);
        }

        private static void HandleRaid(TwitchLib.PubSub.Events.OnRaidGoArgs args)
        {
            LogToConsole(args);
        }

        private static void TwitchPubSubClient_OnChannelSubscription(object sender, TwitchLib.PubSub.Events.OnChannelSubscriptionArgs e)
        {
            LogToConsole(e);
        }

        private static void HandleBits(TwitchLib.PubSub.Events.OnBitsReceivedArgs args)
        {
            LogToConsole(args);
        }

        private static void HandleReward(TwitchLib.PubSub.Events.OnRewardRedeemedArgs args)
        {
            if (args.Status == "ACTION_TAKEN")
            {
                TwitchIRCClient.SendMessage(channel, $"I completed '{args.RewardTitle}' redeemed by @{args.DisplayName}!");
            }
            else
            {
                LogToConsole(args);
            }
        }

        private static void LogToConsole<T>(T logData)
        {
            LogToConsole($"{logData.GetType().Name}\n{JsonConvert.SerializeObject(logData, Formatting.Indented)}\n");
        }

        private static void LogToConsole(string jsonData)
        {
            Console.WriteLine($"{DateTime.Now.ToString()} :: {jsonData}");
        }

        public class OverWolfBehavior : WebSocketBehavior
        {
            protected override void OnMessage(MessageEventArgs e)
            {
                var data = (JObject)JsonConvert.DeserializeObject(e.Data);

                HandleOverWolfEvents(data);
                LogToConsole(data);
                base.OnMessage(e);
            }

            private void HandleOverWolfEvents(JObject item)
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
                                    PlayerStats.Wins++;
                                }
                                else
                                {
                                    PlayerStats.Losses++;
                                }
                            }
                            break;
                        case "match_summary":
                            if (!string.IsNullOrWhiteSpace(item["info"]?["match_info"]?["match_summary"]?.ToString()))
                            {
                                var jsObj = (JObject)JsonConvert.DeserializeObject(item["info"]?["match_info"]?["match_summary"]?.ToString());
                                PlayerStats.SquadKills += (int)jsObj["squadKills"];
                            }
                            break;
                        default:
                            //LogToConsole(item);
                            break;
                    }

                    PlayerStats.SaveToStreamFile();
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
                                    PlayerStats.CurrentlyAlive = true;
                                    break;
                                case "match_end":
                                    PlayerStats.CurrentlyAlive = false;
                                    break;
                                case "death":
                                    PlayerStats.Deaths++;
                                    PlayerStats.CurrentlyAlive = false;
                                    break;
                                case "respawn":
                                    PlayerStats.CurrentlyAlive = true;
                                    break;
                                case "healed_from_ko":
                                    PlayerStats.CurrentlyAlive = true;
                                    break;
                                case "knocked_out":
                                    PlayerStats.KnockedOut++;
                                    break;
                                case "kill":
                                    PlayerStats.Kills++;
                                    break;
                                case "knockdown":
                                    PlayerStats.Knockdowns++;
                                    break;
                                case "assist":
                                    PlayerStats.Assists++;
                                    break;
                                case "damage":
                                    if (PlayerStats.CurrentlyAlive)
                                    {
                                        PlayerStats.TotalDamageDealt += (double)eventItemData["damageAmount"];
                                        if ((bool)eventItemData["headshot"])
                                        {
                                            PlayerStats.Headshots++;
                                        }
                                    }
                                    break;
                                case "kill_feed":
                                    if (!string.IsNullOrWhiteSpace(eventItemData["local_player_name"]?.ToString()))
                                    {
                                        PlayerStats.LocalPlayerName = eventItemData["local_player_name"].ToString();
                                    }

                                    if (eventItemData["attackerName"].ToString() == PlayerStats.LocalPlayerName)
                                    {
                                        var killActions = new string[] { "kill", "Bleed Out" };
                                        if (killActions.Contains(eventItemData["action"].ToString()))
                                        {
                                            PlayerStats.Kills++;
                                        }
                                    }
                                    break;
                                default:
                                    //LogToConsole(eventItemData);
                                    break;
                            }
                        }

                        PlayerStats.SaveToStreamFile();
                    }
                }
            }
        }
    }
}
