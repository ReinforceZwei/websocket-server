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
        internal static string ReadLine(Stream input)
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

        /// <summary>
        /// Parse a raw response string to response object
        /// </summary>
        /// <param name="raw">raw response string</param>
        /// <returns></returns>
        private HttpResponse ParseResponse(string raw)
        {
            int statusCode;
            string responseBody = "";
            NameValueCollection headers = new NameValueCollection();
            NameValueCollection cookies = new NameValueCollection();
            if (raw.StartsWith("HTTP/1.1") || raw.StartsWith("HTTP/1.0"))
            {
                Queue<string> msg = new Queue<string>(raw.Split(new string[] { "\r\n" }, StringSplitOptions.None));
                statusCode = int.Parse(msg.Dequeue().Split(' ')[1]);

                while (msg.Peek() != "")
                {
                    string[] header = msg.Dequeue().Split(new char[] { ':' }, 2); // Split first ':' only
                    string key = header[0].ToLower(); // Key is case-insensitive
                    string value = header[1];
                    if (key == "set-cookie")
                    {
                        string[] cookie = value.Split(';'); // cookie options are ignored
                        string[] tmp = cookie[0].Split(new char[] { '=' }, 2); // Split first '=' only
                        string cname = tmp[0].Trim();
                        string cvalue = tmp[1].Trim();
                        cookies.Add(cname, cvalue);
                    }
                    else
                    {
                        headers.Add(key, value.Trim());
                    }
                }
                msg.Dequeue();
                if (msg.Count > 0)
                    responseBody = msg.Dequeue();

                return new HttpResponse()
                {
                    StatusCode = statusCode,
                    Body = responseBody,
                    Headers = headers,
                    Cookies = cookies
                };
            }
            else
            {
                return new HttpResponse()
                {
                    StatusCode = 520, // 502 - Web Server Returned an Unknown Error
                    Body = "",
                    Headers = headers,
                    Cookies = cookies
                };
            }
        }

        public event EventHandler<HttpRequestEventArgs> HttpRequestEvent;
    }

    public class HttpRequest
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

    /// <summary>
    /// Basic response object
    /// </summary>
    public class HttpResponse
    {
        public int StatusCode;
        public string Body;
        public NameValueCollection Headers;
        public NameValueCollection Cookies;

        /// <summary>
        /// Get an empty response object
        /// </summary>
        /// <returns></returns>
        public static HttpResponse Empty()
        {
            return new HttpResponse()
            {
                StatusCode = 204, // 204 - No content
                Body = "",
                Headers = new NameValueCollection(),
                Cookies = new NameValueCollection()
            };
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Status code: " + StatusCode);
            sb.AppendLine("Headers:");
            foreach (string key in Headers)
            {
                sb.AppendLine(string.Format("  {0}: {1}", key, Headers[key]));
            }
            if (Cookies.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Cookies: ");
                foreach (string key in Cookies)
                {
                    sb.AppendLine(string.Format("  {0}={1}", key, Cookies[key]));
                }
            }
            if (Body != "")
            {
                sb.AppendLine();
                if (Body.Length > 200)
                {
                    sb.AppendLine("Body: (Truncated to 200 characters)");
                }
                else sb.AppendLine("Body: ");
                sb.AppendLine(Body.Length > 200 ? Body.Substring(0, 200) + "..." : Body);
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
