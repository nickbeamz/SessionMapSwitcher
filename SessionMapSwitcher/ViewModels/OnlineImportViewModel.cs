﻿using SessionMapSwitcher.Classes;
using SessionMapSwitcher.Utils;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;

namespace SessionMapSwitcher.ViewModels
{
    public class OnlineImportViewModel : ViewModelBase
    {
        private const string GitHubDocUrl = "https://raw.githubusercontent.com/rodriada000/SessionMapSwitcher/url_updates/docs/mapDownloadUrls.txt";

        private string _headerMessage;
        private bool _isImportingMap = false;
        private ObservableCollection<DownloadableMap> _downloadableMaps;
        private object collectionLock = new object();

        public delegate void ImportComplete(bool wasSuccessful);

        public event ImportComplete MapImported;

        public bool IsImportingMap
        {
            get => _isImportingMap;

            set
            {
                _isImportingMap = value;
                NotifyPropertyChanged();
                NotifyPropertyChanged(nameof(IsNotImportingMap));
                NotifyPropertyChanged(nameof(ImportButtonText));
            }
        }

        public bool IsNotImportingMap
        {
            get
            {
                return !IsImportingMap;
            }
        }

        private string SessionPath { get; set; }

        public string PathToSessionContent
        {
            get
            {
                return $"{SessionPath}\\SessionGame\\Content";
            }
        }

        public string PathToMapDownloads
        {
            get
            {
                return $"{SessionPath}\\SessionGame\\MapDownloads";
            }
        }

        public string HeaderMessage
        {
            get { return _headerMessage; }
            set
            {
                _headerMessage = value;
                NotifyPropertyChanged();
            }
        }

        public string ImportButtonText
        {
            get
            {
                if (IsImportingMap)
                {
                    return "Cancel Import";
                }
                return "Import Selected Map";
            }
        }

        public ObservableCollection<DownloadableMap> DownloadableMaps
        {
            get
            {
                if (_downloadableMaps == null)
                {
                    _downloadableMaps = new ObservableCollection<DownloadableMap>();
                    BindingOperations.EnableCollectionSynchronization(_downloadableMaps, collectionLock);
                }
                return _downloadableMaps;
            }
            set
            {
                _downloadableMaps = value;
                NotifyPropertyChanged();
            }
        }

        public OnlineImportViewModel()
        {
        }

        internal void CancelPendingImport()
        {
            try
            {
                tokenSource.Cancel(true);
            }
            catch // exception is logged else where so can ignore the 'Operation was cancelled' exception
            {
            }

            LoadDownloadableMaps();
        }

        internal void SetSessionPath(string sessionPath)
        {
            SessionPath = sessionPath;
        }

        internal void LoadDownloadableMaps()
        {
            if (DownloadableMaps.Count == 0)
            {
                DownloadableMaps = new ObservableCollection<DownloadableMap>(GetDownloadableMapsFromGit());
                BindingOperations.EnableCollectionSynchronization(DownloadableMaps, collectionLock);

                if (DownloadableMaps.Count == 0)
                {
                    // failed to get maps so return
                    return;
                }
            }

            HeaderMessage = "Maps loaded! Select a map from the list and click 'Import Selected Map' to add to Session.";
        }

        internal List<DownloadableMap> GetDownloadableMapsFromGit()
        {
            List<DownloadableMap> maps = new List<DownloadableMap>();
            string rawTxtFile;

            try
            {
                rawTxtFile = DownloadUtils.GetTxtDocumentFromGitHubRepo(GitHubDocUrl);
            }
            catch (Exception e)
            {
                HeaderMessage = $"Could not get list of maps: {e.Message}";
                return maps;
            }

            foreach (string line in rawTxtFile.Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.Contains("MapTitle | DownloadUrl"))
                {
                    continue; // skip first line which is an example line in the file
                }

                string[] parts = line.Split('|');
                DownloadableMap map = new DownloadableMap()
                {
                    MapName = parts[0].Trim(),
                    DownloadUrl = parts[1].Trim(),
                    PreviewUrl = parts[2].Trim(),
                    ImageUrl = parts[3].Trim()
                };

                maps.Add(map);
            }


            maps = maps.OrderBy(m => m.MapName).ToList();
            return maps;
        }


        private System.Threading.CancellationTokenSource tokenSource = new System.Threading.CancellationTokenSource();
        private System.Threading.CancellationToken cancelToken;

