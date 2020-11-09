using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace NoLifeKing_TwitchBot
{
    public partial class Program
    {
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
                if (item["game"] != null)
                {
                    switch (item["game"].ToString())
                    {
                        case "APEX":
                            PlayerStats.OverwolfApexData((JObject)item["data"]);
                            break;
                    }
                }
            }
        }
    }
}
