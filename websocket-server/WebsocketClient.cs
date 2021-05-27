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

        private WebsocketConnection connection;
        
        public WebsocketClient(string url)
        {
            Uri uri = new Uri(url);
            if (uri.Scheme != "ws" && uri.Scheme != "wss")
                throw new NotSupportedException("Expected ws or wss URL, " + uri.Scheme + " found");
            connection = new WebsocketConnection(uri);
            connection.TextMessage += MessageHandler;
            connection.DisconnectEvent += DisconnectHandler;
        }

        public void Connect()
        {
            connection.Connect();
            connection.Handshake();
            connection.StartReceiveAsync();
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
        /// Close the connection
        /// </summary>
        public void Close()
        {
            connection.Close();
        }

        private void MessageHandler(object sender, TextMessageEventArgs e)
        {
            OnMessage?.Invoke(this, e);
        }

        private void DisconnectHandler(object sender, DisconnectEventArgs e)
        {
            OnDisconnect?.Invoke(this, e);
        }

        /// <summary>
        /// Fires on client message received
        /// </summary>
        public event EventHandler<TextMessageEventArgs> OnMessage;

        /// <summary>
        /// Firese on client disconnected
        /// </summary>
        public event EventHandler<DisconnectEventArgs> OnDisconnect;
    }
}
