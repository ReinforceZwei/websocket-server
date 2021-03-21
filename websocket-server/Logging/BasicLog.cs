using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace websocket_server
{
    public class BasicLog : LoggerBase
    {
        public override void Debug(string msg)
        {
            Log("[Debug] " + msg);
        }

        public override void Error(string msg)
        {
            Log("[Error] " + msg);
        }

        public override void Info(string msg)
        {
            Log("[Info] " + msg);
        }

        public override void Warn(string msg)
        {
            Log("[Warn] " + msg);
        }
    }
}
