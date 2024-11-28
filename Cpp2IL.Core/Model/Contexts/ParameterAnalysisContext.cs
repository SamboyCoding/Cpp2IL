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
    public string ReadableTypeName => ParameterTypeContext.FullName;

    /// <summary>
    /// The human-readable display value of the parameter, as it would appear in a c# method declaration.
    /// </summary>
    public string HumanReadableSignature => $"{ReadableTypeName} {Name}";

    /// <summary>
    /// The ParameterAttributes of this parameter.
    /// </summary>
    public virtual ParameterAttributes ParameterAttributes => (ParameterAttributes)ParameterType.Attrs;

    /// <summary>
    /// True if this parameter is passed by reference.
    /// </summary>
    public virtual bool IsRef => ParameterType.Byref == 1 || ParameterAttributes.HasFlag(ParameterAttributes.Out);

    /// <summary>
    /// The default value data for this parameter. Null if, and only if, the parameter has no default value. If it has a default value of literally null, this will be non-null and have a data index of -1.
    /// </summary>
    public Il2CppParameterDefaultValue? DefaultValue { get; }

    public virtual TypeAnalysisContext ParameterTypeContext => DeclaringMethod.DeclaringType!.DeclaringAssembly.ResolveIl2CppType(ParameterType);

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
        if (!AppContext.HasFinishedInitializing)
            //Cannot safely access ParameterTypeContext.Name if we haven't finished initializing as it may require doing system type lookups etc.
            return $"Parameter {Name} (ordinal {ParamIndex}) of {DeclaringMethod}";

        var result = new StringBuilder();

        if (ParameterAttributes.HasFlag(ParameterAttributes.Out))
            result.Append("out ");
        else if (ParameterAttributes.HasFlag(ParameterAttributes.In))
            result.Append("in ");
        else if (ParameterType.Byref == 1)
            result.Append("ref ");

        result.Append(CsFileUtils.GetTypeName(ParameterTypeContext.Name)).Append(' ');

        if (string.IsNullOrEmpty(ParameterName))
            result.Append("unnamed_param_").Append(ParamIndex);
        else
            result.Append(ParameterName);

        if (ParameterAttributes.HasFlag(ParameterAttributes.HasDefault))
        {
            var defaultValue = DefaultValue!.ContainedDefaultValue;
            if (defaultValue is string stringDefaultValue)
                defaultValue = $"\"{stringDefaultValue}\"";
            else if (defaultValue is bool boolDefaultValue)
                defaultValue = boolDefaultValue.ToString().ToLowerInvariant();
            else if (defaultValue is null)
                defaultValue = "null";

            result.Append(" = ").Append(defaultValue);
        }

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
