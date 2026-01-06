// <copyright file="ToolsViewModel.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SharpCoreDB.Viewer.Services;
using System;
using System.IO;
using System.Threading.Tasks;

namespace SharpCoreDB.Viewer.ViewModels;

/// <summary>
/// ViewModel for Tools dialog
/// </summary>
public partial class ToolsViewModel : ViewModelBase
{
    private readonly LocalizationService _localization = LocalizationService.Instance;
    
    [ObservableProperty]
    private string _sourcePath = string.Empty;
    
    [ObservableProperty]
    private string _targetPath = string.Empty;
    
    [ObservableProperty]
    private DatabaseFormatType _sourceFormat = DatabaseFormatType.Directory;
    
    [ObservableProperty]
    private DatabaseFormatType _targetFormat = DatabaseFormatType.SingleFile;
    
    [ObservableProperty]
    private string _password = string.Empty;
    
    [ObservableProperty]
    private bool _isPasswordVisible;
    
    [ObservableProperty]
    private bool _isConverting;
    
    [ObservableProperty]
    private double _conversionProgress;
    
    [ObservableProperty]
    private string _statusMessage = string.Empty;
    
    [ObservableProperty]
    private string _errorMessage = string.Empty;
    
    [ObservableProperty]
    private bool _conversionComplete;
    
    public IStorageProvider? StorageProvider { get; set; }
    
    public ToolsViewModel()
    {
    }
    
    [RelayCommand]
    private void TogglePasswordVisibility()
    {
        IsPasswordVisible = !IsPasswordVisible;
    }
    
    [RelayCommand]
    private async Task BrowseSource()
    {
        if (StorageProvider == null) return;
        
        if (SourceFormat == DatabaseFormatType.SingleFile)
        {
            // Browse for .scdb file
            var options = new FilePickerOpenOptions
            {
                Title = _localization["BrowseSourceDatabase"],
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("SharpCoreDB Single-File Database")
                    {
                        Patterns = new[] { "*.scdb" }
                    }
                }
            };
            
            var result = await StorageProvider.OpenFilePickerAsync(options);
            if (result.Count > 0)
            {
                SourcePath = result[0].Path.LocalPath;
            }
        }
        else
        {
            // Browse for directory
            var options = new FolderPickerOpenOptions
            {
                Title = _localization["BrowseSourceDatabase"],
                AllowMultiple = false
            };
            
            var result = await StorageProvider.OpenFolderPickerAsync(options);
            if (result.Count > 0)
            {
                SourcePath = result[0].Path.LocalPath;
            }
        }
    }
    
    [RelayCommand]
    private async Task BrowseTarget()
    {
        if (StorageProvider == null) return;
        
        if (TargetFormat == DatabaseFormatType.SingleFile)
        {
            // Save as .scdb file
            var options = new FilePickerSaveOptions
            {
                Title = _localization["BrowseTargetDatabase"],
                DefaultExtension = ".scdb",
                SuggestedFileName = "ConvertedDatabase.scdb",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("SharpCoreDB Single-File Database")
                    {
                        Patterns = new[] { "*.scdb" }
                    }
                }
            };
            
            var result = await StorageProvider.SaveFilePickerAsync(options);
            if (result != null)
            {
                TargetPath = result.Path.LocalPath;
            }
        }
        else
        {
            // Browse for directory
            var options = new FolderPickerOpenOptions
            {
                Title = _localization["BrowseTargetDatabase"],
                AllowMultiple = false
            };
            
            var result = await StorageProvider.OpenFolderPickerAsync(options);
            if (result.Count > 0)
            {
                var basePath = result[0].Path.LocalPath;
                var suggestedName = "ConvertedDatabase";
                var targetPath = Path.Combine(basePath, suggestedName);
                
                var counter = 1;
                while (Directory.Exists(targetPath))
                {
                    targetPath = Path.Combine(basePath, $"{suggestedName}{counter}");
                    counter++;
                }
                
                TargetPath = targetPath;
            }
        }
    }
    
    [RelayCommand]
    private void SwapFormats()
    {
        (SourceFormat, TargetFormat) = (TargetFormat, SourceFormat);
        (SourcePath, TargetPath) = (TargetPath, SourcePath);
    }
    
    [RelayCommand]
    private async Task Convert()
    {
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        ConversionProgress = 0;
        ConversionComplete = false;
        
        // Validate inputs
        if (string.IsNullOrWhiteSpace(SourcePath))
        {
            ErrorMessage = _localization["ErrorSourcePathRequired"];
            return;
        }
        
        if (string.IsNullOrWhiteSpace(TargetPath))
        {
            ErrorMessage = _localization["ErrorTargetPathRequired"];
            return;
        }
        
        if (string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = _localization["ErrorPasswordRequired"];
            return;
        }
        
        // Validate source exists
        bool sourceExists = SourceFormat == DatabaseFormatType.SingleFile
            ? File.Exists(SourcePath)
            : Directory.Exists(SourcePath);
            
        if (!sourceExists)
        {
            ErrorMessage = _localization["ErrorSourceNotFound"];
            return;
        }
        
        // Validate formats are different
        if (SourceFormat == TargetFormat)
        {
            ErrorMessage = _localization["ErrorSameFormat"];
            return;
        }
        
        IsConverting = true;
        
        try
        {
            var converter = new FormatConverter();
            
            // Subscribe to progress updates
            converter.ProgressChanged += (sender, progress) =>
            {
                ConversionProgress = progress;
                StatusMessage = _localization.Format("ConvertingProgress", progress.ToString("F1"));
            };
            
            StatusMessage = _localization["ConvertingDatabase"];
            
            // Perform conversion
            await converter.ConvertAsync(
                SourcePath,
                TargetPath,
                Password,
                SourceFormat,
                TargetFormat);
            
            ConversionProgress = 100;
            ConversionComplete = true;
            StatusMessage = _localization["ConversionComplete"];
        }
        catch (Exception ex)
        {
            ErrorMessage = _localization.Format("ErrorConversionFailed", ex.Message);
            ConversionProgress = 0;
        }
        finally
        {
            IsConverting = false;
        }
    }
}
