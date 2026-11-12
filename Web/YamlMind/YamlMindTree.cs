using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace MiniMonitor.Web.YamlMind;

/// <summary>YAML → 思维导图树。完整复刻 yamlmind 的分层编号与 title/children/note 约定。</summary>
public static class YamlMindTree
{
    public const int MaxNodes = 40_000;

    private static readonly string[] TitleKeys = { "title", "name", "text", "label", "标题", "主题", "名称" };
    private static readonly string[] ChildKeys = { "children", "items", "nodes", "list", "子节点", "子项" };
    private static readonly string[] NoteKeys = { "note", "notes", "desc", "description", "comment", "备注", "说明", "描述" };

    private static readonly Regex PyIntPattern = new(
        "^([-+]?0b[0-1_]+|[-+]?0[0-7_]+|[-+]?(0|[1-9][0-9_]*)|[-+]?0x[0-9a-fA-F_]+|[-+]?[1-9][0-9_]*(?::[0-5]?[0-9])+)$",
        RegexOptions.Compiled);
    private static readonly Regex PyFloatPattern = new(
        "^([-+]?([0-9][0-9_]*)\\.[0-9_]*([eE][-+][0-9]+)?|[-+]?\\.[0-9][0-9_]*([eE][-+][0-9]+)?|[-+]?[0-9][0-9_]*(?::[0-5]?[0-9])+\\.[0-9_]*|[-+]?\\.[iI][nN][fF]|\\.([nN][aA][nN]))$",
        RegexOptions.Compiled);

    public sealed class Node
    {
        public string Id = "root";
        public List<int> Path = new();
        public int Depth;
        public string Num = "";
        public string Title = "";
        public string Value = "";
        public string Note = "";
        public List<Node> Children = new();
        public long? Line;

        public JsonObject ToJson(bool includePath)
        {
            var obj = new JsonObject
            {
                ["id"] = Id,
                ["depth"] = Depth,
                ["num"] = Num,
                ["title"] = Title,
                ["value"] = Value,
                ["note"] = Note
            };
            if (includePath)
            {
                var arr = new JsonArray();
                foreach (int p in Path)
                {
                    arr.Add(p);
                }
                obj["path"] = arr;
            }
            if (Line.HasValue)
            {
                obj["line"] = Line.Value;
            }
            var children = new JsonArray();
            foreach (Node child in Children)
            {
                children.Add(child.ToJson(includePath));
            }
            obj["children"] = children;
            return obj;
        }
    }

    public sealed class Result
    {
        public bool Ok = true;
        public JsonObject? Error;
        public bool Empty;
        public bool Truncated;
        public bool Promoted;
        public Node Root = new();
        public int Nodes;
        public int MaxDepth;
        public int Branches;
        public int Lines;
        public string Outline = "";
        public string RootTitle = "思维导图";

        public JsonObject StatsJson() => new()
        {
            ["nodes"] = Nodes,
            ["max_depth"] = MaxDepth,
            ["branches"] = Branches,
            ["lines"] = Lines
        };

        public JsonObject ToFullJson()
        {
            var root = Root.ToJson(true);
            return new JsonObject
            {
                ["ok"] = Ok,
                ["error"] = Error?.DeepClone(),
                ["empty"] = Empty,
                ["truncated"] = Truncated,
                ["root"] = root,
                ["stats"] = StatsJson(),
                ["outline"] = Outline,
                ["promoted"] = Promoted
            };
        }
    }

    [ThreadStatic] private static bool _limitHit;

    private sealed class YNode
    {
        public const int ScalarKind = 0, MapKind = 1, SeqKind = 2;
        public int Kind = ScalarKind;
        public string Value = "";
        public ScalarStyle Style = ScalarStyle.Plain;
        public List<KeyValuePair<YNode, YNode>> Map = new();
        public List<YNode> Seq = new();
        public long Line = 1;
    }

