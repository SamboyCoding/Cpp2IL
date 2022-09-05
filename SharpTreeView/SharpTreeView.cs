// Copyright (c) AlphaSierraPapa for the SharpDevelop Team (for details please see \doc\copyright.txt)
// This code is distributed under the GNU LGPL (for details please see \doc\license.txt)

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Generators;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using AvaloniaEdit;

namespace ICSharpCode.TreeView
{
	public class SharpTreeView : ListBox, IStyleable, IRoutedCommandBindable
	{
		static SharpTreeView()
		{
			SelectionModeProperty?.OverrideDefaultValue<SharpTreeView>(SelectionMode.Multiple);
			//ItemsPanelProperty.OverrideDefaultValue<SharpTreeView>(new FuncTemplate<IPanel>(() => new VirtualizingStackPanel()));

			//AlternationCountProperty.OverrideMetadata(typeof(SharpTreeView),
			//                                          new FrameworkPropertyMetadata(2));

			//VirtualizationModeProperty.OverrideDefaultValue<SharpTreeView>(ItemVirtualizationMode.Recycling);

			DragDrop.DragEnterEvent.AddClassHandler<SharpTreeView>((x, e) => x.OnDragEnter(e));
			DragDrop.DragOverEvent.AddClassHandler<SharpTreeView>((x, e) => x.OnDragOver(e));
			DragDrop.DropEvent.AddClassHandler<SharpTreeView>((x, e) => x.OnDrop(e));
		}

		public SharpTreeView()
		{
			SelectionChanged += OnSelectionChanged;
			RegisterCommands();
		}

		public static readonly StyledProperty<SharpTreeNode> RootProperty =
			AvaloniaProperty.Register<SharpTreeView, SharpTreeNode>(nameof(Root));

		public SharpTreeNode Root
		{
			get => GetValue(RootProperty);
			set => SetValue(RootProperty, value);
		}

		public static readonly StyledProperty<bool> ShowRootProperty =
			AvaloniaProperty.Register<SharpTreeView, bool>(nameof(ShowRoot), true);

		public bool ShowRoot
		{
			get => GetValue(ShowRootProperty);
			set => SetValue(ShowRootProperty, value);
		}

		public static readonly StyledProperty<bool> ShowRootExpanderProperty =
			AvaloniaProperty.Register<SharpTreeView, bool>(nameof(ShowRootExpander));

		public bool ShowRootExpander
		{
			get => GetValue(ShowRootExpanderProperty);
			set => SetValue(ShowRootExpanderProperty, value);
		}

		public static readonly StyledProperty<bool> AllowDropOrderProperty =
			AvaloniaProperty.Register<SharpTreeView, bool>(nameof(AllowDropOrder));

		public bool AllowDropOrder
		{
			get => GetValue(AllowDropOrderProperty);
			set => SetValue(AllowDropOrderProperty, value);
		}

		public static readonly StyledProperty<bool> ShowLinesProperty =
			AvaloniaProperty.Register<SharpTreeView, bool>(nameof(ShowLines), true);

		public bool ShowLines {
			get => GetValue(ShowLinesProperty);
			set => SetValue(ShowLinesProperty, value);
		}

		public static readonly StyledProperty<bool> IsTextSearchCaseSensitiveProperty =
			AvaloniaProperty.Register<SharpTreeView, bool>(nameof(IsTextSearchCaseSensitive));

		public bool IsTextSearchCaseSensitive {
			get => GetValue(IsTextSearchCaseSensitiveProperty);
			set => SetValue(IsTextSearchCaseSensitiveProperty, value);
		}

		public new static readonly StyledProperty<bool> IsTextSearchEnabledProperty =
			AvaloniaProperty.Register<SharpTreeView, bool>(nameof(IsTextSearchEnabled), true);

		public new bool IsTextSearchEnabled {
			get => GetValue(IsTextSearchEnabledProperty);
			set => SetValue(IsTextSearchEnabledProperty, value);
		}

		public static bool GetShowAlternation(AvaloniaObject obj)
		{
			return obj.GetValue(ShowAlternationProperty);
		}

