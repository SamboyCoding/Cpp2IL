using System;
using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Cpp2IL.Gui.ViewModels;

namespace Cpp2IL.Gui
{
    public class ViewLocator : IDataTemplate
    {
        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "All the viewmodel types are hard referenced")]
        [UnconditionalSuppressMessage("Trimming", "IL2057", Justification = "All the viewmodel types are hard referenced")]
        public IControl Build(object? data)
        {
            var name = data!.GetType().FullName!.Replace("ViewModel", "View");
            var type = Type.GetType(name);

            if (type != null)
            {
                return (Control) Activator.CreateInstance(type)!;
            }
            else
            {
                return new TextBlock {Text = "Not Found: " + name};
            }
        }

        public bool Match(object? data)
        {
            return data is ViewModelBase;
        }
    }
}
