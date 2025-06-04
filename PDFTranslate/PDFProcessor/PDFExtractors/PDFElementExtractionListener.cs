using iText.Kernel.Colors;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Xobject;
using PDFTranslate.PDFProcessor.PDFElements;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using iText.Kernel.Geom;

namespace PDFTranslate.PDFProcessor.PDFExtractors
{
    /// <summary>
    /// 实现 iText 7 事件监听器，用于提取页面元素。
    /// </summary>
    public class PdfElementExtractionListener : IEventListener
    {
        private readonly int _pageNumber;
        private readonly List<IPDFElement> _elements = new List<IPDFElement>();

        // 定义已知数学字体名称的子串 (用于启发式判断公式)
        private static readonly HashSet<string> MathFontNameSubstrings = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Math", "Symbol", "MT Extra", "Euclid", "Mathematical", "CambriaMath", "CMSY", "CMEX", "AMS"
            // 可根据需要扩展此列表
        };

        // 检查文本是否包含常用 Unicode 数学符号 (用于启发式判断公式)
        private static bool ContainsMathChars(string text)
        {
            // 简单的检查，可以根据需要扩展范围或提高复杂度
            return text.Any(c =>
                (c >= '\u2200' && c <= '\u22FF') || // Mathematical Operators
                (c >= '\u2190' && c <= '\u21FF') || // Arrows
                (c >= '\u0370' && c <= '\u03FF') || // Greek and Coptic
                (c >= '\u2A00' && c <= '\u2AFF') || // Supplemental Mathematical Operators
                (c >= '\u27C0' && c <= '\u27EF')    // Miscellaneous Mathematical Symbols-A
            );
        }

        // 路径处理常量
        private const float MIN_LINE_LENGTH = 5.0f; // 忽略过短的线条
        private const float LINE_DETECTION_TOLERANCE = 1.5f; // 水平/垂直判断容差

        // --- 新增/调整：文本合并相关的常量 ---
        private const float INTRA_LINE_Y_TOLERANCE_FACTOR = 0.5f;
        private const float INTRA_LINE_X_SPACE_FACTOR_MAX = 2.0f;
        private const float INTRA_LINE_X_SPACE_FACTOR_MIN = 0.1f;
        private const float INTRA_LINE_X_OVERLAP_TOLERANCE = 2.0f;
        private const float PARAGRAPH_LINE_SPACING_FACTOR = 2.2f;
        private const float PARAGRAPH_INDENT_TOLERANCE_FACTOR = 0.8f;
        private const float STYLE_CHANGE_TOLERANCE_FONTSIZE = 0.5f; // 用于AreChunksMergeableForLine, 段落内可略宽松
        private const float STYLE_CHANGE_TOLERANCE_HSCALE = 0.05f; // 水平缩放比例变化容差(行内)，段落内可略宽松

        public PdfElementExtractionListener(int pageNumber)
        {
            _pageNumber = pageNumber;
        }

        /// <summary>
        /// 处理 iText 解析器触发的事件。
        /// </summary>
        public void EventOccurred(IEventData data, EventType type)
        {
            try // 添加整体异常捕获，防止单个元素解析失败导致整个页面失败
            {
                switch (type)
                {
                    case EventType.RENDER_TEXT:
                        ExtractTextElement((TextRenderInfo)data);
                        break;
                    case EventType.RENDER_IMAGE:
                        ExtractImageElement((ImageRenderInfo)data);
                        break;
                    case EventType.RENDER_PATH:
                        ExtractPathElement((PathRenderInfo)data);
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"!! 错误: 在处理第 {_pageNumber} 页的 {type} 事件时发生异常: {ex.Message}");
                // 可以选择记录更详细的日志或采取其他错误处理措施
            }
        }