		public static void SetShowAlternation(AvaloniaObject obj, bool value)
		{
			obj.SetValue(ShowAlternationProperty, value);
		}

		public static readonly StyledProperty<bool> ShowAlternationProperty =
			AvaloniaProperty.Register<SharpTreeView, bool>("ShowAlternation", false, true);

		Type IStyleable.StyleKey => typeof(ListBox);
		
		protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
		{
			base.OnPropertyChanged(e);
			if (e.Property == RootProperty ||
				e.Property == ShowRootProperty ||
				e.Property == ShowRootExpanderProperty) {
				Reload();
			}
		}

		TreeFlattener flattener;
		bool updatesLocked;

		public IDisposable LockUpdates()
		{
			return new UpdateLock(this);
		}

		class UpdateLock : IDisposable
		{
			SharpTreeView instance;

			public UpdateLock(SharpTreeView instance)
			{
				this.instance = instance;
				this.instance.updatesLocked = true;
			}

			public void Dispose()
			{
				instance.updatesLocked = false;
			}
		}

		void Reload()
		{
			flattener?.Stop();
			if (Root != null) {
				if (!(ShowRoot && ShowRootExpander)) {
					Root.IsExpanded = true;
				}
				flattener = new TreeFlattener(Root, ShowRoot);
				flattener.CollectionChanged += flattener_CollectionChanged;
				Items = flattener;
			}
		}

		void flattener_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			// Deselect nodes that are being hidden, if any remain in the tree
			if (e.Action == NotifyCollectionChangedAction.Remove && Items.Any()) {
				List<SharpTreeNode> selectedOldItems = null;
				foreach (SharpTreeNode node in e.OldItems) {
					if (node.IsSelected) {
						if (selectedOldItems == null)
							selectedOldItems = new List<SharpTreeNode>();
						selectedOldItems.Add(node);
					}
				}
				if (!updatesLocked && selectedOldItems != null) {
					var list = SelectedItems.Cast<SharpTreeNode>().Except(selectedOldItems).ToList();
					UpdateFocusedNode(list, Math.Max(0, e.OldStartingIndex - 1));
				}
			}
		}

		void UpdateFocusedNode(List<SharpTreeNode> newSelection, int topSelectedIndex)
		{
			if (updatesLocked) return;
			SetSelectedNodes(newSelection ?? Enumerable.Empty<SharpTreeNode>());
			if (SelectedItem == null) {
				// if we removed all selected nodes, then move the focus to the node 
				// preceding the first of the old selected nodes
				SelectedIndex = topSelectedIndex;
				if (SelectedItem != null)
					FocusNode((SharpTreeNode)SelectedItem);
			}
		}

		protected override IItemContainerGenerator CreateItemContainerGenerator()
		{
			return new ItemContainerGenerator<SharpTreeViewItem>(
				this,
				SharpTreeViewItem.ContentProperty,
				SharpTreeViewItem.ContentTemplateProperty)
			{
				ItemTemplate = ItemTemplate
			};
		}

		protected override void OnContainersMaterialized(ItemContainerEventArgs e)
		{
			base.OnContainersMaterialized(e);
			foreach (var item in e.Containers) {
				var container = item.ContainerControl as SharpTreeViewItem;
				container.ParentTreeView = this;
				// Make sure that the line renderer takes into account the new bound data
				container.NodeView?.LinesRenderer.InvalidateVisual();
			}
		}

		protected override void OnContainersRecycled(ItemContainerEventArgs e)
		{
			base.OnContainersRecycled(e);

			foreach (var item in e.Containers)
			{
				var container = item.ContainerControl as SharpTreeViewItem;
				container.ParentTreeView = this;
				// Make sure that the line renderer takes into account the new bound data
				container.NodeView?.LinesRenderer.InvalidateVisual();
			}
		}

		internal IControl ContainerFromItem(object item)
		{
			int index = IndexOf(Items, item);
			if (index != -1) {
				return ItemContainerGenerator.ContainerFromIndex(index);
			}
			return null;
		}
		
		bool doNotScrollOnExpanding;
		
