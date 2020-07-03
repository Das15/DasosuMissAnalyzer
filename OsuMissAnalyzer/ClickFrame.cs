using BMAPI.v1.HitObjects;
using ReplayAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace osuDodgyMomentsFinder
{
    public class ClickFrame
    {
        public ReplayFrame Frame { get; set; }
        public Keys Key { get; set; }

        public ClickFrame(ReplayFrame frame, Keys key)
        {
            this.Frame = frame;
            this.Key = key;
        }

        public override string ToString()
        {
            string res = "";
            res += "* " + Frame.Time + "ms";
            res += " (" + Key + ")";

            return res;
        }
    }
}
