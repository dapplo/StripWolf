// StripWolf - an open source comic book reader
// Copyright (C) 2026 Dapplo - Robin Krom
//
// For more information see: https://github.com/dapplo/StripWolf
// The StripWolf project is hosted on GitHub https://github.com/dapplo/StripWolf
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using StripWolf.Core.ViewModels;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using StripWolf.Core.Models.Komga;

namespace StripWolf.Core.Views;

public partial class KomgaView : UserControl, INotifyPropertyChanged
{
    private KomgaViewModel? _subscribedViewModel;
    private readonly ScrollViewer? _connectedScrollViewer;
    private bool _hadSelectedSeries;
    private double _savedSeriesScrollOffsetY;
    private event PropertyChangedEventHandler? ProxyPropertyChanged;

    public KomgaView()
    {
        InitializeComponent();
        _connectedScrollViewer = this.FindControl<ScrollViewer>("ConnectedScrollViewer");
        if (_connectedScrollViewer is not null)
        {
            _connectedScrollViewer.PropertyChanged += OnConnectedScrollViewerPropertyChanged;
        }
    }

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => ProxyPropertyChanged += value;
        remove => ProxyPropertyChanged -= value;
    }

    public ICommand? GoBackToSeriesCommand => (DataContext as KomgaViewModel)?.GoBackToSeriesCommand;
    
    public KomgaSeries? SelectedSeries => (DataContext as KomgaViewModel)?.SelectedSeries;
    
    public KomgaLibrary? SelectedLibrary => (DataContext as KomgaViewModel)?.SelectedLibrary;
    
    public KomgaReadList? SelectedReadList => (DataContext as KomgaViewModel)?.SelectedReadList;

    public KomgaSeriesDisplay? SeriesPendingDownloadSelection => (DataContext as KomgaViewModel)?.SeriesPendingDownloadSelection;

    public KomgaBookDisplay? BookPendingReadListSelection => (DataContext as KomgaViewModel)?.BookPendingReadListSelection;

    protected override async void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _subscribedViewModel = DataContext as KomgaViewModel;
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged += OnViewModelPropertyChanged;
            _hadSelectedSeries = _subscribedViewModel.SelectedSeries is not null;
        }
        else
        {
            _hadSelectedSeries = false;
        }

        RaiseProxyPropertyChanges();
        
        // Initialize when the view is displayed
        if (DataContext is KomgaViewModel viewModel)
        {
            try
            {
                await viewModel.InitializeCommand.ExecuteAsync(null);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to initialize Komga: {ex.Message}");
            }
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(KomgaViewModel.SelectedSeries):
                HandleSelectedSeriesChanged();
                OnPropertyChanged(nameof(SelectedSeries));
                break;
            case nameof(KomgaViewModel.SelectedLibrary):
                OnPropertyChanged(nameof(SelectedLibrary));
                break;
            case nameof(KomgaViewModel.SelectedReadList):
                OnPropertyChanged(nameof(SelectedReadList));
                break;
            case nameof(KomgaViewModel.SeriesPendingDownloadSelection):
                OnPropertyChanged(nameof(SeriesPendingDownloadSelection));
                break;
            case nameof(KomgaViewModel.BookPendingReadListSelection):
                OnPropertyChanged(nameof(BookPendingReadListSelection));
                break;
        }
    }

    private void OnConnectedScrollViewerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != ScrollViewer.OffsetProperty ||
            _connectedScrollViewer is null ||
            _subscribedViewModel is null)
        {
            return;
        }

        if (_subscribedViewModel.SelectedLibrary is not null &&
            _subscribedViewModel.SelectedSeries is null &&
            _subscribedViewModel.SelectedReadList is null)
        {
            _savedSeriesScrollOffsetY = _connectedScrollViewer.Offset.Y;
        }
    }

    private void HandleSelectedSeriesChanged()
    {
        var hasSelectedSeries = _subscribedViewModel?.SelectedSeries is not null;
        if (!hasSelectedSeries &&
            _hadSelectedSeries &&
            _subscribedViewModel?.SelectedLibrary is not null &&
            _subscribedViewModel.SelectedReadList is null)
        {
            RestoreSeriesScrollOffset();
        }

        _hadSelectedSeries = hasSelectedSeries;
    }

    private void RestoreSeriesScrollOffset()
    {
        if (_connectedScrollViewer is null)
        {
            return;
        }

        void ApplyOffset()
        {
            if (_connectedScrollViewer is null)
            {
                return;
            }

            _connectedScrollViewer.Offset = new Vector(_connectedScrollViewer.Offset.X, _savedSeriesScrollOffsetY);
        }

        Dispatcher.UIThread.Post(ApplyOffset, DispatcherPriority.Background);
        Dispatcher.UIThread.Post(ApplyOffset, DispatcherPriority.Loaded);
    }

    private void RaiseProxyPropertyChanges()
    {
        OnPropertyChanged(nameof(GoBackToSeriesCommand));
        OnPropertyChanged(nameof(SelectedSeries));
        OnPropertyChanged(nameof(SelectedLibrary));
        OnPropertyChanged(nameof(SelectedReadList));
        OnPropertyChanged(nameof(SeriesPendingDownloadSelection));
        OnPropertyChanged(nameof(BookPendingReadListSelection));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        ProxyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