        /// <summary>
        /// 提取文本元素信息。
        /// </summary>
        private void ExtractTextElement(TextRenderInfo renderInfo)
        {
            string text = renderInfo.GetText();
            if (string.IsNullOrWhiteSpace(text)) return; // 忽略空白文本块

            var font = renderInfo.GetFont();
            string fontName = font?.GetFontProgram()?.GetFontNames()?.GetFontName() ?? "Unknown";
            var fillColor = renderInfo.GetFillColor() ?? ColorConstants.BLACK; // 默认黑色

            // 计算位置和边界框
            var startPoint = renderInfo.GetBaseline().GetStartPoint();
            var endPoint = renderInfo.GetBaseline().GetEndPoint();
            var ascentLine = renderInfo.GetAscentLine();
            var descentLine = renderInfo.GetDescentLine();
            float height = ascentLine.GetStartPoint().Get(Vector.I2) - descentLine.GetStartPoint().Get(Vector.I2);
            if (height <= 0 && renderInfo.GetFontSize() > 0)
                height = renderInfo.GetFontSize() * 1.2f; // 估算高度
            float width = endPoint.Get(Vector.I1) - startPoint.Get(Vector.I1);
            if (Math.Abs(width) < 0.01f && text.Length > 0)
                width = renderInfo.GetFontSize() * text.Length * 0.6f; // 估算宽度
            Rectangle bbox = new Rectangle(startPoint.Get(Vector.I1), descentLine.GetStartPoint().Get(Vector.I2), width, height);

            // 初步类型判断 (公式 vs 普通)
            PDFElementType elementType = PDFElementType.Text;
            bool isMathFont = fontName != "Unknown" && MathFontNameSubstrings.Any(s => fontName.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0);
            bool hasMathChars = ContainsMathChars(text);
            // 规则: 数学字体 或 (短文本 且 含数学符号) -> 认为是公式
            if (isMathFont || (hasMathChars && text.Length < 20))
            {
                elementType = PDFElementType.Formula;
            }

            // 创建 TextElement 对象
            var textElement = new TextElement
            {
                PageNum = _pageNumber,
                Text = text,
                ApproximateBoundingBox = bbox,
                StartPoint = startPoint,
                EndPoint = endPoint,
                FontName = fontName,
                FontSize = renderInfo.GetFontSize(),
                FontColor = fillColor,
                CharacterSpacing = renderInfo.GetCharSpacing(),
                WordSpacing = renderInfo.GetWordSpacing(),
                HorizontalScaling = renderInfo.GetHorizontalScaling(),
                OriginalFont = font,
                ElementType = elementType, // 设置初步判断的类型
                NeedsTranslated = true // 默认需要翻译，后续可能会调整
            };
            _elements.Add(textElement);
        }

        /// <summary>
        /// 提取图像元素信息。
        /// </summary>
        private void ExtractImageElement(ImageRenderInfo renderInfo)
        {
            PdfImageXObject imageObject = renderInfo.GetImage();
            if (imageObject == null)
            {
                Console.WriteLine($"警告: 在第 {_pageNumber} 页检测到图像事件，但无法获取图像对象。");
                return;
            }
            Matrix matrix = renderInfo.GetImageCtm(); // 获取变换矩阵，保证位置正确
            _elements.Add(new ImageElement(_pageNumber, imageObject, matrix));
        }

        /// <summary>
        /// 提取路径元素信息，并筛选出直线用于表格检测。
        /// </summary>
        private void ExtractPathElement(PathRenderInfo renderInfo)
        {
            // 只关心描边操作 (Stroke)，因为表格线通常是画出来的
            if (renderInfo.GetOperation() != PathRenderInfo.STROKE) return;

            Path path = renderInfo.GetPath();
            if (path == null) return;

            Matrix ctm = renderInfo.GetCtm(); // 获取当前变换矩阵

            // 遍历路径的所有子路径
            foreach (var subpath in path.GetSubpaths())
            {
                // 获取子路径的所有段 (Segment)
                var segments = subpath.GetSegments();

                // --- 重点处理由单条直线构成的子路径 ---
                // 这是表格线最常见的情况
                if (segments != null && segments.Count == 1 && segments[0] is Line lineSegment)
                {
                    // 从 Line Segment 获取其基点 (base points)
                    // 注意: GetStartPoint() 和 GetEndPoint() 返回的是 Point 对象
                    Point start = lineSegment.GetBasePoints()[0];
                    Point end = lineSegment.GetBasePoints()[1];

                    // 将路径点转换为 Vector 并应用 CTM 得到页面坐标
                    Vector finalStart = new Vector((float)start.GetX(), (float)start.GetY(), 1).Cross(ctm);
                    Vector finalEnd = new Vector((float)end.GetX(), (float)end.GetY(), 1).Cross(ctm);

                    var lineElement = new LineElement(
                        _pageNumber, finalStart, finalEnd,
                        renderInfo.GetLineWidth(),
                        renderInfo.GetStrokeColor()
                    );

                    // 过滤：只保留足够长且近似水平或垂直的线
                    if (lineElement.Length() >= MIN_LINE_LENGTH &&
                        (lineElement.IsHorizontal(LINE_DETECTION_TOLERANCE) || lineElement.IsVertical(LINE_DETECTION_TOLERANCE)))
                    {
                        _elements.Add(lineElement);
                    }
                }
                // else: 可以添加逻辑处理更复杂的子路径，比如矩形框等
            }
        }

