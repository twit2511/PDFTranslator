using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace PDFTranslate // 修改这里
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        public ObservableCollection<FileInfoItem> FileItems { get; set; }

        public MainWindow()
        {
            InitializeComponent();
            FileItems = new ObservableCollection<FileInfoItem>();
            // 如果 FileInfoItem 类定义在 MainWindow.xaml.cs 内部，则不需要单独修改它的 namespace
            this.DataContext = this;
        }

        private void AddFile_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "PDF Files (*.pdf)|*.pdf|All files (*.*)|*.*";
            openFileDialog.Title = "选择要添加的文件";

            if (openFileDialog.ShowDialog() == true)
            {
                string filePath = openFileDialog.FileName;

                if (!FileItems.Any(item => item.FullPath.Equals(filePath, StringComparison.OrdinalIgnoreCase)))
                {
                    FileInfoItem newItem = new FileInfoItem // 确保 FileInfoItem 可访问
                    {
                        FileName = Path.GetFileName(filePath),
                        FullPath = filePath
                    };
                    FileItems.Add(newItem);
                }
                else
                {
                    MessageBox.Show($"文件 '{Path.GetFileName(filePath)}' 已存在于列表中。", "重复添加", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private void OpenButton_Click(object sender, RoutedEventArgs e)
        {
            Button clickedButton = sender as Button;
            if (clickedButton != null)
            {
                string filePath = clickedButton.Tag as string;
                if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                {
                    try
                    {
                        ProcessStartInfo psi = new ProcessStartInfo(filePath) { UseShellExecute = true };
                        Process.Start(psi);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"无法打开文件: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    MessageBox.Show("文件路径无效或文件不存在。", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private void TranslateButton_Click(object sender, RoutedEventArgs e)
        {
            Button clickedButton = sender as Button;
            if (clickedButton != null)
            {
                string filePath = clickedButton.Tag as string;
                if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                {
                    // --- 调用翻译逻辑 ---
                    MessageBox.Show($"已触发翻译操作，文件路径:\n{filePath}", "翻译占位符", MessageBoxButton.OK, MessageBoxImage.Information);
                    // 这里你需要添加实际打开翻译窗口或执行翻译的代码
                    // 例如:
                    // TranslateWindow translateWin = new TranslateWindow(filePath); // 假设你有翻译窗口
                    // translateWin.Owner = this;
                    // translateWin.Show(); // 或者 ShowDialog()
                }
                else
                {
                    MessageBox.Show("文件路径无效或文件不存在。", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        // 如果 FileInfoItem 类定义在这里，它会自动使用 PDFTranslate 命名空间
        // public class FileInfoItem { ... }
    }

    // 如果 FileInfoItem 类定义在 MainWindow.xaml.cs 外部但在同一个文件中（不推荐）
    // public class FileInfoItem { ... }
}