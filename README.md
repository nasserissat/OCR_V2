# Extractor de Texto de PDF con OCR

Este proyecto es una aplicación de consola desarrollada en .NET Framework 4.7.2 que permite extraer texto de archivos PDF, incluyendo PDFs escaneados mediante OCR (Reconocimiento Óptico de Caracteres) utilizando la biblioteca Tesseract.

## Requisitos

- .NET Framework 4.7.2
- Tesseract OCR (versión 4.1.1)
- iText7 (versión 7.2.5)

## Estructura del Proyecto

El proyecto está estructurado de la siguiente manera:

```
PDFTextExtractor/
├── PDFTextExtractor.csproj    # Archivo de proyecto
├── Program.cs                 # Código fuente principal
├── tessdata/                  # Datos de entrenamiento para Tesseract
│   └── spa.traineddata        # Datos para idioma español
└── x64/                       # Bibliotecas nativas para Windows
    ├── tesseract41.dll        # Biblioteca nativa de Tesseract
    └── leptonica-1.80.0.dll   # Biblioteca nativa de Leptonica
```

## Configuración

### Windows

1. Asegúrese de tener instalado .NET Framework 4.7.2 o superior.
2. Descargue los archivos de datos de entrenamiento de Tesseract para el idioma español desde [aquí](https://github.com/tesseract-ocr/tessdata/blob/main/spa.traineddata) y colóquelos en la carpeta `tessdata`.
3. Descargue las DLLs nativas de Tesseract y Leptonica y colóquelas en la carpeta `x64`:
   - `tesseract41.dll`
   - `leptonica-1.80.0.dll`

### macOS

1. Instale Tesseract utilizando Homebrew:
   ```
   brew install tesseract
   ```
2. Descargue los archivos de datos de entrenamiento para el idioma español y colóquelos en la carpeta `tessdata`.

## Compilación

Para compilar el proyecto, utilice Visual Studio o la línea de comandos:

```
dotnet build
```

## Uso

Puede ejecutar la aplicación de las siguientes maneras:

1. Sin argumentos: la aplicación buscará archivos PDF en la carpeta actual.
   ```
   PDFTextExtractor.exe
   ```

2. Con rutas de archivos específicas:
   ```
   PDFTextExtractor.exe ruta/al/archivo1.pdf ruta/al/archivo2.pdf
   ```

La aplicación extraerá el texto de los PDFs y lo guardará en archivos de texto con el mismo nombre base.

## Solución de Problemas

### Error al cargar las DLLs nativas

Si recibe errores relacionados con la carga de las DLLs nativas de Tesseract, asegúrese de que:

1. Las DLLs estén en la carpeta `x64` dentro del directorio de la aplicación.
2. Las DLLs sean de la versión correcta (4.1.1 para Tesseract).
3. Esté utilizando la versión de 64 bits de .NET Framework.

### Problemas con el OCR

Si el OCR no reconoce correctamente el texto:

1. Asegúrese de tener los archivos de datos de entrenamiento correctos para el idioma deseado.
2. Verifique que los archivos `.traineddata` estén en la carpeta `tessdata`.
3. Pruebe con una imagen de mayor resolución o mejore la calidad del PDF escaneado.

## Notas Adicionales

- La aplicación está configurada para trabajar con documentos en español por defecto.
- Para cambiar el idioma, modifique el parámetro "spa" en la creación del motor de Tesseract en el método `ApplyOcrToImage`.
#   O C R _ V 2  
 