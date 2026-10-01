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
using Avalonia.Animation;
using Avalonia.Controls;
using StripWolf.Core.ViewModels;
using StripWolf.Core.Views;

namespace StripWolf.Core.Controls;

/// <summary>
/// Shows the view for <see cref="CurrentContent"/> (a main view model) and keeps the views of the main tabs alive.
/// A DataTemplate based ContentControl rebuilds the view on every tab switch: for the big Library and Komga views
/// that is a lot of XAML, and the Komga view re-ran its (network) initialization every time.
/// Cached views are hidden with IsVisible=false, so they are not measured or rendered while another tab is shown.
/// Views that depend on being recreated, or hold a lot of memory while not shown, are not cached (see <see cref="CreateView"/>).
/// This replaces the former ViewLocator, which was never reached because MainView had its own DataTemplates.
/// </summary>
public class MainContentHost : Panel
{
    public static readonly StyledProperty<object?> CurrentContentProperty =
        AvaloniaProperty.Register<MainContentHost, object?>(nameof(CurrentContent));

    private static readonly TimeSpan FadeInDuration = TimeSpan.FromMilliseconds(250);

    // The VMs of the main views are singletons, so one view per VM type is enough
    private readonly Dictionary<Type, Control> _cachedViews = new();
    private Control? _currentView;

    /// <summary>
    /// The view model to show
    /// </summary>
    public object? CurrentContent
    {
        get => GetValue(CurrentContentProperty);
        set => SetValue(CurrentContentProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == CurrentContentProperty)
        {
            ShowContent(change.NewValue);
        }
    }

    private void ShowContent(object? content)
    {
        var previousView = _currentView;
        Control? nextView = null;

        if (content is not null)
        {
            if (!_cachedViews.TryGetValue(content.GetType(), out nextView))
            {
                nextView = CreateView(content, out var keepAlive);
                // Set the DataContext before the view is attached, views initialize in OnDataContextChanged
                nextView.DataContext = content;
                if (keepAlive)
                {
                    _cachedViews[content.GetType()] = nextView;
                }
                Children.Add(nextView);
            }
            else if (!ReferenceEquals(nextView.DataContext, content))
            {
                nextView.DataContext = content;
            }
        }

        if (ReferenceEquals(previousView, nextView))
        {
            return;
        }

        if (previousView is not null)
        {
            if (_cachedViews.ContainsValue(previousView))
            {
                previousView.IsVisible = false;
            }
            else
            {
                Children.Remove(previousView);
                // Like the former TransitioningContentControl: a discarded view loses its DataContext, so it unsubscribes
                // from its (singleton) view model in OnDataContextChanged (e.g. ReaderView) and isn't kept alive by it.
                previousView.DataContext = null;
            }
        }

        _currentView = nextView;
        if (nextView is not null)
        {
            nextView.IsVisible = true;
            FadeIn(nextView);
        }
    }

    /// <summary>
    /// Creates the view for a view model (no reflection, Native AOT compatible)
    /// </summary>
    private static Control CreateView(object content, out bool keepAlive)
    {
        keepAlive = true;
        switch (content)
        {
            case LibraryViewModel:
                return new LibraryView();
            case KomgaViewModel:
                return new KomgaView();
            case ActivityViewModel:
                return new ActivityView();
            case SettingsViewModel:
                // SettingsView reloads the server list when its DataContext is set, it expects a fresh view per visit
                keepAlive = false;
                return new SettingsView();
            case ReaderViewModel:
                // The reader shows big page bitmaps, don't keep that visual tree around after leaving the reader (mobile memory)
                keepAlive = false;
                return new ReaderView();
            default:
                keepAlive = false;
                var name = content.GetType().FullName?.Replace("ViewModel", "View", StringComparison.Ordinal) ?? "Unknown";
                return new TextBlock { Text = "Not Found: " + name };
        }
    }

    /// <summary>
    /// Replacement for the CrossFade of the former TransitioningContentControl: fades the newly shown view in
    /// </summary>
    private static void FadeIn(Control view)
    {
        // Without transitions the opacity is reset immediately, the next change is then animated
        view.Transitions = null;
        view.Opacity = 0;
        view.Transitions = new Transitions
        {
            new DoubleTransition { Property = OpacityProperty, Duration = FadeInDuration }
        };
        view.Opacity = 1;
    }
}
