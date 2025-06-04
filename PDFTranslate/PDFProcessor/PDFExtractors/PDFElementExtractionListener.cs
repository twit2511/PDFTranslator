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

        private const float RELAXED_INTRA_LINE_Y_TOLERANCE_FACTOR = 0.7f; // 稍微放宽Y轴容差
        private const float RELAXED_INTRA_LINE_X_SPACE_FACTOR_MAX = 2.5f; // 稍微放宽X轴最大间距
        private const float MIN_FONT_SIZE_FOR_ACCURATE_SPACING = 8.0f; // 用于判断何时使用字号估算间距
        private bool AreChunksMergeable(TextElement prev, TextElement current) // 移除了默认参数，让调用处更明确
        {
            if (prev == null || current == null) return false;
            if (prev.PageNum != current.PageNum) return false;

            // 样式检查: 字体名称、颜色、水平缩放
            // 字体大小的比较放后面，因为它与Y坐标容差有关
            if (prev.FontName != current.FontName ||
                !AreColorsSimilar(prev.FontColor, current.FontColor) || // 颜色比较可以稍微宽松一点点
                Math.Abs(prev.HorizontalScaling - current.HorizontalScaling) > STYLE_CHANGE_TOLERANCE_HSCALE)
            {
                return false;
            }

            // Y坐标检查 (判断是否在同一行)
            // 使用 prev 和 current 中较大的字号来计算容差，或者取平均？这里用较大者更保守
            float largerFontSize = Math.Max(prev.FontSize, current.FontSize);
            if (largerFontSize <= 0) largerFontSize = 10f; // 避免除零或负数，给个默认值
            float dynamicYTolerance = largerFontSize * RELAXED_INTRA_LINE_Y_TOLERANCE_FACTOR; // 使用建议的宽松容差

            // 比较基线Y坐标 (StartPoint.Y)
            // 有些PDF文本块的Y坐标可能用的是中心或顶部，这里假设是基线
            // 或者比较 BoundingBox 的 Y 中心
            float prevY = prev.StartPoint.Get(Vector.I2);
            float currentY = current.StartPoint.Get(Vector.I2);
            // 或者使用 BBox 的 Y 中心：
            // float prevY = prev.ApproximateBoundingBox.GetY() + prev.ApproximateBoundingBox.GetHeight() / 2;
            // float currentY = current.ApproximateBoundingBox.GetY() + current.ApproximateBoundingBox.GetHeight() / 2;


            if (Math.Abs(prevY - currentY) > dynamicYTolerance)
            {
                // 如果Y坐标差异较大，再检查一下字体大小是否也差异很大，
                // 因为有时同一行的上标下标会导致Y变化，但字体大小也不同
                if (Math.Abs(prev.FontSize - current.FontSize) > STYLE_CHANGE_TOLERANCE_FONTSIZE * 2) // 更严格的字号变化才算不同行
                {
                    return false;
                }
                // 如果字号相近，但Y差值略大，可以考虑是否是因为基线对齐问题，
                // 例如，一个字符的基线在底部，另一个在中间。
                // 此时可以尝试比较 BoundingBox 的重叠度。
                // 这是一个复杂情况，暂时先依赖 dynamicYTolerance
            }


            // 字体大小检查 (放在Y坐标检查后，因为Y容差依赖它)
            if (Math.Abs(prev.FontSize - current.FontSize) > STYLE_CHANGE_TOLERANCE_FONTSIZE)
            {
                return false;
            }

            // X坐标检查 (判断水平间距)
            float prevEndX = prev.ApproximateBoundingBox.GetRight(); // 使用BBox的右边界
            float currentStartX = current.ApproximateBoundingBox.GetLeft(); // 使用BBox的左边界

            // 如果 prev.EndPoint 和 current.StartPoint 更可靠，也可以用它们：
            // float prevEndX = prev.EndPoint.Get(Vector.I1);
            // float currentStartX = current.StartPoint.Get(Vector.I1);

            float spaceBetween = currentStartX - prevEndX;

            // 计算允许的最大间距
            // 使用 prev 和 current 中较小的字号来估算空格宽度，因为如果一个是标题一个是正文，空格应按正文算
            float smallerFontSize = Math.Min(prev.FontSize, current.FontSize);
            if (smallerFontSize <= 0) smallerFontSize = 10f; // 默认值

            // 使用一个更稳定的平均字符宽度估算
            // 0.5 到 0.6 倍字号通常是一个字符的平均渲染宽度（非等宽字体）
            float avgCharWidthApproximation = smallerFontSize * 0.6f;
            // 如果文本块很短，直接用字号估算可能更准
            if (prev.Text.Length > 0 && prev.FontSize > MIN_FONT_SIZE_FOR_ACCURATE_SPACING && prev.ApproximateBoundingBox.GetWidth() > 0)
            {
                avgCharWidthApproximation = prev.ApproximateBoundingBox.GetWidth() / prev.Text.Length;
            }


            float maxAllowedSpace = avgCharWidthApproximation * RELAXED_INTRA_LINE_X_SPACE_FACTOR_MAX; // 使用建议的宽松容差
            float minAllowedOverlap = -(avgCharWidthApproximation * 0.5f); // 允许一定的重叠，比如半个字符宽度

            // 如果 prev 的 BBox.Right 小于 current 的 BBox.Left (即 current 在 prev 右边)
            if (spaceBetween > maxAllowedSpace)
            {
                return false; // 间距过大
            }
            // 如果 current 在 prev 左边，或者重叠过多 (spaceBetween 是负数)
            if (spaceBetween < minAllowedOverlap - INTRA_LINE_X_OVERLAP_TOLERANCE) // 减去一个额外的重叠容差
            {
                // 除非是上标/下标等特殊情况，否则不应大幅度重叠或顺序颠倒
                // 这里可以增加一个判断：如果 prev 的 X 比 current 的 X 大很多，则认为不是连续的
                if (prev.ApproximateBoundingBox.GetLeft() > current.ApproximateBoundingBox.GetRight())
                {
                    return false; // prev 完全在 current 右边，这不符合正常阅读顺序的合并
                }
                // 进一步检查：如果重叠很多，但Y坐标确实非常接近，并且文本方向一致，可能仍需合并
                // (此逻辑较复杂，暂时先用上面的判断)
            }

            // 确保 current 不是在 prev 的“内部”开始（极端重叠）
            // 即 current.X 不应远小于 prev.X
            if (current.ApproximateBoundingBox.GetLeft() < prev.ApproximateBoundingBox.GetLeft() - avgCharWidthApproximation)
            {
                // 如果 current 开始位置比 prev 开始位置还靠左一个字符以上，可能不是顺序的
                // 但也要考虑 prev 可能只有一个标点符号的情况
                if (prev.Text.Length > 1) return false;
            }


            return true;
        }

        // 这个方法是你之前提供的，用于合并行内块
        private TextElement CombineTextChunks(List<TextElement> chunks)
        {
            chunks = chunks.OrderBy(c => c.ApproximateBoundingBox.GetLeft()).ToList();
            if (chunks == null || !chunks.Any()) return null;
            if (chunks.Count == 1) return chunks[0];
            TextElement firstChunk = chunks[0];
            TextElement lastChunk = chunks.Last(); // 需要最后一个块来确定 EndPoint
            // 行内合并，直接连接文本，因为它们在同一视觉行
            string combinedText = string.Join("", chunks.Select(c => c.Text));
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
                NeedsTranslated = chunks.Any(c => c.NeedsTranslated) // 如果任一子块需要翻译,
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

        private const float RELAXED_PARAGRAPH_LINE_SPACING_FACTOR = 1.0f; // 稍微放宽行间距因子
        private const float PARAGRAPH_FONT_SIZE_TOLERANCE_FACTOR = 0.1f; // 段落内行间字体大小差异容忍度 (10%)
        private const float PARAGRAPH_HANGING_INDENT_TOLERANCE_FACTOR = 2.0f; // 悬挂缩进的容差倍数
        private bool AreLinesMergeableForParagraph(TextElement paragraphFirstLine, TextElement prevLineInParagraph, TextElement currentLineToTest)
        {
            if (prevLineInParagraph.PageNum != currentLineToTest.PageNum) return false;

            // 检查字体名称是否剧烈变化 (允许段落内有一些样式变化，但不是完全不同字体系列)
            // 这一块可以根据实际情况调整，如果段落内字体变化很常见，可以放宽
            if (prevLineInParagraph.FontName != currentLineToTest.FontName)
            {
                // 可以添加更复杂的字体相似度比较，例如忽略 "Bold", "Italic" 等后缀
                // 暂时先简单判断不相等
                // return false; // 如果严格要求字体一致，取消注释此行
            }

            // 字体大小比较
            // 允许段落内行与行之间有轻微的字体大小变化
            float fontSizeDiff = Math.Abs(prevLineInParagraph.FontSize - currentLineToTest.FontSize);
            float avgFontSizeForTolerance = (prevLineInParagraph.FontSize + currentLineToTest.FontSize) / 2f;
            if (avgFontSizeForTolerance <= 0) avgFontSizeForTolerance = 10f;

            if (fontSizeDiff > avgFontSizeForTolerance * PARAGRAPH_FONT_SIZE_TOLERANCE_FACTOR) // 例如，允许10%的差异
            {
                // 如果字体大小差异很大，几乎不可能是同一段落
                // 但也要考虑标题和正文第一行的情况，那个应该由行间距主导
                // return false; // 如果严格要求字号接近，取消此注释
            }


            // 垂直间距检查
            float prevLineBottom = prevLineInParagraph.ApproximateBoundingBox.GetBottom();
            float currentLineTop = currentLineToTest.ApproximateBoundingBox.GetTop();
            float verticalDistance = prevLineBottom - currentLineTop; // Y轴向上，bottom Y < top Y，所以这个值应该是负的或接近0

            // 或者，使用基线的Y坐标来计算行间距，这通常更准确
            // float verticalDistance = prevLineInParagraph.StartPoint.Get(Vector.I2) - currentLineToTest.StartPoint.Get(Vector.I2);
            // 注意：这个距离是基线间的距离，需要和行高比较

            float prevLineHeight = prevLineInParagraph.ApproximateBoundingBox.GetHeight();
            if (prevLineHeight <= 0) prevLineHeight = prevLineInParagraph.FontSize * 1.2f; // 估算行高

            // 行间距必须是正的（上一行的基线在当前行基线之上）且不能过大
            // 使用你之前定义的 PARAGRAPH_LINE_SPACING_FACTOR，但确保其值合理
            if (verticalDistance <= prevLineHeight * 0.1f || // 行重叠过多或顺序反了 (基于基线计算时)
                verticalDistance > prevLineHeight * RELAXED_PARAGRAPH_LINE_SPACING_FACTOR) // 行间距过大
            {
                return false;
            }

            // 水平对齐/缩进检查 (这是最复杂的部分)
            float xIndentTolerance = Math.Max(paragraphFirstLine.FontSize, currentLineToTest.FontSize) * PARAGRAPH_INDENT_TOLERANCE_FACTOR;

            float firstLineX = paragraphFirstLine.ApproximateBoundingBox.GetLeft();
            float prevLineX = prevLineInParagraph.ApproximateBoundingBox.GetLeft();
            float currentLineX = currentLineToTest.ApproximateBoundingBox.GetLeft();

            // 条件1: 当前行与段首行几乎左对齐
            bool alignedWithFirst = Math.Abs(currentLineX - firstLineX) < xIndentTolerance;

            // 条件2: 当前行与上一行几乎左对齐 (用于段落内非首行的对齐)
            bool alignedWithPrev = Math.Abs(currentLineX - prevLineX) < xIndentTolerance;

            // 条件3: 当前行是悬挂缩进 (比首行缩进，但可能和上一行X不同，但上一行必须接近行尾)
            // 并且 currentLineX 比 firstLineX 小 (悬挂)
            float hangingIndentTolerance = Math.Max(paragraphFirstLine.FontSize, currentLineToTest.FontSize) * PARAGRAPH_HANGING_INDENT_TOLERANCE_FACTOR;
            bool isHangingIndentCandidate = currentLineX < firstLineX - xIndentTolerance && // 当前行比首行明显靠左 (悬挂)
                                            Math.Abs(currentLineX - prevLineX) > xIndentTolerance; // 且和上一行不对齐

            // 获取页面/列宽度估算 (这个 GetPageWidth 可能需要改进为获取列宽)
            float pageWidthApproximation = GetPageWidth(prevLineInParagraph.PageNum); // 你原来的方法
                                                                                      // 或者用一个固定的估算值 float columnWidthApproximation = 300f; // 假设平均列宽

            // 上一行是否接近行尾 (行宽的百分比)
            bool prevLineEndsNearMargin = (prevLineInParagraph.ApproximateBoundingBox.GetRight() > firstLineX + pageWidthApproximation * 0.65);
            // 或者, 上一行文本长度接近某个阈值 (假设平均每行字符数)
            // bool prevLineIsLongEnough = prevLineInParagraph.Text.Length > 50;


            // 决策逻辑:
            // 1. 如果上一行已接近边距（或足够长），则下一行可以有一定程度的缩进变化（包括正常的换行或悬挂缩进的开始）
            if (prevLineEndsNearMargin)
            {
                // 如果下一行与首行对齐，或者与上一行对齐（允许小的浮动），或者是合理的悬挂缩进开始
                // 允许 currentLineX 比 firstLineX 稍大 (普通缩进) 或稍小 (悬挂缩进的开始)
                // 主要检查 currentLineX 不会太靠右（新段落的典型特征）
                if (currentLineX > firstLineX + hangingIndentTolerance * 2 && currentLineX > prevLineX + hangingIndentTolerance * 2)
                {
                    // 如果当前行X比首行和上一行都大幅度靠右，可能是新段落了
                    return false;
                }
                return true; // 上一行很长，下一行很可能是接续
            }
            else // 上一行较短 (未填满)
            {
                // 如果上一行很短就结束了，那么下一行必须严格与段首行对齐，或者与上一行对齐
                // 不允许在这种情况下出现新的、与首行和上一行都不同的缩进
                if (alignedWithFirst || alignedWithPrev)
                {
                    return true;
                }
                // 特例：如果 prevLine 很短，currentLine 也很短，但它们 X 对齐且 Y 连续，也可能是列表项等
                // 但这个逻辑容易误判，暂时保守处理

                return false; // 上一行短，当前行又不对齐，认为是新段落
            }
        }

        private float GetPageWidth(int pageNum) // 简化 GetPageWidth
        {
            var elementsOnPage = _elements.Where(
                e => e.PageNum == pageNum && 
                e.ApproximateBoundingBox != null && 
                e.ApproximateBoundingBox.GetWidth() > 0)
                .ToList(); // 使用成员变量 _elements
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