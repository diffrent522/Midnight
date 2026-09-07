using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Midnight.Models;
using Midnight.Services;

namespace Midnight.ViewModels
{
    public partial class TrayViewModel : ObservableObject
    {
        private readonly MainViewModel _mainViewModel;
        private readonly SecurityService _securityService;

        public ObservableCollection<RobloxAccount> Accounts => _mainViewModel.Accounts;

        [ObservableProperty]
        private string _statusText = "Ready";

        public TrayViewModel(MainViewModel mainViewModel, SecurityService securityService)
        {
            _mainViewModel = mainViewModel;
            _securityService = securityService;
        }

        [RelayCommand]
        public void OpenUI()
        {
            var window = Application.Current.MainWindow;
            if (window == null) return;

            window.Show();
            window.WindowState = WindowState.Normal;
            window.Activate();
        }

        [RelayCommand]
        public void ExitApp()
        {
            App.DisposeTray();

            if (Application.Current.MainWindow is Views.MainWindow mainWindow)
            {
                mainWindow.ForceClose();
            }

            Application.Current.Shutdown();
        }

        [RelayCommand]
        public async System.Threading.Tasks.Task AddAccount()
        {
            OpenUI();

            await System.Threading.Tasks.Task.Delay(200);

            if (Application.Current.MainWindow is Views.MainWindow mainWindow)
            {
                await mainWindow.ShowAddAccountDialogAsync();
            }
        }

        [RelayCommand]
        public void LaunchAccount(RobloxAccount? account)
        {
            if (account == null) return;

            OpenUI();

            foreach (var acc in Accounts)
                acc.IsSelected = false;

            account.IsSelected = true;
            _mainViewModel.LaunchSelectedCommand.Execute(null);
        }

        public string GetSelectedAccountsSummary()
        {
            int count = Accounts.Count(a => a.IsSelected);
            return count == 0 ? "No accounts selected" : $"{count} account{(count == 1 ? "" : "s")} selected";
        }
    }
}