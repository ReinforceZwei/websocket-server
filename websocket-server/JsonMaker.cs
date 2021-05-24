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
        /// <summary>
        /// Convert a list of string to JSON array string
        /// </summary>
        /// <param name="array"></param>
        /// <returns></returns>
        public static string ToArrayB(List<string> array)
        {
            StringBuilder json = new StringBuilder();
            json.Append("[");
            for (int i = 0; i < array.Count; i++)
            {
                string item = EscapeString(array[i]);
                if (i != 0) json.Append(", ");
                json.Append("\"" + item + "\"");
            }
            json.Append("]");
            return json.ToString();
        }

        /// <summary>
        /// Convert a list of number to JSON array string
        /// </summary>
        /// <typeparam name="T">Any numeric type</typeparam>
        /// <param name="array"></param>
        /// <returns>String of JSON array</returns>
        public static string ToArrayB<T>(List<T> array) where T : struct, IComparable<T>
        {
            StringBuilder json = new StringBuilder();
            json.Append("[");
            for (int i = 0; i < array.Count; i++)
            {
                if (i != 0) json.Append(", ");
                json.Append(array[i]);
            }
            json.Append("]");
            return json.ToString();
        }

        public static string ToArrayB(List<Dictionary<string, object>> array)
        {
            StringBuilder json = new StringBuilder();
            json.Append("[");
            for (int i = 0; i < array.Count; i++)
            {
                if (i != 0) json.Append(", ");
                json.Append(ToJson(array[i]));
            }
            json.Append("]");
            return json.ToString();
        }

        /// <summary>
        /// Convert a dictionary to JSON. Only accept string as key. 
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public static string ToJsonB(Dictionary<string, object> obj)
        {
            StringBuilder json = new StringBuilder();
            json.Append("{");
            foreach (var pair in obj)
            {
                json.Append("\"" + pair.Key + "\":");
                if (pair.Value is string)
                {
                    json.Append("\"" + EscapeString((string)pair.Value) + "\"");
                }
                else if (pair.Value is byte
                    || pair.Value is sbyte
                    || pair.Value is short
                    || pair.Value is ushort
                    || pair.Value is int
                    || pair.Value is decimal
                    || pair.Value is double
                    || pair.Value is float)
                {
                    json.Append(pair.Value);
                }
                else if (pair.Value is IEnumerable<string>)
                {
                    json.Append(ToArrayB(((IEnumerable<string>)pair.Value).ToList()));
                }
                else if (pair.Value is IEnumerable<int>)
                {
                    json.Append(ToArrayB(((IEnumerable<int>)pair.Value).ToList()));
                }
                else if (pair.Value is IEnumerable<decimal>)
                {
                    json.Append(ToArrayB(((IEnumerable<decimal>)pair.Value).ToList()));
                }
                else if (pair.Value is IEnumerable<float>)
                {
                    json.Append(ToArrayB(((IEnumerable<float>)pair.Value).ToList()));
                }
                else if (pair.Value is IEnumerable<double>)
                {
                    json.Append(ToArrayB(((IEnumerable<double>)pair.Value).ToList()));
                }
                else if (pair.Value is List<Dictionary<string, object>>)
                {
                    json.Append(ToArrayB((List<Dictionary<string, object>>)pair.Value));
                }
                else if (pair.Value is Dictionary<string, object>
                    || pair.Value is Dictionary<string, string>
                    || pair.Value is Dictionary<string, int>
                    || pair.Value is Dictionary<string, float>
                    || pair.Value is Dictionary<string, decimal>
                    || pair.Value is Dictionary<string, double>)
                {
                    json.Append(ToJson((Dictionary<string, object>)pair.Value));
                }

                if (!obj.Last().Equals(pair))
                    json.Append(",");
            }
            json.Append("}");
            return json.ToString();
        }

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
