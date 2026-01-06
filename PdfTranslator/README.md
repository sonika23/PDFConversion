# PDF Translator - Russian to English

A Windows desktop application that translates Russian PDF documents to English while preserving formatting.

## Features

- **Dual Translation Providers**: Toggle between Microsoft Translator (2M chars/month free) and DeepL (best quality)
- **Dual OCR Engines**: Use free Tesseract OCR or premium Azure Document Intelligence for scanned PDFs
- **Smart PDF Detection**: Automatically detects text-based vs scanned PDFs
- **Best-effort Format Preservation**: Maintains layout and positioning where possible
- **Modern UI**: Material Design interface with real-time progress tracking
- **Usage Statistics**: Track character counts and API usage

## Prerequisites

- Windows 10 or later
- .NET 8.0 Runtime
- Internet connection (for translation APIs)

## Setup Instructions

### 1. Download Tesseract Language Data (Required for OCR)

1. Create a folder: `C:\Users\YourUsername\AppData\Local\PdfTranslator\tessdata`
2. Download Russian language data:
   - Go to: https://github.com/tesseract-ocr/tessdata/raw/main/rus.traineddata
   - Save `rus.traineddata` to the tessdata folder

### 2. Configure Translation Provider

#### Option A: Microsoft Translator (Recommended - Best Free Tier)

1. Go to https://portal.azure.com
2. Create a "Translator" resource (Cognitive Services)
3. Get your API key and region from "Keys and Endpoint" section
4. In the app, click Settings and enter:
   - Azure Translator Key
   - Region (e.g., "eastus" or "global")

**Free Tier**: 2 million characters/month

#### Option B: DeepL (Best Quality)

1. Go to https://www.deepl.com/pro-api
2. Sign up for DeepL API Free
3. Get your API key
4. In the app, click Settings and enter your DeepL API Key

**Free Tier**: 500,000 characters/month

### 3. (Optional) Azure Document Intelligence for Premium OCR

1. Go to https://portal.azure.com
2. Create a "Form Recognizer" or "Document Intelligence" resource
3. Get endpoint and API key
4. In the app, click Settings and enter:
   - Endpoint (e.g., `https://yourname.cognitiveservices.azure.com/`)
   - API Key

**Free Tier**: 500 pages/month

## Usage

1. **Select PDF**: Click "Browse" to select a Russian PDF file
2. **Choose Providers**: 
   - Select translation provider (Microsoft or DeepL)
   - Select OCR engine (Tesseract for free, Azure for premium)
3. **Translate**: Click "Translate PDF" button
4. **Wait**: Progress bar shows processing status
5. **Output**: Translated PDF saved in same folder as input with "_translated" suffix

## API Costs

| Service | Free Tier | Paid Pricing |
|---------|-----------|--------------|
| Microsoft Translator | 2M chars/month | $10 per million |
| DeepL API Free | 500K chars/month | N/A |
| DeepL API Pro | None | $5.49/million + $5.99/month |
| Tesseract OCR | Unlimited | Free forever |
| Azure Document Intelligence | 500 pages/month | $1.50 per 1,000 pages |

## Current Limitations

1. **PDF Overlay Generation**: Current version exports to text file. Full PDF generation with overlay requires additional implementation with iText7 (commercial license) or similar library.
2. **Complex Layouts**: Tables, multi-column layouts, and embedded images may not preserve perfectly.
3. **OCR for Scanned PDFs**: Requires conversion of PDF pages to images (to be implemented).
4. **Font Mapping**: Some Cyrillic fonts may not have perfect equivalents in output.

## Troubleshooting

### "Tesseract is not configured"
- Ensure `rus.traineddata` is in the tessdata folder
- Check the path in Settings matches your tessdata location

### "Translation provider is not configured"
- Open Settings and enter valid API keys
- Verify your API keys are active in Azure/DeepL portal

### Translation quality issues
- Try switching providers (DeepL typically has better quality)
- Check if source PDF has clear, readable text

### Application won't start
- Verify .NET 8.0 Runtime is installed
- Check application logs at: `%AppData%\PdfTranslator\logs\app.log`

## Architecture

- **Framework**: .NET 8 WPF with MVVM pattern
- **PDF Processing**: PdfPig (Apache 2.0 license)
- **OCR**: Tesseract 5.x + OpenCvSharp for preprocessing
- **Translation**: DeepL.net SDK, Azure.AI.Translation.Text
- **UI**: Material Design In XAML Toolkit
- **Logging**: Serilog with file sink

## Project Structure

```
PdfTranslator/
├── Models/              # Data models (AppSettings, TextBlock, etc.)
├── Services/            # Business logic
│   ├── Translation/     # DeepL, Microsoft Translator providers
│   ├── OCR/             # Tesseract, Azure Document Intelligence
│   ├── PDF/             # PDF analysis, text extraction, generation
│   └── Configuration/   # Settings management
├── ViewModels/          # MVVM ViewModels
├── Views/               # WPF Windows (Main, Settings)
├── Helpers/             # Converters, utilities
└── Assets/              # Resources (will contain tessdata)
```

## Future Enhancements

- [ ] Full PDF overlay generation with preserved formatting
- [ ] PDF to image conversion for scanned PDF OCR
- [ ] Batch processing of multiple PDFs
- [ ] Side-by-side preview (original vs translated)
- [ ] Custom glossary/translation memory
- [ ] Export to DOCX for easier manual editing
- [ ] Support for more language pairs
- [ ] Offline translation with MarianMT

## License

This project uses the following open-source libraries:
- PdfPig (Apache 2.0)
- Tesseract (Apache 2.0)
- OpenCvSharp (Apache 2.0)
- Material Design In XAML (MIT)
- CommunityToolkit.Mvvm (MIT)
- Serilog (Apache 2.0)

## Support

For issues, questions, or contributions, please open an issue on the GitHub repository.

## Acknowledgments

Built for translating Russian PDF documents to English with free/low-cost cloud APIs while maintaining document formatting as much as possible.
