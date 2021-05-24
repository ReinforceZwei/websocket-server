using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;

namespace websocket_server
{
    public class SimpleHttpClient
    {
        private readonly string httpVersion = "HTTP/1.0"; // Use 1.0 here because 1.1 server may send chunked data

        private Uri uri;
        private string host { get { return uri.Host; } }
        private int port { get { return uri.Port; } }
        private string path { get { return uri.PathAndQuery; } }
        private bool isSecure { get { return uri.Scheme == "https"; } }

        public NameValueCollection Headers = new NameValueCollection();

        public string UserAgent { get { return Headers.Get("User-Agent"); } set { Headers.Set("User-Agent", value); } }
        public string Accept { get { return Headers.Get("Accept"); } set { Headers.Set("Accept", value); } }
        public string Cookie { set { Headers.Set("Cookie", value); } }

        /// <summary>
        /// Create a new http request
        /// </summary>
        /// <param name="url">Target URL</param>
        public SimpleHttpClient(string url)
        {
            uri = new Uri(url);
            SetupBasicHeaders();
        }

        /// <summary>
        /// Create a new http request with cookies
        /// </summary>
        /// <param name="url">Target URL</param>
        /// <param name="cookies">Cookies to use</param>
        public SimpleHttpClient(string url, NameValueCollection cookies)
        {
            uri = new Uri(url);
            Headers.Add("Cookie", GetCookieString(cookies));
            SetupBasicHeaders();
        }

        /// <summary>
        /// Setup some basic headers
        /// </summary>
        private void SetupBasicHeaders()
        {
            Headers.Add("Host", host);
            //Headers.Add("User-Agent", "MCC/" + Program.Version);
            Headers.Add("Accept", "*/*");
            //Headers.Add("Connection", "close");
        }

        /// <summary>
        /// Perform GET request and get the response. Proxy is handled automatically
        /// </summary>
        /// <returns></returns>
        public Response Get()
        {
            return Send("GET");
        }

        /// <summary>
        /// Perform POST request and get the response. Proxy is handled automatically
        /// </summary>
        /// <param name="contentType">The content type of request body</param>
        /// <param name="body">Request body</param>
        /// <returns></returns>
        public Response Post(string contentType, string body)
        {
            Headers.Add("Content-Type", contentType);
            // Calculate length
            Headers.Add("Content-Length", Encoding.UTF8.GetBytes(body).Length.ToString());
            return Send("POST", body);
        }

        /// <summary>
        /// Send a http request to the server. Proxy is handled automatically
        /// </summary>
        /// <param name="method">Method in string representation</param>
        /// <param name="body">Optional request body</param>
        /// <returns></returns>
        private Response Send(string method, string body = "")
        {
            List<string> requestMessage = new List<string>()
            {
                string.Format("{0} {1} {2}", method.ToUpper(), path, httpVersion) // Request line
            };
            foreach (string key in Headers) // Headers
            {
                var value = Headers[key];
                requestMessage.Add(string.Format("{0}: {1}", key, value));
            }
            requestMessage.Add(""); // <CR><LF>
            if (body != "")
            {
                requestMessage.Add(body);
            }
            else requestMessage.Add(""); // <CR><LF>
            //if (Settings.DebugMessages)
            //{
            //    foreach (string l in requestMessage)
            //    {
            //        ConsoleIO.WriteLine("< " + l);
            //    }
            //}
            Response response = Response.Empty();
            
            TcpClient client = new TcpClient(host, port);
            Stream stream;
            if (isSecure)
            {
                stream = new SslStream(client.GetStream());
                ((SslStream)stream).AuthenticateAsClient(host);
            }
            else
            {
                stream = client.GetStream();
            }
            string h = string.Join("\r\n", requestMessage.ToArray());
            byte[] data = Encoding.ASCII.GetBytes(h);
            stream.Write(data, 0, data.Length);
            stream.Flush();
            StreamReader sr = new StreamReader(stream);
            string rawResult = sr.ReadToEnd();
            response = ParseResponse(rawResult);
            try
            {
                sr.Close();
                stream.Close();
                client.Close();
            }
            catch { }
            
            return response;
        }

        /// <summary>
        /// Parse a raw response string to response object
        /// </summary>
        /// <param name="raw">raw response string</param>
        /// <returns></returns>
        internal static Response ParseResponse(string raw)
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

