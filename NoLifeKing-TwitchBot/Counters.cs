using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace NoLifeKing_TwitchBot
{
    public class Counters
    {
        private Dictionary<string, long> _counters { get; set; } = new Dictionary<string, long>();
        public Counters()
        {
            if (!File.Exists("counters.json"))
            {
                File.WriteAllText("counters.json", JsonConvert.SerializeObject(_counters));
            }
            else
            {
                _counters = JsonConvert.DeserializeObject<Dictionary<string, long>>(File.ReadAllText("counters.json"));
            }
        }

        public async Task<long> ChangeValueAsync(string counter, long value)
        {
            if (!_counters.ContainsKey(counter))
            {
                _counters.TryAdd(counter, 0);
            }

            _counters[counter] += value;

            await SaveCounters();

            return _counters[counter];
        }

        public bool HasCounter(string counter)
        {
            return _counters.ContainsKey(counter);
        }

        internal async Task SaveCounters()
        {
            await File.WriteAllTextAsync("counters.json", JsonConvert.SerializeObject(_counters));
        }
    }
}
