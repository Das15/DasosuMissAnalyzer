using BMAPI.v1;
using osuDodgyMomentsFinder;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace ReplayAPI
{
    public class Replay : IDisposable
    {
        // for customizing which replays to flip
        public bool AxisFlip { get; set; }
        public List<ReplayFrame> Times { get; private set; }

        public GameModes GameMode;
        public string Filename;
        public int FileFormat;
        public string MapHash;
        public string PlayerName;
        public string ReplayHash;
        public uint TotalScore;
        public UInt16 Count300;
        public UInt16 Count100;
        public UInt16 Count50;
        public UInt16 CountGeki;
        public UInt16 CountKatu;
        public UInt16 CountMiss;
        public UInt16 MaxCombo;
        public bool IsPerfect;
        public Mods Mods;
        public List<LifeFrame> LifeFrames = new List<LifeFrame>();
        public DateTime PlayTime;
        public int ReplayLength;
        public List<ReplayFrame> ReplayFrames = new List<ReplayFrame>();
        public int Seed;

        private readonly BinaryReader replayReader;
        private readonly CultureInfo culture = new CultureInfo("en-US", false);
        private bool headerLoaded;
        public bool FullLoaded { get; private set; }

        public Replay(string replayFile, bool fullLoad, bool calculateSpeed)
        {
            calculateSpeed = true;
            Filename = replayFile;
            using (replayReader = new BinaryReader(new FileStream(replayFile, FileMode.Open, FileAccess.Read, FileShare.Read)))
            {
                LoadHeader();
                if (fullLoad)
                {
                    Load();
                }
            }
            if (fullLoad && !FullLoaded)
                throw new Exception("Replay is not full but requsted to be read full.");
            if (calculateSpeed)
                CalculateCursorSpeed();
        }

        private Keys ParseKeys(string v)
        {
            return (Keys)Enum.Parse(typeof(Keys), v);
        }

        private void LoadHeader()
        {
            GameMode = (GameModes)Enum.Parse(typeof(GameModes), replayReader.ReadByte().ToString(culture));
            FileFormat = replayReader.ReadInt32();
            MapHash = replayReader.ReadNullableString();
            PlayerName = replayReader.ReadNullableString();
            ReplayHash = replayReader.ReadNullableString();
            Count300 = replayReader.ReadUInt16();
            Count100 = replayReader.ReadUInt16();
            Count50 = replayReader.ReadUInt16();
            CountGeki = replayReader.ReadUInt16();
            CountKatu = replayReader.ReadUInt16();
            CountMiss = replayReader.ReadUInt16();
            TotalScore = replayReader.ReadUInt32();
            MaxCombo = replayReader.ReadUInt16();
            IsPerfect = replayReader.ReadBoolean();
            Mods = (Mods)replayReader.ReadInt32();
            headerLoaded = true;
        }

        private void CalculateCursorSpeed()
        {
            double distance = 0;

            Times = ReplayFrames.Where(x => x.TimeDiff > 0).ToList();

            if (!ReferenceEquals(Times, null) && Times.Count > 0)
            {

                Times[0].TravelledDistance = distance;
                Times[0].TravelledDistanceDiff = 0;
                for (int i = 0; i < Times.Count - 1; ++i)
                {
                    ReplayFrame from = Times[i], to = Times[i + 1];
                    double newDist = Utils.dist(from.X, from.Y, to.X, to.Y);
                    distance += newDist;
                    to.TravelledDistance = distance;
                    to.TravelledDistanceDiff = newDist;
                }

                Times[0].Speed = 0;
                for (int i = 0; i < Times.Count - 1; ++i)
                {
                    ReplayFrame to = Times[i + 1], current = Times[i];

                    double V = (to.TravelledDistance - current.TravelledDistance) / (to.TimeDiff);
                    to.Speed = V;
                }
                Times.Last().Speed = 0;

                Times[0].Acceleration = 0;
                for (int i = 0; i < Times.Count - 1; ++i)
                {
                    ReplayFrame to = Times[i + 1], current = Times[i];

                    double A = (to.Speed - current.Speed) / (to.TimeDiff);
                    to.Acceleration = A;
                }
                Times.Last().Acceleration = 0;
            }
        }

        /// <summary>
        /// Loads Metadata if not already loaded and loads Lifedata, Timestamp, Playtime and Clicks.
        /// </summary>
        public void Load()
        {
            if (!headerLoaded)
                LoadHeader();
            if (FullLoaded)
                return;

            // Life
            string lifeData = replayReader.ReadNullableString();
            if (!string.IsNullOrEmpty(lifeData))
            {
                foreach (string lifeBlock in lifeData.Split(','))
                {
                    string[] split = lifeBlock.Split('|');
                    if (split.Length < 2)
                        continue;

                    LifeFrames.Add(new LifeFrame()
                    {
                        Time = int.Parse(split[0], culture),
                        Percentage = float.Parse(split[1], culture)
                    });
                }
            }

            Int64 ticks = replayReader.ReadInt64();
            PlayTime = new DateTime(ticks, DateTimeKind.Utc);

            ReplayLength = replayReader.ReadInt32();

            // Data
            if (ReplayLength > 0)
            {
                int lastTime = 0;
                using (MemoryStream codedStream = LZMACoder.Decompress(replayReader.BaseStream as FileStream))
                using (StreamReader sr = new StreamReader(codedStream))
                {
                    foreach (string frame in sr.ReadToEnd().Split(','))
                    {
                        if (string.IsNullOrEmpty(frame))
                            continue;

                        string[] split = frame.Split('|');
                        if (split.Length < 4)
                            continue;

                        if (split[0] == "-12345")
                        {
                            Seed = int.Parse(split[3], culture);
                            continue;
                        }

                        ReplayFrames.Add(new ReplayFrame()
                        {
                            TimeDiff = int.Parse(split[0], culture),
                            Time = int.Parse(split[0], culture) + lastTime,
                            X = float.Parse(split[1], culture),
                            Y = float.Parse(split[2], culture),
                            Keys = ParseKeys(split[3])
                        });
                        lastTime = ReplayFrames[ReplayFrames.Count - 1].Time;
                    }
                }
                FullLoaded = true;
            }

            ReplayFrames.RemoveRange(0, 3);

            // Todo: There are some extra bytes here
        }

        public void Save(string file)
        {
            using (BinaryWriter bw = new BinaryWriter(new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.Read)))
            {
                // Header
                bw.Write((byte)GameMode);
                bw.Write(FileFormat);
                bw.WriteNullableString(MapHash);
                bw.WriteNullableString(PlayerName);
                bw.WriteNullableString(ReplayHash);
                bw.Write(Count300);
                bw.Write(Count100);
                bw.Write(Count50);
                bw.Write(CountGeki);
                bw.Write(CountKatu);
                bw.Write(CountMiss);
                bw.Write(TotalScore);
                bw.Write((UInt16)MaxCombo);
                bw.Write(IsPerfect);
                bw.Write((int)Mods);

                // Life
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < LifeFrames.Count; i++)
                    sb.AppendFormat("{0}|{1},", LifeFrames[i].Time.ToString(culture), LifeFrames[i].Percentage.ToString(culture));
                bw.WriteNullableString(sb.ToString());

                bw.Write(PlayTime.ToUniversalTime().Ticks);

                // Data
                if (ReplayFrames.Count == 0)
                    bw.Write(0);
                else
                {
                    sb.Clear();
                    for (int i = 0; i < ReplayFrames.Count; i++)
                        sb.AppendFormat("{0}|{1}|{2}|{3},", ReplayFrames[i].TimeDiff.ToString(culture), ReplayFrames[i].X.ToString(culture), ReplayFrames[i].Y.ToString(culture), (int)ReplayFrames[i].Keys);
                    sb.AppendFormat("{0}|{1}|{2}|{3},", -12345, 0, 0, Seed);
                    byte[] rawBytes = Encoding.ASCII.GetBytes(sb.ToString());
                    using (MemoryStream ms = new MemoryStream())
                    {
                        ms.Write(rawBytes, 0, rawBytes.Length);

                        MemoryStream codedStream = LZMACoder.Compress(ms);

                        byte[] rawBytesCompressed = new byte[codedStream.Length];
                        codedStream.Read(rawBytesCompressed, 0, rawBytesCompressed.Length);
                        bw.Write(rawBytesCompressed.Length - 8);
                        bw.Write(rawBytesCompressed);
                    }
                }
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected void Dispose(bool state)
        {
            if (replayReader != null)
                replayReader.Close();
            ReplayFrames.Clear();
            LifeFrames.Clear();
        }

        public void Flip()
        {
            AxisFlip = !AxisFlip;
            ReplayFrames.ForEach((t) => t.Y = 384 - t.Y);
        }

        public override string ToString()
        {
            return this.PlayerName + " +" + Mods.ToString() + " on " + this.PlayTime;
        }

        public string SaveText(Beatmap map = null)
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine(ToString());
            sb.AppendLine("Count 300: " + Count300);
            sb.AppendLine("Count 100: " + Count100);
            sb.AppendLine("Count 50: " + Count50);

            sb.AppendLine("Count Geki: " + CountGeki);
            sb.AppendLine("Count Katu: " + CountKatu);
            sb.AppendLine("Count Miss: " + CountMiss);

            sb.AppendLine("Total Score: " + TotalScore);
            sb.AppendLine("Max Combo: " + MaxCombo);
            sb.AppendLine("Is fullcombo: " + IsPerfect);

            sb.AppendLine("Mods: " + Mods.ToString());

            List<HitFrame> hits = null;
            List<HitFrame> attemptedHits = null;
            if (!ReferenceEquals(map, null))
            {
                var analyzer = new ReplayAnalyzer(map, this);
                hits = analyzer.Hits;
                attemptedHits = analyzer.AttemptedHits;
            }

            int hitIndex = 0;
            int attemptedHitIndex = 0;
            for (int i = 0; i < ReplayFrames.Count; i++)
            {
                if (!ReferenceEquals(hits, null) && hitIndex < hits.Count && hits[hitIndex].Frame.Time == ReplayFrames[i].Time)
                {
                    sb.AppendLine(ReplayFrames[i].ToString() + " " + hits[hitIndex].ToString());
                    ++hitIndex;
                    continue;
                }
                if (!ReferenceEquals(attemptedHits, null) && attemptedHitIndex < attemptedHits.Count && attemptedHits[attemptedHitIndex].Frame.Time == ReplayFrames[i].Time)
                {
                    sb.AppendLine(ReplayFrames[i].ToString() + " " + attemptedHits[attemptedHitIndex].Note.ToString());
                    ++attemptedHitIndex;
                    continue;
                }
                sb.AppendLine(ReplayFrames[i].ToString());
            }

            return sb.ToString();
        }

        public bool IsPass()
        {
            return (!this.Mods.HasFlag(Mods.NoFail)) || LifeFrames.All((x) => x.Percentage > 0);
        }
    }
}