    private static YNode? LoadDocument(string text)
    {
        var parser = new Parser(new StringReader(text));
        var anchors = new Dictionary<string, YNode>(StringComparer.Ordinal);
        while (parser.MoveNext())
        {
            ParsingEvent evt = parser.Current!;
            if (evt is DocumentStart)
            {
                if (!parser.MoveNext() || parser.Current is DocumentEnd)
                {
                    return null;
                }
                return ReadNode(parser, anchors);
            }
            if (evt is Scalar or MappingStart or SequenceStart or AnchorAlias)
            {
                return ReadNode(parser, anchors);
            }
        }
        return null;
    }

    private static YNode ReadNode(Parser parser, Dictionary<string, YNode> anchors)
    {
        ParsingEvent evt = parser.Current!;
        if (evt is AnchorAlias alias)
        {
            return anchors.TryGetValue(alias.Value.Value, out YNode? target)
                ? target
                : throw new YamlException(alias.Start, alias.End, $"未定义的 YAML 别名：{alias.Value.Value}");
        }
        if (evt is Scalar scalar)
        {
            var node = new YNode
            {
                Kind = YNode.ScalarKind,
                Value = scalar.Value ?? "",
                Style = scalar.Style,
                Line = scalar.Start.Line
            };
            if (!scalar.Anchor.IsEmpty)
            {
                anchors[scalar.Anchor.Value] = node;
            }
            return node;
        }
        if (evt is SequenceStart seqStart)
        {
            var node = new YNode { Kind = YNode.SeqKind, Line = seqStart.Start.Line };
            if (!seqStart.Anchor.IsEmpty)
            {
                anchors[seqStart.Anchor.Value] = node;
            }
            while (parser.MoveNext())
            {
                if (parser.Current is SequenceEnd)
                {
                    break;
                }
                node.Seq.Add(ReadNode(parser, anchors));
            }
            return node;
        }
        if (evt is MappingStart mapStart)
        {
            var node = new YNode { Kind = YNode.MapKind, Line = mapStart.Start.Line };
            if (!mapStart.Anchor.IsEmpty)
            {
                anchors[mapStart.Anchor.Value] = node;
            }
            while (parser.MoveNext())
            {
                if (parser.Current is MappingEnd)
                {
                    break;
                }
                YNode key = ReadNode(parser, anchors);
                if (!parser.MoveNext() || parser.Current is MappingEnd)
                {
                    break;
                }
                YNode value = ReadNode(parser, anchors);
                node.Map.Add(new KeyValuePair<YNode, YNode>(key, value));
            }
            return node;
        }
        throw new YamlException(parser.Current!.Start, parser.Current!.End, "无法识别的 YAML 节点");
    }

    private sealed class Payload
    {
        public const int None = 0, Map = 1, Seq = 2, Scalar = 3;
        public int Kind;
        public List<KeyValuePair<YNode?, YNode>> MapItems = new();
        public List<YNode> SeqItems = new();
    }

