using LibCpp2IL;

namespace Cpp2IL.Core.Model.Contexts;

public class HasToken : HasApplicationContext, IIl2CppTokenProvider
{
    public uint Token { get; }
    
    public HasToken(uint token, ApplicationAnalysisContext appContext) : base(appContext)
    {
        Token = token;
    }
}