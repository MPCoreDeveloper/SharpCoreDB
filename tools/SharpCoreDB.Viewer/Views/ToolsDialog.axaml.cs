// <copyright file="ToolsDialog.axaml.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using Avalonia;
using Avalonia.Controls;
using Avalonia.Diagnostics;
using Avalonia.Markup.Xaml;
using SharpCoreDB.Viewer.ViewModels;

namespace SharpCoreDB.Viewer.Views;

/// <summary>
/// Tools dialog window
/// </summary>
public partial class ToolsDialog : Window
{
    public ToolsDialog()
    {
        InitializeComponent();
#if DEBUG
        this.AttachDevTools();
#endif
        
        // Set StorageProvider when DataContext is set
        DataContextChanged += (sender, e) =>
        {
            if (DataContext is ToolsViewModel viewModel)
            {
                viewModel.StorageProvider = StorageProvider;
            }
        };
    }
    
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
