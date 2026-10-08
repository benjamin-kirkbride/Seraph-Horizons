using System.Text;

namespace SeraphHorizons.Mod.ConfigDefaults.Core;

/// <summary>
/// Reads JSON the way the game's Newtonsoft does: <c>//</c> and <c>/* */</c> comments, unquoted
/// keys, single-quoted strings, trailing commas, <c>NaN</c> and <c>Infinity</c>, and a byte order
/// mark. Every value keeps its span in the text (<see cref="ConfigNode"/>). Throws
/// <see cref="FormatException"/> on text it cannot read.
/// </summary>
public sealed class LenientJson
{
    private readonly string _s;
    private int _i;

    private LenientJson(string s) => _s = s;

    public static ConfigNode Parse(string text)
    {
        var p = new LenientJson(text);
        p.Skip();
        var root = p.Value();
        p.Skip();
        if (p._i < text.Length)
            throw p.Error("text after the end of the value");
        return root;
    }

    private FormatException Error(string what)
    {
        int line = 1;
        for (int k = 0; k < Math.Min(_i, _s.Length); k++)
            if (_s[k] == '\n') line++;
        return new FormatException($"JSON line {line}: {what}");
    }

    private void Skip()
    {
        while (_i < _s.Length)
        {
            char c = _s[_i];
            if (char.IsWhiteSpace(c) || c == '﻿') { _i++; continue; }
            if (c == '/' && _i + 1 < _s.Length && _s[_i + 1] == '/')
            {
                while (_i < _s.Length && _s[_i] != '\n') _i++;
                continue;
            }
            if (c == '/' && _i + 1 < _s.Length && _s[_i + 1] == '*')
            {
                int end = _s.IndexOf("*/", _i + 2, StringComparison.Ordinal);
                if (end < 0) throw Error("unclosed comment");
                _i = end + 2;
                continue;
            }
            break;
        }
    }

    private ConfigNode Value()
    {
        if (_i >= _s.Length) throw Error("a value expected");
        char c = _s[_i];
        int start = _i;
        if (c == '{') return Object();
        if (c == '[') return Array();
        if (c == '"' || c == '\'')
        {
            var text = QuotedString();
            return new ConfigNode { Kind = NodeKind.String, Start = start, End = _i, Raw = _s[start.._i], Text = text };
        }
        var word = Word();
        if (word.Length == 0) throw Error($"unexpected '{c}'");
        var raw = _s[start.._i];
        switch (word)
        {
            case "true": return new ConfigNode { Kind = NodeKind.Bool, Start = start, End = _i, Raw = raw, Bool = true };
            case "false": return new ConfigNode { Kind = NodeKind.Bool, Start = start, End = _i, Raw = raw, Bool = false };
            case "null" or "undefined": return new ConfigNode { Kind = NodeKind.Null, Start = start, End = _i, Raw = raw };
            case "NaN": return new ConfigNode { Kind = NodeKind.Number, Start = start, End = _i, Raw = raw, Number = double.NaN };
            case "Infinity" or "+Infinity": return new ConfigNode { Kind = NodeKind.Number, Start = start, End = _i, Raw = raw, Number = double.PositiveInfinity };
            case "-Infinity": return new ConfigNode { Kind = NodeKind.Number, Start = start, End = _i, Raw = raw, Number = double.NegativeInfinity };
        }
        try
        {
            var n = word.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? Convert.ToInt64(word[2..], 16)
                : ConfigNode.ParseNumber(word);
            return new ConfigNode { Kind = NodeKind.Number, Start = start, End = _i, Raw = raw, Number = n };
        }
        catch (Exception e) when (e is FormatException or OverflowException)
        {
            throw Error($"'{word}' is not a value");
        }
    }

    /// <summary>A bare token: a number, a keyword or an unquoted key.</summary>
    private string Word()
    {
        int start = _i;
        while (_i < _s.Length)
        {
            char c = _s[_i];
            if (char.IsLetterOrDigit(c) || c is '_' or '$' or '-' or '+' or '.') _i++;
            else break;
        }
        return _s[start.._i];
    }

    private string QuotedString()
    {
        char quote = _s[_i++];
        var sb = new StringBuilder();
        while (true)
        {
            if (_i >= _s.Length) throw Error("unclosed string");
            char c = _s[_i++];
            if (c == quote) return sb.ToString();
            if (c != '\\') { sb.Append(c); continue; }
            if (_i >= _s.Length) throw Error("unclosed string");
            char e = _s[_i++];
            switch (e)
            {
                case 'n': sb.Append('\n'); break;
                case 't': sb.Append('\t'); break;
                case 'r': sb.Append('\r'); break;
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case 'u':
                    if (_i + 4 > _s.Length) throw Error("bad \\u escape");
                    sb.Append((char)Convert.ToInt32(_s.Substring(_i, 4), 16));
                    _i += 4;
                    break;
                default: sb.Append(e); break;
            }
        }
    }

    private ConfigNode Object()
    {
        int start = _i++;
        var props = new List<ConfigProperty>();
        while (true)
        {
            Skip();
            if (_i >= _s.Length) throw Error("unclosed object");
            if (_s[_i] == '}') { _i++; break; }
            int keyStart = _i;
            string key = _s[_i] is '"' or '\'' ? QuotedString() : Word();
            if (key.Length == 0 && _i == keyStart) throw Error("a key expected");
            int keyEnd = _i;
            Skip();
            if (_i >= _s.Length || _s[_i] != ':') throw Error($"':' expected after key '{key}'");
            _i++;
            Skip();
            props.Add(new ConfigProperty(key, keyStart, keyEnd, Value()));
            Skip();
            if (_i < _s.Length && _s[_i] == ',') { _i++; continue; }
            if (_i < _s.Length && _s[_i] == '}') { _i++; break; }
            throw Error("',' or '}' expected");
        }
        return new ConfigNode { Kind = NodeKind.Object, Start = start, End = _i, Raw = _s[start.._i], Properties = props };
    }

    private ConfigNode Array()
    {
        int start = _i++;
        var items = new List<ConfigNode>();
        while (true)
        {
            Skip();
            if (_i >= _s.Length) throw Error("unclosed array");
            if (_s[_i] == ']') { _i++; break; }
            items.Add(Value());
            Skip();
            if (_i < _s.Length && _s[_i] == ',') { _i++; continue; }
            if (_i < _s.Length && _s[_i] == ']') { _i++; break; }
            throw Error("',' or ']' expected");
        }
        return new ConfigNode { Kind = NodeKind.Array, Start = start, End = _i, Raw = _s[start.._i], Items = items };
    }
}
