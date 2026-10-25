using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace QingShu
{
    internal static class TextTools
    {
        internal const int MaximumLength = 2000;

        internal static string NormalizeLines(string value)
        {
            return value.Replace("\r\n", "\n").Replace('\r', '\n')
                .Replace('\u2028', '\n').Replace('\u2029', '\n');
        }

        internal static string Clean(string value, bool trim)
        {
            StringBuilder result = new StringBuilder();
            string normalized = NormalizeLines(value);
            for (int i = 0; i < normalized.Length; i++)
            {
                char c = normalized[i];
                if (c == '\n' || c == '\t') result.Append(' ');
                else if (char.IsHighSurrogate(c))
                {
                    if (i + 1 < normalized.Length && char.IsLowSurrogate(normalized[i + 1]))
                    {
                        result.Append(c);
                        result.Append(normalized[++i]);
                    }
                }
                else if (!char.IsControl(c) && !char.IsLowSurrogate(c)) result.Append(c);
            }
            return trim ? result.ToString().Trim() : result.ToString();
        }

        internal static List<string> Lines(string value, bool trim)
        {
            List<string> lines = new List<string>();
            foreach (string line in NormalizeLines(value).Split('\n'))
            {
                string cleaned = Clean(line, trim);
                if (!string.IsNullOrWhiteSpace(cleaned)) lines.Add(cleaned);
            }
            return lines;
        }

        internal static List<string> Elements(string value)
        {
            List<string> elements = new List<string>();
            TextElementEnumerator enumerator = StringInfo.GetTextElementEnumerator(value);
            while (enumerator.MoveNext()) elements.Add(enumerator.GetTextElement());
            return elements;
        }

        internal static int Count(string value)
        {
            return StringInfo.ParseCombiningCharacters(value).Length;
        }
    }
}
