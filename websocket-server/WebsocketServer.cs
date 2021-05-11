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
        protected static ILogger log = new BasicLog() { DebugEnabled = true };

        private List<WebsocketClient> clients = new List<WebsocketClient>();
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

        private void OnHttpRequest(object sender, HttpRequestEventArgs e)
        {
            var request = e.Request;
            if (request.Method == "GET"
                && request.Headers.Get("Upgrade") == "websocket"
                && request.Headers.Get("Sec-WebSocket-Key") != null)
            {
                // perform handshake
                e.KeepAlive = true;
                ClientHandshake(e);
                new Task(new Action(() => 
                {
                    try { HandleClient(e.Client); }
                    catch (Exception err)
                    {
                        log.Error(err.Message);
                        log.Error(err.StackTrace);
                    }
                })).Start();
                log.Debug("New client connected from " + e.Client.Client.RemoteEndPoint.ToString());
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
        /// <param name="client"></param>
        private void HandleClient(TcpClient client)
        {
            var wsClient = new WebsocketClient(client);
            AddNewClient(wsClient);
            ClientConnected?.Invoke(this, new ClientConnectedEventArgs() { Client = wsClient });
            log.Info("A client connected");
            log.Info("Total client: " + clients.Count);
            wsClient.DisconnectEvent += (s, e) =>
            {
                RemoveClient(wsClient);
                log.Info("A client disconnected");
                log.Info("Total client: " + clients.Count);
            };
            // Start websocket message
            // Will block program
            wsClient.Start();
        }

        /// <summary>
        /// Add new client to server clients list
        /// </summary>
        /// <param name="client"></param>
        private void AddNewClient(WebsocketClient client)
        {
            lock (clientListLock)
            {
                clients.Add(client);
            }
        }

        private void RemoveClient(WebsocketClient client)
        {
            lock (clientListLock)
            {
                clients.Remove(client);
            }
        }

        /// <summary>
        /// Preform handshake with client
        /// </summary>
        /// <param name="client"></param>
        /// <returns></returns>
        private bool ClientHandshake(HttpRequestEventArgs e)
        {
            log.Debug("Do client handshaking");
            var stream = e.Client.GetStream();
            string swk = e.Request.Headers.Get("Sec-WebSocket-Key");
            string swka = swk + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
            byte[] swkaSha1 = System.Security.Cryptography.SHA1.Create().ComputeHash(Encoding.UTF8.GetBytes(swka));
            string swkaSha1Base64 = Convert.ToBase64String(swkaSha1);
            byte[] response = Encoding.UTF8.GetBytes(
                "HTTP/1.1 101 Switching Protocols\r\n" +
                "Connection: Upgrade\r\n" +
                "Upgrade: websocket\r\n" +
                "Sec-WebSocket-Accept: " + swkaSha1Base64 + "\r\n\r\n");

            stream.Write(response, 0, response.Length);
            log.Debug("Client handshaking done");
            return true;
        }

        public event EventHandler<ClientConnectedEventArgs> ClientConnected;
        public event EventHandler<HttpRequestEventArgs> HttpRequest;

        public class ClientConnectedEventArgs : EventArgs
        {
            public WebsocketClient Client;
        }

        /// <summary>
        /// Parse a raw request string to request object
        /// </summary>
        /// <param name="raw">raw response string</param>
        /// <returns></returns>
        private HttpRequest ParseRequest(string raw)
        {
            NameValueCollection headers = new NameValueCollection();
            Queue<string> msg = new Queue<string>(raw.Split(new string[] { "\r\n" }, StringSplitOptions.None));
            string body = null;
            string[] requestString = msg.Dequeue().Split(' ');
            string method = requestString[0];
            string path = requestString[1];
            string httpVersion = requestString[2];
            while (msg.Peek() != "")
            {
                string[] header = msg.Dequeue().Split(new char[] { ':' }, 2); // Split first ':' only
                string key = header[0].ToLower(); // Key is case-insensitive
                string value = header[1];
                headers.Add(key, value.Trim());
            }
            msg.Dequeue();
            if (msg.Count > 0)
                body = msg.Dequeue();

            return new HttpRequest()
            {
                Headers = headers,
                Method = method,
                Path = path,
                HttpVersion = httpVersion,
                Body = Encoding.UTF8.GetBytes(body)
            };
        }
    }
}
