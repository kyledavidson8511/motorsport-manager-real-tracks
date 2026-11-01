using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

// Unity-free and game-free on purpose: this file is compiled by the mod (net35) and by the net8 console test in
// /root/work/m1/wp4/test, so the JSON the mod writes is exactly what the test validates.
namespace RealTracks
{
    /// <summary>Minimal JSON reader. Objects are Dictionary&lt;string, object&gt;, arrays List&lt;object&gt;, numbers double.</summary>
    public static class AtJson
    {
        public static object Parse(string text, out string error)
        {
            error = null;
            if (text == null) { error = "no text"; return null; }
            try
            {
                int i = 0;
                object v = ReadValue(text, ref i);
                SkipWs(text, ref i);
                if (i != text.Length) { error = "trailing characters at " + i; return null; }
                return v;
            }
            catch (Exception e)
            {
                error = e.Message;
                return null;
            }
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n' || s[i] == '﻿')) i++;
        }

        private static object ReadValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) throw new Exception("unexpected end");
            char c = s[i];
            if (c == '{') return ReadObject(s, ref i);
            if (c == '[') return ReadArray(s, ref i);
            if (c == '"') return ReadString(s, ref i);
            if (c == 't' && string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (c == 'f' && string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (c == 'n' && string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            return ReadNumber(s, ref i);
        }

        private static Dictionary<string, object> ReadObject(string s, ref int i)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            i++; // {
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return d; }
            while (true)
            {
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != '"') throw new Exception("expected a key at " + i);
                string k = ReadString(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new Exception("expected ':' at " + i);
                i++;
                d[k] = ReadValue(s, ref i);
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') { i++; return d; }
                throw new Exception("expected ',' or '}' at " + i);
            }
        }

