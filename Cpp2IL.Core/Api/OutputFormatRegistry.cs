using System;
using System.Collections.Generic;
using System.Linq;

namespace Cpp2IL.Core.Api;

public static class OutputFormatRegistry
{
    private static readonly Dictionary<string, Cpp2IlOutputFormat> _formatsById = new();
    
    public static IReadOnlyList<Cpp2IlOutputFormat> AllOutputFormats => _formatsById.Values.ToList();

    public static void Register<T>() where T : Cpp2IlOutputFormat, new()
    {
        var format = new T();
        _formatsById.Add(format.OutputFormatId, format);
    }
    
    public static Cpp2IlOutputFormat GetFormat(string formatId) 
        => _formatsById.TryGetValue(formatId, out var format) ? format : throw new ArgumentException($"No output format registered with id '{formatId}'");
}