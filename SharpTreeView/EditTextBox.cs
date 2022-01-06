// Copyright (c) AlphaSierraPapa for the SharpDevelop Team (for details please see \doc\copyright.txt)
// This code is distributed under the GNU LGPL (for details please see \doc\license.txt)

using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace ICSharpCode.TreeView
{
	class EditTextBox : TextBox
	{
		public SharpTreeViewItem Item { get; set; }

		public SharpTreeNode Node => Item.Node;

		protected override void OnTemplateApplied(TemplateAppliedEventArgs e)
		{
			base.OnTemplateApplied(e);
			Init();
		}

		void Init()
		{
			Text = Node.LoadEditText();
			Focus();
			SelectAll();
		}

		void SelectAll()
		{
			SelectionStart = 0;
			SelectionEnd = Text.Length;
		}

		protected override void OnKeyDown(KeyEventArgs e)
		{
			if (e.Key == Key.Enter) {
				Commit();
			} else if (e.Key == Key.Escape) {
				Node.IsEditing = false;
			}
		}

		protected override void OnLostFocus(RoutedEventArgs e)
		{
			if (Node.IsEditing) {
				Commit();
			}
		}

		bool commiting;

		void Commit()
		{
			if (!commiting) {
				commiting = true;

				Node.IsEditing = false;
				if (!Node.SaveEditText(Text)) {
					Item.Focus();
				}
				Node.RaisePropertyChanged("Text");

				//if (Node.SaveEditText(Text)) {
				//    Node.IsEditing = false;
				//    Node.RaisePropertyChanged("Text");
				//}
				//else {
				//    Init();
				//}

				commiting = false;
			}
		}
	}
}
