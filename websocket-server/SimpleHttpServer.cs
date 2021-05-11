using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace websocket_server
{
    public class SimpleHttpServer
    {
        private int timeout = 30000;
        private TcpListener tcpListener;

        public SimpleHttpServer(TcpListener listener)
        {
            tcpListener = listener;
        }

        public SimpleHttpServer(int port)
        {
            tcpListener = new TcpListener(IPAddress.Any, port);
        }

        public void Listen()
        {
            tcpListener.Start();
            while (true)
            {
                var connection = tcpListener.AcceptTcpClient();
                new Task(new Action(() => { HandleConnection(connection); })).Start();
            }
        }

        private void HandleConnection(TcpClient connection)
        {
            connection.ReceiveTimeout = timeout;
            var stream = connection.GetStream();
            var request = ReadRequest(stream);
            var response = new HttpRequestEventArgs() { Request = request, Client = connection };
            HttpRequestEvent?.Invoke(this, response);
            if (!response.KeepAlive)
            {
                try
                {
                    connection.Close();
                }
                catch (ObjectDisposedException) { }
            }
        }

        private static HttpRequest ReadRequest(NetworkStream stream)
        {
            string requestString = ReadLine(stream);
            string[] requestParts = requestString.Split(' ');
            string method = requestParts[0];
            string path = requestParts[1];
            string httpVersion = requestParts[2];
            NameValueCollection headers = new NameValueCollection();

            string headerLine = "";
            do
            {
                headerLine = ReadLine(stream);
                if (string.IsNullOrEmpty(headerLine))
                    break;
                string[] headerParts = headerLine.Split(new char[] { ':' }, 2); // Split first ':' only
                string key = headerParts[0].ToLower(); // Key is case-insensitive
                string value = headerParts[1];
                headers.Add(key, value.Trim());
            } while (!string.IsNullOrEmpty(headerLine));

            byte[] bodyByte = null;
            if (stream.DataAvailable)
            {
                if (headers.Get("Content-Length") != null)
                {
                    int bodyLength = int.Parse(headers.Get("Content-Length"));
                    byte[] buffer = new byte[bodyLength];
                    stream.Read(buffer, 0, bodyLength);
                    bodyByte = buffer;
                }
                else
                {
                    bodyByte = ReadRemainData(stream);
                }
            }
            return new HttpRequest()
            {
                Headers = headers,
                Method = method,
                Path = path,
                HttpVersion = httpVersion,
                Body = bodyByte
            };
        }

        /// <summary>
        /// Read a line, \r\n ended, char by char from a stream, without consuming more bytes.
        /// </summary>
        /// <param name="input">input stream</param>
        /// <returns>the line as a byte array</returns>
        private static string ReadLine(Stream input)
        {
            List<byte> result = new List<byte>();
            int b = 0x00;
            while (true)
            {
                b = input.ReadByte();
                if (b == '\r')
                {
                    int c = input.ReadByte();
                    if (c == '\n')
                    {
                        break;
                    }
                    else
                    {
                        result.Add((byte)b);
                        result.Add((byte)c);
                    }
                }
                else result.Add((byte)b);
            }
            return Encoding.UTF8.GetString(result.ToArray());
        }

        private static byte[] ReadRemainData(NetworkStream stream)
        {
            List<byte> data = new List<byte>();
            while (stream.DataAvailable)
            {
                int b = stream.ReadByte();
                if (b == -1) break;
                data.Add((byte)b);
            }
            return data.ToArray();
        }

        public event EventHandler<HttpRequestEventArgs> HttpRequestEvent;
    }

    public struct HttpRequest
    {
        public NameValueCollection Headers;
        public string Method;
        public string Path;
        public string HttpVersion;
        public byte[] Body;

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("> {0} {1} {2}\n", Method, Path, HttpVersion);
            foreach (string key in Headers.AllKeys)
            {
                sb.AppendFormat("> {0}: {1}\n", key, Headers.Get(key));
            }
            return sb.ToString();
        }
    }

    public class HttpRequestEventArgs : EventArgs
    {
        public HttpRequest Request;
        public TcpClient Client;
        public bool KeepAlive = false;
    }
}
