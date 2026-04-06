// <copyright file="DiagnosticsDialog.axaml.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SharpCoreDB.Viewer.ViewModels;

namespace SharpCoreDB.Viewer.Views;

/// <summary>
/// Diagnostics dialog — shows runtime storage stats and exposes safe admin actions.
/// </summary>
public partial class DiagnosticsDialog : Window
{
    public DiagnosticsDialog()
    {
        InitializeComponent();

        DataContextChanged += (_, _) =>
        {
            if (DataContext is DiagnosticsViewModel viewModel)
            {
                viewModel.StorageProvider = StorageProvider;
            }
        };

        Opened += async (_, _) =>
        {
            if (DataContext is DiagnosticsViewModel viewModel && viewModel.Connection is not null)
            {
                await viewModel.RunDiagnosticsCommand.ExecuteAsync(null).ConfigureAwait(true);
            }
        };
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
