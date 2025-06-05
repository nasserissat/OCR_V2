using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.IO;

namespace PDFTextExtractor
{
    /// <summary>
    /// Clase para mejorar la calidad de imagen antes del OCR
    /// Especializada en documentos registrales con marcas de agua
    /// </summary>
    public static class ImageEnhancer
    {
        /// <summary>
        /// Mejora una imagen para OCR, especialmente para documentos registrales con marcas de agua
        /// </summary>
        public static Bitmap EnhanceForOcr(Bitmap original)
        {
            Console.WriteLine("Aplicando mejoras avanzadas para documentos registrales con marcas de agua...");
            
            // Paso 1: Eliminación de marcas de agua y optimización de contraste
            Bitmap preprocessed = RemoveWatermarks(original);
            
            // Paso 2: Optimizar para detección de texto
            Bitmap enhanced = OptimizeContrast(preprocessed);
            
            // Paso 3: Aplicar suavizado selectivo para mejorar la calidad de reconocimiento
            Bitmap final = ApplySelectiveSharpening(enhanced);
            
            // Limpiar imágenes intermedias
            preprocessed.Dispose();
            enhanced.Dispose();
            
            return final;
        }
        
        /// <summary>
        /// Elimina marcas de agua basado en análisis de color
        /// </summary>
        private static Bitmap RemoveWatermarks(Bitmap original)
        {
            Bitmap result = new Bitmap(original.Width, original.Height);
            
            // Analizar la distribución de colores para detectar marcas de agua
            for (int y = 0; y < original.Height; y++)
            {
                for (int x = 0; x < original.Width; x++)
                {
                    Color pixel = original.GetPixel(x, y);
                    
                    // Detectar colores típicos de marcas de agua (tonos claros azulados, grisáceos)
                    bool isWatermark = IsLikelyWatermark(pixel);
                    
                    if (isWatermark)
                    {
                        // Eliminar la marca de agua convirtiéndola a blanco
                        result.SetPixel(x, y, Color.White);
                    }
                    else
                    {
                        // Mantener el píxel pero aumentar ligeramente el contraste
                        result.SetPixel(x, y, IncreasePixelContrast(pixel, 20));
                    }
                }
            }
            
            return result;
        }
        
        /// <summary>
        /// Determina si un color es probablemente parte de una marca de agua
        /// </summary>
        private static bool IsLikelyWatermark(Color pixel)
        {
            // Características comunes de marcas de agua:
            
            // 1. Tonos muy claros (casi blancos)
            bool isVeryLight = pixel.R > 225 && pixel.G > 225 && pixel.B > 225;
            
            // 2. Tonos azulados claros (común en sellos oficiales)
            bool isLightBlue = pixel.B > pixel.R + 15 && pixel.B > pixel.G + 15 && pixel.B > 180;
            
            // 3. Tonos grisáceos o sepia claros (común en fondos de documentos oficiales)
            bool isLightGray = Math.Abs(pixel.R - pixel.G) < 10 && 
                               Math.Abs(pixel.R - pixel.B) < 10 && 
                               Math.Abs(pixel.G - pixel.B) < 10 &&
                               pixel.R > 180;
            
            return isVeryLight || isLightBlue || isLightGray;
        }
        
        /// <summary>
        /// Optimiza el contraste para mejorar la detección de texto
        /// </summary>
        private static Bitmap OptimizeContrast(Bitmap original)
        {
            Bitmap result = new Bitmap(original.Width, original.Height);
            
            // Valores para ajuste automático de niveles
            int[] histogram = new int[256];
            int min = 255, max = 0;
            
            // Calcular histograma
            for (int y = 0; y < original.Height; y++)
            {
                for (int x = 0; x < original.Width; x++)
                {
                    Color pixel = original.GetPixel(x, y);
                    int gray = (int)(pixel.R * 0.3 + pixel.G * 0.59 + pixel.B * 0.11);
                    histogram[gray]++;
                    if (gray < min) min = gray;
                    if (gray > max) max = gray;
                }
            }
            
            // Evitar división por cero
            if (min == max) max = min + 1;
            
            // Aplicar ajuste de niveles
            for (int y = 0; y < original.Height; y++)
            {
                for (int x = 0; x < original.Width; x++)
                {
                    Color pixel = original.GetPixel(x, y);
                    int r = AdjustColorLevel(pixel.R, min, max);
                    int g = AdjustColorLevel(pixel.G, min, max);
                    int b = AdjustColorLevel(pixel.B, min, max);
                    
                    result.SetPixel(x, y, Color.FromArgb(r, g, b));
                }
            }
            
            return result;
        }
        
