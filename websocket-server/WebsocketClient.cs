using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Threading;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace websocket_server
{
    public class WebsocketClient
    {
        protected static ILogger log = new BasicLog() { DebugEnabled = true };

        WebsocketConnection connection;
        
        public WebsocketClient(string url)
        {
            Uri uri = new Uri(url);
            if (uri.Scheme != "ws" && uri.Scheme != "wss")
                throw new NotSupportedException("Expected ws or wss URL, " + uri.Scheme + " found");
            connection = new WebsocketConnection(uri);
        }

        public void Connect()
        {
            connection.Connect();
            if (connection.Handshake())
            {
                new Task(new Action(() =>
                {
                    Start();
                })).Start();
            }
        }


        /// <summary>
        /// Start the message listening loop. Will block program
        /// </summary>
        public void Start()
        {
            while (true)
            {
                var frame = connection.ReadNextFrame();

                //File.WriteAllText(@"s:\t.txt", frame.GetDataAsString());

                Console.WriteLine(frame);

                //if (stream.DataAvailable)
                //{
                //    Console.WriteLine("After frame but more data: " + client.Available + " bytes");
                //}

                if (frame.Opcode == Opcode.Text)
                {
                    Message?.Invoke(this, new MessageEventArgs { Message = frame.GetDataAsString(), Client = this });
                }
            }
        }

        

        /// <summary>
        /// Send string message to client
        /// </summary>
        /// <param name="message"></param>
        public void Send(string message)
        {
            connection.Send(message);
        }

        public void Ping()
        {
            connection.Ping();
        }

        /// <summary>
        /// Close the TCP connection
        /// </summary>
        public void Close()
        {
            // TODO: Handle close
            connection.Close();
            DisconnectEvent?.Invoke(this, new DisconnectEventArgs());
        }
            

        public void Disconnect()
        {
            // TODO: Rewrite GetFrame method to support all opcode
            Close();
        }

        /// <summary>
        /// Fires on client message received
        /// </summary>
        public event EventHandler<MessageEventArgs> Message;

        public event EventHandler<DisconnectEventArgs> DisconnectEvent; // Disconnect used as method name

        // FIXME: Where to put eventargs classes?
        public class MessageEventArgs : EventArgs
        {
            /// <summary>
            /// Client that sends this message
            /// </summary>
            public WebsocketClient Client;

            /// <summary>
            /// Message content
            /// </summary>
            public string Message;
        }
    }
}
