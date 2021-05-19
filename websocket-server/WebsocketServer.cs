using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Net.Sockets;
using System.Net;
using System.Threading;
using System.Collections.Specialized;
using System.Threading.Tasks;

namespace websocket_server
{
    public class WebsocketServer
    {
        //protected static ILogger log = new BasicLog() { DebugEnabled = true };

        private List<WebsocketConnection> clients = new List<WebsocketConnection>();
        private object clientListLock = new object();

        private SimpleHttpServer httpServer;

        public WebsocketServer(int port = 8080)
            : this(new SimpleHttpServer(port))
        { }

        public WebsocketServer(SimpleHttpServer httpServer)
        {
            this.httpServer = httpServer;
        }

        /// <summary>
        /// Start listening for connection. Will block program
        /// </summary>
        /// <param name="port"></param>
        public void Listen()
        {
            httpServer.HttpRequestEvent += OnHttpRequest;
            httpServer.Listen();
        }

        /// <summary>
        /// On http request. Check for websocket upgrade header
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnHttpRequest(object sender, HttpRequestEventArgs e)
        {
            var request = e.Request;
            if (request.Method == "GET"
                && request.Headers.Get("Upgrade") == "websocket"
                && request.Headers.Get("Sec-WebSocket-Key") != null)
            {
                // perform handshake
                e.KeepAlive = true;
                var connection = new WebsocketConnection(e.Client, e.Client.GetStream());
                if (connection.Handshake(e.Request.Headers.Get("Sec-WebSocket-Key")))
                {
                    new Task(new Action(() =>
                    {
                        HandleClient(connection);
                        //try {  }
                        //catch (Exception err)
                        //{
                        //    log.Error(err.Message);
                        //    log.Error(err.StackTrace);
                        //}
                    })).Start();
                    //log.Debug("New client connected from " + e.Client.Client.RemoteEndPoint.ToString());
                }
                else
                {
                    byte[] response = Encoding.UTF8.GetBytes("HTTP/1.1 400 Bad Request\r\nConnection: close\r\n\r\n");
                    e.Client.GetStream().Write(response, 0, response.Length);
                    connection.Close();
                }
            }
            else
            {
                HttpRequest?.Invoke(this, e);
            }
        }

        /// <summary>
        /// Broadcast a message to all connected client
        /// </summary>
        /// <param name="message"></param>
        public void Broadcast(string message)
        {
            lock (clientListLock)
            {
                foreach (var client in clients)
                {
                    client.Send(message);
                }
            }
        }

        /// <summary>
        /// Handle new connection
        /// </summary>
        /// <param name="connection"></param>
        private void HandleClient(WebsocketConnection connection)
        {
            AddNewClient(connection);
            OnClientConnected?.Invoke(this, new ConnectEventArgs() { Client = connection });

            connection.DisconnectEvent += (s, e) =>
            {
                RemoveClient(connection);
                OnClientDisconnected?.Invoke(this, e);
            };

            connection.Message += (s, e) => { OnTextMessage?.Invoke(this, e); };

            // Start websocket message
            connection.StartReceive();
        }

        /// <summary>
        /// Add new client to server clients list
        /// </summary>
        /// <param name="client"></param>
        private void AddNewClient(WebsocketConnection client)
        {
            lock (clientListLock)
            {
                clients.Add(client);
            }
        }

        private void RemoveClient(WebsocketConnection client)
        {
            lock (clientListLock)
            {
                clients.Remove(client);
            }
        }

        public event EventHandler<ConnectEventArgs> OnClientConnected;
        public event EventHandler<DisconnectEventArgs> OnClientDisconnected;
        public event EventHandler<HttpRequestEventArgs> HttpRequest;
        public event EventHandler<TextMessageEventArgs> OnTextMessage;
    }
}
