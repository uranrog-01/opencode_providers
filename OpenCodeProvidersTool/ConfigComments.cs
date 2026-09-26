using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OpenCodeProvidersTool
{
    /// <summary>
    /// Keeps the comments of a .jsonc config alive across a save.
    ///
    /// Newtonsoft cannot hold comments in a <see cref="JObject"/> — an object's children
    /// must be <see cref="JProperty"/>, so <c>CommentHandling.Load</c> parses them and then
    /// silently drops them, and a save would wipe every note the user wrote. Comments are
    /// therefore captured separately, keyed by the path of the property they belong to, and
    /// re-emitted while the tree is written back.
    /// </summary>
    public sealed class ConfigComments
    {
        /// <summary>Separator for path segments; keys may themselves contain dots.</summary>
        private const char Sep = '\u0001';

        private readonly Dictionary<string, List<string>> _leading =
            new Dictionary<string, List<string>>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<string>> _inline =
            new Dictionary<string, List<string>>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<string>> _trailing =
            new Dictionary<string, List<string>>(StringComparer.Ordinal);

        /// <summary>True when the file had at least one comment worth writing back.</summary>
        public bool Any
        {
            get { return _leading.Count > 0 || _inline.Count > 0 || _trailing.Count > 0; }
        }

        // ------------------------------------------------------------------ capture

        /// <summary>Reads the comments out of JSONC text. Never throws on bad input.</summary>
        public static ConfigComments Capture(string jsoncText)
        {
            var map = new ConfigComments();
            if (string.IsNullOrEmpty(jsoncText)) return map;

            try
            {
                map.Read(jsoncText);
            }
            catch
            {
                // A comment map is a nicety; a parse problem here must not fail a load.
                return new ConfigComments();
            }
            return map;
        }

        /// <summary>One nesting level: where we are, and what value comes next.</summary>
        private class Frame
        {
            public string Path;
            public bool IsArray;
            public string PendingName;
        }

        private void Read(string text)
        {
            // A null path marks the document itself: its children sit at the root, with no
            // leading separator, which is what the writer expects too.
            var stack = new List<Frame> { new Frame { Path = null, IsArray = false } };
            var pending = new List<string>();

            // Path and line of the value that finished most recently, so a comment sitting
            // on that same line can be recognised as trailing it rather than leading the next.
            string lastValuePath = null;
            int lastValueLine = -1;

            using (var reader = new JsonTextReader(new StringReader(text)))
            {
                while (reader.Read())
                {
                    Frame frame = stack[stack.Count - 1];

                    if (reader.TokenType == JsonToken.Comment)
                    {
                        string comment = (reader.Value ?? "").ToString().Trim();
                        if (comment.Length == 0) continue;

                        if (lastValuePath != null && reader.LineNumber == lastValueLine)
                        {
                            Add(_inline, lastValuePath, comment);
                        }
                        else
                        {
                            pending.Add(comment);
                        }
                        continue;
                    }

                    if (reader.TokenType == JsonToken.PropertyName)
                    {
                        frame.PendingName = reader.Value == null ? "" : reader.Value.ToString();
                        Flush(pending, _leading, ChildPath(frame, frame.PendingName));
                        lastValuePath = null;
                        continue;
                    }

                    if (reader.TokenType == JsonToken.StartObject || reader.TokenType == JsonToken.StartArray)
                    {
                        string path = ValuePath(frame);
                        Advance(frame);
                        stack.Add(new Frame
                        {
                            Path = path,
                            IsArray = reader.TokenType == JsonToken.StartArray
                        });
                        lastValuePath = null;
                        continue;
                    }

                    if (reader.TokenType == JsonToken.EndObject || reader.TokenType == JsonToken.EndArray)
                    {
                        // Comments still pending here sit at the end of the container.
                        Flush(pending, _trailing, frame.Path);
                        stack.RemoveAt(stack.Count - 1);

                        lastValuePath = frame.Path;
                        lastValueLine = reader.LineNumber;
                        continue;
                    }

                    // Any scalar value.
                    lastValuePath = ValuePath(frame);
                    lastValueLine = reader.LineNumber;
                    Advance(frame);
                }
            }

            // Anything left over belongs to the document as a whole.
            Flush(pending, _trailing, "");
        }

        private static void Advance(Frame frame)
        {
            if (frame.IsArray) frame.PendingName = null;
        }

        /// <summary>Path of the value that is about to be read from this frame.</summary>
        private static string ValuePath(Frame frame)
        {
            if (frame.Path == null) return "";
            if (frame.IsArray) return frame.Path + Sep + "[]";
            return frame.Path + Sep + (frame.PendingName ?? "");
        }

        /// <summary>Path of a named property inside this frame.</summary>
        private static string ChildPath(Frame frame, string name)
        {
            return (frame.Path ?? "") + Sep + (name ?? "");
        }

        private static void Add(Dictionary<string, List<string>> map, string path, string comment)
        {
            List<string> list;
            if (!map.TryGetValue(path, out list))
            {
                list = new List<string>();
                map[path] = list;
            }
            list.Add(comment);
        }

        private static void Flush(List<string> pending, Dictionary<string, List<string>> map, string path)
        {
            if (pending.Count == 0) return;
            foreach (string comment in pending) Add(map, path, comment);
            pending.Clear();
        }

        // -------------------------------------------------------------------- write

        /// <summary>Two spaces per level, matching the stock serialiser's indented output.</summary>
        private const int Indent = 2;

        private static readonly string NewLine = Environment.NewLine;

        /// <summary>Serialises the tree, putting the captured comments back where they were.</summary>
        public string Write(JObject root)
        {
            // A file with no comments takes the stock serialiser, so its bytes are exactly
            // what they were before this class existed.
            if (!Any) return root.ToString(Formatting.Indented);

            var text = new StringBuilder();
            WriteToken(root, text, 0, "");
            return text.ToString();
        }

        private void WriteToken(JToken token, StringBuilder text, int depth, string path)
        {
            var block = token as JObject;
            if (block != null)
            {
                WriteObject(block, text, depth, path);
                return;
            }

            var array = token as JArray;
            if (array != null)
            {
                WriteArray(array, text, depth, path);
                return;
            }

            // Scalars go through Newtonsoft, so quoting and escaping are its problem.
            text.Append(token.ToString(Formatting.None));
        }

        private void WriteObject(JObject block, StringBuilder text, int depth, string path)
        {
            var properties = new List<JProperty>(block.Properties());
            if (properties.Count == 0)
            {
                text.Append("{}");
                return;
            }

            text.Append('{');
            for (int i = 0; i < properties.Count; i++)
            {
                JProperty property = properties[i];
                string child = path + Sep + property.Name;

                CommentsAbove(text, _leading, child, depth + 1);
                StartLine(text, depth + 1);
                text.Append(Quote(property.Name)).Append(": ");
                WriteToken(property.Value, text, depth + 1, child);

                // The comma belongs to the value, before any note about it.
                if (i < properties.Count - 1) text.Append(',');

                List<string> inline;
                if (_inline.TryGetValue(child, out inline))
                {
                    foreach (string comment in inline) text.Append("  // ").Append(SingleLine(comment));
                }
            }

            CommentsAbove(text, _trailing, path, depth + 1);
            text.Append(NewLine).Append(Spaces(depth)).Append('}');
        }

        private void WriteArray(JArray array, StringBuilder text, int depth, string path)
        {
            if (array.Count == 0)
            {
                text.Append("[]");
                return;
            }

            string child = path + Sep + "[]";
            // Element paths carry no index, so a note inside an array is anchored to the
            // array as a whole and written once, above the first element.
            CommentsAbove(text, _leading, child, depth + 1);

            text.Append('[');
            for (int i = 0; i < array.Count; i++)
            {
                StartLine(text, depth + 1);
                WriteToken(array[i], text, depth + 1, child);
                if (i < array.Count - 1) text.Append(',');

                List<string> inline;
                if (_inline.TryGetValue(child, out inline))
                {
                    foreach (string comment in inline) text.Append("  // ").Append(SingleLine(comment));
                }
            }

            CommentsAbove(text, _trailing, path, depth + 1);
            text.Append(NewLine).Append(Spaces(depth)).Append(']');
        }

        /// <summary>Writes each note for a path on its own comment line.</summary>
        private static void CommentsAbove(StringBuilder text, Dictionary<string, List<string>> map,
            string path, int depth)
        {
            List<string> list;
            if (!map.TryGetValue(path, out list)) return;
            foreach (string comment in list)
            {
                // A note that spanned lines becomes one // line per line, so a stray
                // newline can never leave half a comment uncommented.
                foreach (string line in Split(comment))
                {
                    StartLine(text, depth);
                    text.Append("// ").Append(line);
                }
            }
        }

        private static IEnumerable<string> Split(string comment)
        {
            string[] lines = comment.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (string line in lines) yield return line.Trim();
        }

        private static string SingleLine(string comment)
        {
            return comment.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();
        }

        private static void StartLine(StringBuilder text, int depth)
        {
            text.Append(NewLine).Append(Spaces(depth));
        }

        private static string Spaces(int depth)
        {
            return new string(' ', depth * Indent);
        }

        private static string Quote(string name)
        {
            return new JValue(name ?? "").ToString(Formatting.None);
        }
    }
}
