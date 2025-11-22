using System;

namespace Cpp2IL.Core.Model.Contexts;

public class InjectedAssemblyAnalysisContext(
    string name,
    ApplicationAnalysisContext appContext,
    Version? version = null,
    uint hashAlgorithm = 0,
    uint flags = 0,
    string? culture = null,
    byte[]? publicKeyToken = null,
    byte[]? publicKey = null)
    : AssemblyAnalysisContext(null, appContext)
{
    public override string DefaultName => name;
    public override Version DefaultVersion => version ?? base.DefaultVersion;
    public override uint DefaultHashAlgorithm => hashAlgorithm;
    public override uint DefaultFlags => flags;
    public override string? DefaultCulture => culture;
    public override byte[]? DefaultPublicKeyToken => publicKeyToken;
    public override byte[]? DefaultPublicKey => publicKey;
}
