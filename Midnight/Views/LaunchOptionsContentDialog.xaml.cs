using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace Midnight.Views
{
    public partial class LaunchOptionsContentDialog : ContentDialog
    {
        public string PlaceId { get; private set; } = string.Empty;
        public string JobId { get; private set; } = string.Empty;
        public string AccessCode { get; private set; } = string.Empty;
        public string? SelectedVersion { get; private set; }
        public bool Launched { get; private set; } = false;

        /// <summary>Seconds to wait between launching each account. Default 3.</summary>
        public int LaunchDelaySeconds { get; private set; } = 3;

        public LaunchOptionsContentDialog(ContentDialogHost presenter, System.Collections.Generic.IEnumerable<string>? availableVersions = null) : base(presenter)
        {
            InitializeComponent();
            PopulateVersions(availableVersions);
        }

        private void PopulateVersions(System.Collections.Generic.IEnumerable<string>? versions)
        {
            CmbVersion.Items.Clear();
            CmbVersion.Items.Add("Default (Latest Installed)");
            if (versions != null)
            {
                foreach (var v in versions)
                {
                    CmbVersion.Items.Add(v);
                }
            }
            CmbVersion.SelectedIndex = 0;
        }

        private void SldDelay_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtDelayValue != null)
                TxtDelayValue.Text = $"{(int)e.NewValue} s";
        }

        private void BtnLaunch_Click(object sender, RoutedEventArgs e)
        {
            PlaceId = TxtPlaceId.Text.Trim();

            string currentJobInput = TxtJobId.Text.Trim();
            if (!string.IsNullOrEmpty(AccessCode) && currentJobInput == "(Private Server Access Code)")
            {
                JobId = "";
            }
            else
            {
                JobId = currentJobInput;
                AccessCode = "";
            }

            string selectedVer = CmbVersion.Text.Trim();
            SelectedVersion = (!string.IsNullOrEmpty(selectedVer) && !selectedVer.StartsWith("Default", System.StringComparison.OrdinalIgnoreCase))
                ? selectedVer
                : null;

            LaunchDelaySeconds = (int)SldDelay.Value;

            Launched = true;
            Hide();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Launched = false;
            Hide();
        }

        private void BtnBrowser_Click(object sender, RoutedEventArgs e)
        {
            string currentPlaceId = TxtPlaceId.Text.Trim();

            var browserView = new ServerBrowserView();
            browserView.Initialize(currentPlaceId);
            browserView.ServerSelected += (placeId, jobId, accessCode) =>
            {
                TxtPlaceId.Text = placeId;

                if (!string.IsNullOrEmpty(accessCode))
                {
                    AccessCode = accessCode;
                    TxtJobId.Text = "(Private Server Access Code)";
                }
                else
                {
                    AccessCode = "";
                    TxtJobId.Text = jobId;
                }

                SwitchToConfig();
            };

            browserView.BrowserCanceled += () =>
            {
                SwitchToConfig();
            };

            BrowserContainer.Content = browserView;

            ConfigPanel.Visibility = Visibility.Collapsed;
            BrowserPanel.Visibility = Visibility.Visible;
            this.Width = 950;
            this.Height = 600;
        }

        private void SwitchToConfig()
        {
            BrowserPanel.Visibility = Visibility.Collapsed;
            ConfigPanel.Visibility = Visibility.Visible;
            this.Width = 500;
            this.Height = double.NaN;
        }
    }
}

