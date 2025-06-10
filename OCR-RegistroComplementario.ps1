<#  OCR-RegistroComplementario.ps1
    Automatiza la instalación y el OCR de PDFs con fondo verde.
#>

# 1. Instalar dependencias con Chocolatey
if (!(Get-Command choco -ErrorAction SilentlyContinue)) {
    Write-Host "Instalando Chocolatey..."
    Set-ExecutionPolicy Bypass -Scope Process -Force
    [System.Net.ServicePointManager]::SecurityProtocol = 3072
    Invoke-Expression ((New-Object System.Net.WebClient).DownloadString('https://chocolatey.org/install.ps1'))
}

choco install -y tesseract ghostscript python opencv4 ocrmypdf

# 2. Crear entorno Python y paquetes
$env:Path += ";$env:ProgramFiles\Python311\Scripts;$env:ProgramFiles\Python311\"
python -m pip install --upgrade pip
pip install pdfplumber opencv-python pillow numpy

# 3. Función: limpia verde, deskew, corre OCR
function Invoke-Ocr {
    param(
        [Parameter(Mandatory=$true)]
        [string]$PdfPath
    )

    $name = [System.IO.Path]::GetFileNameWithoutExtension($PdfPath)
    $tmp  = "$env:TEMP\$name-clean.pdf"

    # 3a. Pre-procesado y OCR de "golpe" con ocrmypdf
    ocrmypdf --remove-background `
             --deskew `
             --clean `
             --rotate-pages `
             --output-type pdf `
             "$PdfPath" "$tmp"

    # 3b. Extrae texto plano
    ocrmypdf --sidecar "$name.txt" "$tmp" "$tmp"

    Write-Host "`n✅  OCR terminado. Archivo limpio: $tmp`n   Texto: $name.txt"
}

# 4. Pregunta ruta y ejecuta
$archivo = Read-Host "Arrastra aquí tu PDF y presiona Enter"
Invoke-Ocr -PdfPath $archivo.Trim('"')
