using Discord.WebSocket;
using KeyVault.Client;
using Microsoft.AspNetCore.Hosting;
using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TwitchLib.Api;
using TwitchLib.Client;
using TwitchLib.Client.Models;
using TwitchLib.PubSub;
using WebSocketSharp.Server;

namespace NoLifeKing_TwitchBot
{
    public partial class Program
    {
        static TwitchAPI TwitchAPIClient;
        static TwitchClient TwitchIRCClient;
        static TwitchPubSub TwitchPubSubClient;
        static WebSocketServer WebsocketServer;
        static JoinedChannel channel = null;

        static DiscordSocketClient DiscordSocketClient;

        static KeyVaultClient KeyVaultClient;

        static ApexPlayerStats PlayerStats = new ApexPlayerStats();

        internal static string TwitchIRCName;

        public static string VerificationCode = "";
        public static string VerificationState = "";

        const string CommandIdentifier = "!";

        static string[] TwitchScopes = new[] {
            "analytics:read:extensions",
            "analytics:read:games",
            "bits:read",
            "channel:edit:commercial",
            "channel:manage:broadcast",
            "channel:moderate",
            "channel:read:hype_train",
            "channel:read:redemptions",
            "channel:read:stream_key",
            "channel:read:subscriptions",
            "chat:edit",
            "chat:read",
            "clips:edit",
            "user:edit",
            "user:edit:follows",
            "user:read:broadcast",
            "user:read:email",
            "whispers:edit",
            "whispers:read",
            "channel_read",
            "user_follows_edit",
            "channel_editor",
            "channel_commercial",
            "channel_subscriptions"
        };
        private static IWebHost host;
#if DEBUG
        const string TwitchRedirectUri = "http://localhost:51145/twitch_auth";
#else
        const string TwitchRedirectUri = "https://twitch-bot.itssimple.se/twitch_auth";
#endif

        async static Task Main(string[] args)
        {
            if (!await SetupKeyVaultClient(args))
            {
                return;
            }

            CancellationTokenSource cts = new CancellationTokenSource();

            var server = SetupWebserver(cts.Token);

            var channelId = await SetTwitchAPIClient();

            SetupIRCClient();
            SetupPubSubClient(channelId);

            SetupWebsocketServer();

            await SetupDiscordBot();

            Console.ReadLine();

            LogToConsole("Shutting down Twitch bot, IRC, PubSub, WebSocket and WebServer");

            TwitchIRCClient.Disconnect();
            TwitchPubSubClient.Disconnect();

            await DiscordSocketClient.LogoutAsync();
            DiscordSocketClient?.Dispose();

            WebsocketServer.Stop();

            cts.Cancel();

            LogToConsole("Everything shut down, thank you!");
        }

        private static async Task SetupDiscordBot()
        {
            DiscordSocketClient = new DiscordSocketClient();
            DiscordSocketClient.Log += (args) =>
            {
                LogToConsole(args);
                return Task.CompletedTask;
            };

            DiscordSocketClient.MessageReceived += DiscordSocketClient_MessageReceived;

            DiscordSocketClient.Ready += DiscordSocketClient_Ready;

            var botToken = await KeyVaultClient.GetSecretAsync("DiscordBotToken");

            await DiscordSocketClient.LoginAsync(Discord.TokenType.Bot, botToken);
            await DiscordSocketClient.StartAsync();
        }

        private static Task DiscordSocketClient_MessageReceived(SocketMessage arg)
        {
            if (arg.Author.IsBot || arg.Author.Id == DiscordSocketClient.CurrentUser.Id) return Task.CompletedTask;

            if (arg.Content.StartsWith(CommandIdentifier))
            {
                var commandData = arg.Content.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                var command = commandData[0].Replace(CommandIdentifier, "");
                var args = commandData.Skip(1).ToArray();
                HandleCommands(false, command, args);
            }

            //arg.Channel.SendMessageAsync($"You wrote: {arg.Content}");

            //LogToConsole(arg);
            return Task.CompletedTask;
        }

        private static Task DiscordSocketClient_Ready()
        {
            LogToConsole("Discord bot is ready");
            return Task.CompletedTask;
        }

        private static Task SetupWebserver(CancellationToken ct)
        {
            host = new WebHostBuilder()
                .UseKestrel()
                .UseUrls("http://localhost:51145")
                .UseStartup<Startup>()
                .Build();

            return host.RunAsync(ct);
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

                Console.WriteLine($"Authenticated with KeyVault-API: {checkCert}");
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

            string accessToken = await FetchTwitchAccessToken(TwitchAPIClient.Settings.ClientId, TwitchAPIClient.Settings.Secret);

            TwitchAPIClient.Settings.AccessToken = accessToken;

            var user = (await TwitchAPIClient.Helix.Users.GetUsersAsync(new System.Collections.Generic.List<string>() { "101962985" })).Users.First();

            LogToConsole($"Logged in and authenticated as {user.DisplayName}");

            return user.Id;
        }

