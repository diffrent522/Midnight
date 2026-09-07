using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls; // For SymbolIcon
using Midnight.ViewModels;
using Midnight.Models;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Midnight.Views
{
    public partial class AccountsPage : Page
    {
        public AccountsPage()
        {
            InitializeComponent();
            if (Application.Current.MainWindow.DataContext is MainViewModel vm)
            {
                DataContext = vm;

                // Keep the selected-count label in sync whenever the collection or any
                // account's IsSelected property changes.
                vm.Accounts.CollectionChanged += OnAccountsChanged;
                foreach (var acc in vm.Accounts)
                    acc.PropertyChanged += OnAccountPropertyChanged;

                UpdateSelectedCount(vm);
            }
        }

        // ── Selection counter ────────────────────────────────────────────────

        private void OnAccountsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
                foreach (RobloxAccount acc in e.NewItems)
                    acc.PropertyChanged += OnAccountPropertyChanged;

            if (e.OldItems != null)
                foreach (RobloxAccount acc in e.OldItems)
                    acc.PropertyChanged -= OnAccountPropertyChanged;

            if (DataContext is MainViewModel vm)
                UpdateSelectedCount(vm);
        }

        private void OnAccountPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(RobloxAccount.IsSelected) && DataContext is MainViewModel vm)
                UpdateSelectedCount(vm);
        }

        private void UpdateSelectedCount(MainViewModel vm)
        {
            int selected = vm.Accounts.Count(a => a.IsSelected);
            int total    = vm.Accounts.Count;
            TxtSelectedCount.Text = selected > 0
                ? $"{selected}/{total} selected"
                : total > 0 ? $"{total} accounts" : string.Empty;
        }

        // ── Select All / Deselect All ────────────────────────────────────────

        private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
                foreach (var acc in vm.Accounts)
                    acc.IsSelected = true;
        }

        private void BtnDeselectAll_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
                foreach (var acc in vm.Accounts)
                    acc.IsSelected = false;
        }

        // ── Launch Selected ──────────────────────────────────────────────────

        private async void BtnLaunchSelected_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                if (!vm.Accounts.Any(a => a.IsSelected))
                {
                    var mw = Application.Current.MainWindow as MainWindow;
                    if (mw != null)
                        await mw.ShowAlertAsync("No Accounts Selected", "Check at least one account before launching.");
                    return;
                }

                // Reuse the same LaunchOptions dialog the toolbar button uses
                vm.LaunchSelected();
            }
        }

        private async void BtnAddAccount_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel)
            {
                var mw = Application.Current.MainWindow as MainWindow;
                if (mw != null)
                    await mw.ShowAddAccountDialogAsync();
            }
        }

        private void BtnEditAccount_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is RobloxAccount account)
            {
                 if (DataContext is MainViewModel vm)
                {
                    vm.NavigateAccountDetails(account);
                }
            }
        }

        private void BtnDeleteAccount_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is RobloxAccount account)
            {
                if (DataContext is MainViewModel vm)
                {
                    vm.RemoveAccountCommand.Execute(account);
                }
            }
        }
        private void MnuMoveToGroup_SubmenuOpened(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.MenuItem menuItem && DataContext is MainViewModel vm)
            {
                menuItem.Items.Clear();

                // "New Group..." Option
                var newGroupItem = new System.Windows.Controls.MenuItem { Header = "New Group...", Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.Add24 } };
                newGroupItem.Click += BtnCreateNewGroup_Click;
                newGroupItem.Tag = menuItem.DataContext; // Pass the RobloxAccount
                menuItem.Items.Add(newGroupItem);

                menuItem.Items.Add(new System.Windows.Controls.Separator());

                // Existing Groups
                var groups = vm.Accounts.Select(a => a.Group).Distinct().OrderBy(g => g).ToList();
                
                // Always ensure "Default" is there or handled by list
                if (!groups.Contains("Default")) groups.Insert(0, "Default");

                foreach (var group in groups)
                {
                    var groupItem = new System.Windows.Controls.MenuItem { Header = group };
                    groupItem.Click += BtnMoveToExistingGroup_Click;
                    groupItem.Tag = menuItem.DataContext; // Pass the RobloxAccount
                    menuItem.Items.Add(groupItem);
                }
            }
        }

        private async void BtnCreateNewGroup_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.MenuItem item && item.Tag is RobloxAccount account && DataContext is MainViewModel vm)
            {
                var mw = Application.Current.MainWindow as MainWindow;
                if (mw != null)
                {
                    string? newGroup = await mw.ShowInputDialogAsync("New Group", "Enter a name for the new group:");
                    if (!string.IsNullOrWhiteSpace(newGroup))
                    {
                        await vm.MoveAccountToGroupAsync(account, newGroup.Trim());
                    }
                }
            }
        }

        private async void BtnMoveToExistingGroup_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.MenuItem item && item.Tag is RobloxAccount account && DataContext is MainViewModel vm)
            {
                if (item.Header is string groupName)
                {
                    await vm.MoveAccountToGroupAsync(account, groupName);
                }
            }
        }

        private async void BtnDeleteGroup_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is string groupName && DataContext is MainViewModel vm)
            {
                if (groupName == "Default") return; // Safety check

                // Direct delete for now, assuming user intent.
                await vm.DeleteGroupAsync(groupName);
            }
        }
    }
}

