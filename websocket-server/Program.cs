using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace websocket_server
{
    class Program
    {
        static void Main(string[] args)
        {
            var server = new WebsocketServer();
            
            server.ClientConnected += onClientConnected;

            server.Listen();
        }

        private static void onClientConnected(object sender, WebsocketServer.ClientConnectedEventArgs e)
        {
            Console.WriteLine("New client");
            e.Client.Message += onMessage;
        }

        private static void onMessage(object sender, WebsocketClient.MessageEventArgs e)
        {
            Console.WriteLine(">>> " + e.Message);
            e.Client.Send(e.Message);
        }
    }
}
