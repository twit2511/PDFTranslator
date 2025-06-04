using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using iText.Layout.Element;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.IO.Font;
using iText.IO.Font.Constants;

using PDFTranslate.PDFProcessor.PDFElements;
using System.Windows;
using iText.Layout.Renderer;
using System.Windows.Media;
using iText.Kernel.Pdf.Xobject;
using iText.IO.Image;
using System.Reflection;
using System.Text;



namespace PDFTranslate.PDFProcessor.PDFBuilder
{
    public static class Rebuilder
    {
        static PdfFont _cjkFont; // 用于中文文本 (如 STSong-Light)
        private static PdfFont _defaultLatinFont; // 用于西文文本 (如 Helvetica)
        static Rebuilder()
        {
            _cjkFont = LoadEmbeddedFont("PDFTranslate.Fonts.NotoSansSC-6.ttf");

            if (_cjkFont == null)
            {
                try
                {
                    _cjkFont = PdfFontFactory.CreateFont("STSong-Light",
                        PdfEncodings.IDENTITY_H,
                        PdfFontFactory.EmbeddingStrategy.PREFER_EMBEDDED);
                    Console.WriteLine("成功加载 iText 内置 CJK 字体 'STSong-Light' (依赖 itext7.font-asian)。");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"警告: 无法加载 iText 内置 CJK 字体 'STSong-Light'。中文可能无法正确显示。");
                    Console.WriteLine($"这通常意味着 'itext7.font-asian' NuGet 包未被引用，或者字体资源不可用。错误: {ex.Message}");
                    _cjkFont = null;
                }
            }

