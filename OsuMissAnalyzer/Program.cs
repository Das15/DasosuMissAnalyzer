using BMAPI.v1;
using BMAPI.v1.Events;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OsuMissAnalyzer
{
    class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
#if !DEBUG
            try
            {
#endif
                ConfigFileCheck();
                Debug.Print("Starting MissAnalyser... ");
                String replayPath = null, beatmap = null;
                if (args.Length > 0 && args[0].EndsWith(".osr"))
                {
                    replayPath = args[0];
                    Debug.Print("Found [{0}]", args[0]);
                    if (args.Length > 1 && args[1].EndsWith(".osu"))
                    {
                        Debug.Print("Found [{0}]", args[1]);
                        beatmap = args[1];
                    }
                }
                else if (args.Length > 1 && args[1].EndsWith(".osr"))  // Necessary to support drag & drop
                {
                    replayPath = args[1];
                }
                MissAnalyzer missAnalyzer = new MissAnalyzer(replayPath, beatmap);       
                missAnalyzer.InitializeComponent();
                Application.Run(missAnalyzer);
#if !DEBUG
            }
            catch (Exception e)
            {
                if (e is FileNotFoundException)
                {
                    MessageBox.Show("uwu it works", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                MessageBox.Show("Error: " + e.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                string[] errorMessage = { "Message: " + e.Message, 
                                          "Source: " + e.Source + ", " + e.TargetSite,
                                          "Exception: " + e.InnerException,
                                          "Stacktrace: " + e.StackTrace };
                File.WriteAllLines("error.txt", errorMessage);
            }
#endif
        }
        public static Beatmap GetBeatmapFromHash(string dir, MissAnalyzer missAnalyzer, bool songsdir = true)
        {
            Debug.Print("\nChecking API Key...");
            JArray apiString = JArray.Parse("[]");
            if (missAnalyzer.options.Settings.ContainsKey("apikey"))
            {
                Debug.Print("Found API key, searching for beatmap...");

                using (WebClient w = new WebClient())
                {
                    apiString = JArray.Parse(w.DownloadString("https://osu.ppy.sh/api/get_beatmaps" +
                                                            "?k=" + missAnalyzer.options.Settings["apikey"] +
                                                            "&h=" + missAnalyzer.replay.MapHash));
                }
            }
            else
            {
                Debug.Print("No API key found, searching manually. It could take a while...");
                Thread t = new Thread(() =>
                               MessageBox.Show("No API key found, searching manually. It could take a while..."));
            }
            if (songsdir)
            {
                string[] folders;

                if (apiString.Count > 0) folders = Directory.GetDirectories(dir, apiString[0]["beatmapset_id"] + "*");
                else folders = Directory.GetDirectories(dir);

                foreach (string folder in folders)
                {
                    Beatmap map = ReadFolder(folder, apiString.Count > 0 ? (string)apiString[0]["beatmap_id"] : null, missAnalyzer);
                    if (map != null) return map;
                }
            }
            else
            {
                Beatmap map = ReadFolder(dir, apiString.Count > 0 ? (string)apiString[0]["beatmap_id"] : null, missAnalyzer);
                if (map != null) return map;
            }
            return null;
        }
        private static Beatmap ReadFolder(string folder, string id, MissAnalyzer missAnalyzer)
        {
            foreach (string file in Directory.GetFiles(folder, "*.osu"))
            {
                using (StreamReader f = new StreamReader(file))
                {
                    string line = f.ReadLine();
                    if (line == null)
                        continue;
                    while (!f.EndOfStream
                           && !line.StartsWith("BeatmapID"))
                    {
                        line = f.ReadLine();
                    }
                    if (line.StartsWith("BeatmapID") && id != null)
                    {
                        if (line.Substring(10) == id)
                        {
                            return new Beatmap(file);
                        }
                    }
                    else
                    {
                        if (missAnalyzer.replay.MapHash == Beatmap.MD5FromFile(file))
                        {
                            return new Beatmap(file);
                        }
                    }
                }
            }
            return null;
        }
        private static void ConfigFileCheck()
        {
            if (!File.Exists("options.cfg"))
            {
                File.Create("options.cfg").Close();
                Console.ForegroundColor = ConsoleColor.Green;
                Debug.Print("\nCreating options.cfg... ");
                Debug.Print("- In options.cfg, you can define various settings that impact the program. ");
                Debug.Print("- To add these to options.cfg, add a new line formatted <Setting Name>=<Value> ");
                Debug.Print("- Available settings : SongsDir | Value = Specify osu!'s songs dir.");
                Debug.Print("-                       APIKey  | Value = Your osu! API key (https://osu.ppy.sh/api/");
                Debug.Print("-                       OsuDir  | Value = Your osu! directory");

                Console.ResetColor();
            }
        }
    }
}
