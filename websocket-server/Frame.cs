using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;

namespace websocket_server
{
    public class Frame
    {
        public readonly bool FinalFrame;
        public readonly byte[] Data;
        public readonly Opcode Opcode;

        public Frame(Opcode opcode)
        {
            Opcode = opcode;
        }

        public Frame(byte[] data)
            : this(Opcode.Text)
        {
            Data = data;
            FinalFrame = true;
        }

        public Frame(byte[] data, Opcode opcode)
            : this(opcode)
        {
            Data = data;
            FinalFrame = opcode != Opcode.Fragment;
        }

        public Frame(byte[] data, Opcode opcode, bool finalFrame)
            : this(opcode)
        {
            Data = data;
            FinalFrame = finalFrame;
        }

        public string GetDataAsString()
        {
            return Encoding.UTF8.GetString(Data);
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Frame:");
            sb.AppendLine("  Opcode: " + Opcode.ToString());
            sb.AppendLine("  Data array length: " + Data.Length);
            sb.AppendLine("  Final frame: " + FinalFrame.ToString());
            return sb.ToString();
        }

        /// <summary>
        /// Mask or unmask data for websocket message frame
        /// </summary>
        /// <param name="mask">Mask keys, should be 4 bytes</param>
        /// <param name="data">Data to mask/unmask</param>
        /// <returns>Masked/unmasked data</returns>
        public static byte[] MaskData(byte[] mask, byte[] data)
        {
            byte[] decoded = new byte[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                decoded[i] = (byte)(data[i] ^ mask[i % 4]);
            }
            return decoded;
        }

        public static Frame ReadFrame(Stream stream)
        {
            byte[] head = new byte[2];
            stream.Read(head, 0, 2);
            bool finalFrame = (head[0] & 0b10000000) != 0; // & 0x80
            bool rsv = (head[0] & 0b01110000 | 0) != 0;
            if (rsv)
            {
                // Fail the connection
                throw new NotSupportedException("RSV value not equal to zero");
                //log.Debug("RSV value not equal to zero");
                //stream.Close();
                //stream.Dispose();
                //return;
            }
            int opcode = head[0] & 0b00001111;
            bool mask = (head[1] & 0b10000000) != 0;
            ulong messageLength = head[1];
            if (mask)
                messageLength -= 128;
            if (messageLength == 126)
            {
                // Following 2 bytes are length
                Console.WriteLine("2 bytes length");
                byte[] length = new byte[2];
                stream.Read(length, 0, 2);
                messageLength = BitConverter.ToUInt16(length.Reverse().ToArray(), 0);
            }
            else if (messageLength == 127)
            {
                // Following 8 bytes are length
                Console.WriteLine("8 bytes length");
                byte[] length = new byte[8];
                stream.Read(length, 0, 8);
                messageLength = BitConverter.ToUInt64(length.Reverse().ToArray(), 0);
            }

            byte[] content = new byte[messageLength];
            byte[] maskKey = new byte[4];

            Console.WriteLine("message length: " + messageLength + " byte array length: " + content.Length);

            if (mask)
            {
                stream.Read(maskKey, 0, 4);
            }

            if (messageLength > 1024)
            {
                byte[] data = new byte[1024];
                using (MemoryStream ms = new MemoryStream())
                {
                    ulong totalReadCount = 0;
                    int readCount = 0;
                    while (totalReadCount < messageLength)
                    {
                        readCount = stream.Read(data, 0, data.Length);
                        totalReadCount += (ulong)readCount;
                        ms.Write(data, 0, readCount);
                    }

                    content = ms.ToArray();
                }
            }
            else
                stream.Read(content, 0, (int)messageLength);

            if (mask)
                content = MaskData(maskKey, content);

            return new Frame(content, (Opcode)opcode, finalFrame);
        }

        public static byte[] GetByte(byte[] data, Opcode opcode)
        {
            List<byte> frame = new List<byte>();
            int length = data.Length;

            frame.Add((byte)(128 + (int)opcode)); // FIN & Opcode
            
            // Mask bit & data length
            if (length <= 125)
            {
                frame.Add((byte)length);
            }
            else if (length >= 126 && length <= 65535)
            {
                frame.Add(126);
                frame.Add((byte)((length >> 8) & 255));
                frame.Add((byte)(length & 255));
            }
            else
            {
                frame.Add(127);
                frame.Add((byte)((length >> 56) & 255));
                frame.Add((byte)((length >> 48) & 255));
                frame.Add((byte)((length >> 40) & 255));
                frame.Add((byte)((length >> 32) & 255));
                frame.Add((byte)((length >> 24) & 255));
                frame.Add((byte)((length >> 16) & 255));
                frame.Add((byte)((length >> 8) & 255));
                frame.Add((byte)(length & 255));
            }

            frame.AddRange(data);

            return frame.ToArray();
        }

        public static byte[] GetByte(byte[] data, Opcode opcode, byte[] mask)
        {
            List<byte> frame = new List<byte>();
            int length = data.Length;

            frame.Add((byte)(128 + (int)opcode)); // FIN & Opcode

            // Mask bit & data length
            if (length <= 125)
            {
                frame.Add((byte)(length + 128)); // Mask bit to 1
            }
            else if (length >= 126 && length <= 65535)
            {
                frame.Add(126 + 128);
                frame.Add((byte)((length >> 8) & 255));
                frame.Add((byte)(length & 255));
            }
            else
            {
                frame.Add(127 + 128);
                frame.Add((byte)((length >> 56) & 255));
                frame.Add((byte)((length >> 48) & 255));
                frame.Add((byte)((length >> 40) & 255));
                frame.Add((byte)((length >> 32) & 255));
                frame.Add((byte)((length >> 24) & 255));
                frame.Add((byte)((length >> 16) & 255));
                frame.Add((byte)((length >> 8) & 255));
                frame.Add((byte)(length & 255));
            }

            frame.AddRange(mask);
            frame.AddRange(MaskData(mask, data));

            return frame.ToArray();
        }
    }
}