        private async static Task<string> FetchTwitchAccessToken(string clientId, string secret)
        {
            using var client = new HttpClient();

            var previousAccessToken = await KeyVaultClient.GetSecretAsync("TwitchAccessToken");
            var refreshToken = await KeyVaultClient.GetSecretAsync("TwitchRefreshToken");

            bool tokenStillValid = false;

            if (!string.IsNullOrWhiteSpace(previousAccessToken))
            {
                tokenStillValid = true;

                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("OAuth", previousAccessToken);
                var validateOldToken = await client.GetAsync("https://id.twitch.tv/oauth2/validate");

                var content = await validateOldToken.Content.ReadAsStringAsync();

                client.DefaultRequestHeaders.Authorization = null;

                if (!validateOldToken.IsSuccessStatusCode)
                {
                    if (!string.IsNullOrWhiteSpace(refreshToken))
                    {
                        var res = await client.PostAsync(
                            $"https://id.twitch.tv/oauth2/token?client_id={clientId}&client_secret={secret}&grant_type=refresh_token&refresh_token={refreshToken}",
                            new StringContent("")
                        );

                        if (res.IsSuccessStatusCode)
                        {
                            LogToConsole("Managed to login to Twitch!");
                            var json = await res.Content.ReadAsStringAsync();

                            dynamic tokenData = JsonConvert.DeserializeObject(json);

                            await KeyVaultClient.SaveSecretAsync("TwitchAccessToken", tokenData.access_token.ToString());
                            await KeyVaultClient.SaveSecretAsync("TwitchRefreshToken", tokenData.refresh_token.ToString());

                            return tokenData.access_token;
                        }
                    }
                    else
                    {
                        tokenStillValid = false;
                    }
                }
                else
                {
                    return previousAccessToken;
                }
            }

            if (!tokenStillValid)
            {
                var state = Convert.ToBase64String(Encoding.Default.GetBytes(DateTime.UtcNow.Ticks.ToString()));
                var authUrl = $"https://id.twitch.tv/oauth2/authorize?response_type=code&client_id={clientId}&redirect_uri={TwitchRedirectUri}&scope={string.Join("+", TwitchScopes)}&state={state}";

#if DEBUG
                var psi = new ProcessStartInfo
                {
                    FileName = authUrl,
                    UseShellExecute = true
                };

                using var process = Process.Start(psi);
#else
                Console.WriteLine("Please open this link to fix the authentication for the bot.");
                Console.WriteLine(authUrl);
#endif
                while (string.IsNullOrWhiteSpace(VerificationCode))
                {
                    Thread.Sleep(500);
                }

                if (state != VerificationState)
                {
                    LogToConsole("Verifying state failed");
                    throw new Exception("Invalid state");
                }

                var res = await client.PostAsync(
                    $"https://id.twitch.tv/oauth2/token?client_id={clientId}&client_secret={secret}&code={VerificationCode}&grant_type=authorization_code&redirect_uri={TwitchRedirectUri}",
                    new StringContent("")
                );

                if (res.IsSuccessStatusCode)
                {
                    LogToConsole("Managed to login to Twitch!");
                    var json = await res.Content.ReadAsStringAsync();

                    dynamic tokenData = JsonConvert.DeserializeObject(json);

                    await KeyVaultClient.SaveSecretAsync("TwitchAccessToken", tokenData.access_token.ToString());
                    await KeyVaultClient.SaveSecretAsync("TwitchRefreshToken", tokenData.refresh_token.ToString());

                    return tokenData.access_token;
                }

                var errorMsg = await res.Content.ReadAsStringAsync();
                LogToConsole("Error when authenticating to Twitch");
                LogToConsole(errorMsg);
            }

            throw new Exception("Could not authenticate with Twitch");
        }

        private static void SetupIRCClient()
        {
            TwitchIRCClient = new TwitchClient();
            var creds = new ConnectionCredentials(TwitchIRCName, TwitchAPIClient.Settings.AccessToken);

            TwitchIRCClient.OnJoinedChannel += (sender, args) => channel = TwitchIRCClient.GetJoinedChannel(args.Channel);
            TwitchIRCClient.OnLog += (sender, args) => HandleIRCLog(args);

            TwitchIRCClient.OnChatCommandReceived += TwitchIRCClient_OnChatCommandReceived;

            TwitchIRCClient.AutoReListenOnException = true;
            TwitchIRCClient.OnConnectionError += TwitchIRCClient_OnConnectionError;

            TwitchIRCClient.Initialize(creds, "nolifeking85");
            TwitchIRCClient.Connect();
        }

        private static void TwitchIRCClient_OnConnectionError(object sender, TwitchLib.Client.Events.OnConnectionErrorArgs e)
        {
            TwitchIRCClient.Reconnect();
        }

        private static void TwitchIRCClient_OnChatCommandReceived(object sender, TwitchLib.Client.Events.OnChatCommandReceivedArgs e)
        {
            if (e.Command.CommandIdentifier != '!') return;

            HandleCommands(true, e.Command.CommandText, e.Command.ArgumentsAsList.ToArray());

            LogToConsole(e);
        }

        private static void HandleCommands(bool isIRC, string commandIdentifier, params string[] arguments)
        {
            switch (commandIdentifier)
            {
                case "discord":
                    if (isIRC)
                    {
                        TwitchIRCClient.SendMessage(channel, "You can join my discord by clicking this link: https://discord.gg/6fP8vWW");
                    }
                    break;
                case "commands":
                    if (isIRC)
                    {
                        TwitchIRCClient.SendMessage(channel, "!discord, !commands (this command)");
                    }
                    break;
            }
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
            LogToConsole($"{logData.GetType().Name}\n{JsonConvert.SerializeObject(logData, Formatting.Indented, new JsonSerializerSettings() { ReferenceLoopHandling = ReferenceLoopHandling.Ignore })}\n");
        }

        private static void LogToConsole(string jsonData)
        {
            Console.WriteLine($"{DateTime.Now} :: {jsonData}");
        }
    }
}
