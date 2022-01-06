// Copyright (c) AlphaSierraPapa for the SharpDevelop Team (for details please see \doc\copyright.txt)
// This code is distributed under the GNU LGPL (for details please see \doc\license.txt)

using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace ICSharpCode.TreeView
{
	public class SharpTreeViewItem : ListBoxItem
	{
		static SharpTreeViewItem()
		{
			DragDrop.DragEnterEvent.AddClassHandler<SharpTreeViewItem>((x, e) => x.OnDragEnter(e));
			DragDrop.DragLeaveEvent.AddClassHandler<SharpTreeViewItem>((x, e) => x.OnDragLeave(e));
			DragDrop.DragOverEvent.AddClassHandler<SharpTreeViewItem>((x, e) => x.OnDragOver(e));
			DragDrop.DropEvent.AddClassHandler<SharpTreeViewItem>((x, e) => x.OnDrop(e));
		}

		public SharpTreeNode Node => DataContext as SharpTreeNode;

		public SharpTreeNodeView NodeView { get; internal set; }
		public SharpTreeView ParentTreeView { get; internal set; }

		protected override void OnKeyDown(KeyEventArgs e)
		{
			switch (e.Key) {
				case Key.F2:
//					if (SharpTreeNode.ActiveNodes.Count == 1 && Node.IsEditable) {
//						Node.IsEditing = true;
//						e.Handled = true;
//					}
					break;
				case Key.Escape:
					Node.IsEditing = false;
					break;
			}
		}

		#region Mouse

		Point startPoint;
		bool wasSelected;
		bool wasDoubleClick;

		protected override void OnPointerPressed(PointerPressedEventArgs e)
		{
			wasSelected = IsSelected;
			if (!IsSelected) {
				base.OnPointerPressed(e);
			}

			if (e.MouseButton == MouseButton.Left) {
				startPoint = e.GetPosition(this);
				e.Pointer.Capture(this);

				if (e.ClickCount == 2) {
					wasDoubleClick = true;
				}
			}
		}

		protected override void OnPointerMoved(PointerEventArgs e)
		{
			if (e.Pointer.Captured == this) {
				var currentPoint = e.GetPosition(this);
				if (Math.Abs(currentPoint.X - startPoint.X) >= SystemParameters.MinimumHorizontalDragDistance ||
					Math.Abs(currentPoint.Y - startPoint.Y) >= SystemParameters.MinimumVerticalDragDistance) {

					var selection = ParentTreeView.GetTopLevelSelection().ToArray();
					if (Node.CanDrag(selection)) {
						Node.StartDrag(e, this, selection);
					}
				}
			} else {
				base.OnPointerMoved(e);
			}
		}

		protected override void OnPointerReleased(PointerReleasedEventArgs e)
		{
			
			if (wasDoubleClick) {
				wasDoubleClick = false;
				Node.ActivateItem(e);
				if (!e.Handled) {
					if (!Node.IsRoot || ParentTreeView.ShowRootExpander) {
						Node.IsExpanded = !Node.IsExpanded;
					}
				}
			}

			//ReleaseMouseCapture();
			e.Pointer.Capture(null);
			if (wasSelected) {
				base.OnPointerReleased(e);
			}
		}

		#endregion
		
		#region Drag and Drop

		protected virtual void OnDragEnter(DragEventArgs e)
		{
			ParentTreeView.HandleDragEnter(this, e);
		}

		protected virtual void OnDragOver(DragEventArgs e)
		{
			ParentTreeView.HandleDragOver(this, e);
		}

		protected virtual void OnDrop(DragEventArgs e)
		{
			ParentTreeView.HandleDrop(this, e);
		}

		protected virtual void OnDragLeave(RoutedEventArgs e)
		{
			ParentTreeView.HandleDragLeave(this, e);
		}

		#endregion
	}
}
