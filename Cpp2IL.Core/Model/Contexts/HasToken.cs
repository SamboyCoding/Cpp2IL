using LibCpp2IL;

namespace Cpp2IL.Core.Model.Contexts;

public class HasToken(uint token, ApplicationAnalysisContext appContext)
    : HasApplicationContext(appContext), IIl2CppTokenProvider
{
    public uint Token { get; } = token;
}