        private static List<object> ReadArray(string s, ref int i)
        {
            List<object> l = new List<object>();
            i++; // [
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return l; }
            while (true)
            {
                l.Add(ReadValue(s, ref i));
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == ']') { i++; return l; }
                throw new Exception("expected ',' or ']' at " + i);
            }
        }

        private static string ReadString(string s, ref int i)
        {
            StringBuilder sb = new StringBuilder();
            i++; // opening quote
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) break;
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        if (i + 4 > s.Length) throw new Exception("bad \\u escape");
                        sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default: sb.Append(e); break;
                }
            }
            throw new Exception("unterminated string");
        }

        private static double ReadNumber(string s, ref int i)
        {
            int st = i;
            while (i < s.Length && ("+-0123456789.eE".IndexOf(s[i]) >= 0)) i++;
            if (i == st) throw new Exception("unexpected character '" + s[i] + "' at " + i);
            return double.Parse(s.Substring(st, i - st), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        // ---- typed getters on parsed objects ----
        public static string Str(Dictionary<string, object> d, string k, string def)
        {
            object v;
            if (d == null || !d.TryGetValue(k, out v) || v == null) return def;
            return v as string ?? Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        public static double Num(Dictionary<string, object> d, string k, double def)
        {
            object v;
            if (d == null || !d.TryGetValue(k, out v) || v == null) return def;
            if (v is double) return (double)v;
            double r;
            return double.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out r) ? r : def;
        }

        public static bool Bool(Dictionary<string, object> d, string k, bool def)
        {
            object v;
            if (d == null || !d.TryGetValue(k, out v) || v == null) return def;
            if (v is bool) return (bool)v;
            string s = Convert.ToString(v, CultureInfo.InvariantCulture);
            return string.Equals(s, "true", StringComparison.OrdinalIgnoreCase) || s == "1";
        }

        public static List<object> Arr(Dictionary<string, object> d, string k)
        {
            object v;
            if (d == null || !d.TryGetValue(k, out v)) return null;
            return v as List<object>;
        }

        // ---- formatting ----
        public static string Num(double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) return "null";
            if (d == Math.Floor(d) && Math.Abs(d) < 1e15) return ((long)d).ToString(CultureInfo.InvariantCulture);
            return d.ToString("0.######", CultureInfo.InvariantCulture);
        }

        public static string Quote(string s)
        {
            if (s == null) return "null";
            StringBuilder sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }

    /// <summary>Small JSON writer (compact for JSON-lines, indented for files people read).</summary>
    public sealed class JsonOut
    {
        private readonly StringBuilder sb = new StringBuilder();
        private readonly bool pretty;
        private readonly List<bool> hasItem = new List<bool>();

        public JsonOut(bool pretty) { this.pretty = pretty; }

        private void Indent()
        {
            for (int i = 0; i < hasItem.Count; i++) sb.Append("  ");
        }

        private void Pre(string key)
        {
            if (hasItem.Count > 0)
            {
                int last = hasItem.Count - 1;
                if (hasItem[last]) sb.Append(',');
                hasItem[last] = true;
                if (pretty) { sb.Append('\n'); Indent(); }
            }
            if (key != null)
            {
                sb.Append(AtJson.Quote(key));
                sb.Append(pretty ? ": " : ":");
            }
        }

        public JsonOut BeginObject(string key) { Pre(key); sb.Append('{'); hasItem.Add(false); return this; }
        public JsonOut BeginObject() { return BeginObject(null); }
        public JsonOut BeginArray(string key) { Pre(key); sb.Append('['); hasItem.Add(false); return this; }
        public JsonOut BeginArray() { return BeginArray(null); }

        public JsonOut EndObject() { return End('}'); }
        public JsonOut EndArray() { return End(']'); }

        private JsonOut End(char c)
        {
            bool had = hasItem[hasItem.Count - 1];
            hasItem.RemoveAt(hasItem.Count - 1);
            if (pretty && had) { sb.Append('\n'); Indent(); }
            sb.Append(c);
            return this;
        }

        public JsonOut Prop(string key, string v) { Pre(key); sb.Append(v == null ? "null" : AtJson.Quote(v)); return this; }
        public JsonOut Prop(string key, int v) { Pre(key); sb.Append(v.ToString(CultureInfo.InvariantCulture)); return this; }
        public JsonOut Prop(string key, long v) { Pre(key); sb.Append(v.ToString(CultureInfo.InvariantCulture)); return this; }
        public JsonOut Prop(string key, double v) { Pre(key); sb.Append(AtJson.Num(v)); return this; }
        public JsonOut Prop(string key, bool v) { Pre(key); sb.Append(v ? "true" : "false"); return this; }
        public JsonOut PropNull(string key) { Pre(key); sb.Append("null"); return this; }
        /// <summary>Writes an already-formatted JSON fragment.</summary>
        public JsonOut PropRaw(string key, string raw) { Pre(key); sb.Append(raw); return this; }

        public JsonOut Item(string v) { Pre(null); sb.Append(v == null ? "null" : AtJson.Quote(v)); return this; }
        public JsonOut Item(double v) { Pre(null); sb.Append(AtJson.Num(v)); return this; }

        public JsonOut Value(string key, object v)
        {
            if (v == null) return PropNull(key);
            if (v is string) return Prop(key, (string)v);
            if (v is bool) return Prop(key, (bool)v);
            if (v is int) return Prop(key, (int)v);
            if (v is long) return Prop(key, (long)v);
            if (v is float) return Prop(key, (double)(float)v);
            if (v is double) return Prop(key, (double)v);
            return Prop(key, Convert.ToString(v, CultureInfo.InvariantCulture));
        }

        public override string ToString() { return sb.ToString(); }
    }

    /// <summary>The one place that formats events.jsonl lines and gate_speeds.csv rows.</summary>
    public static class AtLines
    {
        public const string GateCsvHeader = "item,car,lap,gate,s_m,speedKph";

        /// <summary>One events.jsonl line: t (realtime seconds since run start), item (index into summary.items, -1 = run level), simT, lap, type, then key/value pairs.</summary>
        public static string Event(double t, int item, double simT, int lap, string type, params object[] kv)
        {
            JsonOut j = new JsonOut(false);
            j.BeginObject();
            j.Prop("t", Math.Round(t, 3));
            j.Prop("item", item);
            j.Prop("simT", Math.Round(simT, 2));
            j.Prop("lap", lap);
            j.Prop("type", type);
            if (kv != null)
            {
                for (int i = 0; i + 1 < kv.Length; i += 2) j.Value(Convert.ToString(kv[i], CultureInfo.InvariantCulture), kv[i + 1]);
            }
            j.EndObject();
            return j.ToString();
        }

        /// <summary>item is the readable label of the run item ("Austin A"): WP5's chart groups its series by this column.</summary>
        public static string GateRow(string item, string car, int lap, int gate, double sM, double speedKph)
        {
            return Csv(item) + "," + Csv(car) + "," + lap.ToString(CultureInfo.InvariantCulture) + ","
                + gate.ToString(CultureInfo.InvariantCulture) + "," + AtJson.Num(Math.Round(sM, 1)) + "," + AtJson.Num(Math.Round(speedKph, 1));
        }

        private static string Csv(string s)
        {
            if (s == null) return "";
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
    }
}
