namespace Cpp2IL.Core.Model.Contexts;

public class HasToken : HasApplicationContext
{
    public uint Token;
    
    public HasToken(uint token, ApplicationAnalysisContext appContext) : base(appContext)
    {
        Token = token;
    }
}