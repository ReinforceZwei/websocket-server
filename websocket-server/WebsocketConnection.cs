using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

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
        private int messageTimeout = -1;
        private Role role;
        private State state;

        private TcpClient client;
        private Stream stream;

        // Role is Client only
        private Uri url;

        public Stream Stream { get { return stream; } }
        public TcpClient TcpClient { get { return client; } }

        public WebsocketConnection(TcpClient client, Stream stream)
        {
            role = Role.Server;
            state = State.Connecting; // Not sure what is the state now. We dont know server create this object before or after handshake
            this.client = client;
            this.stream = stream;
        }

        public WebsocketConnection(Uri url)
        {
            role = Role.Client;
            state = State.Connecting;
            this.url = url;
            if (this.url.Scheme != "ws" && this.url.Scheme != "wss")
                throw new InvalidOperationException("Expected ws or wss URL, " + this.url.Scheme + " found");
        }

        public void Connect()
        {
            if (role == Role.Client)
            {
                client = new TcpClient(url.Host, url.Port);
                if (url.Scheme == "wss")
                {
                    var stream = new SslStream(client.GetStream());
                    stream.AuthenticateAsClient(url.Host);
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

        public bool Handshake(string clientSwk = null)
        {
            if (role == Role.Client)
            {
                // Handshake as client
                return HandshakeAsClient();
            }
            else
            {
                // Handshake as server
                if (clientSwk == null)
                    throw new InvalidOperationException("No client swk provided");
                return HandshakeAsServer(clientSwk);
            }
        }

        private bool HandshakeAsClient()
        {
            byte[] swkBytes = new byte[16];
            rngCsp.GetBytes(swkBytes);
            string swk = Convert.ToBase64String(swkBytes);
            string[] request =
            {
                "GET / HTTP/1.1\r\n",
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
            Console.WriteLine(response.ToString());
            if (response.StatusCode == 101) // Switching Protocol
            {
                if (response.Headers.Get("Upgrade") != "websocket")
                    throw new Exception("No upgrade header");
                // Validate swk
                if (string.IsNullOrEmpty(response.Headers.Get("Sec-WebSocket-Accept")))
                    throw new Exception("No accept header");

                string sswk = response.Headers.Get("Sec-WebSocket-Accept");
                string cswk = ComputeSecWebsocketKey(swk);
                if (sswk == cswk)
                {
                    return true;
                }
                else
                    throw new Exception("Accept header not match");
            }
            else
                throw new Exception("Unexpected status code");
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

        internal static string ComputeSecWebsocketKey(string secWebsocketKey)
        {
            string swk = secWebsocketKey;
            string swka = swk + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
            byte[] swkaSha1 = SHA1.Create().ComputeHash(Encoding.UTF8.GetBytes(swka));
            return Convert.ToBase64String(swkaSha1);
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            return stream.Read(buffer, offset, count);
        }

        public void Write(byte[] buffer, int offset, int count)
        {
            stream.Write(buffer, offset, count);
        }

        public Frame ReadNextFrame()
        {
            if (TcpClient.Connected)
            {
                try
                {
                    return Frame.ReadFrame(Stream);
                }
                catch (IOException)
                {
                    // Connection lost/closed
                    return Frame.Empty;
                }
            }
            else
                return Frame.Empty; // Close websocket
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

        public void Ping()
        {
            Send(new byte[0], Opcode.Ping);
        }

        public void Pong()
        {
            Send(new byte[0], Opcode.Pong);
        }

        public void Close()
        {
            if (TcpClient.Connected)
            {
                try
                {
                    Send(new byte[0], Opcode.ClosedConnection);
                    // Should wait for close resopnse
                    //Thread.Sleep(200);
                    TcpClient.Close();
                }
                catch (ObjectDisposedException) { }
            }
        }

        //public event EventHandler<ConnectEventArgs> ConnectEvent;
        public event EventHandler<DisconnectEventArgs> DisconnectEvent;
    }

    public class ConnectEventArgs : EventArgs { }
    public class DisconnectEventArgs : EventArgs { }
}
