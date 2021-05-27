using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace websocket_server
{
    public class WebsocketConnection
    {
        public enum Role
        {
            Server,
            Client,
        }

        private static RNGCryptoServiceProvider rngCsp = new RNGCryptoServiceProvider();
        private int messageTimeout = 0;
        private Role role;
        private State state;
        private object sendLock = new object();

        private TcpClient client;
        private Stream stream;

        // Role is Client only
        private Uri url;

        public State State { get { return state; } }
        public Stream Stream { get { return stream; } }
        public TcpClient TcpClient { get { return client; } }
        public int MessageTimeout
        {
            get { return messageTimeout; }
            set
            {
                messageTimeout = value;
                if (client != null)
                {
                    client.ReceiveTimeout = value;
                }
            }
        }

        /// <summary>
        /// Create a new <see cref="WebsocketConnection"/> representing a server side connection (S to C)
        /// </summary>
        /// <param name="client"><see cref="System.Net.Sockets.TcpClient"/> of the client</param>
        /// <param name="stream"><see cref="SslStream"/> for SSL connection or <see cref="NetworkStream"/> for plain connection</param>
        public WebsocketConnection(TcpClient client, Stream stream)
        {
            role = Role.Server;
            state = State.Connecting;
            this.client = client;
            this.stream = stream;
        }

        /// <summary>
        /// Create a new <see cref="WebsocketConnection"/> representing a client side connection (C to S)
        /// </summary>
        /// <param name="url"></param>
        public WebsocketConnection(Uri url)
        {
            role = Role.Client;
            state = State.Connecting;
            this.url = url;
            if (this.url.Scheme != "ws" && this.url.Scheme != "wss")
                throw new InvalidOperationException("Expected ws or wss URL, " + this.url.Scheme + " found");
        }

        /// <summary>
        /// Create Tcp connection to receiver
        /// </summary>
        public void Connect()
        {
            if (role == Role.Client)
            {
                client = new TcpClient(url.Host, url.Port);
                client.ReceiveTimeout = messageTimeout;
                if (url.Scheme == "wss")
                {
                    var stream = new SslStream(client.GetStream());
                    System.Security.Cryptography.X509Certificates.X509Certificate2Collection xc = new System.Security.Cryptography.X509Certificates.X509Certificate2Collection();
                    stream.AuthenticateAsClient(url.Host, xc, System.Security.Authentication.SslProtocols.Tls, false);
                    this.stream = stream;
                }
                else
                {
                    stream = client.GetStream();
                }
            }
            else
                throw new InvalidOperationException("Role is not client");
        }

        /// <summary>
        /// Perform handshake with receiver
        /// </summary>
        /// <param name="clientSwk">Client swk. Required as a server in order to handshake with client</param>
        /// <returns></returns>
        public bool Handshake(string clientSwk = null)
        {
            if (role == Role.Client)
            {
                // Handshake as client
                bool result = HandshakeAsClient();
                if (result)
                {
                    SwitchState(State.Open);
                    ConnectEvent?.Invoke(this, new ConnectEventArgs() { Client = this});
                }
                return result;
            }
            else
            {
                // Handshake as server
                if (clientSwk == null)
                    throw new InvalidOperationException("No client swk provided");
                bool result = HandshakeAsServer(clientSwk);
                if (result)
                {
                    SwitchState(State.Open);
                    ConnectEvent?.Invoke(this, new ConnectEventArgs() { Client = this });
                }
                return result;
            }
        }

        private bool HandshakeAsClient()
        {
            byte[] swkBytes = new byte[16];
            rngCsp.GetBytes(swkBytes);
            string swk = Convert.ToBase64String(swkBytes);
            string[] request =
            {
                $"GET {url.PathAndQuery} HTTP/1.1\r\n",
                $"Host: {url.Host}\r\n",
                "Upgrade: websocket\r\n",
                "Connection: Upgrade\r\n",
                $"Sec-WebSocket-Key: {swk}\r\n", // random 16 bytes number base64 encoded
                "Sec-WebSocket-Version: 13\r\n",
                "\r\n",
            };
            // Use stream
            byte[] requestBuffer = Encoding.UTF8.GetBytes(string.Join(string.Empty, request));
            stream.Write(requestBuffer, 0, requestBuffer.Length);
            // Wait for response
            string responseRaw = "";
            while (true)
            {
                string line = SimpleHttpServer.ReadLine(stream);
                if (string.IsNullOrEmpty(line))
                    break;
                responseRaw += line + "\r\n";
            }
            var response = SimpleHttpClient.ParseResponse(responseRaw);
            //Console.WriteLine(response.ToString());
            if (response.StatusCode == 101) // Switching Protocol
            {
                if (response.Headers.Get("Upgrade") != "websocket")
                    throw new Exception("Upgrade websocket header mismatch/missing");
                // Validate swk
                if (string.IsNullOrEmpty(response.Headers.Get("Sec-WebSocket-Accept")))
                    throw new Exception("Sec-WebSocket-Accept header missing");

                string sswk = response.Headers.Get("Sec-WebSocket-Accept");
                string cswk = ComputeSecWebsocketKey(swk);
                if (sswk == cswk)
                {
                    return true;
                }
                else
                    throw new Exception("Sec-WebSocket-Accept header value not match");
            }
            else
                throw new Exception("Unexpected response status code: " + response.StatusCode);
        }

        private bool HandshakeAsServer(string clientSwk)
        {
            string swk = clientSwk;
            string swkaSha1Base64 = ComputeSecWebsocketKey(swk);
            byte[] response = Encoding.UTF8.GetBytes(
                "HTTP/1.1 101 Switching Protocols\r\n" +
                "Connection: Upgrade\r\n" +
                "Upgrade: websocket\r\n" +
                "Sec-WebSocket-Accept: " + swkaSha1Base64 + "\r\n\r\n");

            stream.Write(response, 0, response.Length);
            return true;
        }

        internal void SwitchState(State state)
        {
            this.state = state;
        }

        internal static string ComputeSecWebsocketKey(string secWebsocketKey)
        {
            string swk = secWebsocketKey;
            string swka = swk + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
            byte[] swkaSha1 = SHA1.Create().ComputeHash(Encoding.UTF8.GetBytes(swka));
            return Convert.ToBase64String(swkaSha1);
        }

        /// <summary>
        /// Start message receiving loop
        /// </summary>
        public void StartReceive()
        {
            while (true)
            {
                try
                {
                    var frame = ReadNextFrame();
                    if (frame.Opcode == Opcode.ClosedConnection)
                    {
                        if (state == State.Open)
                        {
                            SwitchState(State.Closing);
                            if (frame.Data.Length > 0)
                            {
                                Send(frame.Data, Opcode.ClosedConnection); // Echo back data
                            }
                            else 
                                Send(Opcode.ClosedConnection);
                        }
                        SwitchState(State.Closed);
                        DisconnectEvent?.Invoke(this, new DisconnectEventArgs() { Code = frame.CloseStatusCode ?? -1 });
                        Close();
                        break;
                    }
                    else if (frame.Opcode == Opcode.Text)
                    {
                        TextMessage?.Invoke(this, new TextMessageEventArgs() { Message = frame.DataAsString, Client = this });
                    }
                    else if (frame.Opcode == Opcode.Binary)
                    {
                        // Currently ignored
                    }
                    else if (frame.Opcode == Opcode.Ping)
                    {
                        if (frame.Data.Length > 0)
                            Send(frame.Data, Opcode.Pong);
                        else 
                            Pong();
                    }
                }
                catch (IOException) { DisconnectEvent?.Invoke(this, new DisconnectEventArgs()); }
                catch (ObjectDisposedException) { DisconnectEvent?.Invoke(this, new DisconnectEventArgs()); }
            }
        }

        /// <summary>
        /// Start message receiving loop in <see cref="Task"/>
        /// </summary>
        public void StartReceiveAsync()
        {
            new Task(new Action(() =>
            {
                StartReceive();
            })).Start();
        }

        /// <summary>
        /// Read bytes from connection stream
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        /// <returns>Number of bytes read</returns>
        public int Read(byte[] buffer, int offset, int count)
        {
            return stream.Read(buffer, offset, count);
        }

        /// <summary>
        /// Write bytes to the connection stream
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        public void Write(byte[] buffer, int offset, int count)
        {
            lock (sendLock)
            {
                try
                {
                    stream.Write(buffer, offset, count);
                }
                catch (IOException) { DisconnectEvent?.Invoke(this, new DisconnectEventArgs()); Close(); }
                catch (ObjectDisposedException) { DisconnectEvent?.Invoke(this, new DisconnectEventArgs()); }
            }
        }

        /// <summary>
        /// Read next message frame from connection
        /// </summary>
        /// <returns><see cref="Frame"/></returns>
        public Frame ReadNextFrame()
        {
            return Frame.ReadFrame(Stream);
        }

        /// <summary>
        /// Send a message frame to receiver. Apply data masking if necessary
        /// </summary>
        /// <param name="data">Data to send</param>
        /// <param name="opcode">Opcode</param>
        private void Send(byte[] data, Opcode opcode)
        {
            bool mask = false;
            byte[] maskKey = new byte[4];
            if (role == Role.Client)
            {
                mask = true;
                rngCsp.GetBytes(maskKey);
            }
            byte[] frame;
            if (mask)
                frame = Frame.GetByte(data, opcode, maskKey);
            else
                frame = Frame.GetByte(data, opcode);
            Write(frame, 0, frame.Length);
        }

        /// <summary>
        /// Send a message with empty data. Used for sending control frame
        /// </summary>
        /// <param name="opcode"></param>
        public void Send(Opcode opcode)
        {
            Send(new byte[0], opcode);
        }

        /// <summary>
        /// Send a text message to receiver
        /// </summary>
        /// <param name="message"></param>
        public void Send(string message)
        {
            Send(Encoding.UTF8.GetBytes(message), Opcode.Text);
        }

        /// <summary>
        /// Send a binary message to receiver
        /// </summary>
        /// <param name="data"></param>
        public void Send(byte[] data)
        {
            Send(data, Opcode.Binary);
        }

        /// <summary>
        /// Send a ping message to receiver
        /// </summary>
        public void Ping()
        {
            Send(Opcode.Ping);
        }

        /// <summary>
        /// Send a pong message to receiver
        /// </summary>
        public void Pong()
        {
            Send(Opcode.Pong);
        }

        /// <summary>
        /// Send disconnect message to receiver and close the connection
        /// </summary>
        /// <param name="code">Status code</param>
        public void Disconnect(int code = 1000)
        {
            switch (state)
            {
                case State.Connecting:
                case State.Closed:
                    {
                        // Force-close tcp
                        Close();
                        DisconnectEvent?.Invoke(this, new DisconnectEventArgs());
                        return;
                    }
                case State.Open:
                    {
                        // Initiate the disconnect
                        try
                        {
                            byte[] b = new byte[] { (byte)(code >> 8), (byte)(code & 0xFF) }; // Big-endian
                            Send(b, Opcode.ClosedConnection);
                            SwitchState(State.Closing);
                        }
                        catch (ObjectDisposedException)
                        {
                            DisconnectEvent?.Invoke(this, new DisconnectEventArgs());
                        }
                        break;
                    }
                case State.Closing:
                    {
                        // ???
                        break;
                    }
            }
        }

        /// <summary>
        /// Close the Tcp client
        /// </summary>
        public void Close()
        {
            try
            {
                TcpClient.Close();
            }
            catch (ObjectDisposedException) { }
        }

        /// <summary>
        /// Emitted when connected to receiver and ready to send message
        /// </summary>
        public event EventHandler<ConnectEventArgs> ConnectEvent;

        /// <summary>
        /// Emitted when disconnected from receiver
        /// </summary>
        public event EventHandler<DisconnectEventArgs> DisconnectEvent;

        /// <summary>
        /// Emitted when a text message is received
        /// </summary>
        public event EventHandler<TextMessageEventArgs> TextMessage;
    }

    public class ConnectEventArgs : EventArgs 
    {
        public WebsocketConnection Client;
    }
    public class DisconnectEventArgs : EventArgs 
    {
        /// <summary>
        /// Disconnect reason code. -1 for no code specified
        /// </summary>
        public int Code = -1;
    }
    public class TextMessageEventArgs : EventArgs
    {
        /// <summary>
        /// Client that sends this message
        /// </summary>
        public WebsocketConnection Client;

        /// <summary>
        /// Message content
        /// </summary>
        public string Message;
    }
}
