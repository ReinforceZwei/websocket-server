using System;
namespace websocket_server
{
    public enum State
    {
        Connecting,
        Open,
        Closing,
        Closed,
    }
}
