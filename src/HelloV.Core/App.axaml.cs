using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using HelloV.Localization;
using HelloV.Services;
using HelloV.ViewModels;
using HelloV.Views;

namespace HelloV;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = CreateMainViewModel()
            };
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime activityLifetime)
        {
            // Avalonia 12 Android can recreate MainActivity. A new MainView and ViewModel must
            // therefore be created for every activity instance instead of reusing one view.
            // Delaying CreateMainViewModel() until this factory runs also ensures that
            // MainActivity has already registered AndroidCameraService with AppServices.
            activityLifetime.MainViewFactory = static () => new MainView
            {
                DataContext = CreateMainViewModel()
            };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            // iOS, browser and embedded single-view platforms.
            singleView.MainView = new MainView
            {
                DataContext = CreateMainViewModel()
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static MainViewModel CreateMainViewModel()
    {
        var camera = AppServices.CreateCameraService();
        var localization = new LocalizationManager();

        // Pass the recognizer factory instead of constructing ONNX Runtime here. The UI can be
        // shown immediately; MainViewModel loads the model on a worker thread after Loaded.
        return new MainViewModel(
            camera,
            AppServices.CreateGestureRecognizer,
            AppServices.PlatformKind,
            localization);
    }
}
