using BMAPI.v1.HitObjects;
using ReplayAPI;

namespace osuDodgyMomentsFinder
{
    public class HitFrame
    {
        public ReplayFrame Frame { get; set; }
        public CircleObject Note { get; set; }
        public Keys Key { get; set; }

        private double _perfectness = -1;
        public double Perfectness { get { return _perfectness > -1 ? _perfectness : Calc_perfectness(); } private set { } }

        private double Calc_perfectness()
        {
            _perfectness = Utils.pixelPerfectHitFactor(Frame, Note);
            return _perfectness;
        }

        public HitFrame(CircleObject note, ReplayFrame frame, Keys key)
        {
            this.Frame = frame;
            this.Note = note;
            this.Key = key;
        }


        public override string ToString()
        {
            double hit = Perfectness;
            string res = Note.ToString();
            res += hit <= 1 ? "" : " ATTEMPTED";
            res += " HIT at " + Frame.Time + "ms";
            res += " (" + (Frame.Time - Note.StartTime) + "ms error, " + hit + " perfectness)";
            //res += "(" + frame.keyCounter + ")";

            return res;
        }

    }
}
