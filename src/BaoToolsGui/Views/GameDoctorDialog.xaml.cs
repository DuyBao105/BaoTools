using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using BaoToolsGui.Services;
using Microsoft.Win32;
using Wpf.Ui.Controls;
using Res = BaoToolsGui.Resources.Strings;

namespace BaoToolsGui.Views;

public partial class GameDoctorDialog : Window
{
    private readonly GameDoctorService _doctor;
    private readonly ToastService? _toast;
    private string _currentExePath;
    private string? _gameName;
    private GameDiagnosisResult? _lastDiagnosis;

    public GameDoctorDialog(
        GameDoctorService doctor,
        string exePath,
        string? gameName = null,
        ToastService? toast = null)
    {
        InitializeComponent();
        _doctor = doctor;
        _currentExePath = exePath;
        _gameName = gameName;
        _toast = toast;

        Loaded += (_, _) => RunDiagnosis();
    }

    private void RunDiagnosis()
    {
        if (string.IsNullOrWhiteSpace(_currentExePath) || !File.Exists(_currentExePath))
        {
            TxtSubTitle.Text = Res.GameDoctor_ExeNotFound_Title;
            HealthCard.Background = new SolidColorBrush(Color.FromArgb(0x20, 0xEF, 0x44, 0x44));
            HealthCard.BorderBrush = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
            HealthIcon.Symbol = SymbolRegular.DismissCircle24;
            HealthIcon.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
            TxtHealthStatus.Text = Res.GameDoctor_ExeNotFound_Status;
            TxtHealthDetails.Text = _currentExePath;
            BtnAutoFix.Visibility = Visibility.Collapsed;
            return;
        }

        string fileName = Path.GetFileName(_currentExePath);
        TxtSubTitle.Text = string.IsNullOrEmpty(_gameName)
            ? _currentExePath
            : $"{_gameName} ({fileName})";

        _lastDiagnosis = _doctor.DiagnoseExe(_currentExePath, _gameName);

        TxtArch.Text = _lastDiagnosis.Architecture switch
        {
            PeArchitecture.X64 => "64-bit (x64)",
            PeArchitecture.X86 => "32-bit (x86)",
            PeArchitecture.Arm64 => "ARM64",
            _ => "Unknown"
        };

        ListDependencies.ItemsSource = _lastDiagnosis.Dependencies;

        if (_lastDiagnosis.IsHealthy)
        {
            HealthCard.Background = new SolidColorBrush(Color.FromArgb(0x18, 0x10, 0xB9, 0x81));
            HealthCard.BorderBrush = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
            HealthIcon.Symbol = SymbolRegular.CheckmarkCircle24;
            HealthIcon.Foreground = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
            TxtHealthStatus.Text = Res.GameDoctor_Healthy;
            TxtHealthDetails.Text = string.Format(Res.GameDoctor_AllPeImportsValid, _lastDiagnosis.TotalImportedDlls);
            BtnAutoFix.Visibility = Visibility.Collapsed;
        }
        else
        {
            HealthCard.Background = new SolidColorBrush(Color.FromArgb(0x20, 0xEF, 0x44, 0x44));
            HealthCard.BorderBrush = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
            HealthIcon.Symbol = SymbolRegular.Warning24;
            HealthIcon.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));

            TxtHealthStatus.Text = string.Format(Res.GameDoctor_MissingDllCount, _lastDiagnosis.MissingDlls.Count);

            if (_lastDiagnosis.HasArchitectureMismatch)
            {
                TxtHealthDetails.Text = Res.GameDoctor_ArchitectureMismatch;
            }
            else
            {
                TxtHealthDetails.Text = string.Format(Res.GameDoctor_NeedsRuntimePackages, _lastDiagnosis.MissingPackages.Count);
            }

            BtnAutoFix.Visibility = _lastDiagnosis.MissingPackages.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private async void AutoFixButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lastDiagnosis is null || _lastDiagnosis.MissingPackages.Count == 0) return;

        BtnAutoFix.IsEnabled = false;
        ProgressFix.Visibility = Visibility.Visible;
        IconFix.Visibility = Visibility.Collapsed;
        TxtFix.Text = Res.GameDoctor_Status_Installing;

        try
        {
            bool allOk = true;
            foreach (var pkg in _lastDiagnosis.MissingPackages)
            {
                bool ok = await _doctor.InstallPackageAsync(pkg);
                if (!ok) allOk = false;
            }

            if (allOk)
            {
                _toast?.Show(Res.GameDoctor_Title, Res.GameDoctor_Toast_AutoHealSuccess);
            }
        }
        catch (Exception ex)
        {
            _toast?.Show(Res.GameDoctor_Title, ex.Message, error: true);
        }
        finally
        {
            BtnAutoFix.IsEnabled = true;
            ProgressFix.Visibility = Visibility.Collapsed;
            IconFix.Visibility = Visibility.Visible;
            TxtFix.Text = Res.GameDoctor_FixNow;
            RunDiagnosis();
        }
    }

    private void ChooseExeButton_Click(object sender, RoutedEventArgs e)
    {
        var ofd = new OpenFileDialog
        {
            Filter = "Game Executable (*.exe)|*.exe|All files (*.*)|*.*",
            Title = Res.GameDoctor_ChooseExe
        };

        if (ofd.ShowDialog() == true)
        {
            _currentExePath = ofd.FileName;
            _gameName = Path.GetFileNameWithoutExtension(ofd.FileName);
            RunDiagnosis();
        }
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
