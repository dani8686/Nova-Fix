using System.Windows;
using NOVA.FIX.Core.Services;

namespace NOVA.FIX.App;

public partial class MainWindow : Window
{
    public MainWindow(ServiceContainer container)
    {
        InitializeComponent();
        DataContext = new ViewModels.ShellViewModel(container);
    }
}
