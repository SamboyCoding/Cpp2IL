using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Gui.Images;
using ICSharpCode.TreeView;
using LibCpp2IL;

namespace Cpp2IL.Gui.Models;

public class FileTreeEntry : SharpTreeNode
{
    public HasApplicationContext? Context { get; }
    public string? NamespaceName { get; }

    public FileTreeEntry(ApplicationAnalysisContext context)
    {
        Context = null;
        NamespaceName = "Root";

        var assemblies = context.Assemblies.ToList();
        assemblies.SortByExtractedKey(a => a.Definition.AssemblyName.Name);

        foreach (var assemblyAnalysisContext in assemblies)
            Children.Add(new FileTreeEntry(assemblyAnalysisContext));
    }

    private FileTreeEntry(AssemblyAnalysisContext context) : this((HasApplicationContext)context)
    {
        var uniqueNamespaces = context.Types.Select(t => t.Definition!.Namespace!).Distinct();

        //Top-level namespaces only
        foreach (var ns in uniqueNamespaces.Where(n => !n.Contains('.')))
            Children.Add(new FileTreeEntry(context, ns));
    }

    private FileTreeEntry(HasApplicationContext context)
    {
        Context = context;
        NamespaceName = null;

        if (context is TypeAnalysisContext tac)
            foreach (var methodAnalysisContext in tac.Methods)
                Children.Add(new FileTreeEntry(methodAnalysisContext));
    }

    private FileTreeEntry(AssemblyAnalysisContext parentCtx, string namespaceName)
    {
        Context = null;
        NamespaceName = namespaceName;

        List<TypeAnalysisContext> allTypesInThisNamespaceAndSubNamespaces;
        if (namespaceName != string.Empty)
        {
            //Add sub-namespaces first
            var namespaceDot = $"{namespaceName}.";
            allTypesInThisNamespaceAndSubNamespaces = parentCtx.Types.Where(t => t.Definition!.Namespace! == namespaceName || t.Definition.Namespace!.StartsWith(namespaceDot)).ToList();
            var uniqueSubNamespaces = allTypesInThisNamespaceAndSubNamespaces.Where(t => t.Definition!.Namespace != namespaceName).Select(t => t.Definition!.Namespace![(namespaceName.Length + 1)..]).Distinct().ToList();
            foreach (var subNs in uniqueSubNamespaces)
            {
                if (subNs.Contains('.'))
                {
                    var directChildNs = subNs[..subNs.IndexOf('.')];
                    if (!uniqueSubNamespaces.Contains(directChildNs))
                        Children.Add(new FileTreeEntry(parentCtx, $"{namespaceDot}{directChildNs}"));

                    continue; //Skip deeper-nested namespaces
                }

                Children.Add(new FileTreeEntry(parentCtx, $"{namespaceDot}{subNs}"));
            }
        }
        else
        {
            //Empty namespace cannot have sub namespaces
            allTypesInThisNamespaceAndSubNamespaces = parentCtx.Types.Where(t => t.Definition!.Namespace == namespaceName).ToList();
        }

        allTypesInThisNamespaceAndSubNamespaces.SortByExtractedKey(t => t.Definition!.Name!);

        //Add types in this namespace
        foreach (var type in allTypesInThisNamespaceAndSubNamespaces.Where(t => t.Definition!.Namespace == namespaceName))
            Children.Add(new FileTreeEntry(type));
    }

    public EntryType Type => Context switch
    {
        TypeAnalysisContext => EntryType.Type,
        MethodAnalysisContext => EntryType.Method,
        AssemblyAnalysisContext => EntryType.Assembly,
        null => EntryType.Namespace,
        _ => throw new ArgumentOutOfRangeException()
    };

    public bool ShouldHaveChildren => Type switch
    {
        EntryType.Method => false,
        EntryType.Assembly => true,
        EntryType.Namespace => true,
        EntryType.Type => ((TypeAnalysisContext)Context!).Methods.Count > 0,
        _ => throw new ArgumentOutOfRangeException()
    };

    public string DisplayName => Context switch
    {
        TypeAnalysisContext tac => tac.Definition!.Name!,
        AssemblyAnalysisContext aac => aac.Definition.AssemblyName.Name,
        MethodAnalysisContext mac => $"{mac.Definition!.Name}({string.Join(", ", mac.Parameters.Select(p => p.ReadableTypeName))})",
        null => NamespaceName!.Contains('.') ? NamespaceName[(NamespaceName.LastIndexOf('.') + 1)..] : NamespaceName,
        _ => throw new ArgumentOutOfRangeException()
    };

    public override string ToString() => $"FileTreeEntry: DisplayName = {DisplayName}, Type = {Type}, Context = {Context}";

    public override object Text => DisplayName;

    public override bool IsCheckable => false;

    // public override bool ShowExpander => ShouldHaveChildren;

    public override object Icon => Type switch
    {
        EntryType.Assembly => ImageResources.Assembly,
        EntryType.Namespace => ImageResources.Namespace,
        EntryType.Type => ImageResources.Class,
        EntryType.Method => ImageResources.Method,
        _ => throw new ArgumentOutOfRangeException()
    };

    public override IBrush Foreground => IsSelected ? SystemColors.HighlightBrush : Brushes.Transparent;

    public enum EntryType
    {
        Assembly,
        Namespace,
        Type,
        Method,
    }
}