    public static Result Parse(string text, string rootTitle = "思维导图")
    {
        text ??= "";
        var result = new Result
        {
            RootTitle = string.IsNullOrWhiteSpace(rootTitle) ? "思维导图" : rootTitle,
            Lines = CountLines(text)
        };
        result.Root = new Node { Title = result.RootTitle, Id = "root", Depth = 0, Num = "" };
        string trimmed = text.Trim();
        if (trimmed.Length == 0 || trimmed is "{}" or "[]" or "null" or "~")
        {
            result.Empty = true;
            result.Nodes = 0;
            return result;
        }

        YNode? rootNode;
        try
        {
            rootNode = LoadDocument(text);
        }
        catch (YamlException ex)
        {
            result.Ok = false;
            long line = ex.Start.Line;
            long column = ex.Start.Column;
            result.Error = new JsonObject
            {
                ["message"] = ex.Message,
                ["context"] = "",
                ["line"] = line,
                ["column"] = column,
                ["raw"] = ex.ToString()
            };
            return result;
        }
        catch (Exception ex)
        {
            result.Ok = false;
            result.Error = new JsonObject
            {
                ["message"] = ex.Message,
                ["context"] = "",
                ["line"] = null,
                ["column"] = null,
                ["raw"] = ex.ToString()
            };
            return result;
        }

        if (rootNode == null)
        {
            var earlyOutline = new List<string>();
            CollectStats(result.Root, result, earlyOutline);
            result.Outline = "";
            result.Empty = true;
            return result;
        }

        int counter = 0;
        _limitHit = false;
        List<Node> rootChildren = Build(rootNode, 1, new List<int>(), ref counter);
        result.Truncated = _limitHit;

        if (rootNode is YNode mapping && mapping.Kind == YNode.MapKind && FlattenMapping(mapping).Count == 1 && rootChildren.Count == 1
            && rootChildren[0].Value.Length == 0)
        {
            Node only = rootChildren[0];
            if (only.Children.Count > 0)
            {
                result.Promoted = true;
                result.Root.Title = only.Title;
                result.Root.Line = only.Line ?? 1L;
                result.Root.Children = only.Children;
                counter -= 1;
                Reindex(result.Root.Children);
            }
            else
            {
                result.Root.Title = only.Title.Length > 0 ? only.Title : result.RootTitle;
                result.Root.Value = only.Value;
                result.Root.Line = only.Line;
            }
        }
        else if (rootNode.Kind == YNode.ScalarKind && rootChildren.Count > 0)
        {
            result.Root.Title = rootChildren[0].Title.Length > 0 ? rootChildren[0].Title : result.RootTitle;
            result.Root.Line = rootChildren[0].Line;
            counter = 0;
        }
        else
        {
            result.Root.Children = rootChildren;
            if (rootNode.Kind == YNode.ScalarKind)
            {
                result.Root.Line = rootNode.Line;
            }
        }

        result.Branches = result.Root.Children.Count;
        var outline = new List<string>();
        CollectStats(result.Root, result, outline);
        result.Outline = string.Join("\n", outline);
        if (result.Root.Children.Count == 0 && result.Root.Value.Length == 0)
        {
            result.Empty = true;
        }
        return result;
    }

    private static int CountLines(string text)
    {
        if (text.Length == 0)
        {
            return 0;
        }
        int count = 1;
        foreach (char ch in text)
        {
            if (ch == '\n')
            {
                count++;
            }
        }
        if (text.EndsWith('\n'))
        {
            count--;
        }
        return Math.Max(1, count);
    }

    private static List<Node> Build(YNode value, int depth, List<int> path, ref int counter)
    {
        var output = new List<Node>();
        if (IsNoneOrEmptyScalar(value))
        {
            return output;
        }

        IEnumerable<KeyValuePair<YNode?, YNode>> items = value.Kind == YNode.MapKind
            ? FlattenMapping(value)
            : value.Kind == YNode.SeqKind
                ? value.Seq.Select(v => new KeyValuePair<YNode?, YNode>(null, v))
                : new[] { new KeyValuePair<YNode?, YNode>(null, value) };

        int index = 0;
        foreach (var (key, val) in items)
        {
            index++;
            if (counter >= MaxNodes)
            {
                _limitHit = true;
                break;
            }
            var subPath = new List<int>(path) { index };
            counter++;
            var (title, inlineValue, note, payload, line) = Unpack(key, val);
            var node = new Node
            {
                Id = "n" + string.Join("-", subPath),
                Path = subPath,
                Depth = depth,
                Num = NodeNumber(subPath),
                Title = title,
                Value = inlineValue,
                Note = note,
                Line = line
            };
            node.Children = BuildPayload(payload, depth + 1, subPath, ref counter);
            output.Add(node);
        }
        return output;
    }

    private static List<Node> BuildPayload(Payload payload, int depth, List<int> path, ref int counter)
    {
        if (payload.Kind == Payload.None)
        {
            return new List<Node>();
        }
        if (payload.Kind == Payload.Map)
        {
            return BuildMap(payload.MapItems, depth, path, ref counter);
        }
        if (payload.Kind == Payload.Seq)
        {
            return BuildSequence(payload.SeqItems, depth, path, ref counter);
        }
        return new List<Node>();
    }

