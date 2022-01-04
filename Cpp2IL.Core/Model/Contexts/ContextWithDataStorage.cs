using System.Collections.Generic;

namespace Cpp2IL.Core.Model.Contexts;

/// <summary>
/// Represents a context that can store additional data usable by plugins etc for anything they may need to store associated with a given context.
/// </summary>
public abstract class ContextWithDataStorage
{
    private Dictionary<string, object> _dataStorage = new();
    
    /// <summary>
    /// Store the given data value in this context, associated with the given key.
    /// </summary>
    /// <param name="key">The key to associate the data with. The data can then later be retrieved by calling <see cref="GetExtraData{T}"/> with the same key.</param>
    /// <param name="value">The value to store against the provided key. Must be of a nullable type.</param>
    public void PutExtraData<T>(string key, T value) where T : class => _dataStorage[key] = value;

    /// <summary>
    /// Attempt to retrieve the data that was previously associated with the given key from this context.
    ///
    /// If the data was never stored, or the type mismatches the value of T, null will be returned.
    /// </summary>
    /// <param name="key">The key the data is associated with. Should be the same as when the data was stored using <see cref="PutExtraData{T}"/>.</param>
    /// <returns>The data if it is present and of the expected type, else null.</returns>
    public T? GetExtraData<T>(string key) where T : class => _dataStorage.TryGetValue(key, out var ret) ? ret as T : null;
}