using System.Windows;
using PdfTranslator.Models;
using PdfTranslator.Services;

namespace PdfTranslator.Views
{
    public partial class SettingsWindow : Window
    {
        private readonly AppSettings _settings;
        private readonly ConfigurationService _configService;

        public SettingsWindow(AppSettings settings, ConfigurationService configService)
        {
            InitializeComponent();
            _settings = settings;
            _configService = configService;
            LoadSettings();
        }

        private void LoadSettings()
        {
            // DeepL settings
            DeepLFreeApiKeyTextBox.Text = _settings.DeepLFreeApiKey;
            DeepLPaidApiKeyTextBox.Text = _settings.DeepLPaidApiKey;
            
            // Migrate legacy DeepL key if exists
            if (string.IsNullOrEmpty(_settings.DeepLFreeApiKey) && !string.IsNullOrEmpty(_settings.DeepLApiKey))
            {
                DeepLFreeApiKeyTextBox.Text = _settings.DeepLApiKey;
            }
            
            // Microsoft Free (F0) settings
            MicrosoftFreeApiKeyTextBox.Text = _settings.MicrosoftFreeApiKey;
            MicrosoftFreeRegionTextBox.Text = string.IsNullOrEmpty(_settings.MicrosoftFreeRegion) ? "global" : _settings.MicrosoftFreeRegion;
            
            // Migrate legacy Microsoft settings if exists
            if (string.IsNullOrEmpty(_settings.MicrosoftFreeApiKey) && !string.IsNullOrEmpty(_settings.AzureTranslatorKey))
            {
                MicrosoftFreeApiKeyTextBox.Text = _settings.AzureTranslatorKey;
                MicrosoftFreeRegionTextBox.Text = _settings.AzureTranslatorRegion;
            }
            
            // Microsoft Paid (S1) settings
            MicrosoftPaidApiKeyTextBox.Text = _settings.MicrosoftPaidApiKey;
            MicrosoftPaidEndpointTextBox.Text = _settings.MicrosoftPaidEndpoint;
            MicrosoftPaidStorageConnectionStringTextBox.Text = _settings.MicrosoftPaidStorageConnectionString;
            
            // Migrate legacy Microsoft paid settings if exists
            if (string.IsNullOrEmpty(_settings.MicrosoftPaidEndpoint) && !string.IsNullOrEmpty(_settings.AzureTranslatorEndpoint))
            {
                MicrosoftPaidApiKeyTextBox.Text = _settings.AzureTranslatorKey;
                MicrosoftPaidEndpointTextBox.Text = _settings.AzureTranslatorEndpoint;
                MicrosoftPaidStorageConnectionStringTextBox.Text = _settings.AzureStorageConnectionString;
            }
            
            // Google settings
            GoogleCredentialsPathTextBox.Text = _settings.GoogleCloudCredentialsPath;
            GoogleProjectIdTextBox.Text = _settings.GoogleCloudProjectId;
            
            // Azure Document Intelligence settings
            AzureDocIntelEndpointTextBox.Text = _settings.AzureDocumentIntelligenceEndpoint;
            AzureDocIntelApiKeyTextBox.Text = _settings.AzureDocumentIntelligenceKey;
            
            // PDF Generator - Always use PdfPig (hidden from UI)
            PdfPigRadio.IsChecked = true;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // DeepL settings
            _settings.DeepLFreeApiKey = DeepLFreeApiKeyTextBox.Text;
            _settings.DeepLPaidApiKey = DeepLPaidApiKeyTextBox.Text;
            _settings.DeepLApiKey = DeepLFreeApiKeyTextBox.Text; // Keep legacy for backward compatibility
            
            // Microsoft Free (F0) settings
            _settings.MicrosoftFreeApiKey = MicrosoftFreeApiKeyTextBox.Text;
            _settings.MicrosoftFreeRegion = MicrosoftFreeRegionTextBox.Text;
            
            // Microsoft Paid (S1) settings
            _settings.MicrosoftPaidApiKey = MicrosoftPaidApiKeyTextBox.Text;
            _settings.MicrosoftPaidEndpoint = MicrosoftPaidEndpointTextBox.Text;
            _settings.MicrosoftPaidStorageConnectionString = MicrosoftPaidStorageConnectionStringTextBox.Text;
            
            // Keep legacy settings in sync for backward compatibility
            _settings.AzureTranslatorKey = MicrosoftFreeApiKeyTextBox.Text;
            _settings.AzureTranslatorRegion = MicrosoftFreeRegionTextBox.Text;
            _settings.AzureTranslatorEndpoint = MicrosoftPaidEndpointTextBox.Text;
            _settings.AzureStorageConnectionString = MicrosoftPaidStorageConnectionStringTextBox.Text;
            
            // Google settings
            _settings.GoogleCloudCredentialsPath = GoogleCredentialsPathTextBox.Text;
            _settings.GoogleCloudProjectId = GoogleProjectIdTextBox.Text;
            
            // Azure Document Intelligence settings
            _settings.AzureDocumentIntelligenceEndpoint = AzureDocIntelEndpointTextBox.Text;
            _settings.AzureDocumentIntelligenceKey = AzureDocIntelApiKeyTextBox.Text;
            
            // PDF Generator - Always use PdfPig (hidden from UI)
            _settings.SelectedPdfGenerator = PdfGeneratorEngine.PdfPig;

            _configService.SaveSettings(_settings);
            
            DialogResult = true;
            Close();
        }

        private void BrowseGoogleCredentials_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                Title = "Select Google Cloud Service Account JSON File"
            };

            if (dialog.ShowDialog() == true)
            {
                GoogleCredentialsPathTextBox.Text = dialog.FileName;
            }
        }

        private async void TestMicrosoftConnection_Click(object sender, RoutedEventArgs e)
        {
            // Test the paid (S1) connection which requires endpoint and storage
            var tempSettings = new AppSettings
            {
                AzureTranslatorEndpoint = MicrosoftPaidEndpointTextBox.Text,
                AzureTranslatorKey = MicrosoftPaidApiKeyTextBox.Text,
                AzureStorageConnectionString = MicrosoftPaidStorageConnectionStringTextBox.Text
            };

            TestMicrosoftConnectionButton.IsEnabled = false;
            TestMicrosoftConnectionButton.Content = "Testing...";

            try
            {
                var provider = new MicrosoftDocumentTranslationProvider(tempSettings);
                var (success, message) = await provider.TestConnectionAsync();

                if (success)
                {
                    MessageBox.Show(message, "Connection Test", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show(message, "Connection Test Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Connection Test Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                TestMicrosoftConnectionButton.IsEnabled = true;
                TestMicrosoftConnectionButton.Content = "Test Microsoft Connection";
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
