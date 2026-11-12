using System.Text;

namespace MiniMonitor;

/// <summary>
/// 轻量级语法高亮：把代码转成带颜色 span 的 HTML，供 Markdown 代码块与代码文件预览共用。
/// 不依赖第三方库，覆盖常见语言（C#/C/C++/Java/JS/TS/Go/Rust/Python/JSON/XML/SQL/Shell 等）。
/// </summary>
internal static class SyntaxHighlighter
{
    /// <summary>代码块与代码页面共用的浅色主题样式。</summary>
    public const string Css =
        "pre.code{margin:0;padding:14px 16px;overflow:auto;background:#f6f8fa;color:#24292f;"
        + "font-family:Consolas,'Cascadia Code','Microsoft YaHei UI',monospace;font-size:13px;"
        + "line-height:1.55;tab-size:4}"
        + "pre.code.code-file{margin:0;padding:20px 22px;min-height:100vh;box-sizing:border-box}"
        + ".tok-k{color:#cf222e;font-weight:600}.tok-s{color:#0a3069}.tok-c{color:#6e7781;font-style:italic}"
        + ".tok-n{color:#0550ae}.tok-t{color:#8250df}.tok-a{color:#953800}.tok-f{color:#6639ba}";

    private static readonly HashSet<string> CommonKeywords = new(StringComparer.Ordinal)
    {
        // C# / C / C++ / Java / Go / Rust / Kotlin / Swift / JS / TS / PHP 常用关键字合集
        "abstract", "alignas", "alignof", "and", "as", "async", "auto", "await", "base", "bool",
        "boolean", "break", "byte", "case", "catch", "chan", "char", "checked", "class", "clone",
        "companion", "const", "constexpr", "constructor", "continue", "crate", "data", "decimal",
        "declare", "decltype", "defer", "default", "delegate", "delete", "do", "double", "dynamic",
        "echo", "else", "elseif", "endfor", "endforeach", "endif", "endwhile", "enum", "event",
        "explicit", "export", "extends", "extern", "false", "fallthrough", "final", "finally",
        "fixed", "float", "fn", "for", "foreach", "friend", "from", "func", "function", "get",
        "global", "go", "goto", "guard", "if", "impl", "implements", "implicit", "import", "in",
        "include", "init", "inline", "inout", "instanceof", "interface", "internal", "is", "lateinit",
        "let", "lock", "long", "loop", "map", "match", "mod", "module", "move", "mutable", "namespace",
        "native", "new", "noexcept", "null", "nullptr", "number", "object", "of", "operator", "out",
        "override", "package", "params", "partial", "private", "protected", "protocol", "pub",
        "public", "range", "readonly", "record", "ref", "register", "reinterpret_cast", "require",
        "restrict", "return", "sbyte", "sealed", "select", "set", "short", "signed", "sizeof",
        "some", "stackalloc", "static", "static_cast", "strictfp", "string", "struct", "super",
        "suspend", "switch", "symbol", "synchronized", "template", "this", "throw", "throws",
        "trait", "transient", "trap", "true", "try", "type", "typedef", "typeid", "typename",
        "typeof", "uint", "ulong", "unchecked", "undefined", "union", "unknown", "unsafe",
        "unsigned", "use", "ushort", "using", "val", "var", "virtual", "void", "volatile",
        "when", "where", "while", "yield", "yield return"
    };

