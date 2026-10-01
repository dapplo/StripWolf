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
    private bool _isLoadNextPageCheckPending;
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
    public ICommand? SelectSeriesCommand => (DataContext as KomgaViewModel)?.SelectSeriesCommand;
    public ICommand? DownloadSeriesCommand => (DataContext as KomgaViewModel)?.DownloadSeriesCommand;
    public ICommand? ShowBookInfoCommand => (DataContext as KomgaViewModel)?.ShowBookInfoCommand;
    public ICommand? DownloadBookCommand => (DataContext as KomgaViewModel)?.DownloadBookCommand;
    public ICommand? ShowReadListPickerCommand => (DataContext as KomgaViewModel)?.ShowReadListPickerCommand;
    public ICommand? MarkBookAsReadCommand => (DataContext as KomgaViewModel)?.MarkBookAsReadCommand;
    public ICommand? SelectLibraryCommand => (DataContext as KomgaViewModel)?.SelectLibraryCommand;
    public ICommand? SelectReadListCommand => (DataContext as KomgaViewModel)?.SelectReadListCommand;
    public ICommand? AddBookToReadListCommand => (DataContext as KomgaViewModel)?.AddBookToReadListCommand;
    public ICommand? ViewSelectedBookSeriesCommand => (DataContext as KomgaViewModel)?.ViewSelectedBookSeriesCommand;
    public ICommand? OpenBookOnlineCommand => (DataContext as KomgaViewModel)?.OpenBookOnlineCommand;
    public ICommand? OpenBookSeriesOnlineCommand => (DataContext as KomgaViewModel)?.OpenBookSeriesOnlineCommand;
    
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
        
        // Initialize when the view is displayed, the view model makes sure this only happens once
        if (DataContext is KomgaViewModel viewModel)
        {
            await EnsureInitializedAsync(viewModel);
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // The view is kept alive between tab switches (see MainContentHost) and only hidden,
        // so becoming visible again is the moment a tab switch to Komga happened.
        // Not async: this override runs for every property change of the view, EnsureInitializedAsync catches all exceptions.
        if (change.Property == IsVisibleProperty && IsVisible && _subscribedViewModel is not null)
        {
            _ = EnsureInitializedAsync(_subscribedViewModel);
            // Page checks are skipped while hidden, catch up on a list that doesn't fill the view yet
            ScheduleLoadNextPageCheck();
        }
    }

    private static async Task EnsureInitializedAsync(KomgaViewModel viewModel)
    {
        // Called from async void handlers: exceptions must not escape
        try
        {
            await viewModel.EnsureInitializedAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to initialize Komga: {ex.Message}");
        }
    }

    /// <summary>
    /// A card of one of the virtualized grids was realized: the view model loads its thumbnail
    /// </summary>
    private void OnCardElementPrepared(object? sender, ItemsRepeaterElementPreparedEventArgs e)
    {
        _subscribedViewModel?.OnThumbnailElementPrepared(e.Element.DataContext);
    }

    /// <summary>
    /// A card of one of the virtualized grids is recycled: the view model releases its thumbnail
    /// </summary>
    private void OnCardElementClearing(object? sender, ItemsRepeaterElementClearingEventArgs e)
    {
        _subscribedViewModel?.OnThumbnailElementClearing(e.Element.DataContext);
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

        // A page check is skipped while the view model is busy, not connected yet or searching. The scroll viewer
        // doesn't change after that (the extent change of the added page was already handled while busy), so without
        // checking again the list stopped after the first page until the user scrolled or resized the window.
        switch (e.PropertyName)
        {
            case nameof(KomgaViewModel.IsBusy):
            case nameof(KomgaViewModel.IsLoadingMore):
            case nameof(KomgaViewModel.IsConnected):
            case nameof(KomgaViewModel.IsSearching):
            case nameof(KomgaViewModel.HasMoreSeries):
            case nameof(KomgaViewModel.HasMoreBooks):
            case nameof(KomgaViewModel.SelectedLibrary):
            case nameof(KomgaViewModel.SelectedSeries):
            case nameof(KomgaViewModel.SelectedReadList):
                ScheduleLoadNextPageCheck();
                break;
        }
    }

    private void OnConnectedScrollViewerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (_connectedScrollViewer is null || _subscribedViewModel is null)
        {
            return;
        }

        // Extent and viewport changes matter too: after a page was added (extent) or when the loaded items
        // don't fill the view yet (there is nothing to scroll), the next page must be requested as well.
        if (e.Property == ScrollViewer.OffsetProperty ||
            e.Property == ScrollViewer.ExtentProperty ||
            e.Property == ScrollViewer.ViewportProperty)
        {
            ScheduleLoadNextPageCheck();
        }

        if (e.Property != ScrollViewer.OffsetProperty)
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

    /// <summary>
    /// Coalesces the many scroll events of a fling into one check after the layout pass
    /// </summary>
    private void ScheduleLoadNextPageCheck()
    {
        if (_isLoadNextPageCheckPending)
        {
            return;
        }

        _isLoadNextPageCheckPending = true;
        Dispatcher.UIThread.Post(CheckLoadNextPage, DispatcherPriority.Background);
    }

    /// <summary>
    /// Requests the next page when the user scrolled to within about a screen of the end of the loaded items
    /// </summary>
    private void CheckLoadNextPage()
    {
        _isLoadNextPageCheckPending = false;
        if (_connectedScrollViewer is null || _subscribedViewModel is null || !IsEffectivelyVisible)
        {
            return;
        }

        var viewportHeight = _connectedScrollViewer.Viewport.Height;
        if (viewportHeight <= 0)
        {
            return;
        }

        var remaining = _connectedScrollViewer.Extent.Height - (_connectedScrollViewer.Offset.Y + viewportHeight);
        if (remaining <= viewportHeight)
        {
            _subscribedViewModel.LoadNextPageIfNeeded();
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
        OnPropertyChanged(nameof(SelectSeriesCommand));
        OnPropertyChanged(nameof(DownloadSeriesCommand));
        OnPropertyChanged(nameof(ShowBookInfoCommand));
        OnPropertyChanged(nameof(DownloadBookCommand));
        OnPropertyChanged(nameof(ShowReadListPickerCommand));
        OnPropertyChanged(nameof(MarkBookAsReadCommand));
        OnPropertyChanged(nameof(SelectLibraryCommand));
        OnPropertyChanged(nameof(SelectReadListCommand));
        OnPropertyChanged(nameof(AddBookToReadListCommand));
        OnPropertyChanged(nameof(ViewSelectedBookSeriesCommand));
        OnPropertyChanged(nameof(OpenBookOnlineCommand));
        OnPropertyChanged(nameof(OpenBookSeriesOnlineCommand));
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
