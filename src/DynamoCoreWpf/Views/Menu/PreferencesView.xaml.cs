using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Dynamo.Configuration;
using Dynamo.Controls;
using Dynamo.Core;
using Dynamo.Exceptions;
using Dynamo.Logging;
using Dynamo.UI;
using Dynamo.ViewModels;
using Res = Dynamo.Wpf.Properties.Resources;

namespace Dynamo.Wpf.Views
{
    /// <summary>
    /// Interaction logic for PreferencesView.xaml
    /// </summary>
    public partial class PreferencesView : Window
    {
        private readonly PreferencesViewModel viewModel;
        private readonly DynamoViewModel dynViewModel;
        private int scaleValue = 0;
        private List<GroupStyleItem> originalCustomGroupStyles { get; set; }

        // Used for tracking the manage package command event
        // This is not a command any more but we keep it
        // around in a compatible way for now
        private IDisposable managePackageCommandEvent;

        /// <summary>
        /// Storing the original custom styles before the user could update them
        /// </summary>
        private void StoreOriginalCustomGroupStyles()
        {
            originalCustomGroupStyles = new List<GroupStyleItem>();
            foreach (var groupStyle in dynViewModel.PreferenceSettings.GroupStyleItemsList)
            {
                if (!groupStyle.IsDefault)
                {
                    originalCustomGroupStyles.Add(new GroupStyleItem() { GroupStyleId = groupStyle.GroupStyleId, HexColorString = groupStyle.HexColorString, FontSize = groupStyle.FontSize });
                }                
            }
        }

        /// <summary>
        /// Constructor of Preferences View
        /// </summary>
        /// <param name="dynamoViewModel"> Dynamo ViewModel</param>
        public PreferencesView(DynamoView dynamoView)
        {            
            dynViewModel = dynamoView.DataContext as DynamoViewModel;            
            SetupPreferencesViewModel(dynViewModel);

            DataContext = dynViewModel.PreferencesViewModel;
 
            InitializeComponent();
            Dynamo.Logging.Analytics.TrackEvent(
                Actions.Open,
                Categories.Preferences);

            Owner = dynamoView;
            dynViewModel.Owner = this;
            if (DataContext is PreferencesViewModel viewModelTemp)
            {
                viewModel = viewModelTemp;
            }

            InitRadioButtonsDescription();

            //We need to store the ScaleFactor value in a temporary variable always when the Preferences dialog is created.
            scaleValue = dynViewModel.ScaleFactorLog;
            ResetGroupStyleForm();
            StoreOriginalCustomGroupStyles();

            viewModel.RequestShowFileDialog += OnRequestShowFileDialog;
        }

        /// <summary>
        ///Given that the PreferencesViewModel persists through the Dynamo session, 
        ///this method will setup all the necessary properties for when the Preferences window is opened.
        /// </summary>
        private void SetupPreferencesViewModel(DynamoViewModel dynamoViewModel)
        {
            //Clear the Saved Changes label and its corresponding tooltip when the Preferences Modal is opened
            dynamoViewModel.PreferencesViewModel.SavedChangesLabel = string.Empty;
            dynamoViewModel.PreferencesViewModel.SavedChangesTooltip = string.Empty;
            dynamoViewModel.PreferencesViewModel.PackagePathsViewModel?.InitializeRootLocations();
            dynamoViewModel.PreferencesViewModel.TrustedPathsViewModel?.InitializeTrustedLocations();

            // Init package paths for install 
            dynamoViewModel.PreferencesViewModel.InitPackagePathsForInstall();

            // Init all package filters 
            dynamoViewModel.PreferencesViewModel.InitPackageListFilters();

            dynamoViewModel.PreferencesViewModel.TrustedPathsViewModel.PropertyChanged += TrustedPathsViewModel_PropertyChanged;
        }

        /// <summary>
        /// Evaluates if the user interacts over the Trusted Locations
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void TrustedPathsViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            List<string> actions = typeof(TrustedPathViewModel.Action).GetFields().Select(a => a.Name).ToList();