		/// <summary>
		/// Handles the node expanding event in the tree view.
		/// This method gets called only if the node is in the visible region (a SharpTreeNodeView exists).
		/// </summary>
		internal void HandleExpanding(SharpTreeNode node)
		{
			if (doNotScrollOnExpanding)
				return;

			SharpTreeNode lastVisibleChild = node;
			while (true) {
				SharpTreeNode tmp = lastVisibleChild.Children.LastOrDefault(c => c.IsVisible);
				if (tmp != null) {
					lastVisibleChild = tmp;
				} else {
					break;
				}
			}
			if (lastVisibleChild != node) {
				// Make the the expanded children are visible; but don't scroll down
				// to much (keep node itself visible)
				base.ScrollIntoView(lastVisibleChild);
				// For some reason, this only works properly when delaying it...
				Dispatcher.UIThread.InvokeAsync(delegate {
					base.ScrollIntoView(node);
				}, DispatcherPriority.Loaded);
			}
		}
		
		protected override void OnKeyDown(KeyEventArgs e)
		{
			SharpTreeViewItem container = e.Source as SharpTreeViewItem;
			switch (e.Key) {
				case Key.Left:
					if (container != null && ItemContainerGenerator.IndexFromContainer(e.Source as IControl) != -1) {
						if (container.Node.IsExpanded) {
							container.Node.IsExpanded = false;
						} else if (container.Node.Parent != null) {
							FocusNode(container.Node.Parent);
						}
						e.Handled = true;
					}
					break;
				case Key.Right:
					// TODO: focus on first child
					if (container != null && ItemContainerGenerator.IndexFromContainer(e.Source as IControl) != -1) {
						if (!container.Node.IsExpanded && container.Node.ShowExpander) {
							container.Node.IsExpanded = true;
						} else if (container.Node.Children.Count > 0) {
							// jump to first child:
							
							//container.MoveFocus(new TraversalRequest(FocusNavigationDirection.Down));
						}
						e.Handled = true;
					}
					break;
				case Key.Return:
				case Key.Space:
					if (container != null && e.KeyModifiers == KeyModifiers.None && SelectedItems.Count == 1 && SelectedItem == container.Node) {
						container.Node.ActivateItem(e);
					}
					break;
				case Key.Add:
					if (container != null && ItemContainerGenerator.IndexFromContainer(e.Source as IControl) != -1) {
						container.Node.IsExpanded = true;
						e.Handled = true;
					}
					break;
				case Key.Subtract:
					if (container != null && ItemContainerGenerator.IndexFromContainer(e.Source as IControl) != -1) {
						container.Node.IsExpanded = false;
						e.Handled = true;
					}
					break;
				case Key.Multiply:
					if (container != null && ItemContainerGenerator.IndexFromContainer(e.Source as IControl) != -1) {
						container.Node.IsExpanded = true;
						ExpandRecursively(container.Node);
						e.Handled = true;
					}
					break;
				case Key.Back:
					if (IsTextSearchEnabled) {
						var instance = SharpTreeViewTextSearch.GetInstance(this);
						if (instance != null) {
							instance.RevertLastCharacter();
							e.Handled = true;
						}
					}
					break;
			}

			foreach (var commandBinding in CommandBindings)
			{
				if (commandBinding.Command.Gesture?.Matches(e) == true)
				{
					commandBinding.Command.Execute(null, this);
					e.Handled = true;
					break;
				}
			}

			if (!e.Handled)
				base.OnKeyDown(e);
		}

		protected override void OnTextInput(TextInputEventArgs e)
		{
			if (!string.IsNullOrEmpty(e.Text) && IsTextSearchEnabled && (e.Source == this || ItemContainerGenerator.IndexFromContainer(e.Source as IControl) != -1)) {
				var instance = SharpTreeViewTextSearch.GetInstance(this);
				if (instance != null) {
					instance.Search(e.Text);
					e.Handled = true;
				}
			}
			if (!e.Handled)
				base.OnTextInput(e);
		}

