using System.Windows.Controls;
using Midnight.ViewModels;

namespace Midnight.Views
{
    public partial class FastFlagsPage : Page
    {
        public FastFlagsPage()
        {
            InitializeComponent();

            Loaded += (_, _) =>
            {
                if (System.Windows.Application.Current.MainWindow?.DataContext is MainViewModel vm)
                    DataContext = vm.FastFlagsVM;
            };
        }

        // Persist inline DataGrid cell edits back to settings as soon as the user
        // commits a change (clicks away or presses Enter).
        private void FlagsGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction == DataGridEditAction.Commit && DataContext is FastFlagsViewModel vm)
            {
                // Dispatch to let the binding update the model first, then save.
                Dispatcher.InvokeAsync(() => vm.NotifyFlagsEdited(),
                    System.Windows.Threading.DispatcherPriority.Background);
            }
        }
    }
}

