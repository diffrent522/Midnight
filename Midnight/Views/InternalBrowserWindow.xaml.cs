using System;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace Midnight.Views
{
    public partial class InternalBrowserWindow : Window
    {
        private readonly string _targetUrl;
        private readonly string? _destinationPath;

        public InternalBrowserWindow(string url, string? destinationPath = null)
        {
            InitializeComponent();
            _targetUrl = url;
            _destinationPath = destinationPath;
            Loaded += InternalBrowserWindow_Loaded;
        }

        private async void InternalBrowserWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await Browser.EnsureCoreWebView2Async();
                Browser.CoreWebView2.Navigate(_targetUrl);
                
                Browser.CoreWebView2.DownloadStarting += CoreWebView2_DownloadStarting;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to initialize browser: {ex.Message}", "Browser Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CoreWebView2_DownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
        {
            if (!string.IsNullOrEmpty(_destinationPath) && System.IO.Directory.Exists(_destinationPath))
            {
                string fileName = System.IO.Path.GetFileName(e.ResultFilePath);
                e.ResultFilePath = System.IO.Path.Combine(_destinationPath, fileName);
                e.Handled = true;
            }
            else
            {
                e.Handled = false;
            }

            var downloadOperation = e.DownloadOperation;
            downloadOperation.StateChanged += (s, args) =>
            {
                if (downloadOperation.State == CoreWebView2DownloadState.Completed)
                {
                    if (!string.IsNullOrEmpty(_destinationPath) && System.IO.Directory.Exists(_destinationPath))
                    {
                         Dispatcher.Invoke(async () => 
                         {
                             Services.LogService.Log("Download completed. Extracting...", Services.LogLevel.Info, "Browser");
                             try
                             {
                                 string zipPath = downloadOperation.ResultFilePath;
                                 string fileName = System.IO.Path.GetFileNameWithoutExtension(zipPath);
                                 
                                 string folderName = fileName;
                                 var match = System.Text.RegularExpressions.Regex.Match(fileName, @"version-[a-f0-9]+", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                 if (match.Success)
                                 {
                                     folderName = match.Value.ToLowerInvariant();
                                 }

                                 string extractPath = System.IO.Path.Combine(_destinationPath, folderName);

                                 if (System.IO.Directory.Exists(extractPath))
                                 {
                                     System.IO.Directory.Delete(extractPath, true);
                                 }
                                 
                                 System.IO.Directory.CreateDirectory(extractPath);

                                 await System.Threading.Tasks.Task.Run(() => 
                                 {
                                     System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, extractPath);
                                     System.IO.File.Delete(zipPath);

                                     string directExe = System.IO.Path.Combine(extractPath, "RobloxPlayerBeta.exe");
                                     if (!System.IO.File.Exists(directExe))
                                     {
                                         var subDirs = System.IO.Directory.GetDirectories(extractPath);
                                         if (subDirs.Length == 1 && System.IO.File.Exists(System.IO.Path.Combine(subDirs[0], "RobloxPlayerBeta.exe")))
                                         {
                                             foreach (var file in System.IO.Directory.GetFiles(subDirs[0]))
                                             {
                                                 string dest = System.IO.Path.Combine(extractPath, System.IO.Path.GetFileName(file));
                                                 if (System.IO.File.Exists(dest)) System.IO.File.Delete(dest);
                                                 System.IO.File.Move(file, dest);
                                             }
                                             foreach (var dir in System.IO.Directory.GetDirectories(subDirs[0]))
                                             {
                                                 string dest = System.IO.Path.Combine(extractPath, System.IO.Path.GetFileName(dir));
                                                 if (System.IO.Directory.Exists(dest)) System.IO.Directory.Delete(dest, true);
                                                 System.IO.Directory.Move(dir, dest);
                                             }
                                             System.IO.Directory.Delete(subDirs[0], true);
                                         }
                                     }
                                 });

                                 Services.LogService.Log($"Extracted version to {extractPath}", Services.LogLevel.Success, "Browser");
                                 Close();
                             }
                             catch (Exception ex)
                             {
                                 Services.LogService.Error($"Extraction failed: {ex.Message}", "Browser");
                                 MessageBox.Show($"Extraction failed: {ex.Message}", "Version Error", MessageBoxButton.OK, MessageBoxImage.Error);
                                 Close();
                             }
                         });
                    }
                    else
                    {
                        Dispatcher.Invoke(() => 
                        {
                            Services.LogService.Log("Download completed. Closing internal browser.", Services.LogLevel.Success, "Browser");
                            Close();
                        });
                    }
                }
            };
        }
    }
}

