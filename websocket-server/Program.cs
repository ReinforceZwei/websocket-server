using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace websocket_server
{
    class Program
    {
        static void Main(string[] args)
        {
            var http = new SimpleHttpServer(8080);

            var server = new WebsocketServer(http);
            
            server.ClientConnected += onClientConnected;
            server.HttpRequest += Http_HttpRequestEvent;

            new Thread(new ThreadStart(() => { server.Listen(); })).Start();

            do
            {
                string msg = Console.ReadLine();
                server.Broadcast(msg);
                Console.WriteLine("<<< " + msg);
            }
            while (true);
        }

        private static void Http_HttpRequestEvent(object sender, HttpRequestEventArgs e)
        {
            Console.WriteLine(e.Request.ToString());
            var stream = e.Client.GetStream();
            string response = "HTTP/1.1 200 OK\r\nConnection: close\r\n\r\n";
            byte[] data = Encoding.UTF8.GetBytes(response);
            stream.Write(data, 0, data.Length);
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
