using System.Linq;
using iText.Kernel.Geom;
using System.Collections.Generic;
using System;
using PDFTranslate.PDFProcessor.PDFElements;

namespace PDFTranslate.PDFProcessor
{
    /// <summary>
    /// 包含用于分析页面结构（目前主要是表格）的静态方法。
    /// </summary>
    public static class StructureAnalyzer
    {
        // 表格检测常量
        private const float TABLE_LINE_CLUSTER_TOLERANCE = 5.0f; // 合并附近线条的容差 (单位: PDF点)
        private const float TABLE_CELL_PADDING = 1.0f;        // 判断文本是否在单元格内时，留出的边距
        // 路径处理常量
        private const float MIN_LINE_LENGTH = 5.0f; // 忽略过短的线条
        private const float LINE_DETECTION_TOLERANCE = 1.5f; // 水平/垂直判断容差

        /// <summary>
        /// 对单个页面的元素列表进行结构分析（目前只检测表格）。
        /// </summary>
        /// <param name="pageElements">从监听器获取的原始页面元素列表。</param>
        /// <returns>经过分析处理后的元素列表（部分TextElement的Type可能被更新）。</returns>
        public static List<IPDFElement> AnalyzePageStructure(List<IPDFElement> pageElements)
        {
            if (pageElements == null || !pageElements.Any()) return pageElements;

            // 1. 按类型分离元素
            var textElements = pageElements.Cast<TextElement>()
                                           .OrderBy(t => t.ApproximateBoundingBox.GetY()) // 按 Y 排序 (方便调试和某些分析)
                                           .ThenBy(t => t.ApproximateBoundingBox.GetX()) // 再按 X 排序
                                           .ToList();
            var lineElements = pageElements.Cast<LineElement>().ToList();

            // 2. 表格检测（基于线条）
            var horizontalLines = lineElements.Where(l => l.IsHorizontal(LINE_DETECTION_TOLERANCE)).ToList();
            var verticalLines = lineElements.Where(l => l.IsVertical(LINE_DETECTION_TOLERANCE)).ToList();

            DetectTablesFromLines(textElements, horizontalLines, verticalLines);

            // (如果需要其他分析，如基于对齐的表格检测，可在此处添加)

            return pageElements; // 返回可能被修改过的列表
        }

        /// <summary>
        /// 基于提取到的水平线和垂直线来检测表格结构，并更新文本元素的类型。
        /// </summary>
        private static void DetectTablesFromLines(List<TextElement> textElements, List<LineElement> horizontalLines, List<LineElement> verticalLines)
        {
            // a. 聚类线条坐标，得到网格线
            var gridY = ClusterLineCoordinates(horizontalLines, v => v.Get(Vector.I2), TABLE_LINE_CLUSTER_TOLERANCE);
            var gridX = ClusterLineCoordinates(verticalLines, v => v.Get(Vector.I1), TABLE_LINE_CLUSTER_TOLERANCE);

            // 必须至少有两条横线和两条竖线才能构成网格
            if (gridY.Count < 2 || gridX.Count < 2) return;

            // 对网格线坐标排序
            gridY.Sort();
            gridX.Sort();

            // b. 根据网格线确定单元格边界
            var cells = new List<Tuple<Rectangle, int, int>>(); // 存储单元格边界、行号、列号
            for (int r = 0; r < gridY.Count - 1; r++)
            {
                for (int c = 0; c < gridX.Count - 1; c++)
                {
                    // PDF Y 轴向上, 所以行 r 对应 Y 坐标 gridY[r] 到 gridY[r+1]
                    float bottom = gridY[r];
                    float top = gridY[r + 1];
                    float left = gridX[c];
                    float right = gridX[c + 1];

                    // 确保坐标有效 (防止因容差导致的小错误)
                    if (top > bottom && right > left)
                    {
                        // 行号从0开始，从上到下计数 (所以用 gridY.Count - 2 - r)
                        cells.Add(Tuple.Create(new Rectangle(left, bottom, right - left, top - bottom), gridY.Count - 2 - r, c));
                    }
                }
            }

            if (!cells.Any()) return; // 没有有效的单元格

            // c. 将文本元素分配到其所属的单元格
            foreach (var textElement in textElements)
            {
                // 跳过已经确定是公式的文本，它们不可能是表格内容
                if (textElement.ElementType == PDFElementType.Formula) continue;

                // 使用文本块的中心点来判断归属
                Point textCenter = GetCenter(textElement.ApproximateBoundingBox);

                foreach (var cellInfo in cells)
                {
                    Rectangle cellRect = cellInfo.Item1;
                    // 判断中心点是否严格落在单元格内部（考虑一点内边距）
                    if (textCenter.GetX() > cellRect.GetLeft() + TABLE_CELL_PADDING &&
                        textCenter.GetX() < cellRect.GetRight() - TABLE_CELL_PADDING &&
                        textCenter.GetY() > cellRect.GetBottom() + TABLE_CELL_PADDING &&
                        textCenter.GetY() < cellRect.GetTop() - TABLE_CELL_PADDING)
                    {
                        
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// 辅助方法：将一组线条的某个坐标值进行聚类。
        /// </summary>
        private static List<float> ClusterLineCoordinates(List<LineElement> lines, Func<Vector, float> getCoord, float tolerance)
        {
            if (lines == null || !lines.Any()) return new List<float>();

            // 提取所有起点和终点的相关坐标，去重并排序
            var coords = lines.SelectMany(l => new[] { getCoord(l.StartPoint), getCoord(l.EndPoint) }).Distinct().ToList();
            coords.Sort();

            if (coords.Count < 2) return coords; // 如果坐标数少于2，无法聚类

            var clustered = new List<float>();
            float currentClusterStartValue = coords[0];
            int countInCluster = 1;

            // 遍历排序后的坐标，合并距离接近的
            for (int i = 1; i < coords.Count; i++)
            {
                if (coords[i] - currentClusterStartValue <= tolerance)
                {
                    // 当前坐标在容差范围内，将其加入当前聚类，并更新聚类中心（简单平均）
                    currentClusterStartValue = (currentClusterStartValue * countInCluster + coords[i]) / (countInCluster + 1);
                    countInCluster++;
                }
                else
                {
                    // 超出容差，结束上一个聚类，将聚类中心添加到结果列表
                    clustered.Add(currentClusterStartValue);
                    // 开始新的聚类
                    currentClusterStartValue = coords[i];
                    countInCluster = 1;
                }
            }
            // 添加最后一个聚类
            clustered.Add(currentClusterStartValue);

            // 再次去重确保唯一性（理论上不需要，但保险起见）
            return clustered.Distinct().ToList();
        }

        /// <summary>
        /// 辅助方法：计算矩形的中心点。
        /// </summary>
        private static Point GetCenter(Rectangle rect)
        {
            // iText Point 使用 double，这里保持一致
            return new Point(rect.GetX() + rect.GetWidth() / 2.0, rect.GetY() + rect.GetHeight() / 2.0);
        }
    }

}
