using System.IO;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PdfTranslator.Models;
using PdfTranslator.Services;
using System.Windows;
using Microsoft.Win32;
using Serilog;

namespace PdfTranslator.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly ConfigurationService _configService;
        private readonly AppSettings _settings;
        private PdfProcessingService? _processingService;

        [ObservableProperty]
        private string _selectedFilePath = string.Empty;

        [ObservableProperty]
        private string _statusMessage = "Ready";

        [ObservableProperty]
        private int _progressValue;

        [ObservableProperty]
        private int _progressMaximum = 100;

        [ObservableProperty]
        private bool _isProcessing;

        [ObservableProperty]
        private int _charactersTranslated;

        [ObservableProperty]
        private bool _isDeepLSelected;

        [ObservableProperty]
        private bool _isMicrosoftTranslatorSelected = true;

        [ObservableProperty]
        private bool _isGoogleCloudSelected;

        [ObservableProperty]
        private bool _isTesseractSelected = true;

        [ObservableProperty]
        private bool _isAzureOcrSelected;

        [ObservableProperty]
        private bool _isDeepLDocumentMode = true;

        [ObservableProperty]
        private bool _isDeepLTextMode;

        /// <summary>
        /// OCR options should be hidden when DeepL or Google Cloud is selected
        /// because they use Document API which doesn't use OCR - it processes the PDF directly.
        /// </summary>
        public bool ShouldShowOcrOptions => !IsDeepLSelected && !IsGoogleCloudSelected;

        public MainViewModel()
        {
            _configService = new ConfigurationService();
            _settings = _configService.LoadSettings();
            CharactersTranslated = _settings.CharactersTranslated;

            // Set initial selections based on settings
            IsDeepLSelected = _settings.SelectedTranslationProvider == TranslationProvider.DeepL;
            IsMicrosoftTranslatorSelected = _settings.SelectedTranslationProvider == TranslationProvider.MicrosoftTranslator;
            IsGoogleCloudSelected = _settings.SelectedTranslationProvider == TranslationProvider.GoogleCloud;
            IsTesseractSelected = _settings.SelectedOcrEngine == OcrEngine.Tesseract;
            IsAzureOcrSelected = _settings.SelectedOcrEngine == OcrEngine.AzureDocumentIntelligence;
            
            // DeepL mode
            IsDeepLDocumentMode = _settings.DeepLMode == DeepLTranslationMode.DocumentTranslation;
            IsDeepLTextMode = _settings.DeepLMode == DeepLTranslationMode.TextTranslation;
        }

        [RelayCommand]
        private void SelectFile()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf",
                Title = "Select PDF to Translate"
            };

            if (dialog.ShowDialog() == true)
            {
                SelectedFilePath = dialog.FileName;
                StatusMessage = $"Selected: {Path.GetFileName(SelectedFilePath)}";
            }
        }

        [RelayCommand]
        private async Task ProcessPdf()
        {
            if (string.IsNullOrEmpty(SelectedFilePath))
            {
                MessageBox.Show("Please select a PDF file first.", "No File Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!File.Exists(SelectedFilePath))
            {
                MessageBox.Show("The selected file does not exist.", "File Not Found", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                IsProcessing = true;
                StatusMessage = "Initializing...";

                _processingService = new PdfProcessingService(_settings);
                _processingService.ProgressChanged += OnProgressChanged;

                // Add timestamp and provider name to output filename to avoid conflicts
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var providerName = _settings.SelectedTranslationProvider switch
                {
                    TranslationProvider.DeepL => "DeepL",
                    TranslationProvider.GoogleCloud => "Google",
                    _ => "Microsoft"
                };
                var outputPath = Path.Combine(
                    Path.GetDirectoryName(SelectedFilePath) ?? "",
                    $"{Path.GetFileNameWithoutExtension(SelectedFilePath)}_translated_{timestamp}_{providerName}.pdf");

                await _processingService.ProcessPdfAsync(SelectedFilePath, outputPath);

                CharactersTranslated = _settings.CharactersTranslated;
                _configService.SaveSettings(_settings);

                MessageBox.Show($"Translation completed!\nOutput saved to:\n{outputPath}", 
                    "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                
                // Open the translated PDF in the default PDF viewer
                if (_settings.OpenPdfAfterTranslation && File.Exists(outputPath))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = outputPath,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception openEx)
                    {
                        Log.Warning(openEx, "Could not open PDF automatically");
                    }
                }
                
                StatusMessage = "Completed successfully!";
            }
            catch (Exception ex)
            {
                Log.Error(ex, "PDF processing failed");
                MessageBox.Show($"Error: {ex.Message}", "Processing Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsProcessing = false;
                if (_processingService != null)
                {
                    _processingService.ProgressChanged -= OnProgressChanged;
                }
            }
        }

        [RelayCommand]
        private void OpenSettings()
        {
            var settingsWindow = new Views.SettingsWindow(_settings, _configService);
            settingsWindow.Owner = Application.Current.MainWindow;
            if (settingsWindow.ShowDialog() == true)
            {
                // Reload settings
                CharactersTranslated = _settings.CharactersTranslated;
                
                // Update provider selections
                IsDeepLSelected = _settings.SelectedTranslationProvider == TranslationProvider.DeepL;
                IsMicrosoftTranslatorSelected = _settings.SelectedTranslationProvider == TranslationProvider.MicrosoftTranslator;
                IsGoogleCloudSelected = _settings.SelectedTranslationProvider == TranslationProvider.GoogleCloud;
                IsTesseractSelected = _settings.SelectedOcrEngine == OcrEngine.Tesseract;
                IsAzureOcrSelected = _settings.SelectedOcrEngine == OcrEngine.AzureDocumentIntelligence;
                IsDeepLDocumentMode = _settings.DeepLMode == DeepLTranslationMode.DocumentTranslation;
                IsDeepLTextMode = _settings.DeepLMode == DeepLTranslationMode.TextTranslation;
            }
        }

        [RelayCommand]
        private void ToggleTranslationProvider(string provider)
        {
            if (provider == "DeepL")
            {
                _settings.SelectedTranslationProvider = TranslationProvider.DeepL;
                // Always use Document API for DeepL (best quality)
                _settings.DeepLMode = DeepLTranslationMode.DocumentTranslation;
                IsDeepLSelected = true;
                IsMicrosoftTranslatorSelected = false;
                IsGoogleCloudSelected = false;
            }
            else if (provider == "Google")
            {
                _settings.SelectedTranslationProvider = TranslationProvider.GoogleCloud;
                IsGoogleCloudSelected = true;
                IsDeepLSelected = false;
                IsMicrosoftTranslatorSelected = false;
            }
            else
            {
                _settings.SelectedTranslationProvider = TranslationProvider.MicrosoftTranslator;
                IsMicrosoftTranslatorSelected = true;
                IsDeepLSelected = false;
                IsGoogleCloudSelected = false;
            }
            
            _configService.SaveSettings(_settings);
            OnPropertyChanged(nameof(ShouldShowOcrOptions));
            StatusMessage = $"Switched to {provider}";
        }

        [RelayCommand]
        private void ToggleDeepLMode(string mode)
        {
            if (mode == "Document")
            {
                _settings.DeepLMode = DeepLTranslationMode.DocumentTranslation;
                IsDeepLDocumentMode = true;
                IsDeepLTextMode = false;
                StatusMessage = "DeepL: Document Translation (best formatting)";
            }
            else
            {
                _settings.DeepLMode = DeepLTranslationMode.TextTranslation;
                IsDeepLTextMode = true;
                IsDeepLDocumentMode = false;
                StatusMessage = "DeepL: Text Translation (text extraction mode)";
            }
            
            _configService.SaveSettings(_settings);
            OnPropertyChanged(nameof(ShouldShowOcrOptions));
        }

        [RelayCommand]
        private void ToggleOcrEngine(string engine)
        {
            if (engine == "Azure")
            {
                _settings.SelectedOcrEngine = OcrEngine.AzureDocumentIntelligence;
                IsAzureOcrSelected = true;
                IsTesseractSelected = false;
            }
            else
            {
                _settings.SelectedOcrEngine = OcrEngine.Tesseract;
                IsTesseractSelected = true;
                IsAzureOcrSelected = false;
            }
            
            _configService.SaveSettings(_settings);
            StatusMessage = $"Switched to {engine} OCR";
        }

        private void OnProgressChanged(object? sender, ProcessingProgress progress)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                ProgressMaximum = progress.TotalPages;
                ProgressValue = progress.CurrentPage;
                StatusMessage = progress.Status;
            });
        }
    }
}
