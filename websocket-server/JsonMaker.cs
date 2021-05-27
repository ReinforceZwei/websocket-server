using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace websocket_server
{
    /// <summary>
    /// A super simple JSON serializer
    /// By ReinforceZwei
    /// </summary>
    static public class JsonMaker
    {
        public static string ToJson(IDictionary<string, object> obj)
        {
            StringBuilder json = new StringBuilder();
            json.Append("{");
            int currentIndex = 0;
            int length = obj.Count - 1;
            foreach (var pair in obj)
            {
                json.Append("\"" + EscapeString(pair.Key) + "\":");

                json.Append(ToJsonStringValue(pair.Value));

                if (currentIndex != length)
                    json.Append(",");

                currentIndex++;
            }
            json.Append("}");
            return json.ToString();
        }

        public static string ToJson(IEnumerable<object> obj)
        {
            StringBuilder json = new StringBuilder();
            json.Append("[");
            int currentIndex = 0;
            int length = obj.Count() - 1;
            foreach (var value in obj)
            {
                json.Append(ToJsonStringValue(value));

                if (currentIndex != length)
                    json.Append(",");
                
                currentIndex++;
            }
            json.Append("]");
            return json.ToString();
        }

        private static string ToJsonStringValue(object obj)
        {
            if (obj is string)
            {
                return "\"" + EscapeString((string)obj) + "\"";
            }
            else if (obj is byte
                   || obj is sbyte
                   || obj is short
                   || obj is ushort
                   || obj is int
                   || obj is decimal
                   || obj is double
                   || obj is float)
            {
                return obj.ToString();
            }
            else if (obj is bool)
            {
                return obj.ToString().ToLower();
            }
            else if (obj is null)
            {
                return "null";
            }
            else if (obj is IDictionary<string, object>)
            {
                return ToJson((IDictionary<string, object>)obj);
            }
            else if (obj is IEnumerable<object>)
            {
                return ToJson((IEnumerable<object>)obj);
            }
            else return obj.ToString();
        }

        private static string EscapeString(string str)
        {
            return str.Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t")
                .Replace("\r", "\\r")
                .Replace("\b", "\\b")
                .Replace("\f", "\\f");
        }
    }
}
