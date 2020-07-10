using System.Collections.Generic;
using System.IO;

namespace OsuMissAnalyzer
{
	public class Options
	{
		public Dictionary<string, string> Settings { get; private set; }
        private string Path { get; set; }
        public Options(string file)
		{
			this.Path = file;
			Settings = new Dictionary<string, string>();
			using (StreamReader streamReader = new StreamReader(Path))
			{
				while (!streamReader.EndOfStream)
				{
					string[] entry = streamReader.ReadLine().Trim().Split(new char[] { '=' }, 2);
					if(entry[1].Length > 0) Settings.Add(entry[0].ToLower(), entry[1]);
				}
			}
		}
		public bool AddEntry(string entry, string value)
        {
			using (StreamWriter streamWriter = new StreamWriter(Path, true))
				streamWriter.WriteLine("{0}={1}", entry, value);

			Settings.Add(entry, value);
			return true;
        }
	}
}
