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

        private int HandshakeTimeout = 5000;
        private int WebsocketMessageTimeout = 30000;

        public void Listen(int port = 8080)
        {
            TcpListener tcp = new TcpListener(IPAddress.Any, port);
            tcp.Start();
            log.Info("Tcp listening on port " + port);
            while (true)
            {
                var client = tcp.AcceptTcpClient();
                client.ReceiveTimeout = 5000;
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

        private void HandleClient(TcpClient client)
        {
            if (!ClientHandshake(client))
                return;
            NetworkStream stream = client.GetStream();
            // Start websocket message
            while (true)
            {
                int timeout = 0;
                while (!stream.DataAvailable)
                {
                    if (WebsocketMessageTimeout > 0)
                    {
                        timeout++;
                        if (timeout * 50 > WebsocketMessageTimeout)
                        {
                            log.Warn("Client connection timeout");
                            stream.Close();
                            stream.Dispose();
                            return;
                        }
                    }
                    Thread.Sleep(50);
                }
                byte[] bytes = new byte[client.Available];
                stream.Read(bytes, 0, client.Available);

                string decoded = GetDecodedData(bytes, bytes.Length);
                log.Info("Message: " + decoded);

                /*

                bool finalFrame = (bytes[0] & 0b10000000) != 0; // & 0x80
                bool rsv = (bytes[0] & 0b01110000 | 0) != 0;
                if (rsv)
                {
                    // Fail the connection
                    log.Debug("RSV value not equal to zero");
                    stream.Close();
                    stream.Dispose();
                    return;
                }
                int opcode = bytes[0] & 0b00001111;
                bool mask = (bytes[1] & 0b10000000) != 0;
                int msgLength = bytes[1] - 128;
                */
            }
        }

        private bool ClientHandshake(TcpClient client)
        {
            log.Debug("Do client handshaking");
            NetworkStream stream = client.GetStream();
            int timeout = 0;
            while (!stream.DataAvailable)
            {
                timeout++;
                if (timeout * 50 > HandshakeTimeout)
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

        private static string GetDecodedData(byte[] buffer, int length)
        {
            byte b = buffer[1];
            int dataLength = 0;
            int totalLength = 0;
            int keyIndex = 0;

            if (b - 128 <= 125)
            {
                dataLength = b - 128;
                keyIndex = 2;
                totalLength = dataLength + 6;
            }

            if (b - 128 == 126)
            {
                dataLength = BitConverter.ToInt16(new byte[] { buffer[3], buffer[2] }, 0);
                keyIndex = 4;
                totalLength = dataLength + 8;
            }

            if (b - 128 == 127)
            {
                dataLength = (int)BitConverter.ToInt64(new byte[] { buffer[9], buffer[8], buffer[7], buffer[6], buffer[5], buffer[4], buffer[3], buffer[2] }, 0);
                keyIndex = 10;
                totalLength = dataLength + 14;
            }

            if (totalLength > length)
                throw new Exception("The buffer length is small than the data length");

            byte[] key = new byte[] { buffer[keyIndex], buffer[keyIndex + 1], buffer[keyIndex + 2], buffer[keyIndex + 3] };

            int dataIndex = keyIndex + 4;
            int count = 0;
            for (int i = dataIndex; i < totalLength; i++)
            {
                buffer[i] = (byte)(buffer[i] ^ key[count % 4]);
                count++;
            }

            return Encoding.ASCII.GetString(buffer, dataIndex, dataLength);
        }

        //function to create  frames to send to client 
        /// <summary>
        /// Enum for opcode types
        /// </summary>
        public enum OpcodeType
        {
            /* Denotes a continuation code */
            Fragment = 0,

            /* Denotes a text code */
            Text = 1,

            /* Denotes a binary code */
            Binary = 2,

            /* Denotes a closed connection */
            ClosedConnection = 8,

            /* Denotes a ping*/
            Ping = 9,

            /* Denotes a pong */
            Pong = 10
        }

        /// <summary>Gets an encoded websocket frame to send to a client from a string</summary>
        /// <param name="Message">The message to encode into the frame</param>
        /// <param name="Opcode">The opcode of the frame</param>
        /// <returns>Byte array in form of a websocket frame</returns>
        private static byte[] GetFrameFromString(string Message, OpcodeType Opcode = OpcodeType.Text)
        {
            byte[] response;
            byte[] bytesRaw = Encoding.Default.GetBytes(Message);
            byte[] frame = new byte[10];

            int indexStartRawData = -1;
            int length = bytesRaw.Length;

            frame[0] = (byte)(128 + (int)Opcode);
            if (length <= 125)
            {
                frame[1] = (byte)length;
                indexStartRawData = 2;
            }
            else if (length >= 126 && length <= 65535)
            {
                frame[1] = (byte)126;
                frame[2] = (byte)((length >> 8) & 255);
                frame[3] = (byte)(length & 255);
                indexStartRawData = 4;
            }
            else
            {
                frame[1] = (byte)127;
                frame[2] = (byte)((length >> 56) & 255);
                frame[3] = (byte)((length >> 48) & 255);
                frame[4] = (byte)((length >> 40) & 255);
                frame[5] = (byte)((length >> 32) & 255);
                frame[6] = (byte)((length >> 24) & 255);
                frame[7] = (byte)((length >> 16) & 255);
                frame[8] = (byte)((length >> 8) & 255);
                frame[9] = (byte)(length & 255);

                indexStartRawData = 10;
            }

            response = new byte[indexStartRawData + length];

            int i, reponseIdx = 0;

            //Add the frame bytes to the reponse
            for (i = 0; i < indexStartRawData; i++)
            {
                response[reponseIdx] = frame[i];
                reponseIdx++;
            }

            //Add the data bytes to the response
            for (i = 0; i < length; i++)
            {
                response[reponseIdx] = bytesRaw[i];
                reponseIdx++;
            }

            return response;
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
