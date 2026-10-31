using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SmartHunter.Game.Helpers;

namespace SmartHunter.Core.Helpers
{
    public sealed class ServerManager
    {
        public static string LegacyEndpoint(string value)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme!="http" && uri.Scheme!="https") || !String.IsNullOrEmpty(uri.UserInfo)) return null;
            return new Uri(uri.GetLeftPart(UriPartial.Authority)+"/index.php").AbsoluteUri;
        }
        readonly SmartHunter.Wilds.WildsRelayDiagnostics diagnostics = new SmartHunter.Wilds.WildsRelayDiagnostics(() => Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency);
        public void Applied(int count, int skipped) => diagnostics.Match(new SmartHunter.Wilds.WildsRelayMatchReport { Applied=count, Skipped=skipped });
        int generation;
        public void Invalidate() { Interlocked.Increment(ref generation); IsServerOline = 0; diagnostics.ResetMatch(); diagnostics.State("Disabled"); }
        public enum Command
        {
            ALIVE,
            HELLO,
            DONE,
            CHECK,
            PUSH,
            ELEVATE,
            DAMAGE,
            PULL
        }

        public int IsServerOline = 0;

        private string Version
        {
            get
            {
                return "1.2-1";
            }
        }
        private static string commandToStr(Command cmd)
        {
            if (cmd == Command.ALIVE)
            {
                return "alive";
            }
            else if (cmd == Command.HELLO)
            {
                return "hello";
            }else if (cmd == Command.DONE)
            {
                return "done";
            }
            else if (cmd == Command.CHECK)
            {
                return "check";
            }
            else if (cmd == Command.PUSH)
            {
                return "push";
            }
            else if (cmd == Command.PULL)
            {
                return "pull";
            }
            else if (cmd == Command.ELEVATE)
            {
                return "elevate";
            }
            else if (cmd == Command.DAMAGE)
            {
                return "damage";
            }
            return "";
        }

        private static readonly ServerManager instance = new ServerManager();
        readonly HttpClient m_Client;

        public static Dictionary<Command, long[]> Stats { get; set; }

        static ServerManager()
        {
            Stats = new Dictionary<Command, long[]>();
            foreach (var cmd in (Command[]) Enum.GetValues(typeof(Command)))
            {
                long[] s = new long[5];
                for (int i = 0; i < 5; i++)
                {
                    s[i] = 0;
                }
                Stats[cmd] = s;
            }
        }

        public void ResetStats()
        {
            foreach (var cmd in (Command[])Enum.GetValues(typeof(Command)))
            {
                for (int i = 0; i < 5; i++)
                {
                    Interlocked.Exchange(ref Stats[cmd][i], 0);
                }
            }
        }

        private string ConvertSize(long bytes)
        {
            string tmp = "byte";
            if (bytes >= 1024)
            {
                bytes /= 1024;
                tmp = "KB";
            }
            if (bytes >= 1024)
            {
                bytes /= 1024;
                tmp = "MB";
            }
            if (bytes >= 1024)
            {
                bytes /= 1024;
                tmp = "GB";
            }
            return $"{bytes} {tmp}";
        }

        public void PrintStats()
        {
            long total = 0;
            long failed = 0;
            long sent = 0;
            long received = 0;
            long ping = 0;
            foreach (var cmd in (Command[])Enum.GetValues(typeof(Command)))
            {
                total += Stats[cmd][0];
                failed += Stats[cmd][1];
                sent += Stats[cmd][2];
                received += Stats[cmd][3];
                ping += Stats[cmd][4];
                if (ConfigHelper.Main.Values.Debug.ShowServerLogs)
                {
                    Log.WriteLine($"Sent {Stats[cmd][0]} {commandToStr(cmd).ToUpper()}, failed {Stats[cmd][1]}, sent {ConvertSize(Stats[cmd][2])}, received {ConvertSize(Stats[cmd][3])} with an average response time of {Stats[cmd][4] / (Stats[cmd][0] > 0 ? Stats[cmd][0] : 1)} ms");
                }
            }
            Log.WriteLine($"Total network operations {total}, failed {failed}, sent {ConvertSize(sent)}, received {ConvertSize(received)} with an average response time of {ping / (total > 0 ? total : 1)} ms");
        }

        private ServerManager() : this(new HttpClient { Timeout = TimeSpan.FromSeconds(5) })
        {
        }

        internal ServerManager(HttpClient client) { m_Client = client; }

        public static ServerManager Instance
        {
            get
            {
                return instance;
            }
        }

        public async void RequestCommadWithHandler(Command cmd, string key, string player, bool isHost, int damage, string data, Action<JObject, long> callback = null, Action<Exception> onError = null)
        {
            await RequestCommandAsync(cmd, key, player, isHost, damage, data, callback, onError);
        }

        internal async Task RequestCommandAsync(Command cmd, string key, string player, bool isHost, int damage, string data, Action<JObject, long> callback = null, Action<Exception> onError = null)
        {
            int requestGeneration = generation;
            long sent=0, received=0; bool recorded=false;
            var transfer=Stopwatch.StartNew();
            Uri.TryCreate(ConfigHelper.Main.Values.ServerUrl, UriKind.Absolute, out var endpoint);
            diagnostics.Configure(endpoint);
            diagnostics.State(cmd==Command.ALIVE ? "Connecting" : isHost ? "Providing" : "Receiving");
            try
            {
                Dictionary<string, string> parameters = new Dictionary<string, string>();
                string command = commandToStr(cmd);
                parameters.Add("command", command);
                parameters.Add("key", key);
                parameters.Add("player", player);
                parameters.Add("host", isHost ? "true" : "false");
                parameters.Add("damage", damage.ToString());
                parameters.Add("data", data);
                parameters.Add("version", Version);
                using var stringContent = new StringContent(JsonConvert.SerializeObject(parameters), Encoding.UTF8, "application/json");

                sent=Encoding.UTF8.GetByteCount(await stringContent.ReadAsStringAsync());
                Interlocked.Increment(ref Stats[cmd][0]);
                Interlocked.Add(ref Stats[cmd][2], stringContent.Headers.ContentLength ?? 0);


                if (ConfigHelper.Main.Values.Debug.ShowServerLogs)
                {
                    switch (cmd)
                    {
                        case Command.PUSH:
                            Log.WriteLine($"Sending {command.ToUpper()} with data of size {stringContent.Headers.ContentLength} byte");
                            break;

                        case Command.ALIVE:
                            Log.WriteLine($"Sending {command.ToUpper()} to {ConfigHelper.Main.Values.ServerUrl} with data of size {stringContent.Headers.ContentLength} byte");
                            break;

                        default:
                            Log.WriteLine($"Sending {command.ToUpper()} with parameters {await stringContent.ReadAsStringAsync()}");
                            break;
                    }
                }

                Stopwatch stpw = new Stopwatch();
                stpw.Start();
                using HttpResponseMessage response = await m_Client.PostAsync(ConfigHelper.Main.Values.ServerUrl, stringContent);

                stpw.Stop();
                Interlocked.Add(ref Stats[cmd][4], stpw.ElapsedMilliseconds);

                if (response.IsSuccessStatusCode)
                {
                    string r = await response.Content.ReadAsStringAsync();
                    received=Encoding.UTF8.GetByteCount(r);
                    Interlocked.Add(ref Stats[cmd][3], response.Content.Headers.ContentLength ?? received);
                    if(requestGeneration==generation) { diagnostics.Record(true,(int)response.StatusCode,"",sent,received,transfer.ElapsedMilliseconds); recorded=true; }
                    if (ConfigHelper.Main.Values.Debug.ShowServerLogs)
                    {
                        if (cmd == Command.PULL)
                        {
                            Log.WriteLine($"Received {command.ToUpper()} with response of size {response.Content.Headers.ContentLength} byte");
                        }
                        else
                        {
                            Log.WriteLine($"Received {command.ToUpper()} with response {r}");
                        }
                    }
                    if (callback != null)
                    {
                        if (requestGeneration == generation) callback(JObject.Parse(r), stpw.ElapsedMilliseconds);
                    }
                }
                else
                {
                    if(requestGeneration==generation) diagnostics.Record(false,(int)response.StatusCode,"HTTP",sent,received,transfer.ElapsedMilliseconds);
                    Interlocked.Increment(ref Stats[cmd][1]);
                    if (ConfigHelper.Main.Values.Debug.ShowServerLogs)
                    {
                        Log.WriteLine($"Received {command.ToUpper()} with error code {response.StatusCode}");
                    }
                    if (callback != null)
                    {
                        if (requestGeneration == generation) callback(null, 0);
                    }
                }
            }
            catch (Exception e)
            {
                if(requestGeneration==generation && !recorded) diagnostics.Record(false,0,"NETWORK",sent,received,transfer.ElapsedMilliseconds);
                Interlocked.Increment(ref Stats[cmd][1]);
                if (ConfigHelper.Main.Values.Debug.ShowServerLogs)
                {
                    Log.WriteLine($"An error has occured while sending the request: {e.Message}");
                }
                try { if (requestGeneration == generation) onError?.Invoke(e); }
                catch (Exception callbackError) { Log.WriteException(callbackError); }
            }
        }
    }
}
