using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OsuMissAnalyzer
{
    class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            try
            {
                Debug.Print("Starting MissAnalyser... ");
                String replay = null, beatmap = null;
                if (args.Length > 0 && args[0].EndsWith(".osr"))
                {
                    replay = args[0];
                    Debug.Print("Found [{0}]", args[0]);
                    if (args.Length > 1 && args[1].EndsWith(".osu"))
                    {
                        Debug.Print("Found [{0}]", args[1]);
                        beatmap = args[1];
                    }
                }
                else if (args.Length > 1 && args[1].EndsWith(".osr"))  // Necessary to support drag & drop
                {
                    replay = args[1];
                }
                MissAnalyzer missAnalyzer = new MissAnalyzer(replay, beatmap);       
                missAnalyzer.InitializeComponent();
                Application.Run(missAnalyzer);
            }
            catch (Exception e)
            {
                MessageBox.Show("Error: " + e.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

        }
    }
}