        // --- 文本合并逻辑 ---
        // 这个方法是你之前提供的，用于行内合并
        private bool AreChunksMergeable(TextElement prev, TextElement current, float yTolerance = 2.0f, float xToleranceFactor = 1.5f)
        {
            if (prev == null || current == null) return false;
            if (prev.PageNum != current.PageNum) return false;
            if (prev.FontName != current.FontName ||
                Math.Abs(prev.FontSize - current.FontSize) > STYLE_CHANGE_TOLERANCE_FONTSIZE || // 使用常量
                !AreColorsSimilar(prev.FontColor, current.FontColor) ||
                Math.Abs(prev.HorizontalScaling - current.HorizontalScaling) > STYLE_CHANGE_TOLERANCE_HSCALE) // 使用常量
            {
                return false;
            }
            float prevY = prev.StartPoint.Get(Vector.I2);
            float currentY = current.StartPoint.Get(Vector.I2);
            // Y容差应该与字号相关
            float dynamicYTolerance = Math.Max(prev.FontSize, current.FontSize) * INTRA_LINE_Y_TOLERANCE_FACTOR;
            if (Math.Abs(prevY - currentY) > dynamicYTolerance) // 使用动态容差
            {
                return false;
            }
            float prevEndX = prev.EndPoint.Get(Vector.I1);
            float currentStartX = current.StartPoint.Get(Vector.I1);
            float avgCharWidthPrev = prev.Text.Length > 0 ? (prev.EndPoint.Get(Vector.I1) - prev.StartPoint.Get(Vector.I1)) / prev.Text.Length : prev.FontSize * 0.5f;
            float maxSpace = avgCharWidthPrev * INTRA_LINE_X_SPACE_FACTOR_MAX; // 使用常量
            float minOverlapOrGap = avgCharWidthPrev * INTRA_LINE_X_SPACE_FACTOR_MIN; // 允许的小间隙或重叠

            if (currentStartX < prevEndX - INTRA_LINE_X_OVERLAP_TOLERANCE || // 使用常量允许重叠
                currentStartX > prevEndX + maxSpace)
            {
                // 进一步检查是否是紧密连接但被分割的块
                if (!(currentStartX >= prevEndX - minOverlapOrGap && currentStartX <= prevEndX + minOverlapOrGap))
                {
                    return false;
                }
            }
            return true;
        }

        // 这个方法是你之前提供的，用于合并行内块
        private TextElement CombineTextChunks(List<TextElement> chunks)
        {
            if (chunks == null || !chunks.Any()) return null;
            if (chunks.Count == 1) return chunks[0];
            TextElement firstChunk = chunks[0];
            TextElement lastChunk = chunks.Last(); // 需要最后一个块来确定 EndPoint
            // 行内合并，直接连接文本，因为它们在同一视觉行
            string combinedText = string.Join(" ", chunks.Select(c => c.Text));
            float minX = chunks.Min(c => c.ApproximateBoundingBox.GetLeft());
            float minY = chunks.Min(c => c.ApproximateBoundingBox.GetBottom());
            float maxX = chunks.Max(c => c.ApproximateBoundingBox.GetRight());
            float maxY = chunks.Max(c => c.ApproximateBoundingBox.GetTop());
            Rectangle combinedBBox = new Rectangle(minX, minY, maxX - minX, maxY - minY);
            return new TextElement
            {
                PageNum = firstChunk.PageNum,
                Text = combinedText,
                ApproximateBoundingBox = combinedBBox,
                StartPoint = firstChunk.StartPoint,
                EndPoint = lastChunk.EndPoint, // 正确的 EndPoint
                FontName = firstChunk.FontName,
                FontSize = firstChunk.FontSize,
                FontColor = firstChunk.FontColor,
                CharacterSpacing = firstChunk.CharacterSpacing,
                WordSpacing = firstChunk.WordSpacing,
                HorizontalScaling = firstChunk.HorizontalScaling,
                OriginalFont = firstChunk.OriginalFont,
                ElementType = firstChunk.ElementType,
                NeedsTranslated = firstChunk.NeedsTranslated,
            };
        }


