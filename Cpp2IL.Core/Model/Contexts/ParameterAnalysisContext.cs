using System;
using System.Reflection;
using System.Text;
using Cpp2IL.Core.Utils;
using LibCpp2IL;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Metadata;
using StableNameDotNet.Providers;

namespace Cpp2IL.Core.Model.Contexts;

public class ParameterAnalysisContext : HasCustomAttributesAndName, IParameterInfoProvider
{
    /// <summary>
    /// The backing il2cpp definition of this parameter. Can be null if the parameter is injected. 
    /// </summary>
    public Il2CppParameterDefinition? Definition { get; }

    /// <summary>
    /// The index of this parameter in the declaring method's parameter list.
    /// </summary>
    public int ParamIndex { get; }

    /// <summary>
    /// The method which this parameter belongs to. Cannot be null.
    /// </summary>
    public MethodAnalysisContext DeclaringMethod { get; }

    /// <summary>
    /// The il2cpp type of the parameter. Cannot be null.
    /// </summary>
    public virtual Il2CppType ParameterType => Definition?.RawType ?? throw new("Subclasses of ParameterAnalysisContext must provide a parameter type");

    protected override int CustomAttributeIndex => Definition?.customAttributeIndex ?? throw new("Subclasses of ParameterAnalysisContext must provide a customAttributeIndex");
    public override AssemblyAnalysisContext CustomAttributeAssembly => DeclaringMethod.DeclaringType!.DeclaringAssembly;
    public override string DefaultName => Definition?.Name ?? throw new("Subclasses of ParameterAnalysisContext must provide a default name");

    /// <summary>
    /// The human-readable display value of the parameter type.
    /// </summary>
    public string ReadableTypeName => LibCpp2ILUtils.GetTypeReflectionData(ParameterType).ToString();

    /// <summary>
    /// The human-readable display value of the parameter, as it would appear in a c# method declaration.
    /// </summary>
    public string HumanReadableSignature => $"{ReadableTypeName} {Name}";

    /// <summary>
    /// The ParameterAttributes of this parameter.
    /// </summary>
    public ParameterAttributes ParameterAttributes => (ParameterAttributes) ParameterType.Attrs;

    /// <summary>
    /// True if this parameter is passed by reference.
    /// </summary>
    public virtual bool IsRef => ParameterType.Byref == 1 || ParameterAttributes.HasFlag(ParameterAttributes.Out);

    /// <summary>
    /// The default value data for this parameter. Null if, and only if, the parameter has no default value. If it has a default value of literally null, this will be non-null and have a data index of -1.
    /// </summary>
    public Il2CppParameterDefaultValue? DefaultValue { get; }

    public TypeAnalysisContext ParameterTypeContext => DeclaringMethod.DeclaringType!.DeclaringAssembly.ResolveIl2CppType(ParameterType);

    public ParameterAnalysisContext(Il2CppParameterDefinition? definition, int paramIndex, MethodAnalysisContext declaringMethod) : base(definition?.token ?? 0, declaringMethod.AppContext)
    {
        Definition = definition;
        ParamIndex = paramIndex;
        DeclaringMethod = declaringMethod;

        if (Definition != null)
        {
            InitCustomAttributeData();

            if (ParameterAttributes.HasFlag(ParameterAttributes.HasDefault))
            {
                DefaultValue = AppContext.Metadata.GetParameterDefaultValueFromIndex(declaringMethod.Definition!.parameterStart + paramIndex)!;
            }
        }
    }

    public override string ToString()
    {
        var result = new StringBuilder();

        if (ParameterAttributes.HasFlag(ParameterAttributes.Out))
            result.Append("out ");
        else if (ParameterAttributes.HasFlag(ParameterAttributes.In))
            result.Append("in ");
        else if(ParameterType.Byref == 1)
            result.Append("ref ");

        result.Append(ParameterTypeContext.Name).Append(" ");

        if (string.IsNullOrEmpty(ParameterName))
            result.Append("unnamed_param_").Append(ParamIndex);
        else
            result.Append(ParameterName);

        if (ParameterAttributes.HasFlag(ParameterAttributes.HasDefault))
            result.Append(" = ").Append(DefaultValue?.ContainedDefaultValue ?? "null");

        return result.ToString();
    }

    #region StableNameDotNet implementation

    public ITypeInfoProvider ParameterTypeInfoProvider 
        => Definition!.RawType!.ThisOrElementIsGenericParam()
        ? new GenericParameterTypeInfoProviderWrapper(Definition.RawType!.GetGenericParamName())
        : TypeAnalysisContext.GetSndnProviderForType(AppContext, Definition.RawType);

    public string ParameterName => Name;

    #endregion
}