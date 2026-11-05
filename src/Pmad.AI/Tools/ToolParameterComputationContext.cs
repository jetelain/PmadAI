using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using Pmad.AI.Healing;

namespace Pmad.AI.Tools;

/// <summary>
/// Context passed to <see cref="ParameterComputer"/> delegates providing unwrapped argument values,
/// automatic parameter alias resolution, null-coalescing fallback, and typed accessors.
/// </summary>
public sealed class ToolParameterComputationContext : IDictionary<string, object?>
{
    private readonly Dictionary<string, object?> _arguments;
    private readonly ToolMapping? _mapping;

    public ToolParameterComputationContext(IDictionary<string, object?> arguments, ToolMapping? mapping = null)
    {
        _arguments = new Dictionary<string, object?>(arguments.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in arguments)
        {
            _arguments[k] = UnwrapClrValue(v);
        }
        _mapping = mapping;
    }

    public object? this[string key]
    {
        get
        {
            if (TryGetValue(key, out var val))
            {
                return val;
            }
            throw new KeyNotFoundException($"The given key '{key}' was not present in the parameter computation context.");
        }
        set => _arguments[key] = UnwrapClrValue(value);
    }

    public ICollection<string> Keys => _arguments.Keys;
    public ICollection<object?> Values => _arguments.Values;
    public int Count => _arguments.Count;
    public bool IsReadOnly => false;

    public void Add(string key, object? value) => _arguments.Add(key, UnwrapClrValue(value));
    public void Add(KeyValuePair<string, object?> item) => _arguments.Add(item.Key, UnwrapClrValue(item.Value));
    public void Clear() => _arguments.Clear();
    public bool Contains(KeyValuePair<string, object?> item) => ((ICollection<KeyValuePair<string, object?>>)_arguments).Contains(item);
    public bool ContainsKey(string key) => HasExplicitKey(key);
    public void CopyTo(KeyValuePair<string, object?>[] array, int arrayIndex) => ((ICollection<KeyValuePair<string, object?>>)_arguments).CopyTo(array, arrayIndex);
    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => _arguments.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _arguments.GetEnumerator();
    public bool Remove(string key) => _arguments.Remove(key);
    public bool Remove(KeyValuePair<string, object?> item) => ((ICollection<KeyValuePair<string, object?>>)_arguments).Remove(item);

    public bool HasExplicitKey(string parameterName)
    {
        if (string.IsNullOrWhiteSpace(parameterName))
        {
            return false;
        }

        if (_arguments.ContainsKey(parameterName))
        {
            return true;
        }

        if (_mapping != null)
        {
            foreach (var (alias, target) in _mapping.ParameterAliases)
            {
                if (string.Equals(target, parameterName, StringComparison.OrdinalIgnoreCase) &&
                    _arguments.ContainsKey(alias))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public bool TryGetValue(string key, [MaybeNullWhen(false)] out object? value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            value = null;
            return false;
        }

        // 1. Direct match with non-null value
        if (_arguments.TryGetValue(key, out var direct) && !LenientToolDispatcher.IsNullOrUndefined(direct))
        {
            value = direct;
            return true;
        }

        // 2. Alias resolution via ToolMapping.ParameterAliases (null-coalescing)
        if (_mapping != null)
        {
            foreach (var (alias, target) in _mapping.ParameterAliases)
            {
                if (string.Equals(target, key, StringComparison.OrdinalIgnoreCase) &&
                    _arguments.TryGetValue(alias, out var aliasVal) &&
                    !LenientToolDispatcher.IsNullOrUndefined(aliasVal))
                {
                    value = aliasVal;
                    return true;
                }
            }
        }

        // If present with null value
        if (_arguments.TryGetValue(key, out var nullDirect))
        {
            value = nullDirect;
            return true;
        }

        value = null;
        return false;
    }

    public int GetInt(string parameterName, int defaultValue = 0)
    {
        if (TryGetValue(parameterName, out var val) && TryParseInt(val, out var parsed))
        {
            return parsed;
        }
        return defaultValue;
    }

    public long GetLong(string parameterName, long defaultValue = 0)
    {
        if (TryGetValue(parameterName, out var val) && TryParseLong(val, out var parsed))
        {
            return parsed;
        }
        return defaultValue;
    }

    public double GetDouble(string parameterName, double defaultValue = 0.0)
    {
        if (TryGetValue(parameterName, out var val) && TryParseDouble(val, out var parsed))
        {
            return parsed;
        }
        return defaultValue;
    }

    public string? GetString(string parameterName, string? defaultValue = null)
    {
        if (TryGetValue(parameterName, out var val) && val != null)
        {
            return val.ToString();
        }
        return defaultValue;
    }

    public bool GetBool(string parameterName, bool defaultValue = false)
    {
        if (TryGetValue(parameterName, out var val) && TryParseBool(val, out var parsed))
        {
            return parsed;
        }
        return defaultValue;
    }

    private static object? UnwrapClrValue(object? val)
    {
        if (val is JsonElement je)
        {
            return ToolCallHealingTransform.UnwrapJsonElement(je);
        }
        return val;
    }

    public static bool TryParseInt(object? value, out int result)
    {
        result = 0;
        if (value == null) return false;
        if (value is int i) { result = i; return true; }
        if (value is long l)
        {
            if (l < int.MinValue || l > int.MaxValue) return false;
            result = (int)l;
            return true;
        }
        if (value is short s) { result = s; return true; }
        if (value is byte b) { result = b; return true; }
        if (value is double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d) || d < int.MinValue || d > int.MaxValue) return false;
            result = (int)d;
            return true;
        }
        if (value is float f)
        {
            if (float.IsNaN(f) || float.IsInfinity(f) || f < int.MinValue || f > int.MaxValue) return false;
            result = (int)f;
            return true;
        }
        if (value is decimal dec)
        {
            if (dec < int.MinValue || dec > int.MaxValue) return false;
            result = (int)dec;
            return true;
        }
        if (value is bool boolVal) { result = boolVal ? 1 : 0; return true; }

        var str = value.ToString()?.Trim();
        if (string.IsNullOrEmpty(str)) return false;
        if (str.StartsWith('#')) str = str[1..].Trim();
        return int.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
    }

