using System.Windows;
using System.Windows.Controls;
using Midnight.ViewModels;

namespace Midnight.Views
{
    public partial class AddFriendPage : Page
    {
        public AddFriendPage()
        {
            InitializeComponent();
            if (Application.Current.MainWindow.DataContext is MainViewModel vm)
                DataContext = vm.AddFriendVM;
        }
    }
}

