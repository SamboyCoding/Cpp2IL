using System.Reflection;
using System.Text;
using Cpp2IL.Core.Utils;
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
    public int ParameterIndex { get; }

    /// <summary>
    /// The method which this parameter belongs to. Cannot be null.
    /// </summary>
    public MethodAnalysisContext DeclaringMethod { get; }

    protected override int CustomAttributeIndex => Definition?.customAttributeIndex ?? throw new("Subclasses of ParameterAnalysisContext must provide a customAttributeIndex");
    public override AssemblyAnalysisContext CustomAttributeAssembly => DeclaringMethod.DeclaringType!.DeclaringAssembly;
    public override string DefaultName => Definition?.Name ?? throw new("Subclasses of ParameterAnalysisContext must provide a default name");

    /// <summary>
    /// The human-readable display value of the parameter type.
    /// </summary>
    public string ReadableTypeName => ParameterType.FullName;

    /// <summary>
    /// The human-readable display value of the parameter, as it would appear in a c# method declaration.
    /// </summary>
    public string HumanReadableSignature => $"{ReadableTypeName} {Name}";

    public virtual ParameterAttributes DefaultAttributes => (ParameterAttributes?)Definition?.RawType?.Attrs ?? throw new("Subclasses of ParameterAnalysisContext must provide parameter attributes");

    public virtual ParameterAttributes? OverrideAttributes { get; set; }

    /// <summary>
    /// The ParameterAttributes of this parameter.
    /// </summary>
    public ParameterAttributes Attributes
    {
        get => OverrideAttributes ?? DefaultAttributes;
        set => OverrideAttributes = value;
    }

    /// <summary>
    /// True if this parameter is passed by reference.
    /// </summary>
    public bool IsRef => ParameterType is ByRefTypeAnalysisContext || Attributes.HasFlag(ParameterAttributes.Out);

    /// <summary>
    /// The default value data for this parameter. Null if, and only if, the parameter has no default value. If it has a default value of literally null, this will be non-null and have a data index of -1.
    /// </summary>
    public Il2CppParameterDefaultValue? DefaultValue { get; }

    public virtual TypeAnalysisContext DefaultParameterType => AppContext.ResolveIl2CppType(Definition?.RawType) ?? throw new("Subclasses of ParameterAnalysisContext must provide a parameter type");

    public TypeAnalysisContext? OverrideParameterType { get; set; }

    public TypeAnalysisContext ParameterType
    {
        get => OverrideParameterType ?? DefaultParameterType;
        set => OverrideParameterType = value;
    }

    public ParameterAnalysisContext(Il2CppParameterDefinition? definition, int parameterIndex, MethodAnalysisContext declaringMethod) : base(definition?.token ?? 0, declaringMethod.AppContext)
    {
        Definition = definition;
        ParameterIndex = parameterIndex;
        DeclaringMethod = declaringMethod;

        if (Definition != null)
        {
            InitCustomAttributeData();

            if (Attributes.HasFlag(ParameterAttributes.HasDefault))
            {
                DefaultValue = AppContext.Metadata.GetParameterDefaultValueFromIndex(Il2CppVariableWidthIndex<Il2CppParameterDefinition>.MakeTemporaryForFixedWidthUsage(declaringMethod.Definition!.parameterStart.Value + parameterIndex))!;
            }
        }
    }

    public override string ToString()
    {
        if (!AppContext.HasFinishedInitializing)
            //Cannot safely access ParameterTypeContext.Name if we haven't finished initializing as it may require doing system type lookups etc.
            return $"Parameter {Name} (ordinal {ParameterIndex}) of {DeclaringMethod}";

        var result = new StringBuilder();

        if (Attributes.HasFlag(ParameterAttributes.Out))
            result.Append("out ");
        else if (Attributes.HasFlag(ParameterAttributes.In))
            result.Append("in ");
        else if (ParameterType is ByRefTypeAnalysisContext)
            result.Append("ref ");

        result.Append(CsFileUtils.GetTypeName(ParameterType)).Append(' ');

        if (string.IsNullOrEmpty(ParameterName))
            result.Append("unnamed_param_").Append(ParameterIndex);
        else
            result.Append(ParameterName);

        if (Attributes.HasFlag(ParameterAttributes.HasDefault))
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