        public void ImportSelectedMapAsync(DownloadableMap selectedMap)
        {
            IsImportingMap = true;

            if (selectedMap.DownloadUrl == "")
            {
                string msgToUser = "Cannot download map directly (due to filesize or requested by map creator).";
                msgToUser += " You must download the map from their link and use 'Import Map > From Computer...' to import the map.\n\n";
                msgToUser += "The browser will now open the link for you to download the map.";
                MessageBox.Show(msgToUser, "Notice!", MessageBoxButton.OK, MessageBoxImage.Information);

                selectedMap.OpenPreviewUrlInBrowser();
                IsImportingMap = false;
                MapImported?.Invoke(false);
                HeaderMessage = "Download the map from the page opened in your web browser. use 'Import Map > From Computer...' to import after downloading.";
                return;
            }

            string directDownloadUrl;

            // check if we have to scrape for the direct download link or it's already provided
            if (selectedMap.IsDownloadUrlDirect() == false)
            {
                directDownloadUrl = DownloadUtils.GetDirectDownloadLinkFromAnonPage(selectedMap.DownloadUrl);

                if (directDownloadUrl == "")
                {
                    HeaderMessage = "Could not get direct download link for selected map.";
                    IsImportingMap = false;
                    MapImported?.Invoke(false);
                    return;
                }
            }
            else
            {
                directDownloadUrl = selectedMap.DownloadUrlWithPrefixRemoved;
            }



            if (Directory.Exists(PathToMapDownloads) == false)
            {
                Directory.CreateDirectory(PathToMapDownloads);
            }

            HeaderMessage = $"Downloading and copying map files. This may take a couple of minutes. Click '{ImportButtonText}' to stop ...";

            bool didDownload = false;
            BoolWithMessage didExtract = null;

            tokenSource = new System.Threading.CancellationTokenSource();
            cancelToken = tokenSource.Token;

            var downloadTask = Task.Factory.StartNew(() =>
            {
                string pathToZip = $"{PathToMapDownloads}\\{selectedMap.ZipFileName}";

                try
                {
                    DownloadUtils.ProgressChanged += DownloadUtils_ProgressChanged;
                    Task task = DownloadUtils.DownloadFileToFolderAsync(directDownloadUrl, pathToZip, cancelToken, noTimeout: true);
                    task.Wait();
                    DownloadUtils.ProgressChanged -= DownloadUtils_ProgressChanged;

                    didDownload = true;
                }
                catch (AggregateException e)
                {
                    HeaderMessage = $"Failed to download map files: {e.InnerExceptions[0].Message}";
                    didDownload = false;
                    MapImported?.Invoke(false);
                    return;
                }
                catch (Exception e)
                {
                    HeaderMessage = $"Failed to download map files: {e.Message}";
                    didDownload = false;
                    MapImported?.Invoke(false);
                    return;
                }

                HeaderMessage = "Extracting downloaded .zip ...";
                didExtract = FileUtils.ExtractZipFile(pathToZip, PathToSessionContent);
            });


            downloadTask.ContinueWith((antecedent) =>
            {
                IsImportingMap = false;

                if (didDownload == false)
                {
                    MapImported?.Invoke(false);
                    return;
                }

                if (didExtract?.Result == false)
                {
                    HeaderMessage = $"Failed to extract map files: {didExtract?.Message}.";
                    MapImported?.Invoke(false);
                    return;
                }

                HeaderMessage = "Map imported! You should now be able to play the downloaded map.";
                MapImported?.Invoke(true);
            });

        }

        private void DownloadUtils_ProgressChanged(long? totalFileSize, long totalBytesDownloaded, double? progressPercentage)
        {
            HeaderMessage = $"Downloading map files: {(double)totalBytesDownloaded / 1000000:0.00} / {(double)totalFileSize / 1000000:0.00} MB | {progressPercentage:0.00}% Complete";
        }
    }

    public class DownloadableMap : ViewModelBase
    {
        private string _mapName;
        public string DownloadUrl;
        public string PreviewUrl;
        public string ImageUrl;

        public string MapName
        {
            get { return _mapName; }
            set
            {
                _mapName = value;
                NotifyPropertyChanged();
            }
        }

        /// <summary>
        /// Name of zip file that will be downloaded for this map with the '.zip' extension
        /// (same as MapName with spaces replaced with '_')
        /// </summary>
        public string ZipFileName
        {
            get
            {
                return $"{MapName.Replace(" ", "_")}.zip";
            }
        }

        internal string DownloadUrlWithPrefixRemoved
        {
            get
            {
                if (IsDownloadUrlDirect())
                {
                    return DownloadUrl.Substring("DIRECT:".Length);
                }
                return DownloadUrl;
            }
        }

        internal void OpenPreviewUrlInBrowser()
        {
            if (String.IsNullOrEmpty(PreviewUrl))
            {
                MessageBox.Show("No preview url for this map.", "Notice!", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }

            ProcessStartInfo info = new ProcessStartInfo()
            {
                FileName = this.PreviewUrl
            };

            Process.Start(info);
        }

        internal void BeginDownloadInBrowser()
        {
            if (String.IsNullOrEmpty(DownloadUrl))
            {
                MessageBox.Show("No direct download url for this map.", "Notice!", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }

            try
            {
                string directDownloadLink = DownloadUtils.GetDirectDownloadLinkFromAnonPage(this.DownloadUrl);
                ProcessStartInfo info = new ProcessStartInfo()
                {
                    FileName = directDownloadLink
                };

                Process.Start(info);
            }
            catch (Exception e)
            {
                System.Windows.MessageBox.Show($"Could not initiate download from browser: {e.Message}", "Notice!", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }

        }

        internal bool IsDownloadUrlDirect()
        {
            return DownloadUrl.StartsWith("DIRECT:");
        }

    }
}
