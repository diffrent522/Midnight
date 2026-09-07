using System.Windows;
using System.Windows.Controls;
using Midnight.ViewModels;

namespace Midnight.Views
{
    public partial class AboutPage : Page
    {
        public AboutPage()
        {
            InitializeComponent();
            if (Application.Current.MainWindow.DataContext is MainViewModel vm)
                DataContext = vm.AboutVM;
        }
    }
}

