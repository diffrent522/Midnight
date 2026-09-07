using System.Windows;
using System.Windows.Controls;
using Midnight.ViewModels;

namespace Midnight.Views
{
    public partial class SettingsPage : Page
    {
        public SettingsPage()
        {
            InitializeComponent();
            if (Application.Current.MainWindow.DataContext is MainViewModel vm)
                DataContext = vm.SettingsVM;
        }
    }
}