		void ExpandRecursively(SharpTreeNode node)
		{
			if (node.CanExpandRecursively) {
				node.IsExpanded = true;
				foreach (SharpTreeNode child in node.Children) {
					ExpandRecursively(child);
				}
			}
		}
		
		/// <summary>
		/// Scrolls the specified node in view and sets keyboard focus on it.
		/// </summary>
		public void FocusNode(SharpTreeNode node)
		{
			if (node == null)
				throw new ArgumentNullException("node");
			ScrollIntoView(node);
			// WPF's ScrollIntoView() uses the same if/dispatcher construct, so we call OnFocusItem() after the item was brought into view.
			//if (this.ItemContainerGenerator.Status == GeneratorStatus.ContainersGenerated) {
			//	OnFocusItem(node);
			//} else {
				Dispatcher.UIThread.InvokeAsync(()=> OnFocusItem(node), DispatcherPriority.Loaded);
			//}
		}
		
		public void ScrollIntoView(SharpTreeNode node)
		{
			if (node == null)
				throw new ArgumentNullException("node");
			doNotScrollOnExpanding = true;
			foreach (SharpTreeNode ancestor in node.Ancestors())
				ancestor.IsExpanded = true;
			doNotScrollOnExpanding = false;
			base.ScrollIntoView(node);
		}
		
		object OnFocusItem(object item)
		{
			if (ContainerFromItem(item) is Control element) {
				element.Focus();
			}
			return null;
		}

		#region Track selection

		protected virtual void OnSelectionChanged(object source, SelectionChangedEventArgs e)
		{
			foreach (SharpTreeNode node in e.RemovedItems) {
				node.IsSelected = false;
			}
			foreach (SharpTreeNode node in e.AddedItems) {
				node.IsSelected = true;
			}
		}
		
		#endregion
		
		#region Drag and Drop

		protected virtual void OnDragEnter(DragEventArgs e)
		{
			OnDragOver(e);
		}

		protected virtual void OnDragOver(DragEventArgs e)
		{
			e.DragEffects = DragDropEffects.None;
			
			if (Root != null && !ShowRoot) {
				e.Handled = true;
				Root.CanDrop(e, Root.Children.Count);
			}
		}

		protected virtual void OnDrop(DragEventArgs e)
		{
			e.DragEffects = DragDropEffects.None;

			if (Root != null && !ShowRoot) {
				e.Handled = true;
				Root.InternalDrop(e, Root.Children.Count);
			}
		}

		internal void HandleDragEnter(SharpTreeViewItem item, DragEventArgs e)
		{
			HandleDragOver(item, e);
		}

		internal void HandleDragOver(SharpTreeViewItem item, DragEventArgs e)
		{
			HidePreview();

			var target = GetDropTarget(item, e);
			if (target != null) {
				e.Handled = true;
				ShowPreview(target.Item, target.Place);
			}
		}

		internal void HandleDrop(SharpTreeViewItem item, DragEventArgs e)
		{
			try {
				HidePreview();

				var target = GetDropTarget(item, e);
				if (target != null) {
					e.Handled = true;
					target.Node.InternalDrop(e, target.Index);
				}
			} catch (Exception ex) {
				Trace.WriteLine(ex.ToString());
				throw;
			}
		}

		internal void HandleDragLeave(SharpTreeViewItem item, RoutedEventArgs e)
		{
			HidePreview();
			e.Handled = true;
		}

		class DropTarget
		{
			public SharpTreeViewItem Item;
			public DropPlace Place;
			public double Y;
			public SharpTreeNode Node;
			public int Index;
		}

		DropTarget GetDropTarget(SharpTreeViewItem item, DragEventArgs e)
		{
			var dropTargets = BuildDropTargets(item, e);
			var y = e.GetPosition(item).Y;
			foreach (var target in dropTargets) {
				if (target.Y >= y) {
					return target;
				}
			}
			return null;
		}

