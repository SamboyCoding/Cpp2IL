// Copyright (c) AlphaSierraPapa for the SharpDevelop Team (for details please see \doc\copyright.txt)
// This code is distributed under the GNU LGPL (for details please see \doc\license.txt)

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace ICSharpCode.TreeView
{
	public partial class SharpTreeNode : INotifyPropertyChanged
	{
		SharpTreeNodeCollection modelChildren;
		internal SharpTreeNode modelParent;
		bool isVisible = true;
		
		void UpdateIsVisible(bool parentIsVisible, bool updateFlattener)
		{
			bool newIsVisible = parentIsVisible && !isHidden;
			if (isVisible != newIsVisible) {
				isVisible = newIsVisible;
				
				// invalidate the augmented data
				SharpTreeNode node = this;
				while (node != null && node.totalListLength >= 0) {
					node.totalListLength = -1;
					node = node.listParent;
				}
				// Remember the removed nodes:
				List<SharpTreeNode> removedNodes = null;
				if (updateFlattener && !newIsVisible) {
					removedNodes = VisibleDescendantsAndSelf().ToList();
				}
				// also update the model children:
				UpdateChildIsVisible(false);
				
				// Validate our invariants:
				if (updateFlattener)
					CheckRootInvariants();
				
				// Tell the flattener about the removed nodes:
				if (removedNodes != null) {
					var flattener = GetListRoot().treeFlattener;
					if (flattener != null) {
						flattener.NodesRemoved(GetVisibleIndexForNode(this), removedNodes);
						foreach (var n in removedNodes)
							n.OnIsVisibleChanged();
					}
				}
				// Tell the flattener about the new nodes:
				if (updateFlattener && newIsVisible) {
					var flattener = GetListRoot().treeFlattener;
					if (flattener != null) {
						flattener.NodesInserted(GetVisibleIndexForNode(this), VisibleDescendantsAndSelf());
						foreach (var n in VisibleDescendantsAndSelf())
							n.OnIsVisibleChanged();
					}
				}
			}
		}
		
		protected virtual void OnIsVisibleChanged() {}
		
		void UpdateChildIsVisible(bool updateFlattener)
		{
			if (modelChildren != null && modelChildren.Count > 0) {
				bool showChildren = isVisible && isExpanded;
				foreach (SharpTreeNode child in modelChildren) {
					child.UpdateIsVisible(showChildren, updateFlattener);
				}
			}
		}
		
		#region Main

		public SharpTreeNodeCollection Children {
			get {
				if (modelChildren == null)
					modelChildren = new SharpTreeNodeCollection(this);
				return modelChildren;
			}
		}
		
		public SharpTreeNode Parent => modelParent;

		public virtual object Text => null;

		public virtual IBrush Foreground => SystemColors.WindowTextBrush;

		public virtual object Icon => null;

		public virtual object ToolTip => null;

		public int Level => Parent != null ? Parent.Level + 1 : 0;

		public bool IsRoot => Parent == null;

		bool isHidden;
		
		public bool IsHidden
		{
			get => isHidden;
			set {
				if (isHidden != value) {
					isHidden = value;
					if (modelParent != null)
						UpdateIsVisible(modelParent.isVisible && modelParent.isExpanded, true);
					RaisePropertyChanged("IsHidden");
					Parent?.RaisePropertyChanged("ShowExpander");
				}
			}
		}
		
		/// <summary>
		/// Return true when this node is not hidden and when all parent nodes are expanded and not hidden.
		/// </summary>
		public bool IsVisible => isVisible;

		bool isSelected;
		
		public bool IsSelected {
			get => isSelected;
			set {
				if (isSelected != value) {
					isSelected = value;
					RaisePropertyChanged("IsSelected");
				}
			}
		}
		
		#endregion
		
		#region OnChildrenChanged
		internal protected virtual void OnChildrenChanged(NotifyCollectionChangedEventArgs e)
		{
			if (e.OldItems != null) {
				foreach (SharpTreeNode node in e.OldItems) {
					Debug.Assert(node.modelParent == this);
					node.modelParent = null;
					Trace.WriteLine($"Removing {node} from {this}");
					SharpTreeNode removeEnd = node;
					while (removeEnd.modelChildren != null && removeEnd.modelChildren.Count > 0)
						removeEnd = removeEnd.modelChildren.Last();
					
					List<SharpTreeNode> removedNodes = null;
					int visibleIndexOfRemoval = 0;
					if (node.isVisible) {
						visibleIndexOfRemoval = GetVisibleIndexForNode(node);
						removedNodes = node.VisibleDescendantsAndSelf().ToList();
					}
					
					RemoveNodes(node, removeEnd);
					
					if (removedNodes != null) {
						var flattener = GetListRoot().treeFlattener;
						flattener?.NodesRemoved(visibleIndexOfRemoval, removedNodes);
					}
				}
			}
			if (e.NewItems != null) {
				SharpTreeNode insertionPos;
				if (e.NewStartingIndex == 0)
					insertionPos = null;
				else
					insertionPos = modelChildren[e.NewStartingIndex - 1];
				
				foreach (SharpTreeNode node in e.NewItems) {
					Debug.Assert(node.modelParent == null);
					node.modelParent = this;
					node.UpdateIsVisible(isVisible && isExpanded, false);
					Trace.WriteLine($"Inserting {node} after {insertionPos}");
					
					while (insertionPos is {modelChildren: { }} && insertionPos.modelChildren.Count > 0) {
						insertionPos = insertionPos.modelChildren.Last();
					}
					InsertNodeAfter(insertionPos ?? this, node);
					
					insertionPos = node;
					if (node.isVisible) {
						var flattener = GetListRoot().treeFlattener;
						flattener?.NodesInserted(GetVisibleIndexForNode(node), node.VisibleDescendantsAndSelf());
					}
				}
			}
			
			RaisePropertyChanged("ShowExpander");
			RaiseIsLastChangedIfNeeded(e);
		}
		#endregion
		
		#region Expanding / LazyLoading
		
		public virtual object ExpandedIcon => Icon;

		public virtual bool ShowExpander
		{
			get { return LazyLoading || Children.Any(c => !c.isHidden); }
		}
		
		bool isExpanded;
		
		public bool IsExpanded
		{
			get => isExpanded;
			set
			{
				if (isExpanded != value) {
					isExpanded = value;
					if (isExpanded) {
						EnsureLazyChildren();
						OnExpanding();
					} else {
						OnCollapsing();
					}
					UpdateChildIsVisible(true);
					RaisePropertyChanged("IsExpanded");
				}
			}
		}
		
		protected virtual void OnExpanding() {}
		protected virtual void OnCollapsing() {}
		
		bool lazyLoading;
		
		public bool LazyLoading
		{
			get => lazyLoading;
			set
			{
				lazyLoading = value;
				if (lazyLoading) {
					IsExpanded = false;
					if (canExpandRecursively) {
						canExpandRecursively = false;
						RaisePropertyChanged("CanExpandRecursively");
					}
				}
				RaisePropertyChanged("LazyLoading");
				RaisePropertyChanged("ShowExpander");
			}
		}
		
		bool canExpandRecursively = true;
		
		/// <summary>
		/// Gets whether this node can be expanded recursively.
		/// If not overridden, this property returns false if the node is using lazy-loading, and true otherwise.
		/// </summary>
		public virtual bool CanExpandRecursively => canExpandRecursively;

		public virtual bool ShowIcon => Icon != null;

		protected virtual void LoadChildren()
		{
			throw new NotSupportedException(GetType().Name + " does not support lazy loading");
		}
		
		/// <summary>
		/// Ensures the children were initialized (loads children if lazy loading is enabled)
		/// </summary>
		public void EnsureLazyChildren()
		{
			if (LazyLoading) {
				LazyLoading = false;
				try {
					LoadChildren();
				} catch (Exception ex) {
					Trace.WriteLine(ex.Message);
					Trace.WriteLine(ex.StackTrace);
					throw ex;
				}
			}
		}
		
		#endregion
		
		#region Ancestors / Descendants
		
		public IEnumerable<SharpTreeNode> Descendants()
		{
			return TreeTraversal.PreOrder(Children, n => n.Children);
		}
		
		public IEnumerable<SharpTreeNode> DescendantsAndSelf()
		{
			return TreeTraversal.PreOrder(this, n => n.Children);
		}
		
		internal IEnumerable<SharpTreeNode> VisibleDescendants()
		{
			return TreeTraversal.PreOrder(Children.Where(c => c.isVisible), n => n.Children.Where(c => c.isVisible));
		}
		
		internal IEnumerable<SharpTreeNode> VisibleDescendantsAndSelf()
		{
			return TreeTraversal.PreOrder(this, n => n.Children.Where(c => c.isVisible));
		}
		
		public IEnumerable<SharpTreeNode> Ancestors()
		{
			var node = this;
			while (node.Parent != null) {
				yield return node.Parent;
				node = node.Parent;
			}
		}
		
		public IEnumerable<SharpTreeNode> AncestorsAndSelf()
		{
			yield return this;
			foreach (var node in Ancestors()) {
				yield return node;
			}
		}
		
		#endregion
		
		#region Editing
		
		public virtual bool IsEditable => false;

		bool isEditing;
		
		public bool IsEditing
		{
			get => isEditing;
			set
			{
				if (isEditing != value) {
					isEditing = value;
					RaisePropertyChanged("IsEditing");
				}
			}
		}
		
		public virtual string LoadEditText()
		{
			return null;
		}
		
		public virtual bool SaveEditText(string value)
		{
			return true;
		}
		
		#endregion
		
		#region Checkboxes
		
		public virtual bool IsCheckable => false;

		bool? isChecked;
		
		public bool? IsChecked {
			get => isChecked;
			set => SetIsChecked(value, true);
		}
		
		void SetIsChecked(bool? value, bool update)
		{
			if (isChecked != value) {
				isChecked = value;
				
				if (update) {
					if (IsChecked != null) {
						foreach (var child in Descendants()) {
							if (child.IsCheckable) {
								child.SetIsChecked(IsChecked, false);
							}
						}
					}
					
					foreach (var parent in Ancestors()) {
						if (parent.IsCheckable) {
							if (!parent.TryValueForIsChecked(true)) {
								if (!parent.TryValueForIsChecked(false)) {
									parent.SetIsChecked(null, false);
								}
							}
						}
					}
				}
				
				RaisePropertyChanged("IsChecked");
			}
		}
		
		bool TryValueForIsChecked(bool? value)
		{
			if (Children.Where(n => n.IsCheckable).All(n => n.IsChecked == value)) {
				SetIsChecked(value, false);
				return true;
			}
			return false;
		}
		
		#endregion
		
		#region Cut / Copy / Paste / Delete
		
		public bool IsCut => false;
		/*
			static List<SharpTreeNode> cuttedNodes = new List<SharpTreeNode>();
			static IDataObject cuttedData;
			static EventHandler requerySuggestedHandler; // for weak event
	
			static void StartCuttedDataWatcher()
			{
				requerySuggestedHandler = new EventHandler(CommandManager_RequerySuggested);
				CommandManager.RequerySuggested += requerySuggestedHandler;
			}
	
			static void CommandManager_RequerySuggested(object sender, EventArgs e)
			{
				if (cuttedData != null && !Clipboard.IsCurrent(cuttedData)) {
					ClearCuttedData();
				}
			}
	
			static void ClearCuttedData()
			{
				foreach (var node in cuttedNodes) {
					node.IsCut = false;
				}
				cuttedNodes.Clear();
				cuttedData = null;
			}
	
			//static public IEnumerable<SharpTreeNode> PurifyNodes(IEnumerable<SharpTreeNode> nodes)
			//{
			//    var list = nodes.ToList();
			//    var array = list.ToArray();
			//    foreach (var node1 in array) {
			//        foreach (var node2 in array) {
			//            if (node1.Descendants().Contains(node2)) {
			//                list.Remove(node2);
			//            }
			//        }
			//    }
			//    return list;
			//}
	
			bool isCut;
	
			public bool IsCut
			{
				get { return isCut; }
				private set
				{
					isCut = value;
					RaisePropertyChanged("IsCut");
				}
			}
	
			internal bool InternalCanCut()
			{
				return InternalCanCopy() && InternalCanDelete();
			}
	
			internal void InternalCut()
			{
				ClearCuttedData();
				cuttedData = Copy(ActiveNodesArray);
				Clipboard.SetDataObject(cuttedData);
	
				foreach (var node in ActiveNodes) {
					node.IsCut = true;
					cuttedNodes.Add(node);
				}
			}
	
			internal bool InternalCanCopy()
			{
				return CanCopy(ActiveNodesArray);
			}
	
			internal void InternalCopy()
			{
				Clipboard.SetDataObject(Copy(ActiveNodesArray));
			}
	
			internal bool InternalCanPaste()
			{
				return CanPaste(Clipboard.GetDataObject());
			}
	
			internal void InternalPaste()
			{
				Paste(Clipboard.GetDataObject());
	
				if (cuttedData != null) {
					DeleteCore(cuttedNodes.ToArray());
					ClearCuttedData();
				}
			}
		 */
		
		public virtual bool CanDelete()
		{
			return false;
		}
		
		public virtual void Delete()
		{
			throw new NotSupportedException(GetType().Name + " does not support deletion");
		}
		
		public virtual void DeleteCore()
		{
			throw new NotSupportedException(GetType().Name + " does not support deletion");
		}
		
		public virtual IDataObject Copy(SharpTreeNode[] nodes)
		{
			throw new NotSupportedException(GetType().Name + " does not support copy/paste or drag'n'drop");
		}
		
		/*
			public virtual bool CanCopy(SharpTreeNode[] nodes)
			{
				return false;
			}
	
			public virtual bool CanPaste(IDataObject data)
			{
				return false;
			}
	
			public virtual void Paste(IDataObject data)
			{
				EnsureLazyChildren();
				Drop(data, Children.Count, DropEffect.Copy);
			}
		 */
		#endregion
		
		#region Drag and Drop
		public virtual bool CanDrag(SharpTreeNode[] nodes)
		{
			return false;
		}

		public virtual async void StartDrag(PointerEventArgs e, AvaloniaObject dragSource, SharpTreeNode[] nodes)
		{
			DragDropEffects effects = DragDropEffects.Copy | DragDropEffects.Link | DragDropEffects.Move;
			if (!nodes.All(n => n.CanDelete()))
				effects &= ~DragDropEffects.Move;
			DragDropEffects result = await DragDrop.DoDragDrop(e, Copy(nodes), effects);
			if (result == DragDropEffects.Move) {
				foreach (SharpTreeNode node in nodes)
					node.DeleteCore();
			}
		}
		
		public virtual bool CanDrop(DragEventArgs e, int index)
		{
			return false;
		}
		
		internal void InternalDrop(DragEventArgs e, int index)
		{
			if (LazyLoading) {
				EnsureLazyChildren();
				index = Children.Count;
			}
			
			Drop(e, index);
		}
		
		public virtual void Drop(DragEventArgs e, int index)
		{
			throw new NotSupportedException(GetType().Name + " does not support Drop()");
		}
		#endregion
		
		#region IsLast (for TreeView lines)
		
		public bool IsLast =>
			Parent == null ||
			Parent.Children[Parent.Children.Count - 1] == this;

		void RaiseIsLastChangedIfNeeded(NotifyCollectionChangedEventArgs e)
		{
			switch (e.Action) {
				case NotifyCollectionChangedAction.Add:
					if (e.NewStartingIndex == Children.Count - 1) {
						if (Children.Count > 1) {
							Children[Children.Count - 2].RaisePropertyChanged("IsLast");
						}
						Children[Children.Count - 1].RaisePropertyChanged("IsLast");
					}
					break;
				case NotifyCollectionChangedAction.Remove:
					if (e.OldStartingIndex == Children.Count) {
						if (Children.Count > 0) {
							Children[Children.Count - 1].RaisePropertyChanged("IsLast");
						}
					}
					break;
			}
		}
		
		#endregion
		
		#region INotifyPropertyChanged Members
		
		public event PropertyChangedEventHandler PropertyChanged;
		
		public void RaisePropertyChanged(string name)
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
		}
		
		#endregion
		
		/// <summary>
		/// Gets called when the item is double-clicked.
		/// </summary>
		public virtual void ActivateItem(RoutedEventArgs e)
		{
		}
		
		public override string ToString()
		{
			// used for keyboard navigation
			object text = Text;
			return text != null ? text.ToString() : string.Empty;
		}
	}
}
