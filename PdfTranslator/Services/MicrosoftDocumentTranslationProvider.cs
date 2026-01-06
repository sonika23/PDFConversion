using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.AI.Translation.Document;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using PdfTranslator.Models;

namespace PdfTranslator.Services
{
    /// <summary>
    /// Provides document translation using Azure Document Translation API (Async Batch mode with polling).
    /// This approach preserves PDF formatting and supports OCR for scanned documents.
    /// Requires Azure Blob Storage for temporary file storage.
    /// 
    /// Workflow:
    /// 1. Upload source PDF to "source" container
    /// 2. Start batch translation job
    /// 3. Poll for completion (every 5 seconds)
    /// 4. Download translated PDF from "target" container
    /// 5. Clean up (delete source and target blobs)
    /// 
    /// Pricing (as of 2024):
    /// - Document Translation: ~$15 per million characters
    /// - Storage: Minimal cost (files deleted after translation)
    /// 
    /// Setup Requirements:
    /// 1. Create Azure Translator resource in Azure Portal
    /// 2. Create Azure Storage Account
    /// 3. Get Translator endpoint and key
    /// 4. Get Storage connection string
    /// </summary>
    public class MicrosoftDocumentTranslationProvider
    {
        private readonly AppSettings _settings;
        private static readonly HttpClient _httpClient = new HttpClient();
        
        private const string SourceContainerName = "source";
        private const string TargetContainerName = "target";
        private const int PollingIntervalSeconds = 5;
        private const int MaxPollingMinutes = 30;

        public MicrosoftDocumentTranslationProvider(AppSettings settings)
        {
            _settings = settings;
        }
        
        /// <summary>
        /// Gets the active endpoint based on tier, falling back to legacy settings.
        /// </summary>
        private string GetActiveEndpoint()
        {
            var endpoint = _settings.GetActiveMicrosoftEndpoint();
            if (string.IsNullOrWhiteSpace(endpoint))
                endpoint = _settings.AzureTranslatorEndpoint;
            return endpoint ?? string.Empty;
        }
        
        /// <summary>
        /// Gets the active API key based on tier, falling back to legacy settings.
        /// </summary>
        private string GetActiveApiKey()
        {
            var apiKey = _settings.GetActiveMicrosoftApiKey();
            if (string.IsNullOrWhiteSpace(apiKey))
                apiKey = _settings.AzureTranslatorKey;
            return apiKey ?? string.Empty;
        }
        
        /// <summary>
        /// Gets the active storage connection string based on tier, falling back to legacy settings.
        /// </summary>
        private string GetActiveStorageConnectionString()
        {
            var connectionString = _settings.GetActiveMicrosoftStorageConnectionString();
            if (string.IsNullOrWhiteSpace(connectionString))
                connectionString = _settings.AzureStorageConnectionString;
            return connectionString ?? string.Empty;
        }

        /// <summary>
        /// Validates that all required settings are configured.
        /// </summary>
        public (bool IsValid, string ErrorMessage) ValidateSettings()
        {
            // Check Paid tier settings first (preferred for Document Translation API)
            var endpoint = _settings.GetActiveMicrosoftEndpoint();
            var apiKey = _settings.GetActiveMicrosoftApiKey();
            var storageConnection = _settings.GetActiveMicrosoftStorageConnectionString();
            
            // Fall back to legacy settings if new settings are empty
            if (string.IsNullOrWhiteSpace(endpoint))
                endpoint = _settings.AzureTranslatorEndpoint;
            if (string.IsNullOrWhiteSpace(apiKey))
                apiKey = _settings.AzureTranslatorKey;
            if (string.IsNullOrWhiteSpace(storageConnection))
                storageConnection = _settings.AzureStorageConnectionString;

            if (string.IsNullOrWhiteSpace(endpoint))
            {
                return (false, "Microsoft Translator Endpoint is required for Document Translation.\nPlease configure your Paid (S1) account settings.\nExample: https://your-translator.cognitiveservices.azure.com/");
            }

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return (false, "Microsoft Translator API Key is required.\nPlease configure your Paid (S1) account settings.");
            }

            if (string.IsNullOrWhiteSpace(storageConnection))
            {
                return (false, "Azure Storage Connection String is required for batch document translation.\nPlease configure your Paid (S1) account settings.\nFind it in Azure Portal → Storage Account → Access Keys");
            }

            if (!endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return (false, "Azure Translator Endpoint must start with 'https://'");
            }

            return (true, string.Empty);
        }

        /// <summary>
        /// Ensures the source and target containers exist, creates them if not.
        /// </summary>
        private async Task<(BlobContainerClient sourceContainer, BlobContainerClient targetContainer)> EnsureContainersExistAsync(
            BlobServiceClient blobServiceClient,
            CancellationToken cancellationToken)
        {
            var sourceContainer = blobServiceClient.GetBlobContainerClient(SourceContainerName);
            var targetContainer = blobServiceClient.GetBlobContainerClient(TargetContainerName);

            // Create containers if they don't exist
            await sourceContainer.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
            await targetContainer.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);

