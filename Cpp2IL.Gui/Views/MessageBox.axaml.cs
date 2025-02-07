using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Cpp2IL.Gui;

public partial class MessageBox : Window
{
    public MessageBox(string error, string exception)
    {
        InitializeComponent();
        ErrorText.Text = error;
        ExceptionText.Text = exception;
    }

    private void Close(object? sender, RoutedEventArgs e) => Close();
}