            // ---- 2. 加载 iText 标准的西文字体 (如 Helvetica) ----
            // PREFER_NOT_EMBEDDED 因为标准14字体通常不需要嵌入。
            try
            {
                _defaultLatinFont = PdfFontFactory.CreateFont(StandardFonts.HELVETICA, PdfEncodings.WINANSI, PdfFontFactory.EmbeddingStrategy.PREFER_NOT_EMBEDDED);
                Console.WriteLine("成功加载 iText 标准西文字体 'Helvetica'。");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"严重错误: 无法加载 iText 标准西文字体 'Helvetica'。错误: {ex.Message}");
                _defaultLatinFont = null;
            }
        }

        private static PdfFont LoadEmbeddedFont(string resourceName)
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly(); // 或者 Assembly.GetCallingAssembly()，或 typeof(Rebuilder).Assembly
                // 资源名称通常是: <默认命名空间>.<文件夹名（如果存在）>.<文件名>
                // 例如，如果你的项目默认命名空间是 "PDFTranslate"，字体在 "Fonts" 文件夹下，文件名为 "NotoSansSC-Regular.otf"，
                // 那么资源名称就是 "PDFTranslate.Fonts.NotoSansSC-Regular.otf"

                using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        Console.WriteLine($"警告: 无法找到嵌入的资源 '{resourceName}'。");
                        return null;
                    }

                    byte[] fontBytes = new byte[stream.Length];
                    stream.Read(fontBytes, 0, fontBytes.Length);

                    PdfFont font = PdfFontFactory.CreateFont(fontBytes,
                        PdfEncodings.IDENTITY_H,
                        PdfFontFactory.EmbeddingStrategy.PREFER_EMBEDDED, // 嵌入字体到PDF中
                        true); // true for embedded fonts, important for some font types
                    Console.WriteLine($"成功加载嵌入的 CJK 字体 '{resourceName}'。");
                    return font;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"警告: 加载嵌入的 CJK 字体 '{resourceName}' 失败。错误: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 判断文本是否可能包含中日韩字符。
        /// 这是一个基础的启发式检查。
        /// </summary>
        private static bool ContainsCjkCharacters(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (char c in text)
            {
                // CJK Unicode 块通常从 U+2E80 开始 (包括部首、符号、主要表意文字区等)
                // 这是一个比较宽泛的检查，可以根据需要调整。
                if (c >= 0x2E80 && c <= 0x9FFF) // 常用CJK统一表意文字范围 U+4E00-U+9FFF
                {
                    return true;
                }
                if (c >= 0x3400 && c <= 0x4DBF) // CJK扩展A
                {
                    return true;
                }
                if (c >= 0x20000 && c <= 0x2A6DF) // CJK扩展B
                {
                    return true;
                }

                // 可以添加更多CJK相关范围的检查，如扩展区等
            }
            return false;
        }
        private static PdfFont GetFont(string textToDraw)
        {
            bool isCjkText = ContainsCjkCharacters(textToDraw);

            // 1. 如果是CJK文本且CJK字体已加载
            if (isCjkText && _cjkFont != null)
            {
                if (CanFontDisplay(_cjkFont, textToDraw))
                {
                    return _cjkFont;
                }
                // 即使 STSong-Light 不能完全显示所有字符，对于中文文本，它仍是首选
                // Console.WriteLine($"警告: STSong-Light 可能无法显示文本中的所有字符: '{textToDraw.Substring(0, Math.Min(10,textToDraw.Length))}'");
                return _cjkFont;
            }

            // 2. 如果不是CJK文本，或者CJK字体加载失败，尝试使用默认西文字体
            if (_defaultLatinFont != null)
            {
                if (CanFontDisplay(_defaultLatinFont, textToDraw))
                {
                    return _defaultLatinFont;
                }
               
                return _defaultLatinFont;
            }

            
            if (_cjkFont != null)
            {
                // Console.WriteLine("警告: 西文字体加载失败，回退到CJK字体（如果存在）。");
                return _cjkFont;
            }

            // 4. 极端情况：所有尝试加载的字体都为null
            Console.WriteLine($"严重警告: 无法为文本找到任何 iText 提供的可用字体 (页码未知，文本片段: '{textToDraw.Substring(0, Math.Min(10, textToDraw.Length))}...')。文本将不会被正确绘制。");
            return null;
        }


  


        private static bool CanFontDisplay(PdfFont font, string text)
        {
            if (font == null) return false;
            if (string.IsNullOrEmpty(text)) return true;
            try
            {
                foreach (char c in text)
                {
                    if (!font.ContainsGlyph(c))
                    {
                        return false;
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }


        /// <summary>
        /// 实现PDF的重建
        /// </summary>
        /// <param name="elements"> pdf的所有相关元素 </param>
        /// <param name="outputPath">文件存放路径</param>
        /// <param name="originalDocumentForElements">源pdf文件</param>
        public static void RebuildPdf(List<IPDFElement> elements, string outputPath, PdfDocument originalDocumentForElements)
        {
            if (elements == null || !elements.Any())
            {
                Console.WriteLine("没有元素可用于重建 PDF。");
                return;
            }
            if (originalDocumentForElements == null || originalDocumentForElements.IsClosed())
            {
                Console.WriteLine($"错误：提供的原始文档为 null 或已关闭。");
                return;
            }

            PdfWriter pdfWriter = null;
            PdfDocument pdfDoc = null;
            

            Console.WriteLine($"开始重建 PDF 到: {outputPath}");
            try
            {
                pdfWriter = new PdfWriter(outputPath);
                pdfDoc = new PdfDocument(pdfWriter);

                var groupedElementsByPage = elements.GroupBy( e => e.PageNum).OrderBy(g => g.Key);

                foreach (var pageGroup in groupedElementsByPage)
                {
                    int pageNum = pageGroup.Key;
                    Console.WriteLine($"  重建第 {pageNum} 页...");

                    PdfPage originalPage = null;
                    if (pageNum <= originalDocumentForElements.GetNumberOfPages())
                    {
                        originalPage = originalDocumentForElements.GetPage(pageNum);
                    }

                    if (originalPage == null)
                    {
                        Console.WriteLine($"    警告: 无法从源PDF获取第 {pageNum} 页。跳过此页。");
                        continue;
                    }


                    var RectanglePageSize = originalPage.GetPageSizeWithRotation();
                    PageSize pageSize = new PageSize(RectanglePageSize);
                    var newPage = pdfDoc.AddNewPage(pageSize);
                    var canvas = new PdfCanvas(newPage);

                    foreach (var element in pageGroup)
                    {
                        // 保存状态对于隔离元素绘制非常重要
                        canvas.SaveState();
                        try
                        {
                            switch (element.ElementType) {
                                case PDFElementType.Text:
                                case PDFElementType.Formula:
                                case PDFElementType.TableCell:
                                    DrawTextElement(canvas, (TextElement)element);
                                    break;
                                case PDFElementType.Image:
                                    DrawImageElement(canvas, (ImageElement)element,newPage);
                                    break;
                                case PDFElementType.PathLine:       
                                    DrawLineElement(canvas,(LineElement)element); 
                                    break;
                                default:
                                    Console.WriteLine($"    信息: 跳过未知元素类型 {element.ElementType}");
                                    break;
                            }
                        }
                        catch(Exception ex)
                        {
                            Console.WriteLine($"    错误: 绘制元素 (页 {element.PageNum}, 类型 {element.ElementType}) 时发生内部错误: {ex.Message}");
                        }
                        finally
                        {
                            canvas.RestoreState();
                        }
                    }
                }
                Console.WriteLine("PDF 重建完成。");
            }
            catch( Exception ex ) 
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\nPDF 重建时发生严重错误: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                Console.ResetColor();
            }
            finally
            {
                pdfDoc?.Close();
            }
        }

        private static void DrawTextElement(PdfCanvas canvas, TextElement text)
        {
            //无论是否翻译都继续进行绘制
            string textString = string.IsNullOrEmpty(text.TranslatedText)? text.Text : text.TranslatedText;
            if (string.IsNullOrEmpty(textString)) return;

            PdfFont font = GetFont(textString);

            if (font == null)
            {
                Console.WriteLine($"    严重警告: 无法为文本找到任何可用字体 (页 {text.PageNum}, '{textString.Substring(0, Math.Min(10, textString.Length))}...')。文本将不会被绘制。");
                return;
            }

            // ---- BEGIN DEBUG LOGGING ----
            string fontNameToLog = "NULL FONT";
            if (font != null && font.GetFontProgram() != null && font.GetFontProgram().GetFontNames() != null)
            {
                fontNameToLog = font.GetFontProgram().GetFontNames().GetFontName();
            }
            else if (font != null)
            {
                fontNameToLog = "Font object exists, but name unavailable";
            }

            Console.WriteLine($"\nDEBUG: Page {text.PageNum}, Text: '{textString.Substring(0, Math.Min(20, textString.Length))}', " +
                              $"Font Chosen: '{fontNameToLog}', " +
                              $"FontSize: {text.FontSize}, " +
                              $"FontColor: {text.FontColor},"+
                              $"CharSpacing: {text.CharacterSpacing}, " +
                              $"WordSpacing: {text.WordSpacing}, " +
                              $"HorizScaling: {text.HorizontalScaling}");
            // ---- END DEBUG LOGGING ----

            float textBoxWidth = text.ApproximateBoundingBox?.GetWidth() ?? float.MaxValue;
            float textBoxHeight = text.ApproximateBoundingBox?.GetHeight() ?? float.MaxValue;

            // 尝试用原始字体大小绘制
            bool success = TryDrawText(canvas, text, textString, font, textBoxWidth, textBoxHeight, text.FontSize);

            // 如果原始大小绘制失败，逐步缩小字体
            if (!success)
            {
                float minFontSize = 2f; // 最小允许的字体大小
                float step = 0.5f;      // 每次缩小的步长

                for (float fontSize = text.FontSize - step; fontSize >= minFontSize; fontSize -= step)
                {
                    if (TryDrawText(canvas, text, textString, font, textBoxWidth, textBoxHeight, fontSize))
                    {
                        Console.WriteLine($"文本自动缩小到 {fontSize}pt 以适应空间");
                        break;
                    }
                }
            }

        }

        //辅助实现DrawTextElement函数
        private static bool TryDrawText(PdfCanvas canvas, TextElement text, string textString,
    PdfFont font, float maxWidth, float maxHeight, float fontSize)
        {
            // 计算单行文本宽度
            float textWidth = font.GetWidth(textString, fontSize);

            // 如果是单行文本且不超宽
            if (textWidth <= maxWidth)
            {
                DrawSingleLineText(canvas, text, textString, font, fontSize);
                return true;
            }

            // 多行文本处理
            return TryDrawMultiLineText(canvas, text, textString, font, maxWidth, maxHeight, fontSize);
        }

        private static void DrawSingleLineText(PdfCanvas canvas, TextElement text, string textString,
    PdfFont font, float fontSize)
        {
            DrawTextLine(canvas, text, textString, font, fontSize,
                text.StartPoint.Get(iText.Kernel.Geom.Vector.I1), text.StartPoint.Get(iText.Kernel.Geom.Vector.I2));
        }

        
        private static bool TryDrawMultiLineText(PdfCanvas canvas, TextElement text, string textString,
    PdfFont font, float maxWidth, float maxHeight, float fontSize)
        {
            float lineHeight = font.GetAscent(" ", fontSize) - font.GetDescent(" ", fontSize);
            int maxLines = (int)(maxHeight / lineHeight);

            List<string> lines = new List<string>();

            // 中文换行逻辑
            if (ContainsCjkCharacters(textString))
            {
                StringBuilder currentLine = new StringBuilder();
                float currentWidth = 0;

                foreach (char c in textString)
                {
                    float charWidth = font.GetWidth(c.ToString(), fontSize);

                    if (currentWidth + charWidth > maxWidth)
                    {
                        lines.Add(currentLine.ToString());
                        currentLine.Clear();
                        currentWidth = 0;

                        if (lines.Count >= maxLines) break;
                    }

                    currentLine.Append(c);
                    currentWidth += charWidth;
                }

                if (currentLine.Length > 0 && lines.Count < maxLines)
                {
                    lines.Add(currentLine.ToString());
                }
            }
            // 西文换行逻辑
            else
            {
                string[] words = textString.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                StringBuilder currentLine = new StringBuilder();
                float currentWidth = 0;

                foreach (string word in words)
                {
                    float wordWidth = font.GetWidth(word, fontSize);
                    float spaceWidth = font.GetWidth(" ", fontSize);

                    if (currentLine.Length > 0 && currentWidth + spaceWidth + wordWidth > maxWidth)
                    {
                        lines.Add(currentLine.ToString());
                        currentLine.Clear();
                        currentWidth = 0;

                        if (lines.Count >= maxLines) break;
                    }

                    if (currentLine.Length > 0)
                    {
                        currentLine.Append(" ");
                        currentWidth += spaceWidth;
                    }

                    currentLine.Append(word);
                    currentWidth += wordWidth;
                }

                if (currentLine.Length > 0 && lines.Count < maxLines)
                {
                    lines.Add(currentLine.ToString());
                }
            }

            // 检查是否所有内容都能放下
            if (lines.Count <= maxLines)
            {
                float currentY = text.StartPoint.Get(iText.Kernel.Geom.Vector.I2);
                foreach (string line in lines)
                {
                    DrawTextLine(canvas, text, line, font, fontSize, text.StartPoint.Get(iText.Kernel.Geom.Vector.I1), currentY);
                    currentY -= lineHeight * 1.2f;
                }
                return true;
            }

            return false;
        }

        //最终写入
        private static void DrawTextLine(PdfCanvas canvas, TextElement text, string textString,
    PdfFont font, float fontSize, float x, float y)
        {
            canvas.BeginText()
                .SetFontAndSize(font, fontSize)
                .SetFillColor(text.FontColor ?? ColorConstants.BLACK)
                .SetCharacterSpacing(text.CharacterSpacing)
                .SetWordSpacing(text.WordSpacing)
                .SetHorizontalScaling(text.HorizontalScaling)
                .MoveText(x, y)
                .ShowText(textString)
                .EndText();
        }





        private static void DrawImageElement(PdfCanvas canvas, ImageElement imageElement, PdfPage newPage)
        {
            if (imageElement.ImageObject == null || imageElement.Matrix == null)
            {
                Console.WriteLine($"    警告: 图像元素数据不完整，无法绘制 (页 {imageElement.PageNum})");
                return;
            }

            PdfImageXObject originalImageXObject = imageElement.ImageObject;
            iText.Kernel.Geom.Matrix matrix = imageElement.Matrix;
            PdfDocument newDocument = newPage.GetDocument(); // 正确获取目标文档

            Console.WriteLine($"    DEBUG: Processing ImageElement on Page {imageElement.PageNum}. Original XObject ID: {originalImageXObject?.GetPdfObject()?.GetIndirectReference()}");

            // 方案1: 尝试使用 GetImageBytes(true) 来获取可能已部分解码的数据
            try
            {
                Console.WriteLine("    DEBUG: Attempting originalImageXObject.GetImageBytes(true)");
                // true 参数提示 iText 尝试解码流中的数据
                byte[] decodedImageBytes = originalImageXObject.GetImageBytes(true);

                if (decodedImageBytes != null && decodedImageBytes.Length > 0)
                {
                    Console.WriteLine($"    DEBUG: GetImageBytes(true) returned {decodedImageBytes.Length} bytes. Attempting ImageDataFactory.Create.");
                    ImageData imageData = null;
                    try
                    {
                        imageData = ImageDataFactory.Create(decodedImageBytes);
                        // 注意：即使 GetImageBytes(true) 成功，ImageDataFactory 仍可能失败，
                        // 因为它还需要从图像字典中推断出宽度、高度、颜色空间等。
                        // GetImageBytes(true) 主要关注数据流的解码，而不是元数据的完整解析。
                    }
                    catch (Exception exFactoryFromDecoded)
                    {
                        Console.WriteLine($"    DEBUG: ImageDataFactory.Create failed with bytes from GetImageBytes(true): {exFactoryFromDecoded.Message}.");
                        // imageData 会是 null
                    }

                    if (imageData != null)
                    {
                        Console.WriteLine("    DEBUG: ImageDataFactory.Create successful with decoded bytes. Creating layout Image.");
                        Image layoutImage = new Image(imageData);
                        PdfImageXObject newPdfImageXObject = (PdfImageXObject)layoutImage.GetXObject();
                        if (newPdfImageXObject != null)
                        {
                            Console.WriteLine("    DEBUG: Successfully created new XObject via GetImageBytes(true) and ImageDataFactory. Adding to canvas.");
                            canvas.AddXObjectWithTransformationMatrix(
                                newPdfImageXObject,
                                matrix.Get(iText.Kernel.Geom.Matrix.I11), matrix.Get(iText.Kernel.Geom.Matrix.I12),
                                matrix.Get(iText.Kernel.Geom.Matrix.I21), matrix.Get(iText.Kernel.Geom.Matrix.I22),
                                matrix.Get(iText.Kernel.Geom.Matrix.I31), matrix.Get(iText.Kernel.Geom.Matrix.I32)
                            );
                            return; // 成功处理
                        }
                        else
                        {
                            Console.WriteLine("    DEBUG: layoutImage.GetXObject() returned null");
                        }
                    }
                }
                else
                {
                    Console.WriteLine("    DEBUG: GetImageBytes(true) returned null or empty byte array.");
                }
            }
            catch (Exception exGetImageBytesTrue)
            {
                // GetImageBytes(true) 本身也可能因为无法处理特定滤镜而抛出异常
                Console.WriteLine($"    DEBUG: originalImageXObject.GetImageBytes(true) threw an exception: {exGetImageBytesTrue.Message}");
            }

            // 方案2: 回退到使用原始流字节 (GetBytes(false)) - 这是你之前的逻辑
            // (可以保留这个分支，或者如果上面的尝试已经足够，可以直接跳到CopyTo)
            try
            {
                Console.WriteLine("    DEBUG: Attempting originalImageXObject.GetPdfObject().GetBytes(false) (original stream bytes)");
                byte[] originalStreamBytes = originalImageXObject.GetPdfObject().GetBytes(false); // 获取未解码的原始流

                if (originalStreamBytes != null && originalStreamBytes.Length > 0)
                {
                    Console.WriteLine($"    DEBUG: GetBytes(false) returned {originalStreamBytes.Length} bytes. Attempting ImageDataFactory.Create.");
                    ImageData imageData = null;
                    try
                    {
                        imageData = ImageDataFactory.Create(originalStreamBytes);
                    }
                    catch (Exception exFactoryOriginal)
                    {
                        Console.WriteLine($"    DEBUG: ImageDataFactory.Create failed with original stream bytes: {exFactoryOriginal.Message}.");
                    }

                    if (imageData != null)
                    {
                        Console.WriteLine("    DEBUG: ImageDataFactory.Create successful with original stream bytes. Creating layout Image.");
                        Image layoutImage = new Image(imageData);
                        PdfImageXObject newPdfImageXObject = (PdfImageXObject)layoutImage.GetXObject();
                        if (newPdfImageXObject != null)
                        {
                            Console.WriteLine("    DEBUG: Successfully created new XObject via original stream and ImageDataFactory. Adding to canvas.");
                            canvas.AddXObjectWithTransformationMatrix(
                                newPdfImageXObject,
                                matrix.Get(iText.Kernel.Geom.Matrix.I11), matrix.Get(iText.Kernel.Geom.Matrix.I12),
                                matrix.Get(iText.Kernel.Geom.Matrix.I21), matrix.Get(iText.Kernel.Geom.Matrix.I22),
                                matrix.Get(iText.Kernel.Geom.Matrix.I31), matrix.Get(iText.Kernel.Geom.Matrix.I32)
                            );
                            return; // 成功处理
                        }
                        else
                        {
                            Console.WriteLine("    DEBUG: layoutImage.GetXObject() returned null after original stream.");
                        }
                    }
                }
                else
                {
                    Console.WriteLine("    DEBUG: GetBytes(false) returned null or empty byte array.");
                }
            }
            catch (Exception exGetBytesFalse)
            {
                Console.WriteLine($"    DEBUG: originalImageXObject.GetPdfObject().GetBytes(false) threw an exception: {exGetBytesFalse.Message}");
            }


            // 方案3: 如果以上所有通过 ImageDataFactory 的尝试都失败，最终回退到 CopyTo
            Console.WriteLine("    DEBUG: All ImageDataFactory attempts failed or were inconclusive. Falling back to CopyTo.");
            try
            {
                UseCopyToForImage(canvas, originalImageXObject, matrix);
            }
            catch (Exception exCopyTo)
            {
                // 如果 CopyTo 也失败了，特别是抛出 "Pdf indirect object belongs to other PDF document"
                // 那么问题就比较棘手了。
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"    !!!!!! FATAL ERROR: UseCopyToForImage also failed for XObject  !!!!!!");
                Console.WriteLine($"           Exception: {exCopyTo.Message}");
                Console.ResetColor();
                // 这里可以选择抛出异常，或者记录并跳过这个图像，以尝试完成文档的其余部分
                // throw; // 如果希望程序因这个图像失败而停止
            }
        }

        // 辅助方法，用于 CopyTo 逻辑
        private static void UseCopyToForImage(PdfCanvas canvas, PdfImageXObject imageXObject, iText.Kernel.Geom.Matrix matrix)
        {
            Console.WriteLine($"    信息: 正在尝试使用 CopyTo 绘制图像 (页 {imageXObject.GetPdfObject().GetIndirectReference()?.GetDocument().GetPageNumber(imageXObject.GetPdfObject())})"); // 尝试获取原始页码
            PdfImageXObject copiedImage = imageXObject.CopyTo(canvas.GetDocument());
            canvas.AddXObjectWithTransformationMatrix(
                copiedImage,
                matrix.Get(iText.Kernel.Geom.Matrix.I11),
                matrix.Get(iText.Kernel.Geom.Matrix.I12),
                matrix.Get(iText.Kernel.Geom.Matrix.I21),
                matrix.Get(iText.Kernel.Geom.Matrix.I22),
                matrix.Get(iText.Kernel.Geom.Matrix.I31),
                matrix.Get(iText.Kernel.Geom.Matrix.I32)
            );
        }

        

        private static void DrawLineElement(PdfCanvas canvas, LineElement line) {

            if (line == null && line.StartPoint == null && line.EndPoint == null) {
                Console.WriteLine($"没有完整的LineElement数据！");
                return;
            }
            
            canvas.SetStrokeColor(line.StrokeColor ?? ColorConstants.BLACK)
                .SetLineWidth(line.LineWidth > 0 ? line.LineWidth : 0.5f)
                .MoveTo(line.StartPoint.Get(iText.Kernel.Geom.Vector.I1), line.StartPoint.Get(iText.Kernel.Geom.Vector.I2))
                .LineTo(line.EndPoint.Get(iText.Kernel.Geom.Vector.I1), line.EndPoint.Get(iText.Kernel.Geom.Vector.I2))
                .Stroke();

            Console.WriteLine($"表格框架绘制完成");
        }
    }
}
