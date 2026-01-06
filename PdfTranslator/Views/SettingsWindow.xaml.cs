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
            DeepLApiKeyTextBox.Text = _settings.DeepLApiKey;
            MicrosoftApiKeyTextBox.Text = _settings.AzureTranslatorKey;
            MicrosoftRegionTextBox.Text = _settings.AzureTranslatorRegion;
            GoogleCredentialsPathTextBox.Text = _settings.GoogleCloudCredentialsPath;
            GoogleProjectIdTextBox.Text = _settings.GoogleCloudProjectId;
            AzureDocIntelEndpointTextBox.Text = _settings.AzureDocumentIntelligenceEndpoint;
            AzureDocIntelApiKeyTextBox.Text = _settings.AzureDocumentIntelligenceKey;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            _settings.DeepLApiKey = DeepLApiKeyTextBox.Text;
            _settings.AzureTranslatorKey = MicrosoftApiKeyTextBox.Text;
            _settings.AzureTranslatorRegion = MicrosoftRegionTextBox.Text;
            _settings.GoogleCloudCredentialsPath = GoogleCredentialsPathTextBox.Text;
            _settings.GoogleCloudProjectId = GoogleProjectIdTextBox.Text;
            _settings.AzureDocumentIntelligenceEndpoint = AzureDocIntelEndpointTextBox.Text;
            _settings.AzureDocumentIntelligenceKey = AzureDocIntelApiKeyTextBox.Text;

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

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
