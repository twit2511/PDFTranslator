using Microsoft.Win32;
using PDFTranslate.PDFProcessor.PDFExtractors;
using PDFTranslate.PDFProcessor.PDFBuilder;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using iText.Kernel.Pdf;

namespace PDFTranslate 
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        public ObservableCollection<FileInfoItem> FileList { get; set; }

        public MainWindow()
        {
            InitializeComponent();
            FileList = new ObservableCollection<FileInfoItem>();
            this.DataContext = this;
        }

        private void AddFileButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf|All files (*.*)|*.*",
                Title = "选择要添加的文件"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                string filePath = openFileDialog.FileName;

                if (!FileList.Any(item => item.FullPath.Equals(filePath, StringComparison.OrdinalIgnoreCase)))
                {
                    FileInfoItem newItem = new FileInfoItem // 确保 FileInfoItem 可访问
                    {
                        FileName = Path.GetFileName(filePath),
                        FullPath = filePath
                    };
                    FileList.Add(newItem);
                }
                else
                {
                    MessageBox.Show($"文件 '{Path.GetFileName(filePath)}' 已存在于列表中。", "重复添加", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void OpenButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button clickedButton)
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

        private void RemoveButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button clickedButton)
            {
                string filePath = clickedButton.Tag as string;
                if (!string.IsNullOrEmpty(filePath))
                {
                    FileInfoItem itemToRemove = FileList.FirstOrDefault(item => item.FullPath.Equals(filePath, StringComparison.OrdinalIgnoreCase));
                    if (itemToRemove != null)
                    {
                        FileList.Remove(itemToRemove);
                    }
                }
            }
        }

        private void TranslateButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button clickedButton)
            {
                string filePath = clickedButton.Tag as string;
                if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                {
                    string outPutPath = @"E:\test\MyNewPdf.pdf";
                    // --- 调用翻译逻辑 ---
                    //MessageBox.Show($"已触发翻译操作，文件路径:\n{filePath}", "翻译占位符", MessageBoxButton.OK, MessageBoxImage.Information);
                    Console.WriteLine("RebuildStart");
                    Rebulider.RebuildPdf(AdvancedPdfProcessor.ProcessPdf(filePath), outPutPath, filePath);

                }
                else
                {
                    MessageBox.Show("文件路径无效或文件不存在。", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

    }
}