            if (actions.Contains(e.PropertyName))
            {
                dynViewModel.CheckCurrentFileInTrustedLocation();
            }
        }

        /// <summary>
        /// Add inline description for each geometry scalling radio button
        /// </summary>
        private void InitRadioButtonsDescription()
        {
            RadioSmallDesc.Inlines.Add(viewModel.OptionsGeometryScale.DescriptionScaleRange[0]);

            RadioMediumDesc.Inlines.Add(new Run(Res.ChangeScaleFactorPromptDescriptionDefaultSetting) { FontWeight = FontWeights.Bold });
            RadioMediumDesc.Inlines.Add(" " + viewModel.OptionsGeometryScale.DescriptionScaleRange[1]);

            RadioLargeDesc.Inlines.Add(viewModel.OptionsGeometryScale.DescriptionScaleRange[2]);

            RadioExtraLargeDesc.Inlines.Add(viewModel.OptionsGeometryScale.DescriptionScaleRange[3]);
        }

        /// <summary>
        /// Dialog close button handler
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            managePackageCommandEvent?.Dispose();
            Analytics.TrackEvent(Actions.Close, Categories.Preferences);
            viewModel.PackagePathsViewModel.SaveSettingCommand.Execute(null);
            viewModel.TrustedPathsViewModel?.SaveSettingCommand?.Execute(null);
            dynViewModel.ShowHideFileTrustWarningIfCurrentWorkspaceTrusted();

            viewModel.CommitPackagePathsForInstall();
            PackagePathView.Dispose();
            TrustedPathView.Dispose();
            Dispose();

            RunGraphWhenScaleFactorUpdated();

            dynViewModel.PreferencesViewModel.TrustedPathsViewModel.PropertyChanged -= TrustedPathsViewModel_PropertyChanged;
            dynViewModel.CheckCustomGroupStylesChanges(originalCustomGroupStyles);