    private static List<Node> BuildMap(List<KeyValuePair<YNode?, YNode>> items, int depth, List<int> path,
        ref int counter)
    {
        var output = new List<Node>();
        int index = 0;
        foreach (var (key, val) in items)
        {
            index++;
            if (counter >= MaxNodes)
            {
                _limitHit = true;
                break;
            }
            var subPath = new List<int>(path) { index };
            counter++;
            var (title, inlineValue, note, payload, line) = Unpack(key, val);
            var node = new Node
            {
                Id = "n" + string.Join("-", subPath),
                Path = subPath,
                Depth = depth,
                Num = NodeNumber(subPath),
                Title = title,
                Value = inlineValue,
                Note = note,
                Line = line
            };
            node.Children = BuildPayload(payload, depth + 1, subPath, ref counter);
            output.Add(node);
        }
        return output;
    }

    private static List<Node> BuildSequence(List<YNode> items, int depth, List<int> path, ref int counter)
    {
        var output = new List<Node>();
        int index = 0;
        foreach (YNode val in items)
        {
            index++;
            if (counter >= MaxNodes)
            {
                _limitHit = true;
                break;
            }
            var subPath = new List<int>(path) { index };
            counter++;
            var (title, inlineValue, note, payload, line) = Unpack(null, val);
            var node = new Node
            {
                Id = "n" + string.Join("-", subPath),
                Path = subPath,
                Depth = depth,
                Num = NodeNumber(subPath),
                Title = title,
                Value = inlineValue,
                Note = note,
                Line = line
            };
            node.Children = BuildPayload(payload, depth + 1, subPath, ref counter);
            output.Add(node);
        }
        return output;
    }