                return new Response()
                {
                    StatusCode = statusCode,
                    Body = responseBody,
                    Headers = headers,
                    Cookies = cookies
                };
            }
            else
            {
                return new Response()
                {
                    StatusCode = 520, // 502 - Web Server Returned an Unknown Error
                    Body = "",
                    Headers = headers,
                    Cookies = cookies
                };
            }
        }

        /// <summary>
        /// Get the cookie string representation to use in header
        /// </summary>
        /// <param name="cookies"></param>
        /// <returns></returns>
        internal static string GetCookieString(NameValueCollection cookies)
        {
            var sb = new StringBuilder();
            foreach (string key in cookies)
            {
                var value = cookies[key];
                sb.Append(string.Format("{0}={1}; ", key, value));
            }
            string result = sb.ToString();
            return result.Remove(result.Length - 2); // Remove "; " at the end
        }

        /// <summary>
        /// Do an HTTP request with the provided custom HTTP readers
        /// </summary>
        /// <param name="host">Host to connect to (eg my.site.com)</param>
        /// <param name="port">Port to connect to (usually port 80)</param>
        /// <param name="requestHeaders">HTTP headers</param>
        /// <param name="requestBody">Request body, eg in case of POST request</param>
        /// <returns>Request result, as a byte array, already un-gzipped if compressed</returns>
        public static HTTPRequestResult DoRequest(string host, IEnumerable<string> requestHeaders, int port = 80, byte[] requestBody = null)
        {
            //Connect to remote host
            TcpClient client = new TcpClient(host, port);
            Stream stream = client.GetStream();

            //Prepare HTTP headers
            byte[] headersRaw = Encoding.ASCII.GetBytes(String.Join("\r\n", requestHeaders.ToArray()) + "\r\n\r\n");

            //Using HTTPS ?
            if (port == 443)
            {
                //Authenticate Host / Mono users will need to run mozroots once
                SslStream ssl = new SslStream(client.GetStream());
                ssl.AuthenticateAsClient(host);
                stream = ssl;

                //Build and send headers
                ssl.Write(headersRaw);

                //Send body if there is a body to send
                if (requestBody != null)
                {
                    ssl.Write(requestBody);
                    ssl.Flush();
                }
            }
            else //HTTP
            {
                //Build and send headers
                client.Client.Send(headersRaw);

                //Send body if there is a body to send
                if (requestBody != null)
                    client.Client.Send(requestBody);
            }

            //Read response headers
            string statusLine = Encoding.ASCII.GetString(ReadLine(stream));
            if (statusLine.StartsWith("HTTP/1.1"))
            {
                int responseStatusCode = 0;
                string[] statusSplitted = statusLine.Split(' ');
                if (statusSplitted.Length == 3 && int.TryParse(statusSplitted[1], out responseStatusCode))
                {
                    //Response is a valid HTTP response, read all headers
                    NameValueCollection responseHeaders = new NameValueCollection();
                    byte[] responseBody = null;
                    string line = "";
                    do
                    {
                        line = Encoding.ASCII.GetString(ReadLine(stream));
                        if (line.Length > 0)
                        {
                            string[] header = line.Split(new char[] { ':' }, 2); // Split first ':' only
                            string key = header[0].ToLower(); // Key is case-insensitive
                            string value = header[1];
                            responseHeaders.Add(key, value.Trim());
                        }
                    } while (line.Length > 0);

                    //Read response length
                    int responseLength = -1;
                    try
                    {
                        string lengthStr = responseHeaders.Get("Content-Length");
                        if (!String.IsNullOrEmpty(lengthStr)) { responseLength = int.Parse(lengthStr); }
                    }
                    catch { }

                    //Then, read response body
                    if (responseHeaders.Get("Transfer-Encoding") == "chunked")
                    {
                        //Chunked data in several sends
                        List<byte> responseBuffer = new List<byte>();
                        int chunkLength = 0;
                        do
                        {
                            //Read all data chunk by chunk, first line is length, second line is data
                            string headerLine = Encoding.ASCII.GetString(ReadLine(stream));
                            bool lengthConverted = true;
                            try { chunkLength = Convert.ToInt32(headerLine, 16); }
                            catch (FormatException) { lengthConverted = false; }
                            if (lengthConverted)
                            {
                                int dataRead = 0;
                                while (dataRead < chunkLength)
                                {
                                    byte[] chunkContent = ReadLine(stream);
                                    dataRead += chunkContent.Length;
                                    responseBuffer.AddRange(chunkContent);
                                    if (dataRead < chunkLength)
                                    {
                                        //The chunk contains \r\n
                                        responseBuffer.Add((byte)'\r');
                                        responseBuffer.Add((byte)'\n');
                                        dataRead += 2;
                                    }
                                }
                            }
                            else
                            {
                                //Bad chunk length, invalid response
                                return new HTTPRequestResult()
                                {
                                    Status = 502,
                                    Headers = responseHeaders,
                                    Body = null
                                };
                            }
                            //Last chunk is empty
                        } while (chunkLength > 0);
                        responseBody = responseBuffer.ToArray();
                    }
                    else if (responseLength > -1)
                    {
                        //Full data in one send
                        int receivedLength = 0;
                        byte[] received = new byte[responseLength];
                        while (receivedLength < responseLength)
                            receivedLength += stream.Read(received, receivedLength, responseLength - receivedLength);
                        responseBody = received;
                    }
                    else if (responseHeaders.Get("Connection") == "close")
                    {
                        //Connection close, full read is possible
                        responseBody = ReadFully(stream);
                    }
                    else
                    {
                        //Cannot handle keep-alive without content length.
                        return new HTTPRequestResult()
                        {
                            Status = 417,
                            Headers = null,
                            Body = null
                        };
                    }

                    //Decompress gzipped data if necessary
                    if (responseHeaders.Get("Content-Encoding") == "gzip")
                    {
                        MemoryStream inputStream = new MemoryStream(responseBody, false);
                        GZipStream decomp = new GZipStream(inputStream, CompressionMode.Decompress);
                        byte[] decompressed = ReadFully(decomp);
                        responseBody = decompressed;
                    }

                    //Finally, return the result :)
                    return new HTTPRequestResult()
                    {
                        Status = responseStatusCode,
                        Headers = responseHeaders,
                        Body = responseBody
                    };
                }
            }

            //Invalid response, service is anavailable
            return new HTTPRequestResult()
            {
                Status = 503,
                Headers = null,
                Body = null
            };
        }

        /// <summary>
        /// Read a line, \r\n ended, char by char from a stream, without consuming more bytes.
        /// </summary>
        /// <param name="input">input stream</param>
        /// <returns>the line as a byte array</returns>
        internal static byte[] ReadLine(Stream input)
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
            return result.ToArray();
        }

        /// <summary>
        /// Read all the data from a stream to a byte array
        /// </summary>
        private static byte[] ReadFully(Stream input)
        {
            byte[] buffer = new byte[16 * 1024];
            using (MemoryStream ms = new MemoryStream())
            {
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ms.Write(buffer, 0, read);
                }
                return ms.ToArray();
            }
        }

        /// <summary>
        /// Represents an HTTP request result
        /// </summary>
        public class HTTPRequestResult
        {
            /// <summary>
            /// HTTP status code of the response
            /// </summary>
            public int Status { get; set; }

            /// <summary>
            /// HTTP headers of the response
            /// </summary>
            public NameValueCollection Headers { get; set; }

            /// <summary>
            /// Body of the response, as byte array
            /// </summary>
            public byte[] Body { get; set; }

            /// <summary>
            /// Quick check of response status
            /// </summary>
            public bool Successfull
            {
                get
                {
                    return Headers != null && Status == 200;
                }
            }

            /// <summary>
            /// Quick check if response has been received
            /// </summary>
            public bool HasResponded
            {
                get
                {
                    return Headers != null && Body != null;
                }
            }

            /// <summary>
            /// Get response body as string
            /// </summary>
            public string BodyAsString
            {
                get
                {
                    return Encoding.UTF8.GetString(Body);
                }
            }

            /// <summary>
            /// Get cookies that server sent along with the response
            /// </summary>
            public IEnumerable<KeyValuePair<string, string>> NewCookies
            {
                get
                {
                    var cookies = new List<KeyValuePair<string, string>>();
                    foreach (string header in Headers)
                    {
                        if (header.StartsWith("Set-Cookie: "))
                        {
                            string[] headerSplitted = header.Split(' ');
                            if (headerSplitted.Length > 1)
                            {
                                string[] cookieSplitted = headerSplitted[1].Split(';');
                                foreach (string cookie in cookieSplitted)
                                {
                                    string[] keyValue = cookie.Split('=');
                                    if (keyValue.Length == 2)
                                    {
                                        cookies.Add(new KeyValuePair<string, string>(keyValue[0], keyValue[1]));
                                    }
                                }
                            }
                        }
                    }
                    return cookies;
                }
            }
        }

        /// <summary>
        /// Basic response object
        /// </summary>
        public class Response
        {
            public int StatusCode;
            public string Body;
            public NameValueCollection Headers;
            public NameValueCollection Cookies;

            /// <summary>
            /// Get an empty response object
            /// </summary>
            /// <returns></returns>
            public static Response Empty()
            {
                return new Response()
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
    }
}