		List<DropTarget> BuildDropTargets(SharpTreeViewItem item, DragEventArgs e)
		{
			var result = new List<DropTarget>();
			var node = item.Node;

			if (AllowDropOrder) {
				TryAddDropTarget(result, item, DropPlace.Before, e);
			}

			TryAddDropTarget(result, item, DropPlace.Inside, e);

			if (AllowDropOrder) {
				if (node.IsExpanded && node.Children.Count > 0) {
					var firstChildItem = ItemContainerGenerator.ContainerFromIndex(0) as SharpTreeViewItem;
					//var firstChildItem = ItemContainerGenerator.ContainerFromItem(node.Children[0]) as SharpTreeViewItem;
					TryAddDropTarget(result, firstChildItem, DropPlace.Before, e);
				}
				else {
					TryAddDropTarget(result, item, DropPlace.After, e);
				}
			}

			var h = item.Height;
			var y1 = 0.2 * h;
			var y2 = h / 2;
			var y3 = h - y1;

			if (result.Count == 2) {
				if (result[0].Place == DropPlace.Inside &&
					result[1].Place != DropPlace.Inside) {
					result[0].Y = y3;
				}
				else if (result[0].Place != DropPlace.Inside &&
						 result[1].Place == DropPlace.Inside) {
					result[0].Y = y1;
				}
				else {
					result[0].Y = y2;
				}
			}
			else if (result.Count == 3) {
				result[0].Y = y1;
				result[1].Y = y3;
			}
			if (result.Count > 0) {
				result[result.Count - 1].Y = h;
			}
			return result;
		}

		void TryAddDropTarget(List<DropTarget> targets, SharpTreeViewItem item, DropPlace place, DragEventArgs e)
		{
			SharpTreeNode node;
			int index;

			GetNodeAndIndex(item, place, out node, out index);

			if (node != null) {
				e.DragEffects = DragDropEffects.None;
				if (node.CanDrop(e, index)) {
					DropTarget target = new DropTarget
					{
						Item = item,
						Place = place,
						Node = node,
						Index = index
					};
					targets.Add(target);
				}
			}
		}

		void GetNodeAndIndex(SharpTreeViewItem item, DropPlace place, out SharpTreeNode node, out int index)
		{
			node = null;
			index = 0;

			if (place == DropPlace.Inside) {
				node = item.Node;
				index = node.Children.Count;
			}
			else if (place == DropPlace.Before) {
				if (item.Node.Parent != null) {
					node = item.Node.Parent;
					index = node.Children.IndexOf(item.Node);
				}
			}
			else {
				if (item.Node.Parent != null) {
					node = item.Node.Parent;
					index = node.Children.IndexOf(item.Node) + 1;
				}
			}
		}

		SharpTreeNodeView previewNodeView;
		InsertMarker insertMarker;
		DropPlace previewPlace;

		enum DropPlace
		{
			Before, Inside, After
		}

		void ShowPreview(SharpTreeViewItem item, DropPlace place)
		{
			previewNodeView = item.NodeView;
			previewPlace = place;

			if (place == DropPlace.Inside) {
				previewNodeView.TextBackground = SystemColors.HighlightBrush;
				//previewNodeView.Foreground = SystemColors.HighlightTextBrush;
			}
			else {
				if (insertMarker == null) {
					var adornerLayer = AdornerLayer.GetAdornerLayer(this);
					insertMarker = new InsertMarker();
					var adorner = new VisualLayerManager { Child = insertMarker };
					adornerLayer.Children.Add(adorner);
				}

				insertMarker.IsVisible = true;

				var p1 = previewNodeView.TranslatePoint(new Point(),this).Value;
				var p = new Point(p1.X + previewNodeView.CalculateIndent() + 4.5, p1.Y - 3);

				if (place == DropPlace.After) {
					p = p.WithY(p.Y + previewNodeView.Height);
				}

				insertMarker.Margin = new Thickness(p.X, p.Y, 0, 0);
				
				SharpTreeNodeView secondNodeView = null;
				var index = flattener.IndexOf(item.Node);

				if (place == DropPlace.Before) {
					if (index > 0) {
						secondNodeView = (ItemContainerGenerator.ContainerFromIndex(index - 1) as SharpTreeViewItem).NodeView;
					}
				}
				else if (index + 1 < flattener.Count) {
					secondNodeView = (ItemContainerGenerator.ContainerFromIndex(index + 1) as SharpTreeViewItem).NodeView;
				}
				
				var w = p1.X + previewNodeView.Width - p.X;

				if (secondNodeView != null) {
					var p2 = secondNodeView.TranslatePoint(new Point(), this).Value;
					w = Math.Max(w, p2.X + secondNodeView.Width - p.X);
				}

				insertMarker.Width = w + 10;
			}
		}