    public static bool TryParseLong(object? value, out long result)
    {
        result = 0;
        if (value == null) return false;
        if (value is long l) { result = l; return true; }
        if (value is int i) { result = i; return true; }
        if (value is short s) { result = s; return true; }
        if (value is byte b) { result = b; return true; }
        if (value is double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d) || d < long.MinValue || d > long.MaxValue) return false;
            result = (long)d;
            return true;
        }
        if (value is float f)
        {
            if (float.IsNaN(f) || float.IsInfinity(f) || f < long.MinValue || f > long.MaxValue) return false;
            result = (long)f;
            return true;
        }
        if (value is decimal dec)
        {
            if (dec < long.MinValue || dec > long.MaxValue) return false;
            result = (long)dec;
            return true;
        }
        if (value is bool boolVal) { result = boolVal ? 1 : 0; return true; }

        var str = value.ToString()?.Trim();
        if (string.IsNullOrEmpty(str)) return false;
        if (str.StartsWith('#')) str = str[1..].Trim();
        return long.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
    }

    public static bool TryParseDouble(object? value, out double result)
    {
        result = 0;
        if (value == null) return false;
        if (value is double d) { result = d; return true; }
        if (value is float f) { result = f; return true; }
        if (value is int i) { result = i; return true; }
        if (value is long l) { result = l; return true; }
        if (value is decimal dec) { result = (double)dec; return true; }

        var str = value.ToString()?.Trim();
        if (string.IsNullOrEmpty(str)) return false;
        return double.TryParse(str, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out result);
    }

    public static bool TryParseBool(object? value, out bool result)
    {
        result = false;
        if (value == null) return false;
        if (value is bool b) { result = b; return true; }
        if (value is int i) { result = i != 0; return true; }
        if (value is long l) { result = l != 0; return true; }

        var str = value.ToString()?.Trim();
        if (string.IsNullOrEmpty(str)) return false;
        if (string.Equals(str, "true", StringComparison.OrdinalIgnoreCase) || str == "1") { result = true; return true; }
        if (string.Equals(str, "false", StringComparison.OrdinalIgnoreCase) || str == "0") { result = false; return true; }
        return false;
    }
}

/// <summary>
/// Extension methods for retrieving typed values from parameter computation argument dictionaries.
/// </summary>
public static class ToolParameterComputationExtensions
{
    public static int GetInt(this IDictionary<string, object?> args, string parameterName, int defaultValue = 0)
    {
        if (args is ToolParameterComputationContext ctx)
        {
            return ctx.GetInt(parameterName, defaultValue);
        }
        if (args.TryGetValue(parameterName, out var val) && ToolParameterComputationContext.TryParseInt(val, out var parsed))
        {
            return parsed;
        }
        return defaultValue;
    }

    public static long GetLong(this IDictionary<string, object?> args, string parameterName, long defaultValue = 0)
    {
        if (args is ToolParameterComputationContext ctx)
        {
            return ctx.GetLong(parameterName, defaultValue);
        }
        if (args.TryGetValue(parameterName, out var val) && ToolParameterComputationContext.TryParseLong(val, out var parsed))
        {
            return parsed;
        }
        return defaultValue;
    }

    public static double GetDouble(this IDictionary<string, object?> args, string parameterName, double defaultValue = 0.0)
    {
        if (args is ToolParameterComputationContext ctx)
        {
            return ctx.GetDouble(parameterName, defaultValue);
        }
        if (args.TryGetValue(parameterName, out var val) && ToolParameterComputationContext.TryParseDouble(val, out var parsed))
        {
            return parsed;
        }
        return defaultValue;
    }

    public static string? GetString(this IDictionary<string, object?> args, string parameterName, string? defaultValue = null)
    {
        if (args is ToolParameterComputationContext ctx)
        {
            return ctx.GetString(parameterName, defaultValue);
        }
        if (args.TryGetValue(parameterName, out var val) && val != null)
        {
            return val.ToString();
        }
        return defaultValue;
    }

    public static bool GetBool(this IDictionary<string, object?> args, string parameterName, bool defaultValue = false)
    {
        if (args is ToolParameterComputationContext ctx)
        {
            return ctx.GetBool(parameterName, defaultValue);
        }
        if (args.TryGetValue(parameterName, out var val) && ToolParameterComputationContext.TryParseBool(val, out var parsed))
        {
            return parsed;
        }
        return defaultValue;
    }

    public static bool HasExplicitKey(this IDictionary<string, object?> args, string parameterName)
    {
        if (args is ToolParameterComputationContext ctx)
        {
            return ctx.HasExplicitKey(parameterName);
        }
        return args.ContainsKey(parameterName);
    }
}

