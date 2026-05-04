# Panel Calculator - Setup Guide

## Prerequisites
- Visual Studio 2022 (sudah terinstall ✓)
- .NET 6.0+ SDK (sudah terinstall ✓)
- Python 3.8+ (untuk PDF parser)

## Step 1: Extract Products dari PDF

### 1.1 Install Python Dependencies
```bash
pip install pdfplumber
```

### 1.2 Run PDF Parser
```bash
# Navigate ke folder Tools
cd PanelCalculator.Tools

# Run parser (ganti path ke PDF Schneider Anda)
python pdf_parser.py "C:\path\to\Final distribution product schneider.pdf" products.csv
```

Output: `products.csv` dengan ~200+ products

## Step 2: Open Solution di Visual Studio

1. Open Visual Studio 2022
2. File → Open → Folder
3. Navigate ke `C:\Projects\Panel Calculator`
4. Open `PanelCalculator.sln`

## Step 3: Seed Database dengan Products

### 3.1 Create Migration
Buka Package Manager Console (Tools → NuGet Package Manager → Package Manager Console)

```powershell
# Set default project ke PanelCalculator.Data
Set-DefaultProject PanelCalculator.Data

# Create initial migration
Add-Migration InitialCreate

# Update database
Update-Database
```

### 3.2 Import Products ke Database
Setelah migration selesai, saya akan provide C# seeding script untuk import CSV.

## Step 4: Build & Run

1. Build Solution: Ctrl+Shift+B
2. Set PanelCalculator.WinForms as startup project
3. Run: F5

## Database Location
Database file akan tersimpan di:
```
C:\Users\<YourUsername>\AppData\Roaming\PanelCalculator\PanelCalculator.db
```

Anda bisa backup folder ini untuk data safety.

## Troubleshooting

### "No .NET SDKs were found"
- Check: dotnet --version
- Solution: Reinstall .NET 6.0+ SDK dari https://dotnet.microsoft.com/download

### "pdfplumber not found"
- Jalankan: pip install pdfplumber

### Database locked error
- Close aplikasi dan retry
- Atau delete database file dan recreate

## Next Steps
- Phase 2: Build Calculator UI
- Phase 3: Estimation History & PDF Export
- Phase 4: Reports & Analytics

Questions? Check the plan: `C:\Users\FA\.claude\plans\idempotent-sprouting-thunder.md`
