namespace Decompiler.Transforms;

/// <summary>
/// Transform applied to methods.
/// </summary>
public interface ITransform
{
    /// <summary>
    /// Applies the transform to a method.
    /// </summary>
    /// <param name="method">The method.</param>
    void Apply(Method method);
}
