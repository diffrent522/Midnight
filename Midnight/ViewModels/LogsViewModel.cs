using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Midnight.Services;
using System.Windows;

namespace Midnight.ViewModels
{
    public partial class LogsViewModel : ObservableObject
    {
        private readonly MainViewModel _mainViewModel;

        public LogsViewModel(MainViewModel main)
        {
            _mainViewModel = main;
        }

        [RelayCommand]
        public void NavigateBack()
        {
            _mainViewModel?.NavigateAccounts();
        }
    }
}

