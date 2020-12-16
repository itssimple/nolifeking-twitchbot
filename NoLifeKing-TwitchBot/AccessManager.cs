using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace NoLifeKing_TwitchBot
{
    public class AccessManager
    {
        private List<string> _access { get; set; } = new List<string>();

        const string AccessFile = "access.json";
        public AccessManager()
        {
            if (!File.Exists(AccessFile))
            {
                File.WriteAllText(AccessFile, JsonConvert.SerializeObject(_access));
            }
            else
            {
                _access = JsonConvert.DeserializeObject<List<string>>(File.ReadAllText(AccessFile));
            }
        }

        public async Task AddAccessAsync(bool twitch, string member)
        {
            var memberData = $"{(twitch ? "twitch" : "discord")}-{member}";
            if (!_access.Contains(memberData))
            {
                _access.Add(memberData);
            }

            await SaveAccess();
        }

        public async Task RemoveAccessAsync(bool twitch, string member)
        {
            var memberData = $"{(twitch ? "twitch" : "discord")}-{member}";
            if (_access.Contains(memberData))
            {
                _access.Remove(memberData);
            }

            await SaveAccess();
        }

        public bool HasAccess(bool twitch, string member)
        {
            return _access.Contains($"{(twitch ? "twitch" : "discord")}-{member}");
        }

        internal async Task SaveAccess()
        {
            await File.WriteAllTextAsync(AccessFile, JsonConvert.SerializeObject(_access));
        }
    }
}
