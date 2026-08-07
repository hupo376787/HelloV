using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using HelloV.Models;
using HelloV.Services;
using HelloV.ViewModels;

namespace HelloV.Views;

public partial class MainView : UserControl
{
    private static readonly long BlurPreviewIntervalTicks = Math.Max(1, Stopwatch.Frequency / 24);
    private MainViewModel? _attachedViewModel;
    private long _lastBlurPreviewTimestamp;
    private WindowState _desktopWindowStateBeforeFullscreen = WindowState.Normal;

    public MainView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        RestoreInterruptMode(vm);
        AttachPreview(vm);
        await vm.InitializeAsync();
    }

    private async void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        var vm = _attachedViewModel ?? DataContext as MainViewModel;
        DetachPreview();

        if (vm is not null)
            await vm.DisposeAsync();
    }

    private void AttachPreview(MainViewModel vm)
    {
        if (ReferenceEquals(_attachedViewModel, vm))
            return;

        DetachPreview();
        _attachedViewModel = vm;
        vm.PreviewFrameReady += OnPreviewFrameReady;
        vm.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void DetachPreview()
    {
        if (_attachedViewModel is null)
            return;

        _attachedViewModel.PreviewFrameReady -= OnPreviewFrameReady;
        _attachedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _attachedViewModel = null;
    }

    private static void RestoreInterruptMode(MainViewModel vm)
    {
        try
        {
            var saved = AppServices.LoadInterruptMode?.Invoke();
            if (saved.HasValue)
                vm.IsInterruptModeEnabled = saved.Value;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"读取打断模式设置失败：{ex.Message}");
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.IsInterruptModeEnabled) ||
            sender is not MainViewModel vm)
        {
            return;
        }

        try
        {
            AppServices.SaveInterruptMode?.Invoke(vm.IsInterruptModeEnabled);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"保存打断模式设置失败：{ex.Message}");
        }
    }

    private async void OnCameraSurfacePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount != 2
            || DataContext is not MainViewModel vm
            || (!vm.IsDesktop && !vm.IsBrowser))
        {
            return;
        }

        e.Handled = true;

        if (vm.IsBrowser)
        {
            var toggleFullscreen = AppServices.ToggleFullscreenAsync;
            if (toggleFullscreen is null)
                return;

            try
            {
                await toggleFullscreen();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HelloV browser fullscreen toggle failed: {ex}");
            }

            return;
        }

        if (TopLevel.GetTopLevel(this) is not Window window)
            return;

        if (window.WindowState == WindowState.FullScreen)
        {
            window.WindowState = _desktopWindowStateBeforeFullscreen;
            return;
        }

        _desktopWindowStateBeforeFullscreen = window.WindowState == WindowState.Minimized
            ? WindowState.Normal
            : window.WindowState;
        window.WindowState = WindowState.FullScreen;
    }

    private void OnSettingsBackdropPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || !vm.IsSettingsPanelVisible)
            return;

        vm.CloseSettingsPanelCommand.Execute(null);
        e.Handled = true;
    }

    private void OnPreviewFrameReady(VideoFrame frame)
    {
        // The ViewModel raises this synchronously on Avalonia's UI thread. CameraPreview copies
        // frame.Pixels directly into its persistent WriteableBitmap before the frame is returned
        // to the ArrayPool.
        CameraPreview.Present(frame);

        if (DataContext is MainViewModel
            {
                IsDesktop: true,
                IsSettingsPanelVisible: true
            })
        {
            // The glass background only needs a visually smooth sample rate. Keeping it near
            // 24 FPS avoids running a second 1080p bitmap upload and Gaussian blur for every
            // camera frame while the primary preview can continue at its full frame rate.
            var now = Stopwatch.GetTimestamp();
            if (now - _lastBlurPreviewTimestamp >= BlurPreviewIntervalTicks)
            {
                _lastBlurPreviewTimestamp = now;
                SettingsBlurPreview.ViewportWidth = Math.Max(Bounds.Width, SettingsBlurPreview.Bounds.Width);
                SettingsBlurPreview.Present(frame);
            }
        }
    }
}
