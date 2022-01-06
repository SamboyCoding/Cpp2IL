using ReactiveUI;

namespace Cpp2IL.Gui.ViewModels
{
    public class InputUnityVersionViewModel : ViewModelBase
    {
        private string _version = "";

        public string Version
        {
            get => _version;
            set => this.RaiseAndSetIfChanged(ref _version, value);
        }
    }
}