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

        // Per-tier character display properties
        [ObservableProperty]
        private string _microsoftFreeCharsDisplay = "0";

        [ObservableProperty]
        private string _microsoftPaidCharsDisplay = "0";

        [ObservableProperty]
        private string _deepLFreeCharsDisplay = "0";

        [ObservableProperty]
        private string _deepLPaidCharsDisplay = "0";

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

        [ObservableProperty]
        private bool _isMicrosoftDocumentMode = true;

        [ObservableProperty]
        private bool _isMicrosoftTextMode;

        // Account Tier Selection
        [ObservableProperty]
        private bool _isMicrosoftFreeTier = true;

        [ObservableProperty]
        private bool _isMicrosoftPaidTier;

        [ObservableProperty]
        private bool _isDeepLFreeTier = true;

        [ObservableProperty]
        private bool _isDeepLPaidTier;

        [ObservableProperty]
        private string _currentTierDescription = "Select an account tier for more details.";

        /// <summary>
        /// OCR options should be hidden when DeepL Document mode, Google Cloud, or Microsoft Document mode is selected
        /// because they use Document API which doesn't use OCR - it processes the PDF directly.
        /// </summary>
        public bool ShouldShowOcrOptions => 
            (!IsDeepLSelected && !IsGoogleCloudSelected && !IsMicrosoftDocumentMode) ||
            (IsMicrosoftTranslatorSelected && IsMicrosoftTextMode) ||
            (IsDeepLSelected && IsDeepLTextMode);

        public MainViewModel()
        {
            _configService = new ConfigurationService();
            _settings = _configService.LoadSettings();
            CharactersTranslated = _settings.CharactersTranslated;
            
            // Load per-tier character counts
            UpdateCharacterDisplays();

            // Set initial selections based on settings
            IsDeepLSelected = _settings.SelectedTranslationProvider == TranslationProvider.DeepL;
            IsMicrosoftTranslatorSelected = _settings.SelectedTranslationProvider == TranslationProvider.MicrosoftTranslator;
            IsGoogleCloudSelected = _settings.SelectedTranslationProvider == TranslationProvider.GoogleCloud;
            IsTesseractSelected = _settings.SelectedOcrEngine == OcrEngine.Tesseract;
            IsAzureOcrSelected = _settings.SelectedOcrEngine == OcrEngine.AzureDocumentIntelligence;
            
            // DeepL mode
            IsDeepLDocumentMode = _settings.DeepLMode == DeepLTranslationMode.DocumentTranslation;
            IsDeepLTextMode = _settings.DeepLMode == DeepLTranslationMode.TextTranslation;
            
            // Microsoft mode
            IsMicrosoftDocumentMode = _settings.MicrosoftMode == MicrosoftTranslationMode.DocumentTranslation;
            IsMicrosoftTextMode = _settings.MicrosoftMode == MicrosoftTranslationMode.TextTranslation;
            
            // Account Tiers
            IsMicrosoftFreeTier = _settings.MicrosoftAccountTier == AccountTier.Free;
            IsMicrosoftPaidTier = _settings.MicrosoftAccountTier == AccountTier.Paid;
            IsDeepLFreeTier = _settings.DeepLAccountTier == AccountTier.Free;
            IsDeepLPaidTier = _settings.DeepLAccountTier == AccountTier.Paid;
            
            UpdateTierDescription();
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

                UpdateCharacterDisplays();
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
                IsMicrosoftDocumentMode = _settings.MicrosoftMode == MicrosoftTranslationMode.DocumentTranslation;
                IsMicrosoftTextMode = _settings.MicrosoftMode == MicrosoftTranslationMode.TextTranslation;
                
                // Update tier selections
                IsMicrosoftFreeTier = _settings.MicrosoftAccountTier == AccountTier.Free;
                IsMicrosoftPaidTier = _settings.MicrosoftAccountTier == AccountTier.Paid;
                IsDeepLFreeTier = _settings.DeepLAccountTier == AccountTier.Free;
                IsDeepLPaidTier = _settings.DeepLAccountTier == AccountTier.Paid;
                
                UpdateTierDescription();
            }
        }

        [RelayCommand]
        private void ToggleMicrosoftTier(string tier)
        {
            if (tier == "Free")
            {
                _settings.MicrosoftAccountTier = AccountTier.Free;
                _settings.MicrosoftMode = MicrosoftTranslationMode.TextTranslation;
                IsMicrosoftFreeTier = true;
                IsMicrosoftPaidTier = false;
                IsMicrosoftTextMode = true;
                IsMicrosoftDocumentMode = false;
                StatusMessage = "Microsoft Free (F0): Text API mode";
            }
            else
            {
                _settings.MicrosoftAccountTier = AccountTier.Paid;
                _settings.MicrosoftMode = MicrosoftTranslationMode.DocumentTranslation;
                IsMicrosoftPaidTier = true;
                IsMicrosoftFreeTier = false;
                IsMicrosoftDocumentMode = true;
                IsMicrosoftTextMode = false;
                StatusMessage = "Microsoft Paid (S1): Document Translation API mode";
            }
            
            _configService.SaveSettings(_settings);
            OnPropertyChanged(nameof(ShouldShowOcrOptions));
            UpdateTierDescription();
        }

        [RelayCommand]
        private void ToggleDeepLTier(string tier)
        {
            if (tier == "Free")
            {
                _settings.DeepLAccountTier = AccountTier.Free;
                IsDeepLFreeTier = true;
                IsDeepLPaidTier = false;
                StatusMessage = "DeepL Free: Limited monthly usage";
            }
            else
            {
                _settings.DeepLAccountTier = AccountTier.Paid;
                IsDeepLPaidTier = true;
                IsDeepLFreeTier = false;
                StatusMessage = "DeepL Pro: Unlimited usage";
            }
            
            _configService.SaveSettings(_settings);
            UpdateTierDescription();
        }

        private void UpdateTierDescription()
        {
            if (IsMicrosoftTranslatorSelected)
            {
                if (IsMicrosoftFreeTier)
                {
                    CurrentTierDescription = "Microsoft Free (F0): Uses Text API. Best for simple A4 documents with basic formatting.";
                }
                else
                {
                    CurrentTierDescription = "Microsoft Paid (S1): Uses Document Translation API with Azure Blob Storage. Best for complex documents with tables and formatting.";
                }
            }
            else if (IsDeepLSelected)
            {
                if (IsDeepLFreeTier)
                {
                    CurrentTierDescription = "DeepL Free: Limited to 500,000 characters per month. Good for occasional use.";
                }
                else
                {
                    CurrentTierDescription = "DeepL Pro: Unlimited usage with full document translation support. Best for heavy usage.";
                }
            }
            else if (IsGoogleCloudSelected)
            {
                CurrentTierDescription = "Google Cloud: Document Translation API with built-in OCR. Pay per character translated.";
            }
            else
            {
                CurrentTierDescription = "Select a translation provider to see tier information.";
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
            UpdateTierDescription();
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
        private void ToggleMicrosoftMode(string mode)
        {
            if (mode == "Document")
            {
                _settings.MicrosoftMode = MicrosoftTranslationMode.DocumentTranslation;
                IsMicrosoftDocumentMode = true;
                IsMicrosoftTextMode = false;
                StatusMessage = "Microsoft: Document Translation (Azure Blob Storage)";
            }
            else
            {
                _settings.MicrosoftMode = MicrosoftTranslationMode.TextTranslation;
                IsMicrosoftTextMode = true;
                IsMicrosoftDocumentMode = false;
                StatusMessage = "Microsoft: Text Translation (text extraction mode)";
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

        private void UpdateCharacterDisplays()
        {
            MicrosoftFreeCharsDisplay = _settings.MicrosoftFreeCharactersTranslated.ToString("N0");
            MicrosoftPaidCharsDisplay = _settings.MicrosoftPaidCharactersTranslated.ToString("N0");
            DeepLFreeCharsDisplay = _settings.DeepLFreeCharactersTranslated.ToString("N0");
            DeepLPaidCharsDisplay = _settings.DeepLPaidCharactersTranslated.ToString("N0");
            CharactersTranslated = _settings.CharactersTranslated;
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
