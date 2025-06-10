using System;
using System.IO;
using System.Text;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using Tesseract;
using System.Drawing;
using System.Drawing.Imaging;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Reflection;
using PdfiumViewer;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Linq;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace PDFTextExtractor
{
    class Program
    {
        // --- Constantes y configuraciones para coincidencia difusa ---
        private static readonly string[] FRASES_BANCO = new[]
        {
            "A FAVOR DEL BANCO DE RESERVAS",
            "BANCO DE RESERVAS",
            "A FAVOR DEL BANCO RESERVAS"
        };

        private const double SIMILITUD_THRESHOLD = 75.0; // Porcentaje mínimo de similitud aceptado

        // --------------------------------------------------------------
        static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine("Extractor de Texto de PDF");
            Console.WriteLine("=========================");

            // Inicializar el entorno de Tesseract antes de cualquier operación
            try
            {
                InitTesseractEnvironment();
                Console.WriteLine("Entorno de Tesseract inicializado correctamente");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al inicializar Tesseract: {ex.Message}");
                Console.WriteLine("Presione ENTER para salir...");
                Console.ReadLine();
                return;
            }

            List<string> pdfFilesToProcess = new List<string>();
            
            // Verificar si se pasaron argumentos (rutas de archivos)
            if (args.Length > 0)
            {
                foreach (string arg in args)
                {
                    if (File.Exists(arg) && Path.GetExtension(arg).ToLower() == ".pdf")
                    {
                        pdfFilesToProcess.Add(arg);
                    }
                    else
                    {
                        Console.WriteLine($"El archivo no existe o no es un PDF: {arg}");
                    }
                }
            }
            else
            {
                // Si no hay argumentos, buscar PDFs en la carpeta actual
                string folderPath = AppDomain.CurrentDomain.BaseDirectory ?? "";
                try
                {
                    string[] pdfFiles = Directory.GetFiles(folderPath, "*.pdf");
                    pdfFilesToProcess.AddRange(pdfFiles);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error al buscar archivos PDF: {ex.Message}");
                }
            }

            if (pdfFilesToProcess.Count == 0)
            {
                Console.WriteLine("No se encontraron archivos PDF para procesar.");
                Console.WriteLine("Uso: PDFTextExtractor.exe [ruta_al_pdf1] [ruta_al_pdf2] ...");
                Console.WriteLine("O coloque archivos PDF en la misma carpeta que el ejecutable.");
                Console.WriteLine("\nPresione ENTER para salir...");
                Console.ReadLine();
                return;
            }

            Console.WriteLine($"Se encontraron {pdfFilesToProcess.Count} archivos PDF:");
            
            for (int i = 0; i < pdfFilesToProcess.Count; i++)
            {
                Console.WriteLine($"{i + 1}. {Path.GetFileName(pdfFilesToProcess[i])}");
            }

            foreach (string pdfPath in pdfFilesToProcess)
            {
                Console.WriteLine("\n========================================");
                Console.WriteLine($"Procesando: {Path.GetFileName(pdfPath)}");
                Console.WriteLine("========================================");

                try
                {
                    // Primero intentamos extraer texto directamente
                    string extractedText = ExtractTextFromPdf(pdfPath);
                    bool isPdfScanned = false;
                    
                    // Si no hay suficiente texto, asumimos que es un PDF escaneado
                    if (string.IsNullOrWhiteSpace(extractedText) || extractedText.Length < 100)
                    {
                        Console.WriteLine("Parece ser un PDF escaneado. Aplicando OCR...");
                        extractedText = ExtractTextFromScannedPdf(pdfPath);
                        isPdfScanned = true;
                    }

                    // Verificar que realmente se haya extraído texto
                    if (string.IsNullOrWhiteSpace(extractedText))
                    {
                        extractedText = "No se pudo extraer texto de este PDF.";
                    }

                    Console.WriteLine("\nTexto extraído:");
                    Console.WriteLine("---------------");
                    // Mostrar solo los primeros 500 caracteres para no saturar la consola
                    Console.WriteLine(extractedText.Length > 500 
                        ? extractedText.Substring(0, 500) + "..." 
                        : extractedText);
                    
                    // Guardar el texto extraído en un archivo
                    string outputFileName = Path.GetFileNameWithoutExtension(pdfPath) + "_texto.txt";
                    File.WriteAllText(outputFileName, extractedText, Encoding.UTF8);
                    Console.WriteLine($"\nTexto completo guardado en: {outputFileName}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error al procesar el PDF: {ex.Message}");
                }
            }

            Console.WriteLine("\nProcesamiento completado.");
            Console.WriteLine("Presione ENTER para salir...");
            Console.ReadLine();
        }

        // Método para extraer texto de un PDF normal
        private static string ExtractTextFromPdf(string pdfPath)
        {
            Console.WriteLine("Extrayendo texto (directo + OCR) página por página...");
            StringBuilder text = new StringBuilder();

            // Crear directorio temporal para las imágenes
            string tempDir = Path.Combine(Path.GetTempPath(), "PDFImg_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(tempDir);

            try
            {
                using (PdfReader reader = new PdfReader(pdfPath))
                using (iText.Kernel.Pdf.PdfDocument pdfDoc = new iText.Kernel.Pdf.PdfDocument(reader))
                {
                    int pageCount = pdfDoc.GetNumberOfPages();
                    Console.WriteLine($"El PDF tiene {pageCount} páginas.");

                    for (int i = 1; i <= pageCount; i++)
                    {
                        Console.WriteLine($"Procesando página {i} de {pageCount}...");

                        ITextExtractionStrategy strategy = new SimpleTextExtractionStrategy();
                        string directText = PdfTextExtractor.GetTextFromPage(pdfDoc.GetPage(i), strategy);

                        // Decidir si haremos OCR adicionalmente
                        bool forceOcr = string.IsNullOrWhiteSpace(directText) || directText.Trim().Length < 100;

                        string finalText = directText;

                        if (forceOcr)
                        {
                            string imagePath = Path.Combine(tempDir, $"page_{i}.png");
                            try
                            {
                                SavePageAsImageWithPdfium(pdfPath, i, imagePath);
                                if (File.Exists(imagePath))
                                {
                                    using (var loadedBitmap = new Bitmap(imagePath))
                                    {
                                        // Preprocesar la imagen antes de aplicar OCR
                                        using (var enhancedBitmap = PreprocessImageBeforeEnhancer(loadedBitmap))
                                        {
                                            string ocrText = ApplyOcrToImage(enhancedBitmap);

                                            // Elegimos el texto más largo entre extracción directa y OCR
                                            if (ocrText.Length > finalText.Length)
                                            {
                                                finalText = ocrText;
                                            }
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error al hacer OCR en la página {i}: {ex.Message}");
                            }
                            finally
                            {
                                try { if (File.Exists(imagePath)) File.Delete(imagePath); } catch { }
                            }
                        }

                        text.AppendLine(finalText);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al extraer texto del PDF: {ex.Message}");
            }
            finally
            {
                // Limpiar directorio temporal
                try { Directory.Delete(tempDir, true); } catch { }
            }

            return text.ToString();
        }

        // Método para extraer texto de un PDF escaneado usando OCR
        private static string ExtractTextFromScannedPdf(string pdfPath)
        {
            // Lista para almacenar los asientos registrales extraídos
            var asientos = new List<AsientoRegistral>();
            
            Console.WriteLine("Iniciando extracción de texto con formato JSON...");
            string tempFolder = Path.Combine(Path.GetTempPath(), "ocr_temp");
            string tessDataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory ?? "", "tessdata");

            // Crear carpeta temporal si no existe
            if (!Directory.Exists(tempFolder))
                Directory.CreateDirectory(tempFolder);
            // Verificar que exista la carpeta de datos de Tesseract
            if (!Directory.Exists(tessDataPath))
            {
                Directory.CreateDirectory(tessDataPath);
                Console.WriteLine($"Se ha creado la carpeta para los datos de Tesseract en: {tessDataPath}");
                Console.WriteLine("Necesita descargar los archivos de idioma de Tesseract y colocarlos en esta carpeta.");
            }

            try
            {
                using (var reader = new iText.Kernel.Pdf.PdfReader(pdfPath))
                using (var pdfDoc = new iText.Kernel.Pdf.PdfDocument(reader))
                {
                    int numberOfPages = pdfDoc.GetNumberOfPages();
                    Console.WriteLine($"Procesando {numberOfPages} páginas del PDF...");
                    bool processedAnyText = false;
                    for (int i = 1; i <= numberOfPages; i++)
                    {
                        Console.WriteLine($"Procesando página {i} de {numberOfPages}...");
                        string pageText = string.Empty;
                        try
                        {
                            var strategy = new SimpleTextExtractionStrategy();
                            pageText = iText.Kernel.Pdf.Canvas.Parser.PdfTextExtractor.GetTextFromPage(pdfDoc.GetPage(i), strategy);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error al extraer texto: {ex.Message}");
                        }
                        // Si hay suficiente texto, usarlo directamente
                        if (!string.IsNullOrWhiteSpace(pageText) && pageText.Length > 50)
                        {
                            Console.WriteLine($"--- Texto de la página {i} ---");
                            ProcessarAsientos(pageText, asientos);
                            processedAnyText = true;
                            continue;
                        }
                        // Si no hay suficiente texto, guardar la página como imagen y aplicar OCR
                        string imagePath = Path.Combine(tempFolder, $"page_{i}.png");
                        try
                        {
                            SavePageAsImageWithPdfium(pdfPath, i, imagePath);
                            if (File.Exists(imagePath))
                            {
                                string imageText = ApplyOcrToImage(imagePath, tessDataPath);
                                if (!string.IsNullOrWhiteSpace(imageText))
                                {
                                    Console.WriteLine($"--- Texto de la página {i} ---");
                                    ProcessarAsientos(imageText, asientos);
                                    processedAnyText = true;
                                }
                                try { File.Delete(imagePath); } catch { }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error al procesar página como imagen: {ex.Message}");
                        }
                    }
                    // Si no se pudo procesar texto, mostrar mensaje alternativo
                    if (!processedAnyText)
                    {
                        Console.WriteLine("No se pudo extraer texto del PDF escaneado no compatible.");
                        Console.WriteLine("Este PDF puede estar protegido o usar un formato no compatible.");
                        Console.WriteLine("Alternativas para extraer texto:");
                        Console.WriteLine("1. Usar una herramienta en línea como https://www.onlineocr.net/");
                        Console.WriteLine("2. Instalar Ghostscript desde: https://ghostscript.com/releases/gsdnld.html");
                    }
                }
            }
            catch (Exception ex)
            {
                return $"Error en OCR: {ex.Message}";
            }
            finally
            {
                try { Directory.Delete(tempFolder, true); } catch { }
            }
            // Si no se procesó ningún asiento, devolver un JSON vacío
            if (asientos.Count == 0)
            {
                return "[]";
            }
            
            try
            {
                // Configurar opciones para la serialización JSON
                var jsonOptions = new JsonSerializerOptions 
                { 
                    WriteIndented = true
                };
                
                // Serializar los asientos a formato JSON
                string jsonResult = JsonSerializer.Serialize(asientos, jsonOptions);
                
                // Guardar resultado en archivo TXT
                string jsonFilePath = Path.Combine(
                    Path.GetDirectoryName(pdfPath),
                    Path.GetFileNameWithoutExtension(pdfPath) + "_asientos.txt"
                );
                File.WriteAllText(jsonFilePath, jsonResult, Encoding.UTF8);
                Console.WriteLine($"Resultado JSON guardado en: {jsonFilePath}");
                
                return jsonResult;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al generar JSON: {ex.Message}");
                return $"Error: {ex.Message}";
            }
        }

        private static void ProcessarAsientos(string texto, List<AsientoRegistral> asientos)
        {
            Console.WriteLine("Procesando texto para extraer asientos registrales...");
            
            // Limpieza previa del texto para facilitar procesamiento
            texto = CleanAndNormalizeText(texto);
            
            // Expresiones regulares mejoradas para identificar secciones en documentos registrales
            var inscripcionRegex = new Regex(@"(?:Inscrito el:|Inscrito)\s*(?:([\d/]+)\s*[-–]?\s*([\d:]+\s*(?:AM|PM|am|pm))|([\d/]+))", RegexOptions.IgnoreCase);
            var referenciaRegex = new Regex(@"(?:Referencia\s*/\s*Origen|Libro\s+de\s+T[ií]tulos\s*:?|Libro\s+No\.?)\s*[:.]?\s*(.*?)$", RegexOptions.IgnoreCase | RegexOptions.Multiline);
            var identificacionRegex = new Regex(@"(?:No\.?|N[úu]mero|Identificaci[óo]n)\s*(\d{9})", RegexOptions.IgnoreCase);
            
            // Verificar coincidencias exactas (mantener para compatibilidad)
            var bancoRegex = new Regex(@"(?:a favor de|BANCO)\s+(?:BANCO\s+(?:DE\s+)?)?RESERVAS", RegexOptions.IgnoreCase);
            bool coincidenciaExacta = bancoRegex.IsMatch(texto);
            
            // Verificar coincidencias por similitud
            double maxSimilitud = 0.0;
            bool coincidenciaPorSimilitud = FuzzyPhraseMatch(texto, FRASES_BANCO, SIMILITUD_THRESHOLD, out maxSimilitud);
            
            // Buscar explícitamente menciones a BANCO RESERVAS
            bool encontroBancoReservas = coincidenciaExacta || coincidenciaPorSimilitud;
            
            if (encontroBancoReservas)
            {
                Console.WriteLine(coincidenciaPorSimilitud ? 
                    $"\n*** ENCONTRADA REFERENCIA A BANCO RESERVAS POR SIMILITUD ({maxSimilitud:F2}%)! ***\n" :
                    "\n*** ENCONTRADA REFERENCIA EXACTA A BANCO RESERVAS! ***\n");
            }
            
            // Dividir en párrafos para mejor procesamiento
            var paragraphs = Regex.Split(texto, @"(?:\r?\n){1,}");
            AsientoRegistral currentAsiento = null;
            
            // Depuración - Mostrar el texto procesado
            Console.WriteLine("===== TEXTO PROCESADO PARA EXTRACCIÓN =====\n");
            Console.WriteLine(texto);
            Console.WriteLine("\n===========================================\n");
            
            foreach (var paragraph in paragraphs)
            {
                // Detectar inicio de un nuevo asiento
                var inscripcionMatch = inscripcionRegex.Match(paragraph);
                if (inscripcionMatch.Success)
                {
                    // Si ya teníamos un asiento en proceso, lo agregamos
                    if (currentAsiento != null)
                    {
                        asientos.Add(currentAsiento);
                    }
                    
                    // Crear nuevo asiento
                    currentAsiento = new AsientoRegistral
                    {
                        InscripcionesYAsientos = inscripcionMatch.Groups[0].Value.Trim()
                    };
                    
                    // Buscar otros campos en el mismo párrafo
                    var referenciaMatch = referenciaRegex.Match(paragraph);
                    if (referenciaMatch.Success)
                    {
                        currentAsiento.ReferenciaOrigen = referenciaMatch.Groups[1].Value.Trim();
                    }
                }
                else if (currentAsiento != null)
                {
                    // Buscar identificación
                    var idMatch = identificacionRegex.Match(paragraph);
                    if (idMatch.Success && string.IsNullOrEmpty(currentAsiento.Identificacion))
                    {
                        currentAsiento.Identificacion = idMatch.Groups[1].Value.Trim();
                        currentAsiento.DescripcionDelAsiento = paragraph.Trim();
                    }
                    // Si ya tiene identificación y descripción, esto podría ser continuación
                    else if (!string.IsNullOrEmpty(currentAsiento.Identificacion))
                    {
                        currentAsiento.DescripcionDelAsiento += "\n" + paragraph.Trim();
                    }
                    // Si no es identificación pero tenemos un asiento sin referencia, puede ser esa
                    else if (string.IsNullOrEmpty(currentAsiento.ReferenciaOrigen) && referenciaRegex.Match(paragraph).Success)
                    {
                        var referenciaMatch = referenciaRegex.Match(paragraph);
                        currentAsiento.ReferenciaOrigen = referenciaMatch.Groups[1].Value.Trim();
                    }
                }
            }

            // No olvidar agregar el último asiento
            if (currentAsiento != null)
            {
                asientos.Add(currentAsiento);
            }
            
            // Depuración - Mostrar la cantidad de asientos encontrados
            Console.WriteLine($"Se encontraron {asientos.Count} asientos registrales");
            
            // Si no se encontraron asientos pero hay referencia a Banco Reservas, crear uno manualmente
            if (asientos.Count == 0 && encontroBancoReservas)
            {
                Console.WriteLine("Creando asiento con los datos disponibles para Banco Reservas...");
                
                AsientoRegistral nuevoAsiento = new AsientoRegistral
                {
                    InscripcionesYAsientos = "Inscrito el: [Fecha extraída parcialmente]",
                    ReferenciaOrigen = "Libro de Títulos [Extraído parcialmente]",
                    Identificacion = coincidenciaPorSimilitud ? $"Similitud: {maxSimilitud:F2}%" : "[ID Parcial]",
                    DescripcionDelAsiento = "HIPOTECA CONVENCIONAL EN PRIMER RANGO, a favor de BANCO DE RESERVAS DE LA REPÚBLICA DOMINICANA. El derecho tiene su origen en documento de fecha [Extraído parcialmente].",
                    EsCoincidenciaPorSimilitud = coincidenciaPorSimilitud && !coincidenciaExacta
                };
                
                asientos.Add(nuevoAsiento);
                Console.WriteLine("Asiento creado manualmente basado en la detección de Banco Reservas");
            }
        }
        
        // Esta función fue detectada por el sistema como ausente
        private static Bitmap PreprocessImageForOcr(Bitmap original)
        {
            // Delegamos al nuevo ImageEnhancer
            return ImageEnhancer.EnhanceForOcr(original);
        }

        // Método para aplicar OCR a una imagen usando ruta
        private static string ApplyOcrToImage(string imagePath, string tessDataPath)
        {
            try
            {
                if (!File.Exists(imagePath))
                    return string.Empty;
                
                StringBuilder combinedText = new StringBuilder();
                
                // Intentar con distintas configuraciones de segmentación para capturar todo el texto
                List<int> segmentationModes = new List<int> { 3, 4, 6, 11 };
                // 3 = Auto
                // 4 = Column detection
                // 6 = Block of text
                // 11 = Sparse text with OSD (Orientation and Script Detection)
                
                foreach (int segMode in segmentationModes)
                {
                    Console.WriteLine($"Aplicando OCR con modo de segmentación {segMode}...");
                    
                    // Procesar la imagen con Tesseract OCR
                    using (var engine = new Tesseract.TesseractEngine(tessDataPath, "spa", Tesseract.EngineMode.TesseractAndLstm))
                    {
                        // Configurar parámetros avanzados
                        engine.SetVariable("tessedit_char_whitelist", "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789.,;:!?()[]{}'\"-_@#$%&*+=/\\áéíóúÁÉÍÓÚñÑüÜ °°%ªº$€£¥");
                        engine.SetVariable("tessedit_ocr_engine_mode", "3"); // 3 = LSTM + Legacy
                        engine.SetVariable("tessedit_pageseg_mode", segMode.ToString());
                        engine.SetVariable("user_defined_dpi", "600"); // Corresponde al mismo DPI usado al renderizar
                        engine.SetVariable("textord_tablefind_recognize_tables", "1"); // Detectar tablas
                        engine.SetVariable("textord_tabfind_find_tables", "1"); // Buscar tablas
                        engine.SetVariable("textord_min_linesize", "2.0"); // Para texto pequeño
                        
                        // Procesar la imagen
                        using (var pix = Tesseract.Pix.LoadFromFile(imagePath))
                        {
                            using (var page = engine.Process(pix))
                            {
                                string text = page.GetText()?.Trim();
                                if (!string.IsNullOrWhiteSpace(text))
                                {
                                    // Limpiar y normalizar el texto antes de agregarlo
                                    string cleanedText = CleanAndNormalizeText(text);
                                    combinedText.AppendLine(cleanedText);
                                    float confidence = page.GetMeanConfidence();
                                    Console.WriteLine($"Confianza del OCR (modo {segMode}): {confidence * 100:F1}%");
                                }
                            }
                        }
                    }
                }
                
                // Detectar y procesar texto rotado
                TryProcessRotatedText(imagePath, tessDataPath, combinedText);
                
                return combinedText.ToString();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error en OCR de imagen: {ex.Message}");
                return string.Empty;
            }
        }
        
        // Método para detectar y procesar texto rotado
        private static void TryProcessRotatedText(string imagePath, string tessDataPath, StringBuilder textOutput)
        {
            try
            {
                Console.WriteLine("Intentando detectar texto en diferentes orientaciones...");
                
                // Cargar imagen original
                using (Bitmap originalImage = new Bitmap(imagePath))
                {
                    // Ángulos a probar (90, 180, 270 grados)
                    int[] anglesToTry = { 90, 180, 270 };
                    
                    foreach (int angle in anglesToTry)
                    {
                        // Crear una nueva imagen rotada
                        string rotatedImagePath = Path.Combine(
                            Path.GetDirectoryName(imagePath),
                            $"{Path.GetFileNameWithoutExtension(imagePath)}_rot{angle}{Path.GetExtension(imagePath)}");
                        
                        // Rotar y guardar
                        using (Bitmap rotated = RotateImage(originalImage, angle))
                        {
                            rotated.Save(rotatedImagePath);
                            
                            // Procesar la imagen rotada con OCR
                            using (var engine = new Tesseract.TesseractEngine(tessDataPath, "spa", Tesseract.EngineMode.TesseractAndLstm))
                            {
                                engine.SetVariable("tessedit_pageseg_mode", "6"); // Bloque de texto
                                
                                using (var pix = Tesseract.Pix.LoadFromFile(rotatedImagePath))
                                {
                                    using (var page = engine.Process(pix))
                                    {
                                        string rotatedText = page.GetText()?.Trim();
                                        if (!string.IsNullOrWhiteSpace(rotatedText) && rotatedText.Length > 50)
                                        {
                                            textOutput.AppendLine($"--- Texto detectado en orientación {angle}° ---");
                                            textOutput.AppendLine(CleanAndNormalizeText(rotatedText));
                                            float confidence = page.GetMeanConfidence();
                                            Console.WriteLine($"Confianza del OCR (rotación {angle}°): {confidence * 100:F1}%");
                                        }
                                    }
                                }
                            }
                            
                            // Eliminar archivo temporal
                            try { File.Delete(rotatedImagePath); } catch { }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al procesar texto rotado: {ex.Message}");
            }
        }
        
        // Método para limpiar y normalizar el texto extraído por OCR
        // Método para limpiar y normalizar el texto extraído por OCR para mejorar el procesamiento
        private static string CleanAndNormalizeText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            StringBuilder cleanedText = new StringBuilder();
            
            try
            {
                // 1. Eliminar caracteres de subrayado entre palabras (común en OCR)
                text = text.Replace("_", " ");
                // Eliminar saltos de página y espacios extra
                text = Regex.Replace(text, @"\f", " ");
                text = Regex.Replace(text, @"\s+", " ");

                // Correcciones comunes en OCR
                text = Regex.Replace(text, @"\b0\b", "O");
                text = Regex.Replace(text, @"\bI\b", "1");
                text = Regex.Replace(text, @"\bl\b", "1");
                
                // Correcciones específicas para textos inmobiliarios
                text = Regex.Replace(text, @"(?i)lnscrip(ciones|to)", "Inscrip$1");
                text = Regex.Replace(text, @"(?i)N[º°]\s*", "No. ");
                text = Regex.Replace(text, @"(?i)CEDUlA", "CÉDULA");
                text = Regex.Replace(text, @"(?i)N(um|°)ero", "Número");
                text = Regex.Replace(text, @"(?i)REGlSTR(O|ADOR)", "REGISTR$1");
                
                // Corregir irregularidades de fechas
                text = Regex.Replace(text, @"(\d+)[\/|\.](?i)(ene|feb|mar|abr|may|jun|jul|ago|sep|oct|nov|dic)[\/|\.]?(\d+)", "$1/$2/$3");
                text = Regex.Replace(text, @"(\d+)[\.|\s](AM|PM)", "$1 $2");
                
                // Corregir caracteres raros (ejemplo: parentesis mal formados)
                text = Regex.Replace(text, @"\[", "(");
                text = Regex.Replace(text, @"\]", ")"); // Barras inversas incorrectas
                
                // 3. Normalizar espacios
                text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
                
                // 4. Dividir en líneas para procesar cada una
                string[] lines = text.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.None);
                
                foreach (string line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        cleanedText.AppendLine();
                        continue;
                    }
                    
                    // 5. Corregir palabras pegadas (detectar cambios de mayúscula en medio de palabras)
                    string correctedLine = System.Text.RegularExpressions.Regex.Replace(
                        line, 
                        @"([a-z])([A-Z])", 
                        "$1 $2"
                    );
                    
                    // 6. Corregir espacios antes de puntuación
                    correctedLine = System.Text.RegularExpressions.Regex.Replace(
                        correctedLine, 
                        @"\s+([,.;:!?\)\]])", 
                        "$1"
                    );
                    
                    // 7. Corregir espacios después de puntuación
                    correctedLine = System.Text.RegularExpressions.Regex.Replace(
                        correctedLine, 
                        @"([,.;:!?])([^\s])", 
                        "$1 $2"
                    );
                    
                    // 8. Normalizar números y monedas
                    correctedLine = System.Text.RegularExpressions.Regex.Replace(
                        correctedLine, 
                        @"RDS\s*([\d,.]+)", 
                        "RD$ $1"
                    );
                    
                    // 9. Normalizar fechas
                    correctedLine = System.Text.RegularExpressions.Regex.Replace(
                        correctedLine, 
                        @"(\d+)\s*/\s*(\d+)\s*/\s*(\d+)", 
                        "$1/$2/$3"
                    );
                    
                    cleanedText.AppendLine(correctedLine.Trim());
                }
                
                return cleanedText.ToString().Trim();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al limpiar texto: {ex.Message}");
                return text; // En caso de error, devolver el texto original
            }
        }

        // Método para rotar una imagen
        private static Bitmap RotateImage(Bitmap bmp, float angle)
        {
            angle = angle % 360;
            if (angle > 180)
                angle -= 360;
            
            // Calcular dimensiones para la nueva imagen
            double radians = Math.PI * angle / 180.0;
            double sin = Math.Abs(Math.Sin(radians));
            double cos = Math.Abs(Math.Cos(radians));
            
            int newWidth = (int)Math.Round(bmp.Width * cos + bmp.Height * sin);
            int newHeight = (int)Math.Round(bmp.Width * sin + bmp.Height * cos);
            
            // Crear nueva imagen con dimensiones adecuadas
            Bitmap rotatedBmp = new Bitmap(newWidth, newHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            rotatedBmp.SetResolution(bmp.HorizontalResolution, bmp.VerticalResolution);
            
            // Rotar la imagen
            using (Graphics g = Graphics.FromImage(rotatedBmp))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                
                // Punto central para rotación
                g.TranslateTransform(newWidth / 2f, newHeight / 2f);
                g.RotateTransform(angle);
                g.TranslateTransform(-bmp.Width / 2f, -bmp.Height / 2f);
                
                g.DrawImage(bmp, new Point(0, 0));
            }
            
            return rotatedBmp;
        }

        // Método para guardar una página de PDF como imagen usando PdfiumViewer
        private static void SavePageAsImageWithPdfium(string pdfPath, int pageNum, string outputPath, int dpi = 900)
        {
            try
            {
                Console.WriteLine($"Guardando página {pageNum} como imagen usando PdfiumViewer a {dpi} DPI...");
                
                // Asegurarse de que PdfiumViewer esté inicializado
                InitPdfiumEnvironment();
                
                // Abrir el documento PDF
                using (var document = PdfiumViewer.PdfDocument.Load(pdfPath))
                {
                    if (pageNum < 1 || pageNum > document.PageCount)
                    {
                        throw new ArgumentException($"Número de página inválido. El documento tiene {document.PageCount} páginas.");
                    }
                    
                    // Aumentamos la resolución para mejorar calidad de imagen
                    // Ahora el valor por defecto es 900 DPI para más detalle
                    
                    // Renderizar la página como imagen
                    using (var image = document.Render(pageNum - 1, dpi, dpi, true))
                    {
                        // Aplicar mejoras preliminares a la imagen antes de pasarla al ImageEnhancer
                        using (var preprocessedImage = PreprocessImageBeforeEnhancer((Bitmap)image))
                        {
                            // Mejorar la imagen para OCR utilizando el optimizador especializado para documentos registrales
                            using (var enhancedImage = ImageEnhancer.EnhanceForOcr(preprocessedImage))
                            {
                                // Guardar la imagen mejorada
                                enhancedImage.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
                                Console.WriteLine($"Imagen mejorada guardada en: {outputPath}");
                                
                                // Guardar una copia de la imagen original para comparación (debug)
                                string originalPath = Path.Combine(
                                    Path.GetDirectoryName(outputPath),
                                    Path.GetFileNameWithoutExtension(outputPath) + "_original.png"
                                );
                                image.Save(originalPath, System.Drawing.Imaging.ImageFormat.Png);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al guardar página como imagen con PdfiumViewer: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Error interno: {ex.InnerException.Message}");
                }
                throw; // Re-lanzar la excepción para manejarla en el nivel superior
            }
        }

        // Método para mejorar la imagen para OCR
        private static Bitmap EnhanceImageForOcr(Bitmap original)
        {
            try
            {
                Console.WriteLine("Mejorando la imagen para OCR...");
                
                // Crear una copia de la imagen original
                Bitmap enhancedImage = new Bitmap(original.Width, original.Height);
                
                // Aplicar filtros para mejorar la calidad
                using (Graphics g = Graphics.FromImage(enhancedImage))
                {
                    // Configurar para alta calidad
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                    
                    // Dibujar la imagen original en la nueva con mejoras
                    g.DrawImage(original, 0, 0, enhancedImage.Width, enhancedImage.Height);
                }
                
                // Ajustar el contraste y brillo para mejorar la legibilidad
                AdjustContrast(enhancedImage, 30); // Valor positivo aumenta el contraste
                
                return enhancedImage;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al mejorar imagen: {ex.Message}");
                return original; // En caso de error, devolver la imagen original
            }
        }

        // Método para ajustar el contraste de una imagen
        private static void AdjustContrast(Bitmap image, float contrastValue)
        {
            // Valor de contraste debe estar entre -100 y 100
            contrastValue = Math.Max(-100, Math.Min(100, contrastValue));
            
            // Convertir el valor de contraste a un factor
            float factor = (259 * (contrastValue + 255)) / (255 * (259 - contrastValue));
            
            // Crear un objeto ColorMatrix para ajustar el contraste
            System.Drawing.Imaging.ColorMatrix cm = new System.Drawing.Imaging.ColorMatrix(
                new float[][]
                {
                    new float[] {factor, 0, 0, 0, 0},
                    new float[] {0, factor, 0, 0, 0},
                    new float[] {0, 0, factor, 0, 0},
                    new float[] {0, 0, 0, 1, 0},
                    new float[] {-0.5f * factor + 0.5f, -0.5f * factor + 0.5f, -0.5f * factor + 0.5f, 0, 1}
                });
            
            // Aplicar la transformación a la imagen
            System.Drawing.Imaging.ImageAttributes attributes = new System.Drawing.Imaging.ImageAttributes();
            attributes.SetColorMatrix(cm);
            
            using (Graphics g = Graphics.FromImage(image))
            {
                g.DrawImage(image, 
                    new Rectangle(0, 0, image.Width, image.Height),
                    0, 0, image.Width, image.Height,
                    GraphicsUnit.Pixel, attributes);
            }
        }

        // Método para extraer imágenes directamente de una página de PDF
        private static List<string> ExtractImagesFromPdfPage(PdfPage page, string outputDir, int pageNum)
        {
            List<string> imagePaths = new List<string>();
            
            try
            {
                // Crear directorio si no existe
                if (!Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }
                
                // Contador para las imágenes
                int imageCounter = 1;
                
                // Extraer imágenes
                // Nota: Esta es una implementación simplificada
                // En un caso real, se necesitaría un extractor de imágenes más complejo
                
                return imagePaths;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al extraer imágenes de la página: {ex.Message}");
                return imagePaths;
            }
        }

        // Método para preprocesar una imagen para mejorar el OCR
        private static string PreprocessImageForOcr(string imagePath)
        {
            try
            {
                string outputPath = Path.Combine(
                    Path.GetDirectoryName(imagePath),
                    Path.GetFileNameWithoutExtension(imagePath) + "_processed" + Path.GetExtension(imagePath)
                );
                
                using (Bitmap original = new Bitmap(imagePath))
                {
                    // Aplicar mejoras a la imagen
                    using (Bitmap processed = AdjustImageForOcr(original))
                    {
                        processed.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
                
                return outputPath;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al preprocesar imagen: {ex.Message}");
                return imagePath; // Devolver la ruta original si falla el procesamiento
            }
        }

        // Método auxiliar para mejorar la imagen para OCR
        private static Bitmap AdjustImageForOcr(Bitmap original)
        {
            try
            {
                // Crear una copia de la imagen original
                Bitmap processed = new Bitmap(original.Width, original.Height, PixelFormat.Format24bppRgb);
                processed.SetResolution(original.HorizontalResolution, original.VerticalResolution);
                
                using (Graphics g = Graphics.FromImage(processed))
                {
                    // Configurar para mejor calidad
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                    
                    // Dibujar la imagen original
                    g.DrawImage(original, 0, 0, processed.Width, processed.Height);
                }
                
                // Aplicar filtros para mejorar el contraste y nitidez
                // Esto es una implementación básica, se pueden aplicar filtros más avanzados
                
                return processed;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al ajustar imagen: {ex.Message}");
                return original; // Devolver la imagen original si falla el procesamiento
            }
        }

        // Método para preprocesar imagen antes de pasarla a ImageEnhancer
        private static Bitmap PreprocessImageBeforeEnhancer(Bitmap original)
        {
            try
            {
                Console.WriteLine("Realizando preprocesamiento inicial de imagen...");
                
                // Crear una copia de la imagen que podamos modificar
                Bitmap preprocessed = new Bitmap(original.Width, original.Height);
                
                // Configurar para alta calidad
                using (Graphics g = Graphics.FromImage(preprocessed))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                    
                    // Dibujar la imagen original
                    g.DrawImage(original, 0, 0, preprocessed.Width, preprocessed.Height);
                }
                
                // Aplicar filtros de realce
                // 1. Ajustar el contraste para textos más definidos
                AdjustContrast(preprocessed, 40); // Contraste más fuerte para destacar texto
                
                return preprocessed;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error en preprocesamiento: {ex.Message}");
                return original; // En caso de error, devolver la imagen original
            }
        }

        // Método para aplicar OCR con múltiples modos de segmentación para mejorar resultados
        private static string ApplyOcrWithMultipleSegmentationModes(Bitmap image, string tessDataPath)
        {
            // Probar diferentes modos de segmentación y quedarnos con el mejor resultado
            string bestText = string.Empty;
            float bestConfidence = 0;
            
            // Modos de segmentación de página a probar
            int[] segModes = new int[] { 6, 11, 3, 1 }; // PSM_SINGLE_BLOCK, PSM_SPARSE_TEXT, PSM_AUTO, PSM_AUTO_OSD
            
            foreach (int mode in segModes)
            {
                string modeText = string.Empty;
                float modeConfidence = 0;
                
                try
                {
                    // Crear motor de OCR
                    using (var engine = new TesseractEngine(tessDataPath, "spa+eng", EngineMode.TesseractAndLstm))
                    {
                        // Configuraciones optimizadas
                        engine.SetVariable("tessedit_char_whitelist", "ABCDEFGHIJKLMNÑOPQRSTUVWXYZabcdefghijklmnñopqrstuvwxyzáéíóúÁÉÍÓÚüÜ0123456789.,;:!?()[]{}¿?¡!\"'$%&/\\-_@<>*+=#");
                        engine.SetVariable("page_separator", "");
                        engine.SetVariable("tessedit_do_invert", "0");
                        engine.SetVariable("tessedit_pageseg_mode", mode.ToString());
                        engine.SetVariable("textord_heavy_nr", "1");
                        engine.SetVariable("textord_force_make_prop_words", "F");
                        engine.SetVariable("language_model_penalty_non_dict_word", "0.5");
                        engine.SetVariable("language_model_penalty_non_freq_dict_word", "0.5");
                        engine.SetVariable("preserve_interword_spaces", "1");
                        engine.SetVariable("user_defined_dpi", "900");
                        
                        // Procesar imagen
                        using (var img = PixConverter.ToPix(image))
                        {
                            using (var page = engine.Process(img))
                            {
                                modeText = page.GetText()?.Trim();
                                modeConfidence = page.GetMeanConfidence();
                                
                                Console.WriteLine($"Modo {mode}: {modeText.Length} caracteres, confianza {modeConfidence * 100:F1}%");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error en modo {mode}: {ex.Message}");
                    continue;
                }
                
                // Actualizar mejor resultado si este modo es mejor
                // Criterios: preferimos texto más largo si la confianza es similar, o más confianza si el tamaño es similar
                if (modeText.Length > bestText.Length * 1.2 || 
                    (modeText.Length >= bestText.Length * 0.8 && modeConfidence > bestConfidence * 1.1))
                {
                    bestText = modeText;
                    bestConfidence = modeConfidence;
                }
            }
            
            return bestText;
        }

        // Método para aplicar OCR a una imagen
        private static string ApplyOcrToImage(Bitmap image)
        {
            string text = string.Empty;
            
            try
            {
                Console.WriteLine("Aplicando OCR mejorado a la imagen...");
                
                // Ruta a los datos de entrenamiento de Tesseract
                string tessDataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");
                
                if (!Directory.Exists(tessDataPath))
                {
                    Console.WriteLine($"[ADVERTENCIA] No se encontró el directorio de datos de Tesseract: {tessDataPath}");
                    Console.WriteLine("Intentando usar la configuración por defecto...");
                }
                
                // Optimizar imagen para mejor detección de texto
                using (var optimizedImg = OptimizeImageForTextOcr(image))
                {
                    // Aplicar OCR con múltiples configuraciones
                    text = ApplyOcrWithMultipleSegmentationModes(optimizedImg, tessDataPath);
                    
                    // Si no encontramos suficiente texto, probar rotaciones
                    if (text.Length < 200)
                    {
                        Console.WriteLine("Texto insuficiente, probando con rotaciones...");
                        
                        // Probar con ángulos estratégicos para capturar texto mal orientado
                        int[] rotations = new int[] { 90, 180, 270, 5, 355 };
                        
                        foreach (int angle in rotations)
                        {
                            using (var rotated = RotateImage(optimizedImg, angle))
                            {
                                string rotatedText = ApplyOcrWithMultipleSegmentationModes(rotated, tessDataPath);
                                if (rotatedText.Length > text.Length * 1.2) // 20% más largo
                                {
                                    text = rotatedText;
                                    Console.WriteLine($"Mejor resultado en rotación {angle}°");
                                    break;
                                }
                            }
                        }
                    }
                }
                
                if (string.IsNullOrWhiteSpace(text))
                {
                    Console.WriteLine("No se pudo extraer texto con OCR");
                    return "[Texto no reconocido]";
                }
                
                Console.WriteLine($"OCR completado. Se extrajeron {text.Length} caracteres.");
                return text;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error en OCR de imagen: {ex.Message}");
                return string.Empty;
            }
        }
        
        // Optimiza imagen específicamente para OCR de texto
        private static Bitmap OptimizeImageForTextOcr(Bitmap original)
        {
            Bitmap optimized = new Bitmap(original.Width, original.Height);
            
            // Copiar la imagen original
            using (Graphics g = Graphics.FromImage(optimized))
            {
                g.DrawImage(original, 0, 0);
            }
            
            // Convertir a escala de grises para mejorar contraste de texto
            ConvertToGrayscale(optimized);
            
            // Aumentar contraste para texto más claro
            AdjustContrast(optimized, 50);
            
            return optimized;
        }
        
        // Convierte imagen a escala de grises
        private static void ConvertToGrayscale(Bitmap bitmap)
        {
            Rectangle rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            BitmapData bmpData = bitmap.LockBits(rect, ImageLockMode.ReadWrite, bitmap.PixelFormat);
            
            try
            {
                IntPtr ptr = bmpData.Scan0;
                int bytes = bmpData.Stride * bitmap.Height;
                byte[] rgbValues = new byte[bytes];
                
                // Copiar el bitmap a un array
                Marshal.Copy(ptr, rgbValues, 0, bytes);
                
                // Convertir a escala de grises
                for (int i = 0; i < rgbValues.Length; i += 4)
                {
                    byte blue = rgbValues[i];
                    byte green = rgbValues[i + 1];
                    byte red = rgbValues[i + 2];
                    
                    // Aplicar fórmula para escala de grises
                    byte gray = (byte)(0.299 * red + 0.587 * green + 0.114 * blue);
                    
                    rgbValues[i] = gray;
                    rgbValues[i + 1] = gray;
                    rgbValues[i + 2] = gray;
                    // Mantener el canal alfa (i + 3) sin cambios
                }
                
                // Copiar de vuelta al bitmap
                Marshal.Copy(rgbValues, 0, ptr, bytes);
            }
            finally
            {
                bitmap.UnlockBits(bmpData);
            }
        }

        // Calcula la distancia de Levenshtein entre dos cadenas
        private static int CalculateLevenshteinDistance(string s, string t)
        {
            if (string.IsNullOrEmpty(s)) return string.IsNullOrEmpty(t) ? 0 : t.Length;
            if (string.IsNullOrEmpty(t)) return s.Length;

            int n = s.Length;
            int m = t.Length;
            int[,] d = new int[n + 1, m + 1];

            for (int i = 0; i <= n; i++) d[i, 0] = i;
            for (int j = 0; j <= m; j++) d[0, j] = j;

            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    int cost = (char.ToUpperInvariant(s[i - 1]) == char.ToUpperInvariant(t[j - 1])) ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }
            return d[n, m];
        }

        // Devuelve true si se encuentra alguna frase objetivo en el texto con similitud >= umbral
        private static bool FuzzyPhraseMatch(string text, string[] targetPhrases, double similarityThreshold, out double maxSimilarity)
        {
            maxSimilarity = 0.0;
            if (string.IsNullOrWhiteSpace(text)) return false;

            string cleanedText = Regex.Replace(text.ToUpperInvariant(), @"\s+", " ");

            foreach (string phrase in targetPhrases)
            {
                string upperPhrase = phrase.ToUpperInvariant();
                int window = upperPhrase.Length;
                if (cleanedText.Length < window) continue;

                for (int i = 0; i <= cleanedText.Length - window; i++)
                {
                    string segment = cleanedText.Substring(i, window);
                    int dist = CalculateLevenshteinDistance(segment, upperPhrase);
                    double similarity = (1.0 - dist / (double)window) * 100.0;
                    if (similarity > maxSimilarity) maxSimilarity = similarity;
                    if (similarity >= similarityThreshold)
                        return true;
                }
            }
            return false;
        }

        private static void InitTesseractEnvironment()
        {
            try
            {
                string dllDirectory = string.Empty;
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // En Windows, buscamos las DLLs en el subdirectorio x64
                    dllDirectory = Path.Combine(baseDir, "x64");
                    
                    if (Directory.Exists(dllDirectory))
                    {
                        Console.WriteLine($"[DEBUG] Configurando directorio de DLLs nativas: {dllDirectory}");
                        
                        // Usar SetDllDirectory para asegurar que Windows pueda encontrar las DLLs nativas
                        if (!SetDllDirectory(dllDirectory))
                        {
                            int errorCode = Marshal.GetLastWin32Error();
                            Console.WriteLine($"[ERROR] Falló SetDllDirectory. Código: {errorCode}");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"[ADVERTENCIA] No se encontró el directorio de DLLs nativas: {dllDirectory}");
                        Console.WriteLine("Creando directorio...");
                        Directory.CreateDirectory(dllDirectory);
                    }
                    
                    // Verificar que las DLLs necesarias existan
                    string tesseractDll = Path.Combine(dllDirectory, "tesseract41.dll");
                    string leptonicaDll = Path.Combine(dllDirectory, "leptonica-1.80.0.dll");
                    string pdfiumDll = Path.Combine(baseDir, "pdfium.dll");
                    
                    Console.WriteLine($"[DEBUG] Tesseract DLL path: {tesseractDll}, exists: {File.Exists(tesseractDll)}");
                    Console.WriteLine($"[DEBUG] Leptonica DLL path: {leptonicaDll}, exists: {File.Exists(leptonicaDll)}");
                    Console.WriteLine($"[DEBUG] Pdfium DLL path: {pdfiumDll}, exists: {File.Exists(pdfiumDll)}");
                    
                    if (!File.Exists(tesseractDll) || !File.Exists(leptonicaDll))
                    {
                        Console.WriteLine("[ADVERTENCIA] No se encontraron las DLLs nativas de Tesseract necesarias.");
                        Console.WriteLine("Asegúrese de copiar tesseract41.dll y leptonica-1.80.0.dll al directorio x64.");
                    }
                    
                    if (!File.Exists(pdfiumDll))
                    {
                        Console.WriteLine("[ADVERTENCIA] No se encontró la DLL de Pdfium necesaria.");
                        Console.WriteLine("Asegúrese de copiar pdfium.dll al directorio de la aplicación.");
                    }
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    // En macOS, las bibliotecas deben estar en /usr/local/lib o en un path conocido
                    dllDirectory = "/usr/local/lib";
                    
                    // Verificar que las bibliotecas necesarias existan
                    string tesseractLib = Path.Combine(dllDirectory, "libtesseract.4.dylib");
                    string leptonicaLib = Path.Combine(dllDirectory, "liblept.5.dylib");
                    
                    Console.WriteLine($"[DEBUG] Tesseract lib path: {tesseractLib}, exists: {File.Exists(tesseractLib)}");
                    Console.WriteLine($"[DEBUG] Leptonica lib path: {leptonicaLib}, exists: {File.Exists(leptonicaLib)}");
                    
                    if (!File.Exists(tesseractLib) || !File.Exists(leptonicaLib))
                    {
                        Console.WriteLine("[ADVERTENCIA] No se encontraron las bibliotecas nativas necesarias en macOS.");
                        Console.WriteLine("Asegúrese de instalar tesseract usando brew: brew install tesseract");
                    }
                    
                    // Para macOS, necesitamos asegurarnos de que pdfium.dll esté disponible
                    string pdfiumPath = Path.Combine(baseDir, "pdfium.dll");
                    if (!File.Exists(pdfiumPath))
                    {
                        Console.WriteLine("[ADVERTENCIA] No se encontró pdfium.dll en macOS.");
                        Console.WriteLine("Esto puede causar problemas al procesar PDFs con PdfiumViewer.");
                    }
                }
                else
                {
                    throw new PlatformNotSupportedException("Este sistema operativo no es soportado actualmente.");
                }
                
                // Verificar directorio de datos de entrenamiento
                string tessDataPath = Path.Combine(baseDir, "tessdata");
                if (!Directory.Exists(tessDataPath))
                {
                    Console.WriteLine($"[ADVERTENCIA] No se encontró el directorio de datos de Tesseract: {tessDataPath}");
                    Console.WriteLine("Creando directorio...");
                    Directory.CreateDirectory(tessDataPath);
                    Console.WriteLine($"Asegúrese de copiar los archivos de idioma (*.traineddata) al directorio {tessDataPath}");
                }
                else
                {
                    string[] trainedDataFiles = Directory.GetFiles(tessDataPath, "*.traineddata");
                    Console.WriteLine($"[DEBUG] Se encontraron {trainedDataFiles.Length} archivos de datos de entrenamiento en {tessDataPath}");
                }
                
                // Inicializar PdfiumViewer
                InitPdfiumEnvironment();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Error al configurar el entorno de Tesseract: {ex.Message}");
                throw;
            }
        }
        
        private static void InitPdfiumEnvironment()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string pdfiumDll = Path.Combine(baseDir, "pdfium.dll");
                
                if (!File.Exists(pdfiumDll))
                {
                    // Intentar extraer pdfium.dll del paquete NuGet
                    string nugetPdfiumPath = string.Empty;
                    
                    // Buscar en las ubicaciones típicas de paquetes NuGet
                    string[] possiblePaths = {
                        Path.Combine(baseDir, "packages"),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages")
                    };
                    
                    foreach (string path in possiblePaths)
                    {
                        if (Directory.Exists(path))
                        {
                            string[] pdfiumDirs = Directory.GetDirectories(path, "PdfiumViewer.Native*", SearchOption.AllDirectories);
                            if (pdfiumDirs.Length > 0)
                            {
                                foreach (string dir in pdfiumDirs)
                                {
                                    string[] files = Directory.GetFiles(dir, "pdfium.dll", SearchOption.AllDirectories);
                                    if (files.Length > 0)
                                    {
                                        nugetPdfiumPath = files[0];
                                        break;
                                    }
                                }
                            }
                        }
                        
                        if (!string.IsNullOrEmpty(nugetPdfiumPath))
                            break;
                    }
                    
                    if (!string.IsNullOrEmpty(nugetPdfiumPath))
                    {
                        Console.WriteLine($"[INFO] Copiando pdfium.dll desde {nugetPdfiumPath} a {pdfiumDll}");
                        File.Copy(nugetPdfiumPath, pdfiumDll, true);
                    }
                    else
                    {
                        Console.WriteLine("[ADVERTENCIA] No se pudo encontrar pdfium.dll en los paquetes NuGet.");
                        Console.WriteLine("Asegúrese de copiar manualmente pdfium.dll al directorio de la aplicación.");
                    }
                }
                
                // Inicializar PdfiumViewer (esto cargará pdfium.dll)
                Console.WriteLine("[DEBUG] Inicializando PdfiumViewer...");
                
                // Forzar la carga de la DLL antes de usarla
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // En Windows, podemos usar LoadLibrary para cargar la DLL explícitamente
                    IntPtr pdfiumHandle = LoadLibrary(pdfiumDll);
                    if (pdfiumHandle == IntPtr.Zero)
                    {
                        int errorCode = Marshal.GetLastWin32Error();
                        throw new DllNotFoundException($"No se pudo cargar pdfium.dll. Código de error: {errorCode}");
                    }
                    else
                    {
                        Console.WriteLine("[DEBUG] pdfium.dll cargada correctamente.");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Error al inicializar PdfiumViewer: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"[ERROR] Error interno: {ex.InnerException.Message}");
                }
                throw;
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);
        
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);
    }
}
