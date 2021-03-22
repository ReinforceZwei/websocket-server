using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Net.Sockets;
using System.Net;
using System.Threading;
using System.Collections.Specialized;

namespace websocket_server
{
    public class WebsocketServer
    {
        protected static ILogger log = new BasicLog() { DebugEnabled = true };

        private List<WebsocketClient> clients = new List<WebsocketClient>();
        private object clientListLock = new object();
        private int handshakeTimeout = 5000;

        /// <summary>
        /// Start listening for connection. Will block client
        /// </summary>
        /// <param name="port"></param>
        public void Listen(int port = 8080)
        {
            TcpListener tcp = new TcpListener(IPAddress.Any, port);
            tcp.Start();
            log.Info("Tcp listening on port " + port);
            while (true)
            {
                var client = tcp.AcceptTcpClient();
                log.Debug("New client connected from " + client.Client.RemoteEndPoint.ToString());
                new Thread(new ThreadStart(() => 
                {
                    try { HandleClient(client); }
                    catch (Exception e)
                    {
                        log.Error(e.Message);
                        log.Error(e.StackTrace);
                    }
                })).Start();
            }
        }

        /// <summary>
        /// Handle new connection
        /// </summary>
        /// <param name="client"></param>
        private void HandleClient(TcpClient client)
        {
            if (!ClientHandshake(client))
                return;
            var wsClient = new WebsocketClient(client);
            AddNewClient(wsClient);
            ClientConnected?.Invoke(this, new ClientConnectedEventArgs() { Client = wsClient });
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

        /// <summary>
        /// Preform handshake with client
        /// </summary>
        /// <param name="client"></param>
        /// <returns></returns>
        private bool ClientHandshake(TcpClient client)
        {
            log.Debug("Do client handshaking");
            NetworkStream stream = client.GetStream();
            int timeout = 0;
            while (!stream.DataAvailable)
            {
                timeout++;
                if (timeout * 50 > handshakeTimeout)
                {
                    log.Warn("Client handshake timeout");
                    stream.Close();
                    stream.Dispose();
                    return false;
                }
                Thread.Sleep(50);
            }
            //while (client.Available < 3);
            byte[] bytes = new byte[client.Available];
            stream.Read(bytes, 0, client.Available);
            string requestRaw = Encoding.UTF8.GetString(bytes);
            var request = ParseResponse(requestRaw);
            if (request.Method == "GET"
                && request.Headers.Get("Sec-WebSocket-Key") != null)
            {
                string swk = request.Headers.Get("Sec-WebSocket-Key");
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
            else
            {
                byte[] response = Encoding.UTF8.GetBytes(
                    "HTTP/1.1 400 Bad Request\r\n" +
                    "Connection: Close\r\n\r\n");
                stream.Write(response, 0, response.Length);
                stream.Close();
                stream.Dispose();
                log.Debug("Client handshaking fail");
                return false;
            }
        }

        public event EventHandler<ClientConnectedEventArgs> ClientConnected;

        public class ClientConnectedEventArgs : EventArgs
        {
            public WebsocketClient Client;
        }

        /// <summary>
        /// Parse a raw response string to response object
        /// </summary>
        /// <param name="raw">raw response string</param>
        /// <returns></returns>
        private HttpRequest ParseResponse(string raw)
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
                Body = body
            };
        }

        public struct HttpRequest
        {
            public NameValueCollection Headers;
            public string Method;
            public string Path;
            public string HttpVersion;
            public string Body;
        }
    }
}
