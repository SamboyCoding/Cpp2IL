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
    public override Version Version => version ?? base.Version;
    public override uint HashAlgorithm => hashAlgorithm;
    public override uint Flags => flags;
    public override string? Culture => culture;
    public override byte[]? PublicKeyToken => publicKeyToken;
    public override byte[]? PublicKey => publicKey;
}