		void HidePreview()
		{
			if (previewNodeView != null) {
				previewNodeView.ClearValue(SharpTreeNodeView.TextBackgroundProperty);
				//previewNodeView.ClearValue(SharpTreeNodeView.ForegroundProperty);
				if (insertMarker != null) {
					insertMarker.IsVisible = false;
				}
				previewNodeView = null;
			}
		}
		#endregion

		#region Cut / Copy / Paste / Delete Commands

		public IList<RoutedCommandBinding> CommandBindings { get; } = new List<RoutedCommandBinding>();

		void RegisterCommands()
		{
			CommandBindings.Add(new RoutedCommandBinding(ApplicationCommands.Cut, HandleExecuted_Cut, HandleCanExecute_Cut));
			CommandBindings.Add(new RoutedCommandBinding(ApplicationCommands.Copy, HandleExecuted_Copy, HandleCanExecute_Copy));
			CommandBindings.Add(new RoutedCommandBinding(ApplicationCommands.Paste, HandleExecuted_Paste, HandleCanExecute_Paste));
			CommandBindings.Add(new RoutedCommandBinding(ApplicationCommands.Delete, HandleExecuted_Delete, HandleCanExecute_Delete));
		}

		static void HandleExecuted_Cut(object sender, ExecutedRoutedEventArgs e)
		{
			
		}

		static void HandleCanExecute_Cut(object sender, CanExecuteRoutedEventArgs e)
		{
			e.CanExecute = false;
		}

		static void HandleExecuted_Copy(object sender, ExecutedRoutedEventArgs e)
		{
			
		}

		static void HandleCanExecute_Copy(object sender, CanExecuteRoutedEventArgs e)
		{
			e.CanExecute = false;
		}

		static void HandleExecuted_Paste(object sender, ExecutedRoutedEventArgs e)
		{
			
		}

		static void HandleCanExecute_Paste(object sender, CanExecuteRoutedEventArgs e)
		{
			e.CanExecute = false;
		}

		static void HandleExecuted_Delete(object sender, ExecutedRoutedEventArgs e)
		{
			SharpTreeView treeView = (SharpTreeView)sender;
			treeView.updatesLocked = true;
			int selectedIndex = -1;
			try {
				foreach (SharpTreeNode node in treeView.GetTopLevelSelection().ToArray()) {
					if (selectedIndex == -1)
						selectedIndex = treeView.flattener.IndexOf(node);
					node.Delete();
				}
			} finally {
				treeView.updatesLocked = false;
				treeView.UpdateFocusedNode(null, Math.Max(0, selectedIndex - 1));
			}
		}

		static void HandleCanExecute_Delete(object sender, CanExecuteRoutedEventArgs e)
		{
			SharpTreeView treeView = (SharpTreeView)sender;
			e.CanExecute = treeView.GetTopLevelSelection().All(node => node.CanDelete());
		}
		
		/// <summary>
		/// Gets the selected items which do not have any of their ancestors selected.
		/// </summary>
		public IEnumerable<SharpTreeNode> GetTopLevelSelection()
		{
			var selection = SelectedItems.OfType<SharpTreeNode>();
			var selectionHash = new HashSet<SharpTreeNode>(selection);
			return selection.Where(item => item.Ancestors().All(a => !selectionHash.Contains(a)));
		}

		#endregion

		public void SetSelectedNodes(IEnumerable<SharpTreeNode> nodes)
		{
			SelectedItems.Clear();
			foreach (var item in nodes) {
				SelectedItems.Add(item);
			}
		}

		public new void UnselectAll()
		{
			SelectedItems.Clear();
		}
	}
}