    private static readonly HashSet<string> SqlKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "select", "from", "where", "insert", "into", "values", "update", "set", "delete", "create",
        "table", "drop", "alter", "add", "column", "index", "view", "join", "inner", "left", "right",
        "full", "outer", "cross", "on", "group", "by", "order", "having", "limit", "offset", "as",
        "and", "or", "not", "null", "is", "in", "exists", "between", "like", "case", "when", "then",
        "else", "end", "distinct", "union", "all", "top", "primary", "key", "foreign", "references",
        "constraint", "default", "unique", "check", "begin", "commit", "rollback", "transaction",
        "declare", "procedure", "function", "returns", "return", "if", "while", "loop", "exec",
        "truncate", "grant", "revoke", "cast", "convert", "with", "over", "partition", "asc", "desc"
    };

    private static readonly HashSet<string> ShellKeywords = new(StringComparer.Ordinal)
    {
        "if", "then", "else", "elif", "fi", "for", "while", "until", "do", "done", "case", "esac",
        "function", "return", "exit", "local", "export", "readonly", "declare", "unset", "source",
        "alias", "eval", "exec", "trap", "set", "shift", "echo", "printf", "read", "cd", "test"
    };

    private static readonly HashSet<string> PowerShelKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "function", "param", "begin", "process", "end", "if", "else", "elseif", "switch", "foreach",
        "for", "while", "do", "until", "try", "catch", "finally", "throw", "return", "break",
        "continue", "filter", "in", "trap", "exit", "class", "enum", "using", "hidden", "static"
    };

    private static readonly HashSet<string> BatchKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "if", "else", "for", "in", "do", "goto", "call", "set", "setlocal", "endlocal", "shift",
        "exit", "rem", "echo", "pause", "start", "copy", "move", "del", "ren", "md", "rd", "cd"
    };

    private static readonly HashSet<string> PythonKeywords = new(StringComparer.Ordinal)
    {
        "and", "as", "assert", "async", "await", "break", "class", "continue", "def", "del", "elif",
        "else", "except", "False", "finally", "for", "from", "global", "if", "import", "in", "is",
        "lambda", "None", "nonlocal", "not", "or", "pass", "raise", "return", "True", "try", "while",
        "with", "yield", "match", "case", "self"
    };

    private static readonly HashSet<string> RubyLuaKeywords = new(StringComparer.Ordinal)
    {
        "def", "end", "module", "class", "if", "elsif", "else", "unless", "while", "until", "for",
        "in", "do", "begin", "rescue", "ensure", "return", "yield", "then", "nil", "true", "false",
        "and", "or", "not", "self", "require", "include", "extend", "attr_accessor", "function",
        "local", "repeat", "until", "elseif", "then", "break", "goto", "nil"
    };

    /// <summary>根据文件路径推断语言名。</summary>
    public static string DetectLanguage(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        string name = Path.GetFileName(path).ToLowerInvariant();
        if (name is "makefile" or "dockerfile" or ".gitignore" or ".gitattributes" or ".editorconfig")
        {
            return "ini";
        }
        return ext switch
        {
            ".cs" => "clike",
            ".csproj" or ".vbproj" or ".vcxproj" => "xml",
            ".sln" => "ini",
            ".c" or ".h" or ".cpp" or ".cxx" or ".cc" or ".hpp" or ".hh" or ".m" or ".mm" => "clike",
            ".java" or ".gradle" => "clike",
            ".js" or ".mjs" or ".cjs" or ".jsx" or ".ts" or ".tsx" or ".vue" or ".svelte" => "clike",
            ".go" or ".rs" or ".kt" or ".kts" or ".swift" or ".php" or ".dart" or ".scala" => "clike",
            ".py" or ".pyw" => "python",
            ".rb" or ".lua" => "ruby",
            ".json" or ".jsonc" or ".ipynb" => "json",
            ".xml" or ".xsd" or ".xsl" or ".xaml" or ".svg" or ".plist" or ".resx"
                or ".config" or ".manifest" => "xml",
            ".html" or ".htm" or ".xhtml" => "html",
            ".css" or ".scss" or ".less" => "css",
            ".sql" => "sql",
            ".sh" or ".bash" or ".zsh" or ".fish" => "shell",
            ".ps1" or ".psm1" or ".psd1" => "powershell",
            ".bat" or ".cmd" => "batch",
            ".yml" or ".yaml" or ".toml" or ".ini" or ".cfg" or ".conf" or ".properties"
                or ".env" or ".reg" => "ini",
            ".asm" or ".vhd" or ".vhdl" or ".proto" or ".graphql" => "clike",
            _ => "plain"
        };
    }

    /// <summary>Markdown 代码围栏的语言标记归一化。</summary>
    public static string NormalizeLanguage(string token)
    {
        string value = (token ?? "").Trim().Trim('{', '}').ToLowerInvariant();
        if (value.Length == 0)
        {
            return "plain";
        }
        // 只取第一个词，兼容 “csharp title=xxx” 之类的写法
        int space = value.IndexOfAny(new[] { ' ', '\t' });
        if (space > 0)
        {
            value = value[..space];
        }
        // 已是内部标准名称时直接返回，保证 NormalizeLanguage 幂等。
        switch (value)
        {
            case "clike":
            case "python":
            case "json":
            case "xml":
            case "html":
            case "css":
            case "sql":
            case "shell":
            case "powershell":
            case "batch":
            case "ini":
            case "ruby":
            case "plain":
                return value;
        }
        return value switch
        {
            "c#" or "cs" or "csharp" or "dotnet" => "clike",
            "c++" or "cpp" or "c" or "h" or "hpp" or "objc" or "objective-c" => "clike",
            "java" or "kotlin" or "kt" or "swift" or "go" or "golang" or "rs" or "rust"
                or "php" or "ts" or "typescript" or "js" or "javascript" or "jsx" or "tsx"
                or "vue" or "svelte" or "dart" or "scala" or "gradle" => "clike",
            "py" or "python" => "python",
            "rb" or "ruby" or "lua" => "ruby",
            "json" or "jsonc" => "json",
            "xml" or "xaml" or "svg" or "xsd" => "xml",
            "html" or "htm" or "xhtml" => "html",
            "css" or "scss" or "less" => "css",
            "sql" or "mysql" or "postgresql" or "sqlite" => "sql",
            "sh" or "bash" or "shell" or "zsh" or "fish" or "console" => "shell",
            "ps1" or "powershell" or "pwsh" => "powershell",
            "bat" or "batch" or "cmd" or "dos" => "batch",
            "yaml" or "yml" or "toml" or "ini" or "conf" or "properties" or "env" => "ini",
            _ => "plain"
        };
    }

    /// <summary>把代码转为带高亮标记的 HTML（已做 HTML 转义）。</summary>
    public static string Highlight(string code, string language)
    {
        try
        {
            switch (NormalizeLanguage(language))
            {
                case "python":
                    return ScanCode(code, PythonKeywords, Array.Empty<string>(),
                        caseInsensitive: false, pythonStrings: true, sqlStrings: false,
                        dollarVars: false, hashComment: true);
                case "ruby":
                    return ScanCode(code, RubyLuaKeywords, Array.Empty<string>(),
                        caseInsensitive: false, pythonStrings: false, sqlStrings: false,
                        dollarVars: false, hashComment: true);
                case "json":
                    return ScanJson(code);
                case "xml":
                case "html":
                    return ScanXml(code);
                case "sql":
                    return ScanCode(code, SqlKeywords, new[] { "--" },
                        caseInsensitive: true, pythonStrings: false, sqlStrings: true,
                        dollarVars: false);
                case "css":
                    return ScanCode(code, new HashSet<string>(), Array.Empty<string>(),
                        caseInsensitive: false, pythonStrings: false, sqlStrings: false,
                        dollarVars: false);
                case "shell":
                    return ScanCode(code, ShellKeywords, Array.Empty<string>(),
                        caseInsensitive: false, pythonStrings: false, sqlStrings: false,
                        dollarVars: true, hashComment: true);
                case "powershell":
                    return ScanCode(code, PowerShelKeywords, Array.Empty<string>(),
                        caseInsensitive: true, pythonStrings: false, sqlStrings: false,
                        dollarVars: true, hashComment: true);
                case "batch":
                    return ScanCode(code, BatchKeywords, new[] { "::", "REM ", "rem " },
                        caseInsensitive: true, pythonStrings: false, sqlStrings: false,
                        dollarVars: false);
                case "ini":
                    return ScanKeyValue(code);
                case "clike":
                    return ScanCode(code, CommonKeywords, Array.Empty<string>(),
                        caseInsensitive: false, pythonStrings: false, sqlStrings: false,
                        dollarVars: false);
                default:
                    return Escape(code);
            }
        }
        catch
        {
            return Escape(code);
        }
    }

    private static string ScanCode(string code, HashSet<string> keywords, string[] extraLineComments,
        bool caseInsensitive, bool pythonStrings, bool sqlStrings, bool dollarVars,
        bool hashComment = false)
    {
        var builder = new StringBuilder(code.Length + code.Length / 3);
        int i = 0;
        int n = code.Length;
        while (i < n)
        {
            char c = code[i];
            if (c == '\n')
            {
                builder.Append('\n');
                i++;
                continue;
            }
            if (c == '\r')
            {
                i++;
                continue;
            }
            if (hashComment && c == '#')
            {
                int end = LineEnd(code, i);
                builder.Append("<span class=\"tok-c\">").Append(Escape(code[i..end])).Append("</span>");
                i = end;
                continue;
            }
            if (c == '/' && i + 1 < n && code[i + 1] == '/')
            {
                int end = LineEnd(code, i);
                builder.Append("<span class=\"tok-c\">").Append(Escape(code[i..end])).Append("</span>");
                i = end;
                continue;
            }
            if (c == '/' && i + 1 < n && code[i + 1] == '*')
            {
                int end = code.IndexOf("*/", i + 2, StringComparison.Ordinal);
                end = end < 0 ? n : end + 2;
                builder.Append("<span class=\"tok-c\">").Append(Escape(code[i..end])).Append("</span>");
                i = end;
                continue;
            }
            bool lineComment = false;
            foreach (string prefix in extraLineComments)
            {
                if (StartsWith(code, i, prefix, caseInsensitive))
                {
                    int end = LineEnd(code, i);
                    builder.Append("<span class=\"tok-c\">").Append(Escape(code[i..end])).Append("</span>");
                    i = end;
                    lineComment = true;
                    break;
                }
            }
            if (lineComment)
            {
                continue;
            }
            if (c == '"' || c == '\'')
            {
                if (pythonStrings && i + 2 < n && code[i + 1] == c && code[i + 2] == c)
                {
                    int tripleEnd = code.IndexOf(new string(c, 3), i + 3, StringComparison.Ordinal);
                    tripleEnd = tripleEnd < 0 ? n : tripleEnd + 3;
                    builder.Append("<span class=\"tok-s\">")
                        .Append(Escape(code[i..tripleEnd]))
                        .Append("</span>");
                    i = tripleEnd;
                    continue;
                }
                int end = ScanString(code, i, c, sqlStrings);
                builder.Append("<span class=\"tok-s\">").Append(Escape(code[i..end])).Append("</span>");
                i = end;
                continue;
            }
            if (c == '`' && !sqlStrings)
            {
                int end = code.IndexOf('`', i + 1);
                end = end < 0 ? n : end + 1;
                builder.Append("<span class=\"tok-s\">").Append(Escape(code[i..end])).Append("</span>");
                i = end;
                continue;
            }
            if (char.IsDigit(c))
            {
                int start = i;
                while (i < n && (char.IsLetterOrDigit(code[i]) || code[i] == '.' || code[i] == 'x'
                    || code[i] == 'X' || code[i] == '_'))
                {
                    i++;
                }
                builder.Append("<span class=\"tok-n\">").Append(Escape(code[start..i])).Append("</span>");
                continue;
            }
            if (char.IsLetter(c) || c == '_' || c == '$' || c == '@')
            {
                int start = i;
                if (c == '$' || c == '@')
                {
                    i++;
                }
                while (i < n)
                {
                    char ch = code[i];
                    if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '$')
                    {
                        i++;
                    }
                    else
                    {
                        break;
                    }
                }
                string token = code[start..i];
                if (token.Length == 0)
                {
                    builder.Append(Escape(c.ToString()));
                    i++;
                    continue;
                }
                if (c == '@' && i > start + 1)
                {
                    builder.Append("<span class=\"tok-f\">").Append(Escape(token)).Append("</span>");
                }
                else if (keywords.Contains(token)
                    || (caseInsensitive && keywords.Contains(token.ToLowerInvariant()))
                    || (dollarVars && c == '$' && token.Length > 1))
                {
                    builder.Append("<span class=\"tok-k\">").Append(Escape(token)).Append("</span>");
                }
                else
                {
                    builder.Append(Escape(token));
                }
                continue;
            }
            builder.Append(Escape(c.ToString()));
            i++;
        }
        return builder.ToString();
    }

    private static string ScanJson(string code)
    {
        var builder = new StringBuilder(code.Length + code.Length / 3);
        int i = 0, n = code.Length;
        while (i < n)
        {
            char c = code[i];
            if (c == '"')
            {
                int end = ScanString(code, i, '"', false);
                int look = end;
                while (look < n && char.IsWhiteSpace(code[look]))
                {
                    look++;
                }
                string cls = look < n && code[look] == ':' ? "tok-a" : "tok-s";
                builder.Append("<span class=\"").Append(cls).Append("\">")
                    .Append(Escape(code[i..end])).Append("</span>");
                i = end;
                continue;
            }
            if (c == '/' && i + 1 < n && code[i + 1] == '/')
            {
                int end = LineEnd(code, i);
                builder.Append("<span class=\"tok-c\">").Append(Escape(code[i..end]));
                builder.Append("</span>");
                i = end;
                continue;
            }
            if (c == '/' && i + 1 < n && code[i + 1] == '*')
            {
                int end = code.IndexOf("*/", i + 2, StringComparison.Ordinal);
                end = end < 0 ? n : end + 2;
                builder.Append("<span class=\"tok-c\">").Append(Escape(code[i..end]));
                builder.Append("</span>");
                i = end;
                continue;
            }
            if (char.IsDigit(c) || (c == '-' && i + 1 < n && char.IsDigit(code[i + 1])))
            {
                int start = i++;
                while (i < n && (char.IsDigit(code[i]) || code[i] == '.' || code[i] == 'e'
                    || code[i] == 'E' || code[i] == '+' || code[i] == '-'))
                {
                    i++;
                }
                builder.Append("<span class=\"tok-n\">").Append(Escape(code[start..i]));
                builder.Append("</span>");
                continue;
            }
            if (char.IsLetter(c))
            {
                int start = i;
                while (i < n && (char.IsLetterOrDigit(code[i]) || code[i] == '_'))
                {
                    i++;
                }
                string token = code[start..i];
                if (token is "true" or "false" or "null")
                {
                    builder.Append("<span class=\"tok-k\">").Append(token).Append("</span>");
                }
                else
                {
                    builder.Append(Escape(token));
                }
                continue;
            }
            builder.Append(Escape(c.ToString()));
            i++;
        }
        return builder.ToString();
    }

    private static string ScanXml(string code)
    {
        var builder = new StringBuilder(code.Length + code.Length / 3);
        int i = 0, n = code.Length;
        while (i < n)
        {
            if (StartsWith(code, i, "<!--", false))
            {
                int end = code.IndexOf("-->", i + 4, StringComparison.Ordinal);
                end = end < 0 ? n : end + 3;
                builder.Append("<span class=\"tok-c\">").Append(Escape(code[i..end]));
                builder.Append("</span>");
                i = end;
                continue;
            }
            if (code[i] == '<')
            {
                int tagStart = i;
                i++;
                if (i < n && code[i] == '/')
                {
                    i++;
                }
                if (i < n && (code[i] == '!' || code[i] == '?'))
                {
                    int end = code.IndexOf('>', i);
                    end = end < 0 ? n : end + 1;
                    builder.Append("<span class=\"tok-c\">").Append(Escape(code[tagStart..end]));
                    builder.Append("</span>");
                    i = end;
                    continue;
                }
                builder.Append("&lt;");
                if (i > tagStart + 1 && code[tagStart + 1] == '/')
                {
                    builder.Append('/');
                }
                int nameStart = i;
                while (i < n && (char.IsLetterOrDigit(code[i]) || code[i] == ':' || code[i] == '-'
                    || code[i] == '_' || code[i] == '.'))
                {
                    i++;
                }
                builder.Append("<span class=\"tok-t\">").Append(Escape(code[nameStart..i]));
                builder.Append("</span>");
                while (i < n && code[i] != '>')
                {
                    if (char.IsWhiteSpace(code[i]) || code[i] == '/' || code[i] == '=')
                    {
                        builder.Append(Escape(code[i].ToString()));
                        i++;
                        continue;
                    }
                    if (code[i] == '"' || code[i] == '\'')
                    {
                        int end = ScanString(code, i, code[i], false);
                        builder.Append("<span class=\"tok-s\">").Append(Escape(code[i..end]));
                        builder.Append("</span>");
                        i = end;
                        continue;
                    }
                    int attrStart = i;
                    while (i < n && (char.IsLetterOrDigit(code[i]) || code[i] == ':' || code[i] == '-'
                        || code[i] == '_' || code[i] == '.'))
                    {
                        i++;
                    }
                    if (i > attrStart)
                    {
                        builder.Append("<span class=\"tok-a\">").Append(Escape(code[attrStart..i]));
                        builder.Append("</span>");
                    }
                    else
                    {
                        builder.Append(Escape(code[i].ToString()));
                        i++;
                    }
                }
                if (i < n)
                {
                    builder.Append("&gt;");
                    i++;
                }
                continue;
            }
            int textStart = i;
            while (i < n && code[i] != '<')
            {
                i++;
            }
            builder.Append(Escape(code[textStart..i]));
        }
        return builder.ToString();
    }

    /// <summary>YAML / INI / TOML / properties：行首键名、注释与引号字符串。</summary>
    private static string ScanKeyValue(string code)
    {
        var builder = new StringBuilder(code.Length + code.Length / 3);
        foreach (string rawLine in code.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            string trimmed = line.TrimStart();
            if (trimmed.Length == 0)
            {
                builder.Append('\n');
                continue;
            }
            if (trimmed[0] == '#' || trimmed[0] == ';')
            {
                builder.Append("<span class=\"tok-c\">").Append(Escape(line)).Append("</span>\n");
                continue;
            }
            int separator = -1;
            int colon = line.IndexOf(':');
            int equal = line.IndexOf('=');
            if (colon >= 0 && equal >= 0)
            {
                separator = Math.Min(colon, equal);
            }
            else
            {
                separator = Math.Max(colon, equal);
            }
            if (separator >= 0 && separator < line.Length - 1)
            {
                builder.Append("<span class=\"tok-a\">").Append(Escape(line[..separator]));
                builder.Append("</span>");
                builder.Append(Escape(line[separator].ToString()));
                AppendValue(builder, line[(separator + 1)..]);
            }
            else
            {
                builder.Append(Escape(line));
            }
            builder.Append('\n');
        }
        if (builder.Length > 0 && builder[^1] == '\n')
        {
            builder.Length--;
        }
        return builder.ToString();
    }

    private static void AppendValue(StringBuilder builder, string value)
    {
        int i = 0;
        int n = value.Length;
        while (i < n)
        {
            char c = value[i];
            if ((c == '#' || c == ';') && i > 0 && char.IsWhiteSpace(value[i - 1]))
            {
                builder.Append("<span class=\"tok-c\">").Append(Escape(value[i..]));
                builder.Append("</span>");
                return;
            }
            if (c == '"' || c == '\'')
            {
                int end = ScanString(value, i, c, false);
                builder.Append("<span class=\"tok-s\">").Append(Escape(value[i..end]));
                builder.Append("</span>");
                i = end;
            }
            else if (char.IsDigit(c) && (i == 0 || !char.IsLetter(value[i - 1]))
                && (i == 0 || value[i - 1] != '-'))
            {
                int start = i;
                while (i < n && (char.IsDigit(value[i]) || value[i] == '.' || value[i] == 'x'
                    || value[i] == 'X'))
                {
                    i++;
                }
                builder.Append("<span class=\"tok-n\">").Append(Escape(value[start..i]));
                builder.Append("</span>");
            }
            else
            {
                builder.Append(Escape(c.ToString()));
                i++;
            }
        }
    }

    private static int ScanString(string code, int start, char quote, bool sqlStyle)
    {
        int i = start + 1;
        int n = code.Length;
        while (i < n)
        {
            char c = code[i];
            if (c == '\\' && !sqlStyle && i + 1 < n)
            {
                i += 2;
                continue;
            }
            if (c == quote)
            {
                if (sqlStyle && i + 1 < n && code[i + 1] == quote)
                {
                    i += 2;
                    continue;
                }
                return i + 1;
            }
            if (c == '\n')
            {
                return i;
            }
            i++;
        }
        return n;
    }

    private static int LineEnd(string code, int start)
    {
        int end = code.IndexOf('\n', start);
        return end < 0 ? code.Length : end;
    }

    private static bool StartsWith(string code, int index, string value, bool ignoreCase)
    {
        if (index + value.Length > code.Length)
        {
            return false;
        }
        return ignoreCase
            ? string.Compare(code, index, value, 0, value.Length, StringComparison.OrdinalIgnoreCase) == 0
            : string.CompareOrdinal(code, index, value, 0, value.Length) == 0;
    }

    private static string Escape(string text)
    {
        return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
            .Replace("\"", "&quot;");
    }
}