        /// <summary>
        /// Ajusta un componente de color según los niveles mínimo y máximo
        /// </summary>
        private static int AdjustColorLevel(int colorComponent, int min, int max)
        {
            return (colorComponent - min) * 255 / (max - min);
        }
        
        /// <summary>
        /// Aumenta el contraste de un píxel por un valor dado
        /// </summary>
        private static Color IncreasePixelContrast(Color pixel, int amount)
        {
            int r = ClampColor(pixel.R + (pixel.R > 128 ? amount : -amount));
            int g = ClampColor(pixel.G + (pixel.G > 128 ? amount : -amount));
            int b = ClampColor(pixel.B + (pixel.B > 128 ? amount : -amount));
            
            return Color.FromArgb(r, g, b);
        }
        
        /// <summary>
        /// Mantiene un valor de color dentro del rango 0-255
        /// </summary>
        private static int ClampColor(int value)
        {
            return Math.Max(0, Math.Min(255, value));
        }
        
        /// <summary>
        /// Aplica un enfoque selectivo para mejorar la legibilidad del texto
        /// </summary>
        private static Bitmap ApplySelectiveSharpening(Bitmap original)
        {
            Bitmap result = new Bitmap(original.Width, original.Height);
            
            // Matriz de enfoque (sharpening)
            float[][] sharpenMatrix = {
                new float[] {-1, -1, -1},
                new float[] {-1, 17, -1},
                new float[] {-1, -1, -1}
            };
            
            // Dividir por este valor para normalizar
            float divisor = 9;
            
            using (Graphics g = Graphics.FromImage(result))
            {
                // Alta calidad para el renderizado
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                
                // Aplicar matriz de enfoque usando un ImageAttributes
                ColorMatrix colorMatrix = new ColorMatrix(new float[][]
                {
                    new float[] {1, 0, 0, 0, 0},
                    new float[] {0, 1, 0, 0, 0},
                    new float[] {0, 0, 1, 0, 0},
                    new float[] {0, 0, 0, 1, 0},
                    new float[] {0, 0, 0, 0, 1}
                });
                
                using (ImageAttributes attributes = new ImageAttributes())
                {
                    attributes.SetColorMatrix(colorMatrix);
                    g.DrawImage(original, new Rectangle(0, 0, result.Width, result.Height),
                                0, 0, original.Width, original.Height, GraphicsUnit.Pixel, attributes);
                }
            }
            
            // Aplicar enfoque adicional para bordes
            for (int y = 1; y < original.Height - 1; y++)
            {
                for (int x = 1; x < original.Width - 1; x++)
                {
                    Color pixel = original.GetPixel(x, y);
                    
                    // Solo aplicar enfoque a píxeles oscuros (texto)
                    if (IsLikelyText(pixel))
                    {
                        float r = 0, g = 0, b = 0;
                        
                        // Aplicar matriz de convolución manualmente
                        for (int ky = -1; ky <= 1; ky++)
                        {
                            for (int kx = -1; kx <= 1; kx++)
                            {
                                Color p = original.GetPixel(x + kx, y + ky);
                                int matrixIndex = ky + 1;
                                r += p.R * sharpenMatrix[ky + 1][kx + 1];
                                g += p.G * sharpenMatrix[ky + 1][kx + 1];
                                b += p.B * sharpenMatrix[ky + 1][kx + 1];
                            }
                        }
                        
                        // Normalizar y recortar valores
                        r = Math.Min(255, Math.Max(0, r / divisor));
                        g = Math.Min(255, Math.Max(0, g / divisor));
                        b = Math.Min(255, Math.Max(0, b / divisor));
                        
                        result.SetPixel(x, y, Color.FromArgb((int)r, (int)g, (int)b));
                    }
                }
            }
            
            return result;
        }
        
        /// <summary>
        /// Determina si un píxel probablemente pertenece a texto
        /// </summary>
        private static bool IsLikelyText(Color pixel)
        {
            // El texto suele ser oscuro en documentos
            int gray = (int)(pixel.R * 0.3 + pixel.G * 0.59 + pixel.B * 0.11);
            return gray < 100; // Umbral para considerar que es texto
        }
        
        /// <summary>
        /// Guarda una imagen para depuración
        /// </summary>
        public static void SaveDebugImage(Bitmap image, string path)
        {
            try
            {
                image.Save(path, ImageFormat.Png);
                Console.WriteLine($"Imagen de depuración guardada en: {path}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al guardar imagen de depuración: {ex.Message}");
            }
        }
    }
}
