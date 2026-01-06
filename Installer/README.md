# PDF Translator - Installer Build Guide

## Quick Build

### Option 1: Using the Build Script (Recommended)
1. Double-click `build-installer.bat` in the `Installer` folder
2. The MSI will be created at `Installer\Output\PdfTranslator-Setup.msi`

### Option 2: Manual Build

#### Prerequisites
- .NET 8 SDK installed
- WiX Toolset v6 installed (`dotnet tool install --global wix`)
- WiX UI extension (`wix extension add -g WixToolset.UI.wixext/6.0.0`)

#### Steps

1. **Publish the application:**
   ```powershell
   cd c:\Users\mVarC\source\repos\PDF-Converter\PdfTranslator
   dotnet publish -c Release -r win-x64 --self-contained true -o publish
   ```

2. **Build the MSI:**
   ```powershell
   cd c:\Users\mVarC\source\repos\PDF-Converter\Installer
   wix build Package.wxs -o Output\PdfTranslator-Setup.msi -ext WixToolset.UI.wixext
   ```

3. **Find your installer at:**
   `Installer\Output\PdfTranslator-Setup.msi`

## What's Included

The MSI installer:
- ✅ Self-contained (no .NET runtime required on target machine)
- ✅ 64-bit Windows application
- ✅ Creates Start Menu shortcut
- ✅ Creates Desktop shortcut
- ✅ Installs to `C:\Program Files\PDF Translator`
- ✅ Supports upgrade/uninstall through Windows Settings
- ✅ ~105 MB installer size

## Customization

### Change Company Name
Edit `Package.wxs` and modify the `Manufacturer` attribute:
```xml
<Package Name="PDF Translator"
         Manufacturer="Your Company Name"
         ...
```

### Change Version
Edit `Package.wxs` and modify the `Version` attribute:
```xml
<Package Name="PDF Translator"
         Version="1.0.0.0"
         ...
```

### Add License Agreement
1. Create a `License.rtf` file in the Installer folder
2. Add this line to `Package.wxs` before `</Package>`:
   ```xml
   <WixVariable Id="WixUILicenseRtf" Value="License.rtf" />
   ```

## Distribution

After building, distribute the `PdfTranslator-Setup.msi` file to your customers. They can:
1. Double-click the MSI to install
2. Use silent install: `msiexec /i PdfTranslator-Setup.msi /quiet`
3. Uninstall via Windows Settings > Apps

## Reducing Installer Size (Optional)

For a smaller installer (~20MB), use framework-dependent deployment:

```powershell
dotnet publish -c Release -r win-x64 --self-contained false -o publish
```

Note: This requires .NET 8 Desktop Runtime on the target machine.