    private static (string Title, string Value, string Note, Payload Payload, long? Line) Unpack(
        YNode? key, YNode val)
    {
        if (val.Kind == YNode.MapKind)
        {
            var pairs = FlattenMapping(val);
            string? titleKey = FirstKey(val, TitleKeys);
            if (titleKey != null)
            {
                YNode titleNode = GetNode(val, titleKey)!;
                string? childKey = FirstKey(val, ChildKeys);
                string? noteKey = FirstKey(val, NoteKeys);
                string title = ScalarText(titleNode);
                string note = noteKey != null ? ScalarText(GetNode(val, noteKey)!) : "";
                var rest = new List<KeyValuePair<YNode?, YNode>>();
                foreach (var pair in pairs)
                {
                    string k = ScalarText(pair.Key);
                    if (!string.Equals(k, titleKey, StringComparison.OrdinalIgnoreCase)
                        && (childKey == null || !string.Equals(k, childKey, StringComparison.OrdinalIgnoreCase))
                        && (noteKey == null || !string.Equals(k, noteKey, StringComparison.OrdinalIgnoreCase)))
                    {
                        rest.Add(new KeyValuePair<YNode?, YNode>(pair.Key, pair.Value));
                    }
                }
                Payload payload;
                if (childKey != null)
                {
                    YNode childNode = GetNode(val, childKey)!;
                    var childrenPayload = new List<YNode>();
                    if (childNode.Kind == YNode.SeqKind)
                    {
                        childrenPayload.AddRange(childNode.Seq);
                    }
                    else
                    {
                        childrenPayload.Add(childNode);
                    }
                    if (rest.Count > 0)
                    {
                        var seqItems = new List<YNode>();
                        seqItems.AddRange(childrenPayload);
                        var restMap = new YNode { Kind = YNode.MapKind, Line = val.Line };
                        foreach (var p in rest)
                        {
                            restMap.Map.Add(new KeyValuePair<YNode, YNode>(p.Key!, p.Value));
                        }
                        seqItems.Add(restMap);
                        payload = new Payload { Kind = Payload.Seq, SeqItems = seqItems };
                    }
                    else
                    {
                        if (childNode.Kind == YNode.SeqKind)
                        {
                            payload = new Payload { Kind = Payload.Seq, SeqItems = childrenPayload };
                        }
                        else
                        {
                            payload = new Payload { Kind = Payload.Map, MapItems = MapPairs(childNode) };
                        }
                    }
                }
                else if (rest.Count > 0)
                {
                    payload = new Payload { Kind = Payload.Map, MapItems = rest };
                }
                else
                {
                    payload = new Payload { Kind = Payload.None };
                }
                return (title, "", note, payload, key != null ? key.Line : val.Line);
            }

            if (key == null)
            {
                var first = pairs.FirstOrDefault();
                if (first.Key != null)
                {
                    var rest = new List<KeyValuePair<YNode?, YNode>>();
                    for (int i = 1; i < pairs.Count; i++)
                    {
                        rest.Add(pairs[i]);
                    }
                    if (first.Value.Kind == YNode.MapKind)
                    {
                        var merged = new List<KeyValuePair<YNode?, YNode>>();
                        foreach (var p in FlattenMapping(first.Value))
                        {
                            merged.Add(new KeyValuePair<YNode?, YNode>(p.Key, p.Value));
                        }
                        merged.AddRange(rest);
                        return (ScalarText(first.Key), "", "", new Payload { Kind = Payload.Map, MapItems = merged },
                            first.Key.Line);
                    }
                    if (first.Value.Kind == YNode.SeqKind)
                    {
                        var items = new List<YNode>(first.Value.Seq);
                        if (rest.Count > 0)
                        {
                            var restMap = new YNode { Kind = YNode.MapKind, Line = val.Line };
                            foreach (var p in rest)
                            {
                                restMap.Map.Add(new KeyValuePair<YNode, YNode>(p.Key!, p.Value));
                            }
                            items.Add(restMap);
                        }
                        return (ScalarText(first.Key), "", "", new Payload { Kind = Payload.Seq, SeqItems = items },
                            first.Key.Line);
                    }
                    Payload tail = rest.Count > 0
                        ? new Payload { Kind = Payload.Map, MapItems = rest }
                        : new Payload { Kind = Payload.None };
                    return (ScalarText(first.Key), ScalarText(first.Value), "", tail, first.Key.Line);
                }
            }
        }

        if (key == null)
        {
            if (val.Kind == YNode.SeqKind)
            {
                return ("列表", "", "", new Payload { Kind = Payload.Seq, SeqItems = val.Seq }, val.Line);
            }
            return (ScalarText(val), "", "", new Payload { Kind = Payload.None }, val.Line);
        }

        if (val.Kind == YNode.MapKind)
        {
            return (ScalarText(key), "", "", new Payload { Kind = Payload.Map, MapItems = MapPairs(val) }, key.Line);
        }
        if (val.Kind == YNode.SeqKind)
        {
            return (ScalarText(key), "", "", new Payload { Kind = Payload.Seq, SeqItems = val.Seq }, key.Line);
        }
        return (ScalarText(key), ScalarText(val), "", new Payload { Kind = Payload.None }, key.Line);
    }

    /// <summary>按 PyYAML 的规则展开 YAML 合并键（&lt;&lt;），合并项排在显式键之前。</summary>
    private static List<KeyValuePair<YNode?, YNode>> FlattenMapping(YNode map)
    {
        return FlattenMapping(map, new HashSet<YNode>());
    }