        // --- 新增的段落合并逻辑 ---
        private List<TextElement> StitchTextElementsInLines(List<TextElement> rawTextElements)
        {
            // 这个方法就是你之前的 GetAndMergeExtractedElements 的核心逻辑，但只处理文本
            if (rawTextElements == null || !rawTextElements.Any())
            {
                return new List<TextElement>();
            }

            var sortedChunks = rawTextElements
                .OrderBy(t => t.PageNum) // 使用 PageNum
                .ThenByDescending(t => t.StartPoint.Get(Vector.I2))
                .ThenBy(t => t.StartPoint.Get(Vector.I1))
                .ToList();

            List<TextElement> stitchedLines = new List<TextElement>();
            if (!sortedChunks.Any()) return stitchedLines;

            List<TextElement> currentLineAssembly = new List<TextElement> { sortedChunks[0] };

            for (int i = 1; i < sortedChunks.Count; i++)
            {
                TextElement prevChunk = currentLineAssembly.Last();
                TextElement currentChunk = sortedChunks[i];

                // 使用你定义的 AreChunksMergeable (它现在是行内合并逻辑)
                if (AreChunksMergeable(prevChunk, currentChunk))
                {
                    currentLineAssembly.Add(currentChunk);
                }
                else
                {
                    if (currentLineAssembly.Any())
                    {
                        stitchedLines.Add(CombineTextChunks(currentLineAssembly)); // 使用你定义的 CombineTextChunks
                    }
                    currentLineAssembly.Clear();
                    currentLineAssembly.Add(currentChunk);
                }
            }
            if (currentLineAssembly.Any())
            {
                stitchedLines.Add(CombineTextChunks(currentLineAssembly));
            }
            return stitchedLines;
        }

        private List<TextElement> BuildParagraphsFromLines(List<TextElement> stitchedLines)
        {
            if (stitchedLines == null || !stitchedLines.Any())
            {
                return new List<TextElement>();
            }

            // stitchedLines 已经是按行排序（或至少是按Y粗略排序）的
            List<TextElement> paragraphs = new List<TextElement>();
            if (!stitchedLines.Any()) return paragraphs;

            List<TextElement> currentParagraphAssembly = new List<TextElement> { stitchedLines[0] };

            for (int i = 1; i < stitchedLines.Count; i++)
            {
                TextElement paragraphFirstLine = currentParagraphAssembly.First();
                TextElement prevLineInAssembly = currentParagraphAssembly.Last();
                TextElement currentLineToTest = stitchedLines[i];

                if (AreLinesMergeableForParagraph(paragraphFirstLine, prevLineInAssembly, currentLineToTest))
                {
                    currentParagraphAssembly.Add(currentLineToTest);
                }
                else
                {
                    if (currentParagraphAssembly.Any())
                    {
                        paragraphs.Add(CombineParagraphLinesToSingleElement(currentParagraphAssembly));
                    }
                    currentParagraphAssembly.Clear();
                    currentParagraphAssembly.Add(currentLineToTest);
                }
            }
            if (currentParagraphAssembly.Any())
            {
                paragraphs.Add(CombineParagraphLinesToSingleElement(currentParagraphAssembly));
            }
            return paragraphs;
        }

