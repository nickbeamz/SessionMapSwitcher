﻿using SessionMapSwitcher.Classes;
using SessionMapSwitcher.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;

namespace SessionMapSwitcher.ViewModels
{
    public class ComputerImportViewModel : ViewModelBase
    {
        private readonly List<string> FilesToExclude = new List<string>() { "DefaultEngine.ini", "DefaultGame.ini" };
        private readonly List<string> FoldersToExclude = new List<string>() { "Data" };


        private bool _isZipFileImport;
        private bool _isImporting;
        private string _userMessage;
        private string _pathInput;

        public string PathToTempUnzipFolder
        {
            get
            {
                return $"{SessionPath.ToSessionGame}\\Temp_Unzipped";
            }
        }

        public bool IsZipFileImport
        {
            get { return _isZipFileImport; }
            set
            {
                _isZipFileImport = value;
                NotifyPropertyChanged();
                NotifyPropertyChanged(nameof(PathLabel));
            }
        }

        public bool IsImporting
        {
            get { return _isImporting; }
            set
            {
                _isImporting = value;
                NotifyPropertyChanged();
                NotifyPropertyChanged(nameof(IsNotImporting));
                NotifyPropertyChanged(nameof(ButtonVisibility));
            }
        }

        public bool IsNotImporting
        {
            get
            {
                return !IsImporting;
            }
        }

        public string PathLabel
        {
            get
            {
                if (IsZipFileImport)
                {
                    return "Path To Zip File:";
                }
                else
                {
                    return "Folder Path To Map Files:";
                }
            }
        }

        public string UserMessage
        {
            get { return _userMessage; }
            set
            {
                _userMessage = value;
                NotifyPropertyChanged();
            }
        }

        public string PathInput
        {
            get { return _pathInput; }
            set
            {
                _pathInput = value;
                NotifyPropertyChanged();
            }
        }

        public string PathToFileOrFolder
        {
            get
            {
                if (String.IsNullOrEmpty(PathInput))
                {
                    return "";
                }

                if (PathInput.EndsWith("\\"))
                {
                    PathInput = PathInput.TrimEnd('\\');
                }

                return PathInput;
            }
        }

        public Visibility ButtonVisibility
        {
            get
            {
                if (IsImporting)
                {
                    return Visibility.Hidden;
                }
                return Visibility.Visible;
            }
        }

        public ComputerImportViewModel()
        {
            this.UserMessage = "";
            this.IsZipFileImport = false;
            this.PathInput = "";
        }

        internal void BrowseForFolderOrFile()
        {
            if (IsZipFileImport)
            {
                using (OpenFileDialog fileBrowserDialog = new OpenFileDialog())
                {
                    fileBrowserDialog.Filter = "Zip files (*.zip)|*.zip|All files (*.*)|*.*";
                    fileBrowserDialog.Title = "Select .zip File Containing Session Map";
                    DialogResult result = fileBrowserDialog.ShowDialog();
                    if (result == DialogResult.OK)
                    {
                        PathInput = fileBrowserDialog.FileName;
                    }
                }
            }
            else
            {
                using (FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog())
                {
                    folderBrowserDialog.ShowNewFolderButton = false;
                    folderBrowserDialog.Description = "Select Folder Containing Session Map Files";
                    DialogResult result = folderBrowserDialog.ShowDialog();
                    if (result == DialogResult.OK)
                    {
                        PathInput = folderBrowserDialog.SelectedPath;
                    }
                }
            }
        }

        internal void BeginImportMapAsync(bool isReimport = false)
        {
            UserMessage = "Importing Map ...";

            if (IsZipFileImport)
            {
                UserMessage += " Unzipping and copying can take a couple of minutes depending on the .zip file size.";
            }
            else
            {
                UserMessage += " Copying can take a couple of minutes depending on the amount of files to copy.";
                PathInput = EnsurePathToMapFilesIsCorrect(PathToFileOrFolder);
            }

            IsImporting = true;

            Task<BoolWithMessage> task = ImportMapAsync(isReimport);

            task.ContinueWith((antecedent) =>
            {
                IsImporting = false;

                if (antecedent.Result.Result)
                {
                    UserMessage = "Map Imported!";
                }
            });
        }

        /// <summary>
        /// This will check if the folder path to a map has the 'Content' folder and returns path to the maps 'Content folder if so
        /// </summary>
        /// <returns>
        /// "pathToMapFiles\Content" if Content folder exists; otherwise original pathToMapFiles string is returned
        /// </returns>
        private string EnsurePathToMapFilesIsCorrect(string pathToMapFiles)
        {
            if (Directory.Exists($"{pathToMapFiles}\\Content"))
            {
                return $"{pathToMapFiles}\\Content";
            }

            return pathToMapFiles;
        }

        internal void ImportMapAsyncAndContinueWith(bool isReimport, Action<Task<BoolWithMessage>> continuationTask)
        {
            Task<BoolWithMessage> task = ImportMapAsync(isReimport);
            task.ContinueWith(continuationTask);
        }

        internal Task<BoolWithMessage> ImportMapAsync(bool isReimport = false)
        {
            Task<BoolWithMessage> task = Task.Factory.StartNew(() =>
            {
                string sourceFolderToCopy;

                if (IsZipFileImport)
                {
                    if (File.Exists(PathToFileOrFolder) == false)
                    {
                        System.Windows.MessageBox.Show("File does not exist", "Error!", MessageBoxButton.OK, MessageBoxImage.Error);
                        return new BoolWithMessage(false, $"{PathToFileOrFolder} does not exist.");
                    }

                    // extract files first before copying
                    Directory.CreateDirectory(PathToTempUnzipFolder);
                    BoolWithMessage didExtract = FileUtils.ExtractZipFile(PathToFileOrFolder, PathToTempUnzipFolder);

                    if (didExtract.Result == false)
                    {
                        UserMessage = $"Failed to extract .zip file: {didExtract.Message}";
                        return new BoolWithMessage(false, $"Failed to extract zip: {didExtract.Message}.");
                    }

                    sourceFolderToCopy = EnsurePathToMapFilesIsCorrect(PathToTempUnzipFolder);
                }
                else
                {
                    if (Directory.Exists(PathToFileOrFolder) == false)
                    {
                        System.Windows.MessageBox.Show("Folder does not exist", "Error!", MessageBoxButton.OK, MessageBoxImage.Error);
                        return new BoolWithMessage(false, $"{PathToFileOrFolder} does not exist.");
                    }

                    sourceFolderToCopy = PathToFileOrFolder;
                }

                FileUtils.CopyDirectoryRecursively(sourceFolderToCopy, SessionPath.ToContent, FilesToExclude, FoldersToExclude);

                if (IsZipFileImport && Directory.Exists(PathToTempUnzipFolder))
                {
                    // remove unzipped temp files
                    Directory.Delete(PathToTempUnzipFolder, true);
                }
                else if (isReimport == false)
                {
                    // make .meta file to tag where the imported map came from to support the 'Re-import' feature
                    string mapName = MetaDataManager.GetMapFileNameFromFolder(sourceFolderToCopy);
                    BoolWithMessage result = MetaDataManager.TrackMapLocation(mapName, sourceFolderToCopy);
                }

                return new BoolWithMessage(true);
            });

            return task;
        }
    }
}