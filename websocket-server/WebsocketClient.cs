using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Threading;

namespace websocket_server
{
    public class WebsocketClient
    {
        protected static ILogger log = new BasicLog() { DebugEnabled = true };

        /// <summary>
        /// Use -1 for no timeout
        /// </summary>
        private int MessageTimeout = -1;
        private State state;

        private TcpClient client;
        private NetworkStream stream;
        public WebsocketClient(TcpClient client)
        {
            if (MessageTimeout > 0)
                client.ReceiveTimeout = MessageTimeout;
            else
                client.ReceiveTimeout = 0;
            this.client = client;
            this.stream = client.GetStream();
            state = State.Open; // Handshaked before creating
        }

        /// <summary>
        /// Start the message listening loop. Will block program
        /// </summary>
        public void Start()
        {
            while (true)
            {
                var frame = Frame.ReadFrame(client);

                File.WriteAllText(@"s:\t.txt", frame.GetDataAsString());

                Console.WriteLine(frame);

                if (stream.DataAvailable)
                {
                    Console.WriteLine("After frame but more data: " + client.Available + " bytes");
                }

                if (frame.Opcode == Opcode.Text)
                {
                   Message?.Invoke(this, new MessageEventArgs { Message = frame.GetDataAsString(), Client = this });
                }

                continue;

                int timeout = 0;
                while (!stream.DataAvailable)
                {
                    if (MessageTimeout > 0)
                    {
                        timeout++;
                        if (timeout * 50 > MessageTimeout)
                        {
                            log.Warn("Client connection timeout");
                            Close();
                            return;
                        }
                    }
                    Thread.Sleep(50);
                }
                byte[] bytes = new byte[client.Available];
                stream.Read(bytes, 0, client.Available);

                string decoded = GetDecodedData(bytes, bytes.Length);
                Message?.Invoke(this, new MessageEventArgs { Message = decoded, Client = this });
                //log.Info("Message: " + decoded);

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

        

        /// <summary>
        /// Send string message to client
        /// </summary>
        /// <param name="message"></param>
        public void Send(string message)
        {
            byte[] buffer = GetFrameFromString(message);
            SendRaw(buffer);
        }

        /// <summary>
        /// Close the TCP connection
        /// </summary>
        public void Close()
        {
            if (state != State.Closed)
            {
                state = State.Closed;
                stream.Close();
                stream.Dispose();
                DisconnectEvent?.Invoke(this, new DisconnectEventArgs());
            }
        }
            

        public void Disconnect()
        {
            // TODO: Rewrite GetFrame method to support all opcode
            Close();
        }

        /// <summary>
        /// Send raw byte to TCP stream
        /// </summary>
        /// <param name="buffer"></param>
        private void SendRaw(byte[] buffer)
        {
            stream.Write(buffer, 0, buffer.Length);
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

        public class DisconnectEventArgs : EventArgs { }

        /// <summary>
        /// Decode client data frame
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="length"></param>
        /// <returns></returns>
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

            return Encoding.UTF8.GetString(buffer, dataIndex, dataLength);
        }

        /// <summary>Gets an encoded websocket frame to send to a client from a string</summary>
        /// <param name="Message">The message to encode into the frame</param>
        /// <param name="Opcode">The opcode of the frame</param>
        /// <returns>Byte array in form of a websocket frame</returns>
        private static byte[] GetFrameFromString(string Message, Opcode Opcode = Opcode.Text)
        {
            byte[] response;
            byte[] bytesRaw = Encoding.UTF8.GetBytes(Message);
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
    }
}