        private bool AreLinesMergeableForParagraph(TextElement paragraphFirstLine, TextElement prevLineInParagraph, TextElement currentLineToTest)
        {
            if (prevLineInParagraph.PageNum != currentLineToTest.PageNum) return false;

            if (paragraphFirstLine.FontName != currentLineToTest.FontName && prevLineInParagraph.FontName != currentLineToTest.FontName)
            {
                if (prevLineInParagraph.FontName != currentLineToTest.FontName) return false;
            }
            float fontSizeTolerance = Math.Max(paragraphFirstLine.FontSize, currentLineToTest.FontSize) * 0.2f;
            if (Math.Abs(paragraphFirstLine.FontSize - currentLineToTest.FontSize) > fontSizeTolerance &&
                Math.Abs(prevLineInParagraph.FontSize - currentLineToTest.FontSize) > STYLE_CHANGE_TOLERANCE_FONTSIZE)
            {
                return false;
            }

            float verticalDistance = prevLineInParagraph.StartPoint.Get(Vector.I2) - currentLineToTest.StartPoint.Get(Vector.I2);
            float prevLineHeight = prevLineInParagraph.ApproximateBoundingBox.GetHeight();
            if (prevLineHeight <= 0) prevLineHeight = prevLineInParagraph.FontSize * 1.2f;
            if (verticalDistance <= prevLineHeight * 0.1f || verticalDistance > prevLineHeight * PARAGRAPH_LINE_SPACING_FACTOR)
            {
                return false;
            }

            float xIndentTolerance = Math.Max(paragraphFirstLine.FontSize, currentLineToTest.FontSize) * PARAGRAPH_INDENT_TOLERANCE_FACTOR;
            float firstLineX = paragraphFirstLine.StartPoint.Get(Vector.I1);
            float currentLineX = currentLineToTest.StartPoint.Get(Vector.I1);
            bool alignedWithFirst = Math.Abs(currentLineX - firstLineX) < xIndentTolerance;
            bool alignedWithPrev = true;
            if (paragraphFirstLine != prevLineInParagraph)
            {
                alignedWithPrev = Math.Abs(prevLineInParagraph.StartPoint.Get(Vector.I1) - currentLineX) < xIndentTolerance;
            }

            float pageWidthApproximation = GetPageWidth(prevLineInParagraph.PageNum);
            bool prevLineIsSubstantiallyFull = (prevLineInParagraph.ApproximateBoundingBox.GetRight() - prevLineInParagraph.ApproximateBoundingBox.GetLeft()) > (pageWidthApproximation * 0.70);

            if (prevLineIsSubstantiallyFull)
            {
                if (currentLineX < firstLineX - xIndentTolerance * 2) return false;
                return true;
            }
            else
            {
                if (!alignedWithFirst && !alignedWithPrev)
                {
                    if (prevLineInParagraph.Text.Length < pageWidthApproximation * 0.3f && currentLineToTest.Text.Length > prevLineInParagraph.Text.Length * 1.5)
                    {
                        if (!alignedWithFirst && !alignedWithPrev) return false;
                    }
                    else if (!alignedWithFirst && !alignedWithPrev)
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        private float GetPageWidth(int pageNum) // 简化 GetPageWidth
        {
            var elementsOnPage = _elements.Where(e => e.PageNum == pageNum); // 使用成员变量 _elements
            if (!elementsOnPage.Any()) return 600; // Default
            try
            {
                float minX = elementsOnPage.Min(e => e.ApproximateBoundingBox.GetX());
                float maxX = elementsOnPage.Max(e => e.ApproximateBoundingBox.GetX() + e.ApproximateBoundingBox.GetWidth());
                return maxX - minX;
            }
            catch { return 600; } // 容错
        }

        private TextElement CombineParagraphLinesToSingleElement(List<TextElement> lines)
        {
            if (lines == null || !lines.Any()) return null;
            // 如果段落只有一行（在行内拼接后），则不需要额外处理换行
            if (lines.Count == 1) return lines[0];

            TextElement firstLine = lines[0];
            TextElement lastLine = lines.Last();

            // --- 修改文本连接逻辑 ---
            StringBuilder combinedTextBuilder = new StringBuilder();
            for (int i = 0; i < lines.Count; i++)
            {
                // 获取当前行的文本，并去除可能由之前行内合并产生的首尾多余空格
                string lineText = lines[i].Text.Trim();
                combinedTextBuilder.Append(lineText);

                // 如果不是段落的最后一行，则在行尾添加一个换行符
                // 注意：这里我们假设视觉上的换行都应该变成一个换行符。
                // 如果原始 PDF 中的换行是“软换行”（自动换行），而你想用空格代替，
                // 那么这里的逻辑需要更复杂，可能需要分析行尾字符或与下一行的关系。
                // 但根据你的需求“在中间加上换行”，这里直接加 Environment.NewLine。
                if (i < lines.Count - 1)
                {
                    combinedTextBuilder.Append(Environment.NewLine); // 使用 Environment.NewLine 来获得平台相关的换行符
                }
            }
            string combinedText = combinedTextBuilder.ToString();
            // --- 文本连接逻辑结束 ---

            // 计算段落的整体边界框 (与之前相同)
            float minX = lines.Min(l => l.ApproximateBoundingBox.GetLeft());
            float minY = lines.Min(l => l.ApproximateBoundingBox.GetBottom());
            float maxX = lines.Max(l => l.ApproximateBoundingBox.GetRight());
            float maxY = lines.Max(l => l.ApproximateBoundingBox.GetTop());
            Rectangle combinedBBox = new Rectangle(minX, minY, maxX - minX, maxY - minY);

            return new TextElement
            {
                PageNum = firstLine.PageNum, // 使用你的页码属性名
                Text = combinedText,         // 使用包含换行符的合并文本
                ApproximateBoundingBox = combinedBBox,
                StartPoint = firstLine.StartPoint, // 段落的起点是第一行的起点
                EndPoint = lastLine.EndPoint,     // 段落的终点是最后一行的终点
                FontName = firstLine.FontName,    // 取第一行的样式作为代表
                FontSize = firstLine.FontSize,
                FontColor = firstLine.FontColor,
                CharacterSpacing = firstLine.CharacterSpacing, // 这些可能需要取平均或更复杂的处理
                WordSpacing = firstLine.WordSpacing,
                HorizontalScaling = firstLine.HorizontalScaling,
                OriginalFont = firstLine.OriginalFont,
                ElementType = firstLine.ElementType, // 段落类型通常与首行一致，或默认为 TextNormal (你的是 PDFElementType)
                NeedsTranslated = firstLine.NeedsTranslated // 使用你的 NeedsTranslated 属性
            };
        }

        private bool AreColorsSimilar(Color c1, Color c2, float tolerance = 0.05f)
        {
            if (c1 == null && c2 == null) return true; if (c1 == null || c2 == null) return false;
            var comp1 = c1.GetColorValue(); var comp2 = c2.GetColorValue();
            if (comp1.Length != comp2.Length) return false;
            for (int i = 0; i < comp1.Length; i++) { if (Math.Abs(comp1[i] - comp2[i]) > tolerance) return false; }
            return true;
        }

        public ICollection<EventType> GetSupportedEvents()
        {
            return new List<EventType> { EventType.RENDER_TEXT, EventType.RENDER_IMAGE, EventType.RENDER_PATH };
        }

        public List<IPDFElement> GetExtractedElements() // 修改这里以执行两阶段合并
        {
            var rawTextElements = _elements.OfType<TextElement>().ToList();
            var otherElements = _elements.Where(e => !(e is TextElement)).ToList();

            if (!rawTextElements.Any()) return otherElements;

            // 阶段1: 行内拼接
            List<TextElement> stitchedLines = StitchTextElementsInLines(rawTextElements);

            // 阶段2: 段落构建
            List<TextElement> paragraphs = BuildParagraphsFromLines(stitchedLines);

            List<IPDFElement> finalElements = new List<IPDFElement>();
            finalElements.AddRange(paragraphs); // 添加合并后的段落
            finalElements.AddRange(otherElements); // 添加其他非文本元素

            // 最终排序
            finalElements = finalElements
                .OrderBy(e => e.PageNum)
                .ThenByDescending(e => e.ApproximateBoundingBox.GetY())
                .ThenBy(e => e.ApproximateBoundingBox.GetX())
                .ToList();

            return finalElements;
        }
    }

    // PointComparer (如果之前未在别处定义)
    internal class PointComparer : IEqualityComparer<iText.Kernel.Geom.Point>
    {
        public bool Equals(iText.Kernel.Geom.Point p1, iText.Kernel.Geom.Point p2)
        {
            if (p1 == null && p2 == null) return true; if (p1 == null || p2 == null) return false;
            double tolerance = 0.01;
            return Math.Abs(p1.GetX() - p2.GetX()) < tolerance && Math.Abs(p1.GetY() - p2.GetY()) < tolerance;
        }
        public int GetHashCode(iText.Kernel.Geom.Point p)
        {
            if (p == null) return 0;
            return p.GetX().GetHashCode() ^ p.GetY().GetHashCode();
        }
    }
}