            Serilog.Log.Information("Containers ensured: {Source}, {Target}", SourceContainerName, TargetContainerName);

            return (sourceContainer, targetContainer);
        }

        /// <summary>
        /// Generates a SAS URI for a container with appropriate permissions.
        /// </summary>
        private Uri GenerateContainerSasUri(BlobContainerClient containerClient, BlobContainerSasPermissions permissions)
        {
            // Check if container client can generate SAS
            if (!containerClient.CanGenerateSasUri)
            {
                throw new InvalidOperationException(
                    "Cannot generate SAS URI. Make sure your connection string includes the account key.");
            }

            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = containerClient.Name,
                Resource = "c", // Container
                ExpiresOn = DateTimeOffset.UtcNow.AddHours(2) // 2 hour expiry
            };
            sasBuilder.SetPermissions(permissions);

            return containerClient.GenerateSasUri(sasBuilder);
        }

        /// <summary>
        /// Translates a PDF document using Azure Document Translation (Async Batch API with polling).
        /// </summary>
        public async Task<(string OutputPath, int CharacterCount)> TranslateDocumentAsync(
            string inputPdfPath,
            string sourceLanguage,
            string targetLanguage,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var validation = ValidateSettings();
            if (!validation.IsValid)
            {
                throw new InvalidOperationException(validation.ErrorMessage);
            }

            var fileName = Path.GetFileName(inputPdfPath);
            var uniqueFileName = $"{Guid.NewGuid()}_{fileName}";
            
            Serilog.Log.Information("Starting batch document translation for: {FileName}", fileName);

            try
            {
                // Step 1: Initialize storage
                progress?.Report("Connecting to Azure Storage...");
                var blobServiceClient = new BlobServiceClient(GetActiveStorageConnectionString());
                var (sourceContainer, targetContainer) = await EnsureContainersExistAsync(blobServiceClient, cancellationToken);

                // Step 2: Upload source document
                progress?.Report("Uploading document to Azure...");
                var sourceBlob = sourceContainer.GetBlobClient(uniqueFileName);
                
                await using (var fileStream = File.OpenRead(inputPdfPath))
                {
                    await sourceBlob.UploadAsync(fileStream, overwrite: true, cancellationToken);
                }
                
                Serilog.Log.Information("Uploaded source document: {BlobUri}", sourceBlob.Uri);

                // Step 3: Generate SAS URIs
                var sourceSasUri = GenerateContainerSasUri(sourceContainer, 
                    BlobContainerSasPermissions.Read | BlobContainerSasPermissions.List);
                var targetSasUri = GenerateContainerSasUri(targetContainer, 
                    BlobContainerSasPermissions.Write | BlobContainerSasPermissions.List);

                // Step 4: Start translation job
                progress?.Report("Starting translation job...");
                var translatorEndpoint = new Uri(GetActiveEndpoint().TrimEnd('/'));
                var credential = new AzureKeyCredential(GetActiveApiKey());
                var translationClient = new DocumentTranslationClient(translatorEndpoint, credential);

                // Create translation input - we upload a uniquely named file so no filter needed
                var sourceInput = new TranslationSource(sourceSasUri)
                {
                    LanguageCode = sourceLanguage
                };

                var targetOutput = new TranslationTarget(targetSasUri, targetLanguage);
                var translationInput = new DocumentTranslationInput(sourceInput, new[] { targetOutput });

                var operation = await translationClient.StartTranslationAsync(new[] { translationInput }, cancellationToken);

                Serilog.Log.Information("Translation job started. Operation ID: {OperationId}", operation.Id);

                // Step 5: Poll for completion
                progress?.Report("Translating document (this may take 1-2 minutes)...");
                var startTime = DateTime.UtcNow;
                var maxEndTime = startTime.AddMinutes(MaxPollingMinutes);

                while (!operation.HasCompleted && DateTime.UtcNow < maxEndTime)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    await Task.Delay(TimeSpan.FromSeconds(PollingIntervalSeconds), cancellationToken);
                    await operation.UpdateStatusAsync(cancellationToken);

                    var elapsed = DateTime.UtcNow - startTime;
                    progress?.Report($"Translating... ({elapsed.TotalSeconds:F0}s elapsed)");
                    
                    Serilog.Log.Debug("Translation status: {Status}, Documents: {Total} total, {Succeeded} succeeded, {Failed} failed",
                        operation.Status, operation.DocumentsTotal, operation.DocumentsSucceeded, operation.DocumentsFailed);
                }

                if (!operation.HasCompleted)
                {
                    throw new TimeoutException($"Translation job did not complete within {MaxPollingMinutes} minutes.");
                }

                // Step 6: Check for errors
                long charactersCharged = 0;
                string? translatedBlobName = null;

                await foreach (var document in operation.GetValuesAsync(cancellationToken))
                {
                    Serilog.Log.Information("Document status: {Id}, Status: {Status}", document.Id, document.Status);
                    
                    if (document.Status == DocumentTranslationStatus.Succeeded)
                    {
                        charactersCharged += document.CharactersCharged;
                        // Extract blob name from the translated document URI
                        translatedBlobName = Path.GetFileName(document.TranslatedDocumentUri?.LocalPath ?? uniqueFileName);
                        Serilog.Log.Information("Translation succeeded. Characters charged: {Chars}, Output URI: {Uri}", 
                            document.CharactersCharged, document.TranslatedDocumentUri);
                    }
                    else if (document.Status == DocumentTranslationStatus.Failed)
                    {
                        var errorMsg = document.Error?.Message ?? "Unknown error";
                        Serilog.Log.Error("Document translation failed: {Error}", errorMsg);
                        throw new Exception($"Document translation failed: {errorMsg}");
                    }
                }

                // Step 7: Download translated document
                progress?.Report("Downloading translated document...");
                
                // The translated file has the same name as source in the target container
                var targetBlob = targetContainer.GetBlobClient(translatedBlobName ?? uniqueFileName);
                
                var outputFileName = Path.GetFileNameWithoutExtension(inputPdfPath);
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var outputPath = Path.Combine(
                    Path.GetDirectoryName(inputPdfPath)!,
                    $"{outputFileName}_Translated_{timestamp}_Microsoft.pdf"
                );

                await using (var downloadStream = File.Create(outputPath))
                {
                    await targetBlob.DownloadToAsync(downloadStream, cancellationToken);
                }

                Serilog.Log.Information("Downloaded translated document to: {OutputPath}", outputPath);

                // Step 8: Cleanup - delete source and target blobs
                progress?.Report("Cleaning up temporary files...");
                try
                {
                    await sourceBlob.DeleteIfExistsAsync(cancellationToken: cancellationToken);
                    await targetBlob.DeleteIfExistsAsync(cancellationToken: cancellationToken);
                    Serilog.Log.Information("Cleaned up temporary blobs");
                }
                catch (Exception cleanupEx)
                {
                    // Log but don't fail the operation for cleanup errors
                    Serilog.Log.Warning(cleanupEx, "Failed to cleanup temporary blobs");
                }

                progress?.Report($"Translation complete! Characters: {charactersCharged:N0}");

                return (outputPath, (int)charactersCharged);
            }
            catch (RequestFailedException ex)
            {
                Serilog.Log.Error(ex, "Azure SDK request failed. Error Code: {ErrorCode}, Message: {Message}", 
                    ex.ErrorCode, ex.Message);
                
                string errorMessage = ex.Status switch
                {
                    400 => $"Bad Request - The document format may not be supported.\nDetails: {ex.Message}",
                    401 => "Unauthorized - Please check your API key is correct.",
                    403 => "Forbidden - Your API key may not have permission for Document Translation.",
                    404 => "Not Found - Please check your endpoint URL is correct.",
                    413 => "File too large - The document exceeds the maximum size limit.",
                    429 => "Too many requests - Please wait a moment and try again.",
                    _ => $"Azure error ({ex.ErrorCode}): {ex.Message}"
                };

                throw new Exception($"Document translation failed: {errorMessage}", ex);
            }
        }

        /// <summary>
        /// Tests the connection to Azure Document Translation service and Storage.
        /// </summary>
        public async Task<(bool Success, string Message)> TestConnectionAsync()
        {
            try
            {
                var validation = ValidateSettings();
                if (!validation.IsValid)
                {
                    return (false, validation.ErrorMessage);
                }

                // Test Translator endpoint
                var endpoint = GetActiveEndpoint().TrimEnd('/');
                var testUrl = $"{endpoint}/translator/text/v3.0/languages?scope=translation";

                using var request = new HttpRequestMessage(HttpMethod.Get, testUrl);
                request.Headers.Add("Ocp-Apim-Subscription-Key", GetActiveApiKey());

                var response = await _httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    return (false, $"Translator connection failed (HTTP {(int)response.StatusCode}): {error}");
                }

                // Test Storage connection
                try
                {
                    var blobServiceClient = new BlobServiceClient(GetActiveStorageConnectionString());
                    var properties = await blobServiceClient.GetPropertiesAsync();
                    
                    // Try to ensure containers exist
                    var sourceContainer = blobServiceClient.GetBlobContainerClient(SourceContainerName);
                    var targetContainer = blobServiceClient.GetBlobContainerClient(TargetContainerName);
                    
                    await sourceContainer.CreateIfNotExistsAsync();
                    await targetContainer.CreateIfNotExistsAsync();
                }
                catch (Exception storageEx)
                {
                    return (false, $"Storage connection failed: {storageEx.Message}\n\nMake sure your connection string is correct.");
                }

                return (true, "Connection successful!\n\n✓ Translator API connected\n✓ Storage Account connected\n✓ Source/Target containers ready\n\nBatch document translation is ready to use.");
            }
            catch (Exception ex)
            {
                return (false, $"Connection failed: {ex.Message}");
            }
        }
    }
}