    private static List<KeyValuePair<YNode?, YNode>> FlattenMapping(YNode map, HashSet<YNode> stack)
    {
        if (!stack.Add(map))
        {
            return new List<KeyValuePair<YNode?, YNode>>();
        }
        var merged = new List<KeyValuePair<YNode?, YNode>>();
        var explicitPairs = new List<KeyValuePair<YNode?, YNode>>();
        try
        {
            foreach (var pair in map.Map)
            {
                if (IsMergeKey(pair.Key))
                {
                    if (pair.Value.Kind == YNode.MapKind)
                    {
                        merged.AddRange(FlattenMapping(pair.Value, stack));
                    }
                    else if (pair.Value.Kind == YNode.SeqKind)
                    {
                        var groups = new List<List<KeyValuePair<YNode?, YNode>>>();
                        foreach (YNode node in pair.Value.Seq)
                        {
                            if (node.Kind == YNode.MapKind)
                            {
                                groups.Add(FlattenMapping(node, stack));
                            }
                        }
                        groups.Reverse();
                        foreach (var group in groups)
                        {
                            merged.AddRange(group);
                        }
                    }
                }
                else
                {
                    explicitPairs.Add(new KeyValuePair<YNode?, YNode>(pair.Key, pair.Value));
                }
            }
        }
        finally
        {
            stack.Remove(map);
        }
        merged.AddRange(explicitPairs);
        // 与 Python dict 一致：重复键保留首次出现的位置，值取最后一次
        var output = new List<KeyValuePair<YNode?, YNode>>();
        var indexByKey = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var pair in merged)
        {
            if (pair.Key != null && pair.Key.Kind == YNode.ScalarKind)
            {
                string keyText = ScalarText(pair.Key);
                if (indexByKey.TryGetValue(keyText, out int existing))
                {
                    output[existing] = new KeyValuePair<YNode?, YNode>(output[existing].Key, pair.Value);
                    continue;
                }
                indexByKey[keyText] = output.Count;
            }
            output.Add(pair);
        }
        return output;
    }

    private static bool IsMergeKey(YNode? node)
    {
        return node != null && node.Kind == YNode.ScalarKind
            && node.Style == ScalarStyle.Plain && node.Value == "<<";
    }

    private static List<KeyValuePair<YNode?, YNode>> MapPairs(YNode node)
    {
        if (node.Kind == YNode.MapKind)
        {
            return FlattenMapping(node);
        }
        if (node.Kind == YNode.SeqKind)
        {
            return node.Seq.Select(v => new KeyValuePair<YNode?, YNode>(null, v)).ToList();
        }
        return new List<KeyValuePair<YNode?, YNode>> { new(null, node) };
    }

    private static string? FirstKey(YNode map, string[] candidates)
    {
        foreach (var pair in FlattenMapping(map))
        {
            string keyText = ScalarText(pair.Key).Trim();
            foreach (string candidate in candidates)
            {
                if (string.Equals(keyText, candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return keyText;
                }
            }
        }
        return null;
    }

    private static YNode? GetNode(YNode map, string keyText)
    {
        foreach (var pair in FlattenMapping(map))
        {
            if (string.Equals(ScalarText(pair.Key).Trim(), keyText, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }
        return null;
    }

    private static bool IsNoneOrEmptyScalar(YNode node)
    {
        return node.Kind == YNode.ScalarKind && ScalarText(node).Length == 0;
    }

    private static bool ScalarIsNull(YNode scalar)
    {
        if (scalar.Style != ScalarStyle.Plain)
        {
            return false;
        }
        string value = scalar.Value ?? "";
        return value is "" or "~" or "null" or "Null" or "NULL";
    }

    private static bool ScalarIsBool(YNode scalar, out bool boolValue)
    {
        boolValue = false;
        if (scalar.Style != ScalarStyle.Plain)
        {
            return false;
        }
        string v = scalar.Value ?? "";
        switch (v)
        {
            case "true": case "True": case "TRUE": case "yes": case "Yes": case "YES":
            case "on": case "On": case "ON":
                boolValue = true;
                return true;
            case "false": case "False": case "FALSE": case "no": case "No": case "NO":
            case "off": case "Off": case "OFF":
                boolValue = false;
                return true;
        }
        return false;
    }

    /// <summary>与 PyYAML(SafeLoader) 一致的标量显示文本。</summary>
    private static string ScalarText(YNode? node)
    {
        if (node == null || node.Kind != YNode.ScalarKind)
        {
            return "";
        }
        YNode scalar = node;
        if (ScalarIsNull(scalar))
        {
            return "";
        }
        if (scalar.Style != ScalarStyle.Plain)
        {
            return scalar.Value ?? "";
        }
        string text = scalar.Value ?? "";
        if (ScalarIsBool(scalar, out bool b))
        {
            return b ? "true" : "false";
        }
        if (TryParsePyInt(text, out BigInteger integer))
        {
            return integer.ToString(CultureInfo.InvariantCulture);
        }
        if (TryParsePyFloat(text, out double dbl))
        {
            if (double.IsPositiveInfinity(dbl)) return ".inf";
            if (double.IsNegativeInfinity(dbl)) return "-.inf";
            if (double.IsNaN(dbl)) return ".nan";
            return FormatPyFloat(dbl);
        }
        return text;
    }

    private static bool TryParsePyInt(string text, out BigInteger value)
    {
        value = default;
        if (!PyIntPattern.IsMatch(text))
        {
            return false;
        }
        string t = text.Replace("_", "");
        bool negative = false;
        if (t.Length > 0 && (t[0] == '+' || t[0] == '-'))
        {
            negative = t[0] == '-';
            t = t[1..];
        }
        try
        {
            BigInteger parsed;
            if (t.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
            {
                parsed = ParseBased(t[2..], 2);
            }
            else if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                parsed = ParseBased(t[2..], 16);
            }
            else if (t.Contains(':'))
            {
                parsed = 0;
                foreach (string part in t.Split(':'))
                {
                    parsed = parsed * 60 + BigInteger.Parse(part, CultureInfo.InvariantCulture);
                }
            }
            else if (t.Length > 1 && t[0] == '0')
            {
                parsed = ParseBased(t[1..], 8);
            }
            else
            {
                parsed = BigInteger.Parse(t, CultureInfo.InvariantCulture);
            }
            value = negative ? -parsed : parsed;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static BigInteger ParseBased(string text, int radix)
    {
        BigInteger value = 0;
        foreach (char ch in text)
        {
            if (ch == '_') continue;
            int digit = ch switch
            {
                >= '0' and <= '9' => ch - '0',
                >= 'a' and <= 'f' => ch - 'a' + 10,
                >= 'A' and <= 'F' => ch - 'A' + 10,
                _ => -1
            };
            if (digit < 0 || digit >= radix)
            {
                throw new FormatException();
            }
            value = value * radix + digit;
        }
        return value;
    }

    private static bool TryParsePyFloat(string text, out double value)
    {
        value = 0;
        string t = text.Replace("_", "");
        if (t is ".inf" or ".Inf" or ".INF" or "+.inf" or "+.Inf" or "+.INF")
        {
            value = double.PositiveInfinity;
            return true;
        }
        if (t is "-.inf" or "-.Inf" or "-.INF")
        {
            value = double.NegativeInfinity;
            return true;
        }
        if (t is ".nan" or ".NaN" or ".NAN")
        {
            value = double.NaN;
            return true;
        }
        if (!PyFloatPattern.IsMatch(text))
        {
            return false;
        }
        try
        {
            if (t.Contains(':'))
            {
                // 六十进制浮点：1:20.5 -> 80.5
                bool negative = t.StartsWith("-");
                if (t[0] is '+' or '-') t = t[1..];
                double total = 0;
                string[] parts = t.Split(':');
                for (int i = 0; i < parts.Length; i++)
                {
                    double part = double.Parse(parts[i], CultureInfo.InvariantCulture);
                    total = total * 60 + part;
                }
                value = negative ? -total : total;
                return true;
            }
            return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
        catch
        {
            return false;
        }
    }

    private static string FormatPyFloat(double value)
    {
        string text = value.ToString("R", CultureInfo.InvariantCulture).Replace("E", "e");
        if (!text.Contains('.') && !text.Contains('e') && !text.Contains("Inf") && !text.Contains("NaN"))
        {
            text += ".0";
        }
        return text;
    }

    public static string ChineseNumber(int n)
    {
        string digits = "零一二三四五六七八九";
        if (n <= 0)
        {
            return n.ToString(CultureInfo.InvariantCulture);
        }
        if (n < 10)
        {
            return digits[n].ToString();
        }
        if (n < 20)
        {
            return "十" + (n % 10 > 0 ? digits[n % 10].ToString() : "");
        }
        if (n < 100)
        {
            return digits[n / 10].ToString() + "十" + (n % 10 > 0 ? digits[n % 10].ToString() : "");
        }
        if (n < 1000)
        {
            int head = n / 100;
            int tail = n % 100;
            string s = digits[head].ToString() + "百";
            if (tail == 0)
            {
                return s;
            }
            if (tail < 10)
            {
                return s + "零" + digits[tail];
            }
            return s + ChineseNumber(tail);
        }
        if (n < 10000)
        {
            int head = n / 1000;
            int tail = n % 1000;
            string s = digits[head].ToString() + "千";
            if (tail == 0)
            {
                return s;
            }
            if (tail < 100)
            {
                return s + "零" + ChineseNumber(tail);
            }
            return s + ChineseNumber(tail);
        }
        return n.ToString(CultureInfo.InvariantCulture);
    }

    public static string NodeNumber(IReadOnlyList<int> path)
    {
        int depth = path.Count;
        if (depth == 0)
        {
            return "";
        }
        if (depth == 1)
        {
            return ChineseNumber(path[0]) + "、";
        }
        return string.Join(".", path) + new string('-', depth) + " ";
    }

    private static void Reindex(List<Node> nodes)
    {
        ReindexInner(nodes, new List<int>());
    }

    private static void ReindexInner(List<Node> nodes, List<int> parentPath)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            Node node = nodes[i];
            var path = new List<int>(parentPath) { i + 1 };
            node.Path = path;
            node.Id = "n" + string.Join("-", path);
            node.Num = NodeNumber(path);
            ReindexInner(node.Children, path);
        }
    }

    private static void CollectStats(Node node, Result result, List<string> outline)
    {
        result.Nodes++;
        result.MaxDepth = Math.Max(result.MaxDepth, node.Depth);
        if (node.Depth > 0)
        {
            outline.Add(node.Num + node.Title + (node.Value.Length > 0 ? " " + node.Value : ""));
        }
        foreach (Node child in node.Children)
        {
            CollectStats(child, result, outline);
        }
    }

    public static List<JsonObject> Flatten(Result result, int limit = 5000)
    {
        var output = new List<JsonObject>();
        void Walk(Node node)
        {
            if (output.Count >= limit)
            {
                return;
            }
            if (node.Depth > 0)
            {
                var obj = new JsonObject
                {
                    ["num"] = node.Num.Trim(),
                    ["title"] = node.Title,
                    ["value"] = node.Value,
                    ["depth"] = node.Depth,
                    ["line"] = node.Line.HasValue ? node.Line.Value : null
                };
                output.Add(obj);
            }
            foreach (Node child in node.Children)
            {
                Walk(child);
            }
        }
        Walk(result.Root);
        return output;
    }

    public static JsonObject Slim(Result result)
    {
        JsonObject obj = result.ToFullJson();
        obj.Remove("outline");
        obj.Remove("truncated");
        RemovePath(obj["root"] as JsonObject);
        return obj;

        static void RemovePath(JsonObject? node)
        {
            if (node == null)
            {
                return;
            }
            node.Remove("path");
            if (node["children"] is JsonArray children)
            {
                foreach (JsonNode? child in children)
                {
                    RemovePath(child as JsonObject);
                }
            }
        }
    }

    public static string Preview(Result result)
    {
        Node root = result.Root;
        List<string> labels = root.Children
            .Select(k => k.Title.Length > 0 ? k.Title : k.Value)
            .Where(s => s.Length > 0)
            .Take(6)
            .ToList();
        if (root.Title.Length > 0 && root.Title != "思维导图" && root.Children.Count == 1)
        {
            var inner = root.Children[0].Children.Select(k => k.Title).Where(s => s.Length > 0).Take(6).ToList();
            if (inner.Count > 0)
            {
                labels = inner;
            }
        }
        string joined = string.Join(" · ", labels);
        return joined.Length > 240 ? joined[..240] : joined;
    }
}
