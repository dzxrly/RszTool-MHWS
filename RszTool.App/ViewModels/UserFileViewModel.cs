using Microsoft.Win32;
using RszTool.App.Common;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;

namespace RszTool.App.ViewModels
{
    public class UserFileViewModel(UserFile file) : BaseRszFileViewModel
    {
        public override BaseRszFile File => UserFile;
        public UserFile UserFile { get; } = file;

        public bool ResourceChanged
        {
            get => UserFile.ResourceChanged;
            set => UserFile.ResourceChanged = value;
        }

        public RszViewModel RszViewModel => new(UserFile.RSZ!);

        public override IEnumerable<object> TreeViewItems
        {
            get
            {
                yield return new TreeItemViewModel("Resources", UserFile.ResourceInfoList);
                yield return new TreeItemViewModel("Instances", RszViewModel.Instances);
                yield return new TreeItemViewModel("Objects", RszViewModel.Objects);
            }
        }

        private RelayCommand? exportJsonCommand;
        public ICommand ExportJsonCommand => exportJsonCommand ??= new RelayCommand(_ => ExportJson());

        private void ExportJson()
        {
            try
            {
                var saveFileDialog = new SaveFileDialog
                {
                    Filter = "JSON文件|*.json",
                    Title = "保存JSON文件",
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    var options = new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        Converters = { new RszInstanceJsonConverter() }
                    };

                    string json = JsonSerializer.Serialize(RszViewModel.Objects, options);
                    System.IO.File.WriteAllText(saveFileDialog.FileName, json);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导出JSON时发生错误：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
