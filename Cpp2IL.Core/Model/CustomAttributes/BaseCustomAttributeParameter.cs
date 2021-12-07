using System.IO;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Model.CustomAttributes;

public abstract class BaseCustomAttributeParameter
{
    public abstract void ReadFromV29Blob(BinaryReader reader, ApplicationAnalysisContext context);
}