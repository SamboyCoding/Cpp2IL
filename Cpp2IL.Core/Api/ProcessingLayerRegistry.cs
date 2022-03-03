using System;
using System.Collections.Generic;

namespace Cpp2IL.Core.Api;

public static class ProcessingLayerRegistry
{
    private static readonly Dictionary<string, Cpp2IlProcessingLayer> _processingLayersById = new();

    public static void Register<T>() where T : Cpp2IlProcessingLayer, new()
    {
        var layer = new T();
        _processingLayersById.Add(layer.Id, layer);
    }
    
    public static Cpp2IlProcessingLayer GetById(string id) 
        => _processingLayersById.TryGetValue(id, out var ret) ? ret : throw new ArgumentException($"No processing layer with id {id} registered");
}