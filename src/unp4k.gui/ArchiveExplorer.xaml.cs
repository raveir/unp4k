﻿using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.Zip;
using Microsoft.Win32;
using Ookii.Dialogs.Wpf;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using unp4k.gui.Extensions;
using unp4k.gui.TreeModel;
using Path = System.IO.Path;

namespace unp4k.gui
{
	//public class OpenFileCommand : ICommand
	//{
	//	public void Execute(Object parameter)
	//	{
	//		MessageBox.Show(@"""Hello, world!"" from "
	//			+ (parameter ?? "somewhere secret").ToString());
	//	}

	//	public Boolean CanExecute(Object parameter)
	//	{
	//		return true;
	//	}

	//	public event EventHandler CanExecuteChanged;
	//}

	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class ArchiveExplorer : Window
	{
		private Stream _pakFile;
		private ZipFile _pak;
		private TreeExtractor _extractor;
		private ZipFileTreeItem _root;

		public const Int32 FILTER_DELAY = 250;
		public const Int32 FILTER_PING = 50;

		public ArchiveExplorer()
		{
			InitializeComponent();

			this.Icon = IconManager.GetCachedFileIcon("data.zip", IconManager.IconSize.Large);

			trvFileExplorer.Focus();

			new Thread(async () =>
			{
				while (true)
				{
					await Task.Delay(FILTER_PING);

					while (this._lastFilterText != this._activeFilterText)
					{
						var now = this._lastFilterTime ?? DateTime.Now;

						while ((DateTime.Now - now).TotalMilliseconds < FILTER_DELAY)
						{
							await Task.Delay(FILTER_PING);
							now = this._lastFilterTime ?? DateTime.Now;
						}

						var filterText = this._lastFilterText;
						
						await this.NotifyNodesAsync(this._root);
						
						this._activeFilterText = filterText;

						await Task.Delay(FILTER_PING);
					}
				}
			}).Start();
		}

		~ArchiveExplorer()
		{
			if (this._pak != null)
			{
				this._pak.Close();
				this._pak = null;
			}

			if (this._pakFile != null)
			{
				this._pakFile.Dispose();
				this._pakFile = null;
			}
		}

		public async Task OpenP4kAsync(String path)
		{
			TreeView treeView = this.trvFileExplorer;

			var pakFile = File.OpenRead(path);
			var pak = new ZipFile(pakFile);

			var root = new ZipFileTreeItem(pak, Path.GetFileName(path));

			var filter = this._lastFilterText;

			if (filter.Equals("Filter...", StringComparison.InvariantCultureIgnoreCase)) filter = null;

			await this.Dispatcher.Invoke(async () =>
			{
				treeView.Items.Clear();

				if (this._pak != null)
				{
					this._pak.Close();
					this._pak = null;
				}

				if (this._pakFile != null)
				{
					this._pakFile.Dispose();
					this._pakFile = null;
				}

				this._pak = pak;
				this._pakFile = pakFile;

				this._extractor = new TreeExtractor(pak, this.Filter);
				this._root = root;

				treeView.Items.Add(root);
			});
		}

		public Predicate<Object> Filter => (Object n) =>
		{
			var filter = this._lastFilterText;

			if (String.IsNullOrWhiteSpace(filter)) return true;

			if (n is IBranchItem branch)
			{
				var result = branch.AllChildren.Any(z => z.RelativePath.Contains(filter, CompareOptions.IgnoreCase | CompareOptions.IgnoreSymbols));
				return result;
			}

			if (n is IStreamTreeItem leaf)
			{
				var result = leaf.RelativePath.Contains(filter, CompareOptions.IgnoreCase | CompareOptions.IgnoreSymbols);
				return result;
			}

			return false;
		};

		private void trvFileExplorer_Expanded(object sender, RoutedEventArgs e)
		{
			var node = e.OriginalSource as TreeViewItem;

			if (node != null)
			{
				node.Items.SortDescriptions.Clear();
				node.Items.SortDescriptions.Add(new SortDescription("SortKey", ListSortDirection.Ascending));

				node.Items.Filter = this.Filter;

				if (node.DataContext is IBranchItem branchItem)
				{
					branchItem.Expanded = true;
				}
			}
		}

		private void trvFileExplorer_Collapsed(object sender, RoutedEventArgs e)
		{
			var node = e.OriginalSource as TreeViewItem;

			if (node != null)
			{
				if (node.DataContext is IBranchItem branchItem)
				{
					branchItem.Expanded = false;
				}
			}
		}

		private async void cmdOpenArchive_Executed(Object sender, ExecutedRoutedEventArgs e)
		{
			var openFileDialog = new VistaOpenFileDialog
			{
				Filter = "Star Citizen Data Files|*.p4k",
				CheckFileExists = true,
				AddExtension = true,
				DefaultExt = ".p4k"
			};

			if (openFileDialog.ShowDialog() == true)
			{
				// Move to background thread
				new Thread(async () => await this.OpenP4kAsync(openFileDialog.FileName)).Start();
			}

			await Task.CompletedTask;
		}

		private async void cmdExtractFile_Executed(Object sender, ExecutedRoutedEventArgs e)
		{
			var selectedItem = trvFileExplorer.SelectedItem as ITreeItem;
			
			if (selectedItem == null) return;

			// Move to background thread
			new Thread(async () => await this._extractor.ExtractNodeAsync(selectedItem, false)).Start();

			await Task.CompletedTask;
		}

		private async void cmdOpenFile_Executed(Object sender, ExecutedRoutedEventArgs e)
		{
			var selectedItem = trvFileExplorer.SelectedItem as IStreamTreeItem;

			if (selectedItem == null) return;

			// Move to background thread
			new Thread(async () => await this._extractor.ExtractNodeAsync(selectedItem, true)).Start();

			await Task.CompletedTask;
		}

		#region Mouse Support

		private async void trvFileExplorer_MouseDoubleClick(object sender, MouseButtonEventArgs e)
		{
			var selectedNode = e.OriginalSource as TreeViewItem;

			if (selectedNode == null) return;

			var selectedItem = selectedNode.DataContext as ITreeItem;

			if (selectedItem == null) return;

			// Move to background thread
			new Thread(async () => await this._extractor.ExtractNodeAsync(selectedItem, false)).Start();

			await Task.CompletedTask;
		}

		#endregion

		#region Keyboard Support

		// private Dictionary<Key, Boolean> keyState = new Dictionary<Key, Boolean> { { Key.Enter, false } };
		// 
		// private async void trvFileExplorer_KeyDown(object sender, KeyEventArgs e)
		// {
		// 	this.keyState[e.Key] = true;
		// 
		// 	await Task.CompletedTask;
		// }
		// 
		// private async void trvFileExplorer_KeyUp(object sender, KeyEventArgs e)
		// {
		// 	if (this.keyState[Key.Enter])
		// 	{
		// 		var selectedItem = trvFileExplorer.SelectedItem as TreeModel.TreeItem;
		// 
		// 		if (selectedItem != null)
		// 		{
		// 			var useTemp = trvFileExplorer.SelectedItem is ZipEntryTreeItem;
		// 
		// 			new Thread(async () => await this._extractor.ExtractNodeAsync(selectedItem, useTemp)).Start();
		// 		}
		// 	}
		// 
		// 	this.keyState[e.Key] = false;
		// 
		// 	await Task.CompletedTask;
		// }

		#endregion

		#region Inbound Drag and Drop Support

		private async void trvFileExplorer_Drop(object sender, DragEventArgs e)
		{
			if (e.Data.GetDataPresent(DataFormats.FileDrop))
			{
				String[] files = (String[])e.Data.GetData(DataFormats.FileDrop);

				var path = files.Where(f => Path.GetExtension(f).Equals(".p4k", StringComparison.InvariantCultureIgnoreCase)).FirstOrDefault();

				if (!String.IsNullOrWhiteSpace(path))
				{
					await this.OpenP4kAsync(path);
				}
			}
		}

		#endregion

		#region Filter Support
		
		private DateTime? _lastFilterTime;
		private String _lastFilterText = String.Empty;
		private String _activeFilterText = String.Empty;
		
		private async Task NotifyNodesAsync(ITreeItem node)
		{
			Dispatcher.Invoke(() =>
			{
				node.Children.Touch();
			});

			if (node is IBranchItem branchItem && branchItem.Expanded)
			{
				node.Children.AsParallel().ForAll(async item => await this.NotifyNodesAsync(item));
			}

			await Task.CompletedTask;
		}

		private async void txtFilter_TextChanged(object sender, TextChangedEventArgs e)
		{
			var filter = txtFilter.Text;
			if (filter.Equals("Filter...", StringComparison.InvariantCultureIgnoreCase)) filter = String.Empty;

			if (filter == this._lastFilterText) return;

			this._lastFilterTime = DateTime.Now;
			this._lastFilterText = filter;

			await Task.CompletedTask;
		}

		#endregion

		#region Placeholder Text Support

		private void txtFilter_GotFocus(object sender, RoutedEventArgs e)
		{
			if (txtFilter.Text.Equals("Filter...", StringComparison.InvariantCultureIgnoreCase))
			{
				txtFilter.Text = String.Empty;
			}
		}

		private void txtFilter_LostFocus(object sender, RoutedEventArgs e)
		{
			if (String.IsNullOrWhiteSpace(txtFilter.Text))
			{
				txtFilter.Text = "Filter...";
			}
		}

		#endregion

		#region Outbound Drag and Drop Support

		private Point _start;

		private void trvFileExplorer_PreviewMouseDown(object sender, MouseButtonEventArgs e)
		{
			this._start = e.GetPosition(null);
		}

		private void trvFileExplorer_MouseMove(object sender, MouseEventArgs e)
		{
			if (e.LeftButton != MouseButtonState.Pressed) return;
			if (this.trvFileExplorer.SelectedItem == null) return;

			Point mpos = e.GetPosition(null);
			Vector diff = this._start - mpos;

			if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance &&
				Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
			{
				// right about here you get the file urls of the selected items.
				// should be quite easy, if not, ask.
				String[] files = new String[] { };
				String dataFormat = DataFormats.FileDrop;
				DataObject dataObject = new DataObject(dataFormat, files);
				DragDrop.DoDragDrop(this.trvFileExplorer, dataObject, DragDropEffects.Move);
			}
		}

		#endregion
		
		private void cmdExitApplication_Executed(Object sender, ExecutedRoutedEventArgs e)
		{
			Application.Current.Shutdown();
		}

		private void trvFileExplorer_SelectedItemChanged(Object sender, RoutedPropertyChangedEventArgs<Object> e)
		{
			var node = e.OriginalSource as TreeViewItem;
		}
	}
}