            Close();
        }

        /// <summary>
        /// This method will run the graph only if the Geometry Scaling was updated otherwise will not be executed
        /// </summary>
        private void RunGraphWhenScaleFactorUpdated()
        {
            //If the new radio button selected (ScaleValue) is different than the current one in Dynamo, we update the current one
            if (dynViewModel.ScaleFactorLog != scaleValue)
            {
                dynViewModel.ScaleFactorLog = scaleValue;
                dynViewModel.CurrentSpace.HasUnsavedChanges = true;

                //Due that binding are done before the contructor of this class we need to execute the Log only if the viewModel was assigned previously
                if (viewModel != null)
                {
                    Log(String.Format("Geometry working range changed to {0} ({1}, {2})",
                    viewModel.ScaleRange.Item1, viewModel.ScaleRange.Item2, viewModel.ScaleRange.Item3));
                    viewModel.UpdateSavedChangesLabel();
                    Dynamo.Logging.Analytics.TrackEvent(
                        Actions.Switch,
                        Categories.Preferences,
                        Res.PreferencesViewVisualSettingsGeoScaling);
                }

                var allNodes = dynViewModel.HomeSpace.Nodes;
                dynViewModel.HomeSpace.MarkNodesAsModifiedAndRequestRun(allNodes, forceExecute: true);
            }
        }

        /// <summary>
        /// handler for preferences dialog dragging action. When the TitleBar is clicked this method will be executed.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void PreferencesPanel_MouseDown(object sender, MouseButtonEventArgs e)
        {
            //Drag functionality when the TitleBar is clicked with the left button and dragged to another place
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
                Dynamo.Logging.Analytics.TrackEvent(
                    Actions.Move,
                    Categories.Preferences);
            }
        }

        private void AddStyleButton_Click(object sender, RoutedEventArgs e)
        {
            viewModel.IsVisibleAddStyleBorder = true;
            viewModel.IsEnabledAddStyleButton = false;
            groupNameBox.Focus();
            Logging.Analytics.TrackEvent(Actions.New, Categories.GroupStyleOperations, nameof(GroupStyleItem));
        }

        private void ResetGroupStyleForm()
        {
            viewModel.CurrentWarningMessage = string.Empty;
            viewModel.IsWarningEnabled = false;
            viewModel.IsSaveButtonEnabled = true;
            viewModel.IsVisibleAddStyleBorder = false;
            viewModel.IsEnabledAddStyleButton = true;
        }

        private void AddStyle_SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var saveChangesButton = sender as Button;

            //Get the Grid that contains the Stack Panel that also contains the info related to the new style
            var grid = (saveChangesButton.Parent as Grid).Parent as Grid;

            var groupNameLabel = grid.FindName("groupNameBox") as TextBox;
            var colorHexString = grid.FindName("colorHexVal") as Label;
            var groupStyleFontSize = grid.FindName("groupStyleFontSize") as ComboBox;
            var groupStyleId = Guid.NewGuid();

            var newItem = new StyleItem() { Name = groupNameLabel.Text, HexColorString = colorHexString.Content.ToString(), FontSize = Convert.ToInt32(groupStyleFontSize.SelectedValue), GroupStyleId = groupStyleId };

            if (string.IsNullOrEmpty(newItem.Name))
                newItem.Name = "Input";

            //if the validation returns false it means that the new style that will be added doesn't exists
            if (string.IsNullOrEmpty(groupNameLabel.Text))
            {
                viewModel.EnableGroupStyleWarningState(Res.PreferencesViewEmptyStyleWarning);
            }
            //Means that the Style name to be created already exists
            else if (viewModel.IsStyleNameValid(newItem))
            {
                viewModel.EnableGroupStyleWarningState(Res.PreferencesViewAlreadyExistingStyleWarning);
            }
            //Means that the Style will be created successfully.
            else
            {
                viewModel.AddStyle(newItem);
                viewModel.ResetAddStyleControl();
                Logging.Analytics.TrackEvent(Actions.Save, Categories.GroupStyleOperations, nameof(GroupStyleItem));
            }          
        }

        private void AddStyle_CancelButton_Click(object sender, RoutedEventArgs e)
        {
            viewModel.ResetAddStyleControl();
            Logging.Analytics.TrackEvent(Actions.Cancel, Categories.GroupStyleOperations, nameof(GroupStyleItem));
        }

        private void RemoveStyle_Click(object sender, RoutedEventArgs e)
        {
           var removeButton = sender as Button;

            //Get the Grid that contains all the buttons in the StyleItem
           var grid = (removeButton.Parent as Grid).Parent as Grid;

            //Find inside the Grid the label that contains the GroupName (unique id)
           var groupNameLabel = grid.FindName("groupNameLabel") as TextBlock;

            //Remove the selected style from the list
            viewModel.RemoveStyleEntry(groupNameLabel.Text.ToString());
            Logging.Analytics.TrackEvent(Actions.Delete, Categories.GroupStyleOperations, nameof(GroupStyleItem));
        }

        private void ButtonColorPicker_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Forms.ColorDialog colorDialog = new System.Windows.Forms.ColorDialog();

            if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                Button colorButton = sender as Button;
                if (colorButton != null)
                    colorButton.Background = new SolidColorBrush(Color.FromRgb(colorDialog.Color.R, colorDialog.Color.G, colorDialog.Color.B));
            }
        }

        private void onChangedGroupStyleColor_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Forms.ColorDialog colorDialog = new System.Windows.Forms.ColorDialog();

            if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                Button colorButton = sender as Button;
                
                if (colorButton != null)
                {
                    GroupStyleItem selectedGroupStyle = (GroupStyleItem)colorButton.DataContext;
                    selectedGroupStyle.HexColorString = colorDialog.Color.R.ToString("X2") + colorDialog.Color.G.ToString("X2") + colorDialog.Color.B.ToString("X2");
                }                
            }
        }

        /// <summary>
        /// This event is generated every time the user clicks a Radio Button in the Geometry Scaling section
        /// The method just get the Radio Button clicked and saves the ScaleValue selected
        /// This are the values used for the scales:
        /// - 2 - Small
        ///   0 - Medium (Default)
        ///   2 - Large
        ///   4 - Extra Large
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Geometry_Scaling_Checked(object sender, RoutedEventArgs e)
        {
            RadioButton selectedScaling = sender as RadioButton;
            var radioButtons = GeometryScalingRadiosPanel.Children.OfType<RadioButton>();

            int index = 0;      

            //We need to loop all the radiobuttons in the GeometryScaling section in order to find the index of the selected one
            foreach (var radio in radioButtons)
            {
                if(radio == selectedScaling)
                {
                    scaleValue = GeometryScalingOptions.ConvertUIToScaleFactor(index);
                    break;
                }
                index++;
            }
        }

        private void Log(ILogMessage obj)
        {
            dynViewModel.Model.Logger.Log(obj);
        }

        private void Log(string message)
        {
            Log(LogMessage.Info(message));
        }

        /// <summary>
        /// Unified handler for more info request from mouse left button click
        /// </summary>
        /// <param name="sender">sender control</param>
        /// <param name="e"></param>
        private void OnMoreInfoClicked(object sender, RoutedEventArgs e)
        {
            if (sender is Label lable)
            {
                if (lable.Name == "Titleinfo")
                {
                    dynViewModel.OpenDocumentationLinkCommand.Execute(new OpenDocumentationLinkEventArgs(new Uri(Wpf.Properties.Resources.NodeAutocompleteDocumentationUriString, UriKind.Relative)));
                }
                else if (lable.Name == "TrustWarningInfoLabel")
                {
                    dynViewModel.OpenDocumentationLinkCommand.Execute(new OpenDocumentationLinkEventArgs(new Uri(Wpf.Properties.Resources.FileTrustWarningDocumentationUriString, UriKind.Relative)));

                }
            }
        }

        private void ReloadCPython_Click(object sender, RoutedEventArgs e)
        {
            dynViewModel.Model.OnRequestPythonReset(PythonServices.PythonEngineManager.CPython3EngineName);
        }

        private void InstalledPackagesExpander_OnExpanded(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource == e.Source)
            {
                managePackageCommandEvent = Analytics.TrackCommandEvent("ManagePackage");
            }
        }

        private void InstalledPackagesExpander_OnCollapsed(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource == e.Source)
            {
                managePackageCommandEvent?.Dispose();
            }
        }

        private void groupNameBox_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            var groupNameTextBox = sender as TextBox;
            if (groupNameBox == null) return;
            if (string.IsNullOrEmpty(groupNameBox.Text))
            {
                viewModel.IsSaveButtonEnabled = false;
                if (e.Key == Key.Return)
                {
                    viewModel.EnableGroupStyleWarningState(Res.PreferencesViewAlreadyExistingStyleWarning);
                }
            }
            else
            {
                viewModel.IsSaveButtonEnabled = true;
                viewModel.CurrentWarningMessage = string.Empty;
                viewModel.IsWarningEnabled = false;
                if (e.Key == Key.Return)
                {
                    AddStyle_SaveButton_Click(AddStyle_SaveButton, new RoutedEventArgs());
                }
            }
        }

        private void GroupStylesListBox_LostFocus(object sender, RoutedEventArgs e)
        {
            GroupStylesListBox.UnselectAll();
        }

        private void DisableTrustWarningsChecked(object sender, RoutedEventArgs e)
        {
            if (viewModel != null)
            {
                viewModel.DisableTrustWarnings = (bool)(sender as ToggleButton).IsChecked;
            }
        }

        private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            ScrollViewer scrollviewer = sender as ScrollViewer;
            if (e.Delta > 0)
            {
                scrollviewer.LineUp();
            }
            else
            {
                scrollviewer.LineDown();
            }
            e.Handled = true;
        }

        private void importTextBlock_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            string fileExtension = "*" + Path.GetExtension(PathManager.PreferenceSettingsFileName);
            string[] fileFilter = { string.Format(Res.FileDialogImportSettingsFiles, fileExtension) };
            System.Windows.Forms.OpenFileDialog openFileDialog = new System.Windows.Forms.OpenFileDialog();
            openFileDialog.Filter = String.Join("|", fileFilter);
            openFileDialog.Title = Res.ImportSettingsDialogTitle;
            openFileDialog.Multiselect = false;
            openFileDialog.RestoreDirectory = true;

            var result = openFileDialog.ShowDialog();
            if (result == System.Windows.Forms.DialogResult.OK)
            {
                try
                {
                    bool isImported = viewModel.importSettings(openFileDialog.FileName);
                    if (isImported)
                    {
                        Wpf.Utilities.MessageBoxService.Show(
                            this, Res.ImportSettingsSuccessMessage, Res.ImportSettingsDialogTitle, MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        Wpf.Utilities.MessageBoxService.Show(
                            this, Res.ImportSettingsFailedMessage, Res.ImportSettingsDialogTitle, MessageBoxButton.OK, MessageBoxImage.Exclamation);
                    }
                    Analytics.TrackEvent(Actions.ImportSettings, Categories.Preferences, isImported.ToString());
                }
                catch (Exception ex)
                {
                    Wpf.Utilities.MessageBoxService.Show(
                        this, ex.Message, Res.ImportSettingsFailedMessage, MessageBoxButton.OK, MessageBoxImage.Exclamation);
                }
            }            
        }

        private void exportTextBlock_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var dialog = new DynamoFolderBrowserDialog
            {
                Title = Res.ExportSettingsDialogTitle,
                Owner = this
            };

            //Saves the current settings before exporting the xml file
            dynViewModel.PreferenceSettings.SaveInternal(dynViewModel.Model.PathManager.PreferenceFilePath);

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                string selectedPathFile = Path.Combine(dialog.SelectedPath, PathManager.PreferenceSettingsFileName);
                try
                {
                    if (File.Exists(selectedPathFile))
                    {
                        string uniqueId = ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds().ToString();
                        string suffixPlusDot = $"_{ uniqueId}.";
                        string uniqueFileName = PathManager.PreferenceSettingsFileName.Replace(".", suffixPlusDot);
                        selectedPathFile = Path.Combine(dialog.SelectedPath, uniqueFileName);
                    }

                    File.Copy(dynViewModel.Model.PathManager.PreferenceFilePath, selectedPathFile);
                    string argument = "/select, \"" + selectedPathFile + "\"";
                    System.Diagnostics.Process.Start("explorer.exe", argument);
                    Analytics.TrackEvent(Actions.ExportSettings, Categories.Preferences);
                }
                catch (Exception ex)
                {
                    Wpf.Utilities.MessageBoxService.Show(
                        this,
                        ex.Message,
                        Res.ExportSettingsFailedMessage,
                        MessageBoxButton.OK, MessageBoxImage.Exclamation);
                }
            }
        }

        // Show File path dialog
        private void OnRequestShowFileDialog(object sender, EventArgs e)
        {
            var args = e as PythonTemplatePathEventArgs;
            args.Cancel = true;

            var dialog = new System.Windows.Forms.OpenFileDialog
            {
                // Navigate to initial folder.
                FileName = args.Path
            };

            //Filter python files.
            dialog.Filter = "Python File|*.py";

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                args.Cancel = false;
                args.Path = dialog.FileName;
            }
        }

        // Number input textbox validation
        private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
        {
            Regex regex = new Regex("[^0-9]+");
            e.Handled = regex.IsMatch(e.Text);
        }

        internal void Dispose()
        {
            viewModel.RequestShowFileDialog -= OnRequestShowFileDialog;
        }
    }
}
